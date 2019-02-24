using KlipperSharp.Kinematics;
using KlipperSharp.MachineCodes;
using KlipperSharp.MicroController;
using KlipperSharp.PulseGeneration;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KlipperSharp
{
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
		private Vector4d commanded_pos; // XYZ E
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
			this.commanded_pos = Vector4d.Zero;
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
			catch (ConfigException)
			{
				throw;
			}
			catch (PinsException)
			{
				throw;
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

		private void _calc_print_time()
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

		private void _flush_lookahead(bool must_sync = false)
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

		private void _check_stall()
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

		private double _flush_handler(double eventtime)
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
		public Vector4d get_position()
		{
			return this.commanded_pos;
		}

		public void set_position(Vector4d newpos, List<int> homing_axes = null)
		{
			this._flush_lookahead();
			this.commanded_pos = newpos;
			this.kin.set_position(new Vector3d(newpos.X, newpos.Y, newpos.Z), homing_axes);
		}

		public void move(Vector4d newpos, double speed)
		{
			var move = new Move(this, commanded_pos, newpos, speed);
			if (move.move_d == 0)
			{
				return;
			}
			if (move.is_kinematic_move)
			{
				this.kin.check_move(move);
			}
			if (move.axes_d.W != 0)
			{
				this.extruder.check_move(move);
			}
			this.commanded_pos = move.end_pos;
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
			this.commanded_pos.W = extrude_pos;
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

		private void _handle_shutdown()
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

		private void _calc_junction_deviation()
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

}
