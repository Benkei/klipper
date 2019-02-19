using KlipperSharp.MachineCodes;
using KlipperSharp.MicroController;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KlipperSharp
{
	public enum KinematicType
	{
		none,
		cartesian,
		corexy,
		delta,
		extruder,
		polar,
		winch,
	}

	public class ToolHead
	{
		private static readonly Logger logging = LogManager.GetCurrentClassLogger();

		public const double STALL_TIME = 0.1;
		public const string cmd_SET_VELOCITY_LIMIT_help = "Set printer velocity limits";
		private Machine printer;
		private SelectReactor reactor;
		private List<Mcu> all_mcus;
		private Mcu mcu;
		private MoveQueue move_queue;
		private List<double> commanded_pos;
		public double max_velocity;
		public double max_accel;
		private double requested_accel_to_decel;
		public double max_accel_to_decel;
		private double square_corner_velocity;
		private double config_max_velocity;
		private double config_max_accel;
		private double config_square_corner_velocity;
		public double junction_deviation;
		private double buffer_time_low;
		private double buffer_time_high;
		private double buffer_time_start;
		private double move_flush_time;
		private double print_time;
		private double last_print_start_time;
		private double need_check_stall;
		private int print_stall;
		private bool sync_print_time;
		private double idle_flush_print_time;
		private ReactorTimer flush_timer;
		public BaseExtruder extruder;
		public move cmove;
		public move_fill_callback move_fill;
		public BaseKinematic kin;

		public ToolHead(ConfigWrapper config)
		{
			this.printer = config.get_printer();
			this.reactor = this.printer.get_reactor();
			this.all_mcus = (from mcus in this.printer.lookup_objects<Mcu>(module: "mcu") select mcus.modul as Mcu).ToList();
			this.mcu = this.all_mcus[0];
			this.move_queue = new MoveQueue();
			this.commanded_pos = new List<double> { 0.0, 0.0, 0.0, 0.0 };
			this.printer.register_event_handler("klippy:shutdown", _handle_shutdown);
			// Velocity and acceleration control
			this.max_velocity = config.getfloat("max_velocity", above: 0.0);
			this.max_accel = config.getfloat("max_accel", above: 0.0);
			this.requested_accel_to_decel = config.getfloat("max_accel_to_decel", this.max_accel * 0.5, above: 0.0);
			this.max_accel_to_decel = this.requested_accel_to_decel;
			this.square_corner_velocity = config.getfloat("square_corner_velocity", 5.0, minval: 0.0);
			this.config_max_velocity = this.max_velocity;
			this.config_max_accel = this.max_accel;
			this.config_square_corner_velocity = this.square_corner_velocity;
			this.junction_deviation = 0.0;
			this._calc_junction_deviation();
			// Print time tracking
			this.buffer_time_low = config.getfloat("buffer_time_low", 1.0, above: 0.0);
			this.buffer_time_high = config.getfloat("buffer_time_high", 2.0, above: this.buffer_time_low);
			this.buffer_time_start = config.getfloat("buffer_time_start", 0.25, above: 0.0);
			this.move_flush_time = config.getfloat("move_flush_time", 0.05, above: 0.0);
			this.print_time = 0.0;
			this.last_print_start_time = 0.0;
			this.need_check_stall = -1.0;
			this.print_stall = 0;
			this.sync_print_time = true;
			this.idle_flush_print_time = 0.0;
			this.flush_timer = this.reactor.register_timer(this._flush_handler);
			this.move_queue.set_flush_time(this.buffer_time_high);
			this.printer.try_load_module(config, "idle_timeout");
			this.printer.try_load_module(config, "statistics");
			// Setup iterative solver
			this.cmove = Itersolve.move_alloc();
			this.move_fill = Itersolve.move_fill;
			// Create kinematics class
			this.extruder = new DummyExtruder();
			this.move_queue.set_extruder(this.extruder);
			var kin_name = config.getEnum<KinematicType>("kinematics");
			try
			{
				this.kin = KinematicFactory.load_kinematics(kin_name, this, config);
			}
			catch (Exception ex)
			{
				var msg = $"Error loading kinematics '{kin_name}'";
				logging.Error(msg);
				throw new Exception(msg, ex);
			}
			var gcode = this.printer.lookup_object<GCodeParser>("gcode");
			gcode.register_command("SET_VELOCITY_LIMIT", this.cmd_SET_VELOCITY_LIMIT, desc: cmd_SET_VELOCITY_LIMIT_help);
			gcode.register_command("M204", this.cmd_M204);
		}

		// Print time tracking
		public void update_move_time(double movetime)
		{
			this.print_time += movetime;
			var flush_to_time = this.print_time - this.move_flush_time;
			foreach (var m in this.all_mcus)
			{
				m.flush_moves(flush_to_time);
			}
		}

		public void _calc_print_time()
		{
			var curtime = this.reactor.monotonic();
			var est_print_time = this.mcu.estimated_print_time(curtime);
			if (est_print_time + this.buffer_time_start > this.print_time)
			{
				this.print_time = est_print_time + this.buffer_time_start;
				this.last_print_start_time = this.print_time;
				this.printer.send_event("toolhead:sync_print_time", curtime, est_print_time, this.print_time);
			}
		}

		public double get_next_move_time()
		{
			if (this.sync_print_time)
			{
				this.sync_print_time = false;
				this.reactor.update_timer(this.flush_timer, SelectReactor.NOW);
				this._calc_print_time();
			}
			return this.print_time;
		}

		public void _flush_lookahead(bool must_sync = false)
		{
			var sync_print_time = this.sync_print_time;
			this.move_queue.flush();
			this.idle_flush_print_time = 0.0;
			if (sync_print_time || must_sync)
			{
				this.sync_print_time = true;
				this.move_queue.set_flush_time(this.buffer_time_high);
				this.need_check_stall = -1.0;
				this.reactor.update_timer(this.flush_timer, SelectReactor.NEVER);
				foreach (var m in this.all_mcus)
				{
					m.flush_moves(this.print_time);
				}
			}
		}

		public double get_last_move_time()
		{
			this._flush_lookahead();
			if (this.sync_print_time)
			{
				this._calc_print_time();
			}
			return this.print_time;
		}

		public void reset_print_time(double min_print_time = 0.0)
		{
			this._flush_lookahead(must_sync: true);
			var est_print_time = this.mcu.estimated_print_time(this.reactor.monotonic());
			this.print_time = Math.Max(min_print_time, est_print_time);
		}

		public void _check_stall()
		{
			double est_print_time;
			var eventtime = this.reactor.monotonic();
			if (this.sync_print_time)
			{
				// Building initial queue - make sure to flush on idle input
				if (this.idle_flush_print_time != 0)
				{
					est_print_time = this.mcu.estimated_print_time(eventtime);
					if (est_print_time < this.idle_flush_print_time)
					{
						this.print_stall += 1;
					}
					this.idle_flush_print_time = 0.0;
				}
				this.reactor.update_timer(this.flush_timer, eventtime + 0.1);
				return;
			}
			// Check if there are lots of queued moves and stall if so
			while (true)
			{
				est_print_time = this.mcu.estimated_print_time(eventtime);
				var buffer_time = this.print_time - est_print_time;
				var stall_time = buffer_time - this.buffer_time_high;
				if (stall_time <= 0.0)
				{
					break;
				}
				if (this.mcu.is_fileoutput())
				{
					this.need_check_stall = SelectReactor.NEVER;
					return;
				}
				eventtime = this.reactor.pause(eventtime + Math.Min(1.0, stall_time));
			}
			this.need_check_stall = est_print_time + this.buffer_time_high + 0.1;
		}

		public double _flush_handler(double eventtime)
		{
			try
			{
				var print_time = this.print_time;
				var buffer_time = print_time - this.mcu.estimated_print_time(eventtime);
				if (buffer_time > this.buffer_time_low)
				{
					// Running normally - reschedule check
					return eventtime + buffer_time - this.buffer_time_low;
				}
				// Under ran low buffer mark - flush lookahead queue
				this._flush_lookahead(must_sync: true);
				if (print_time != this.print_time)
				{
					this.idle_flush_print_time = this.print_time;
				}
			}
			catch
			{
				logging.Error("Exception in flush_handler");
				this.printer.invoke_shutdown("Exception in flush_handler");
			}
			return SelectReactor.NEVER;
		}

		// Movement commands
		public List<double> get_position()
		{
			return this.commanded_pos.ToList();
		}

		public void set_position(List<double> newpos, List<int> homing_axes = null)
		{
			this._flush_lookahead();
			this.commanded_pos.Clear();
			this.commanded_pos.AddRange(newpos);
			this.kin.set_position(newpos, homing_axes);
		}

		public void move(List<double> newpos, double speed)
		{
			var move = new Move(this, this.commanded_pos, newpos, speed);
			if (move.move_d == 0)
			{
				return;
			}
			if (move.is_kinematic_move)
			{
				this.kin.check_move(move);
			}
			if (move.axes_d[3] != 0)
			{
				this.extruder.check_move(move);
			}
			this.commanded_pos.Clear();
			this.commanded_pos.AddRange(move.end_pos);
			this.move_queue.add_move(move);
			if (this.print_time > this.need_check_stall)
			{
				this._check_stall();
			}
		}

		public void dwell(double delay, bool check_stall = true)
		{
			this.get_last_move_time();
			this.update_move_time(delay);
			if (check_stall)
			{
				this._check_stall();
			}
		}

		public void motor_off()
		{
			this.dwell(STALL_TIME);
			var last_move_time = this.get_last_move_time();
			this.kin.motor_off(last_move_time);
			foreach (var ext in PrinterExtruder.get_printer_extruders(this.printer))
			{
				ext.motor_off(last_move_time);
			}
			this.dwell(STALL_TIME);
			logging.Debug("; Max time of {0}", last_move_time);
		}

		public void wait_moves()
		{
			this._flush_lookahead();
			if (this.mcu.is_fileoutput())
			{
				return;
			}
			var eventtime = this.reactor.monotonic();
			while (!this.sync_print_time || this.print_time >= this.mcu.estimated_print_time(eventtime))
			{
				eventtime = this.reactor.pause(eventtime + 0.1);
			}
		}

		public void set_extruder(BaseExtruder extruder)
		{
			var last_move_time = this.get_last_move_time();
			this.extruder.set_active(last_move_time, false);
			var extrude_pos = extruder.set_active(last_move_time, true);
			this.extruder = extruder;
			this.move_queue.set_extruder(extruder);
			this.commanded_pos[3] = extrude_pos;
		}

		public BaseExtruder get_extruder()
		{
			return this.extruder;
		}

		// Misc commands
		public (bool, string) stats(double eventtime)
		{
			foreach (var m in this.all_mcus)
			{
				m.check_active(this.print_time, eventtime);
			}
			var buffer_time = this.print_time - this.mcu.estimated_print_time(eventtime);
			var is_active = buffer_time > -60.0 || !this.sync_print_time;
			return (is_active, $"print_time={this.print_time:0.00} buffer_time={Math.Max(buffer_time, 0.0)} print_stall={this.print_stall}");
		}

		public (double, double, double) check_busy(double eventtime)
		{
			var est_print_time = this.mcu.estimated_print_time(eventtime);
			var lookahead_empty = this.move_queue.queue.Count == 0 ? 1 : 0;
			return (this.print_time, est_print_time, lookahead_empty);
		}

		public Dictionary<string, object> get_status(double eventtime)
		{
			string status;
			var print_time = this.print_time;
			var estimated_print_time = this.mcu.estimated_print_time(eventtime);
			var last_print_start_time = this.last_print_start_time;
			var buffer_time = print_time - estimated_print_time;
			if (buffer_time > -1.0 || !this.sync_print_time)
			{
				status = "Printing";
			}
			else
			{
				status = "Ready";
			}
			return new Dictionary<string, object> {
					 { "status", status},
					 { "print_time", print_time},
					 { "estimated_print_time", estimated_print_time},
					 { "printing_time", print_time - last_print_start_time}
			};
		}

		public void _handle_shutdown()
		{
			this.move_queue.reset();
			this.reset_print_time();
		}

		public BaseKinematic get_kinematics()
		{
			return this.kin;
		}

		public (double, double) get_max_velocity()
		{
			return (this.max_velocity, this.max_accel);
		}

		public double get_max_axis_halt()
		{
			// Determine the maximum velocity a cartesian axis could halt
			// at due to the junction_deviation setting.  The 8.0 was
			// determined experimentally.
			return Math.Min(this.max_velocity, Math.Sqrt(8.0 * this.junction_deviation * this.max_accel));
		}

		public void _calc_junction_deviation()
		{
			var scv2 = Math.Pow(this.square_corner_velocity, 2);
			this.junction_deviation = scv2 * (Math.Sqrt(2.0) - 1.0) / this.max_accel;
			this.max_accel_to_decel = Math.Min(this.requested_accel_to_decel, this.max_accel);
		}

		public void cmd_SET_VELOCITY_LIMIT(Dictionary<string, object> parameters)
		{
			var print_time = this.get_last_move_time();
			var gcode = this.printer.lookup_object<GCodeParser>("gcode");
			var max_velocity = gcode.get_float("VELOCITY", parameters, this.max_velocity, above: 0.0);
			var max_accel = gcode.get_float("ACCEL", parameters, this.max_accel, above: 0.0);
			var square_corner_velocity = gcode.get_float("SQUARE_CORNER_VELOCITY", parameters, this.square_corner_velocity, minval: 0.0);
			this.requested_accel_to_decel = gcode.get_float("ACCEL_TO_DECEL", parameters, this.requested_accel_to_decel, above: 0.0);
			this.max_velocity = Math.Min(max_velocity, this.config_max_velocity);
			this.max_accel = Math.Min(max_accel, this.config_max_accel);
			this.square_corner_velocity = Math.Min(square_corner_velocity, this.config_square_corner_velocity);
			this._calc_junction_deviation();
			var msg = $"max_velocity: {max_velocity:0.000}\nmax_accel: {max_accel:0.000}\nmax_accel_to_decel: {this.requested_accel_to_decel:0.000}\nsquare_corner_velocity: {square_corner_velocity:0.000}";
			this.printer.set_rollover_info("toolhead", $"toolhead: {msg}");
			gcode.respond_info(msg);
		}

		public void cmd_M204(Dictionary<string, object> parameters)
		{
			double accel;
			var gcode = this.printer.lookup_object<GCodeParser>("gcode");
			if (parameters.ContainsKey("P") && parameters.ContainsKey("T") && !parameters.ContainsKey("S"))
			{
				// Use minimum of P and T for accel
				accel = Math.Min(gcode.get_float("P", parameters, above: 0.0), gcode.get_float("T", parameters, above: 0.0));
			}
			else
			{
				// Use S for accel
				accel = gcode.get_float("S", parameters, above: 0.0);
			}
			this.max_accel = Math.Min(accel, this.config_max_accel);
			this._calc_junction_deviation();
		}
	}


	public class MoveQueue
	{
		public const double LOOKAHEAD_FLUSH_TIME = 0.25;
		public List<Move> queue = new List<Move>();
		private int leftover;
		private double junction_flush;
		private Func<List<Move>, int, bool, int> extruder_lookahead;

		public MoveQueue()
		{
			this.extruder_lookahead = null;
			this.junction_flush = LOOKAHEAD_FLUSH_TIME;
		}

		public void reset()
		{
			this.queue.Clear();
			this.leftover = 0;
			this.junction_flush = LOOKAHEAD_FLUSH_TIME;
		}

		public void set_flush_time(double flush_time)
		{
			this.junction_flush = flush_time;
		}

		public void set_extruder(BaseExtruder extruder)
		{
			this.extruder_lookahead = extruder.lookahead;
		}

		public void flush(bool lazy = false)
		{
			this.junction_flush = LOOKAHEAD_FLUSH_TIME;
			var update_flush_count = lazy;
			var queue = this.queue;
			var flush_count = queue.Count;
			// Traverse queue from last to first move and determine maximum
			// junction speed assuming the robot comes to a complete stop
			// after the last move.
			var delayed = new List<(Move m, double ms_v2, double me_v2)>();
			var next_end_v2 = 0.0;
			var next_smoothed_v2 = 0.0;
			var peak_cruise_v2 = 0.0;
			for (int i = flush_count - 1; i < this.leftover - 1; i++)
			{
				var move = queue[i];
				var reachable_start_v2 = next_end_v2 + move.delta_v2;
				var start_v2 = Math.Min(move.max_start_v2, reachable_start_v2);
				var reachable_smoothed_v2 = next_smoothed_v2 + move.smooth_delta_v2;
				var smoothed_v2 = Math.Min(move.max_smoothed_v2, reachable_smoothed_v2);
				if (smoothed_v2 < reachable_smoothed_v2)
				{
					// It's possible for this move to accelerate
					if (smoothed_v2 + move.smooth_delta_v2 > next_smoothed_v2 || delayed.Count != 0)
					{
						// This move can decelerate or this is a full accel
						// move after a full decel move
						if (update_flush_count && peak_cruise_v2 != 0)
						{
							flush_count = i;
							update_flush_count = false;
						}
						peak_cruise_v2 = Math.Min(move.max_cruise_v2, (smoothed_v2 + reachable_smoothed_v2) * 0.5);
						if (delayed.Count != 0)
						{
							// Propagate peak_cruise_v2 to any delayed moves
							if (!update_flush_count && i < flush_count)
							{
								foreach (var item in delayed)
								{
									var mc_v2 = Math.Min(peak_cruise_v2, item.ms_v2);
									item.m.set_junction(Math.Min(item.ms_v2, mc_v2), mc_v2, Math.Min(item.me_v2, mc_v2));
								}
							}
							delayed.Clear();
						}
					}
					if (!update_flush_count && i < flush_count)
					{
						var cruise_v2 = Math.Min((start_v2 + reachable_start_v2) * 0.5, move.max_cruise_v2);
						cruise_v2 = Math.Min(cruise_v2, peak_cruise_v2);
						move.set_junction(Math.Min(start_v2, cruise_v2), cruise_v2, Math.Min(next_end_v2, cruise_v2));
					}
				}
				else
				{
					// Delay calculating this move until peak_cruise_v2 is known
					delayed.Add((move, start_v2, next_end_v2));
				}
				next_end_v2 = start_v2;
				next_smoothed_v2 = smoothed_v2;
			}
			if (update_flush_count)
			{
				return;
			}
			// Allow extruder to do its lookahead
			var move_count = this.extruder_lookahead(queue, flush_count, lazy);
			// Generate step times for all moves ready to be flushed
			for (int i = 0; i < move_count; i++)
			{
				queue[i].move();
			}
			// Remove processed moves from the queue
			this.leftover = flush_count - move_count;
			queue.RemoveRange(0, move_count);
		}

		public void add_move(Move move)
		{
			this.queue.Add(move);
			if (this.queue.Count == 1)
			{
				return;
			}
			move.calc_junction(this.queue[-2]);
			this.junction_flush -= move.min_move_t;
			if (this.junction_flush <= 0.0)
			{
				// Enough moves have been queued to reach the target flush time.
				this.flush(lazy: true);
			}
		}
	}

	public class Move
	{
		private ToolHead toolhead;
		public double[] start_pos;
		public move cmove;
		public bool is_kinematic_move;
		public List<double> axes_d;
		internal double accel;
		public double move_d;
		public double[] end_pos;
		internal double min_move_t;
		internal double max_start_v2;
		public double max_cruise_v2;
		public double delta_v2;
		public double max_smoothed_v2;
		public double smooth_delta_v2;
		private double accel_r;
		private double decel_r;
		private double cruise_r;
		internal double start_v;
		internal double cruise_v;
		internal double end_v;
		internal double accel_t;
		internal double cruise_t;
		internal double decel_t;
		internal double extrude_r;
		internal double extrude_max_corner_v;

		public Move(ToolHead toolhead, List<double> start_pos, List<double> end_pos, double speed)
		{
			this.toolhead = toolhead;
			this.start_pos = new[] { start_pos[0], start_pos[1], start_pos[2], start_pos[3] };
			this.end_pos = new[] { end_pos[0], end_pos[1], end_pos[2], end_pos[3] };
			this.accel = toolhead.max_accel;
			var velocity = Math.Min(speed, toolhead.max_velocity);
			this.cmove = toolhead.cmove;
			this.is_kinematic_move = true;
			this.axes_d = (from i in new[] { 0, 1, 2, 3 } select (end_pos[i] - start_pos[i])).ToList();
			this.move_d = Math.Sqrt((from d in axes_d.GetRange(0, 3) select (d * d)).Sum());
			if (move_d < 1E-09)
			{
				// Extrude only move
				this.end_pos = new[] { start_pos[0], start_pos[1], start_pos[2], end_pos[3] };
				axes_d[0] = 0.0;
				this.move_d = Math.Abs(axes_d[3]);
				this.accel = 99999999.9;
				velocity = speed;
				this.is_kinematic_move = false;
			}
			this.min_move_t = move_d / velocity;
			// Junction speeds are tracked in velocity squared.  The
			// delta_v2 is the maximum amount of this squared-velocity that
			// can change in this move.
			this.max_start_v2 = 0.0;
			this.max_cruise_v2 = Math.Pow(velocity, 2);
			this.delta_v2 = 2.0 * move_d * this.accel;
			this.max_smoothed_v2 = 0.0;
			this.smooth_delta_v2 = 2.0 * move_d * toolhead.max_accel_to_decel;
		}

		public void limit_speed(double speed, double accel)
		{
			var speed2 = Math.Pow(speed, 2);
			if (speed2 < this.max_cruise_v2)
			{
				this.max_cruise_v2 = speed2;
				this.min_move_t = this.move_d / speed;
			}
			this.accel = Math.Min(this.accel, accel);
			this.delta_v2 = 2.0 * this.move_d * this.accel;
			this.smooth_delta_v2 = Math.Min(this.smooth_delta_v2, this.delta_v2);
		}

		public void calc_junction(Move prev_move)
		{
			if (!this.is_kinematic_move || !prev_move.is_kinematic_move)
			{
				return;
			}
			// Allow extruder to calculate its maximum junction
			var extruder_v2 = this.toolhead.extruder.calc_junction(prev_move, this);
			// Find max velocity using approximated centripetal velocity as
			// described at:
			// https://onehossshay.wordpress.com/2011/09/24/improving_grbl_cornering_algorithm/
			var axes_d = this.axes_d;
			var prev_axes_d = prev_move.axes_d;
			var junction_cos_theta = -(axes_d[0] * prev_axes_d[0] + axes_d[1] * prev_axes_d[1] + axes_d[2] * prev_axes_d[2]) / (this.move_d * prev_move.move_d);
			if (junction_cos_theta > 0.999999)
			{
				return;
			}
			junction_cos_theta = Math.Max(junction_cos_theta, -0.999999);
			var sin_theta_d2 = Math.Sqrt(0.5 * (1.0 - junction_cos_theta));
			var R = this.toolhead.junction_deviation * sin_theta_d2 / (1.0 - sin_theta_d2);
			var tan_theta_d2 = sin_theta_d2 / Math.Sqrt(0.5 * (1.0 + junction_cos_theta));
			var move_centripetal_v2 = 0.5 * this.move_d * tan_theta_d2 * this.accel;
			var prev_move_centripetal_v2 = 0.5 * prev_move.move_d * tan_theta_d2 * prev_move.accel;
			//this.max_start_v2 = Math.Min(R * this.accel, R * prev_move.accel,
			//	move_centripetal_v2, prev_move_centripetal_v2,
			//	extruder_v2, this.max_cruise_v2,
			//	prev_move.max_cruise_v2, prev_move.max_start_v2 + prev_move.delta_v2
			//	);
			this.max_start_v2 = Math.Min(R * this.accel, R * prev_move.accel);
			this.max_start_v2 = Math.Min(this.max_start_v2, move_centripetal_v2);
			this.max_start_v2 = Math.Min(this.max_start_v2, prev_move_centripetal_v2);
			this.max_start_v2 = Math.Min(this.max_start_v2, extruder_v2);
			this.max_start_v2 = Math.Min(this.max_start_v2, this.max_cruise_v2);
			this.max_start_v2 = Math.Min(this.max_start_v2, prev_move.max_cruise_v2);
			this.max_start_v2 = Math.Min(this.max_start_v2, prev_move.max_start_v2 + prev_move.delta_v2);
			this.max_smoothed_v2 = Math.Min(this.max_start_v2, prev_move.max_smoothed_v2 + prev_move.smooth_delta_v2);
		}

		public void set_junction(double start_v2, double cruise_v2, double end_v2)
		{
			// Determine accel, cruise, and decel portions of the move distance
			var inv_delta_v2 = 1.0 / this.delta_v2;
			this.accel_r = (cruise_v2 - start_v2) * inv_delta_v2;
			this.decel_r = (cruise_v2 - end_v2) * inv_delta_v2;
			this.cruise_r = 1.0 - accel_r - decel_r;
			// Determine move velocities
			this.start_v = Math.Sqrt(start_v2);
			this.cruise_v = Math.Sqrt(cruise_v2);
			this.end_v = Math.Sqrt(end_v2);
			// Determine time spent in each portion of move (time is the
			// distance divided by average velocity)
			this.accel_t = accel_r * this.move_d / ((start_v + cruise_v) * 0.5);
			this.cruise_t = cruise_r * this.move_d / cruise_v;
			this.decel_t = decel_r * this.move_d / ((end_v + cruise_v) * 0.5);
		}

		public void move()
		{
			// Generate step times for the move
			var next_move_time = this.toolhead.get_next_move_time();
			if (this.is_kinematic_move)
			{
				this.toolhead.move_fill(ref this.cmove, next_move_time,
					this.accel_t, this.cruise_t, this.decel_t,
					this.start_pos[0], this.start_pos[1], this.start_pos[2],
					this.axes_d[0], this.axes_d[1], this.axes_d[2],
					this.start_v, this.cruise_v, this.accel);
				this.toolhead.kin.move(next_move_time, this);
			}
			if (this.axes_d[3] != 0)
			{
				this.toolhead.extruder.move(next_move_time, this);
			}
			this.toolhead.update_move_time(this.accel_t + this.cruise_t + this.decel_t);
		}
	}

}
