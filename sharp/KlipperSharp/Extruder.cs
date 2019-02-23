using KlipperSharp.MachineCodes;
using KlipperSharp.PulseGeneration;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KlipperSharp
{
	public abstract class BaseExtruder
	{
		public abstract double set_active(double print_time, bool is_active);

		public abstract void motor_off(double move_time);

		public abstract void check_move(Move move);

		public abstract double calc_junction(Move prev_move, Move move);

		public abstract int lookahead(List<Move> moves, int flush_count, bool lazy);

		public abstract void move(double print_time, Move move);
	}

	public class PrinterExtruder : BaseExtruder
	{
		private static readonly Logger logging = LogManager.GetCurrentClassLogger();

		public const double EXTRUDE_DIFF_IGNORE = 1.02;
		public const string cmd_SET_PRESSURE_ADVANCE_help = "Set pressure advance parameters";

		private Machine printer;
		private string name;
		private PrinterStepper stepper;
		private double nozzle_diameter;
		private double filament_area;
		private double max_extrude_ratio;
		private double max_e_velocity;
		private double max_e_accel;
		private double max_e_dist;
		private string activate_gcode;
		private string deactivate_gcode;
		private double pressure_advance;
		private double pressure_advance_lookahead_time;
		private bool need_motor_enable;
		private double extrude_pos;
		private move cmove;
		private Action<move, double, double, double, double, double, double, double, double, double, double> extruder_move_fill;
		private Heater heater;

		public PrinterExtruder(ConfigWrapper config, object extruder_num)
		{
			this.printer = config.get_printer();
			this.name = config.get_name();
			var shared_heater = config.get("shared_heater", null);
			var pheater = this.printer.lookup_object<PrinterHeaters>("heater");
			var gcode_id = $"T{extruder_num}";
			if (shared_heater == null)
			{
				this.heater = pheater.setup_heater(config, gcode_id);
			}
			else
			{
				this.heater = pheater.lookup_heater(shared_heater);
			}
			this.stepper = new PrinterStepper(config);
			this.nozzle_diameter = config.getfloat("nozzle_diameter", above: 0.0);
			var filament_diameter = config.getfloat("filament_diameter", minval: this.nozzle_diameter);
			this.filament_area = Math.PI * Math.Pow(filament_diameter * 0.5, 2);
			var def_max_cross_section = 4.0 * Math.Pow(this.nozzle_diameter, 2);
			var def_max_extrude_ratio = def_max_cross_section / this.filament_area;
			var max_cross_section = config.getfloat("max_extrude_cross_section", def_max_cross_section, above: 0.0);
			this.max_extrude_ratio = max_cross_section / this.filament_area;
			logging.Info("Extruder max_extrude_ratio={0:0.0000}", this.max_extrude_ratio);
			var toolhead = this.printer.lookup_object<ToolHead>("toolhead");
			var _tup_1 = toolhead.get_max_velocity();
			var max_velocity = _tup_1.Item1;
			var max_accel = _tup_1.Item2;
			this.max_e_velocity = config.getfloat("max_extrude_only_velocity", max_velocity * def_max_extrude_ratio, above: 0.0);
			this.max_e_accel = config.getfloat("max_extrude_only_accel", max_accel * def_max_extrude_ratio, above: 0.0);
			this.stepper.set_max_jerk(9999999.9, 9999999.9);
			this.max_e_dist = config.getfloat("max_extrude_only_distance", 50.0, minval: 0.0);
			this.activate_gcode = config.get("activate_gcode", "");
			this.deactivate_gcode = config.get("deactivate_gcode", "");
			this.pressure_advance = config.getfloat("pressure_advance", 0.0, minval: 0.0);
			this.pressure_advance_lookahead_time = config.getfloat("pressure_advance_lookahead_time", 0.01, minval: 0.0);
			this.need_motor_enable = true;
			this.extrude_pos = 0.0;
			// Setup iterative solver
			this.cmove = Itersolve.move_alloc();
			this.extruder_move_fill = KinematicStepper.extruder_move_fill;
			this.stepper.setup_itersolve(KinematicType.extruder, null);
			// Setup SET_PRESSURE_ADVANCE command
			var gcode = this.printer.lookup_object<GCodeParser>("gcode");
			if (new[] { "extruder", "extruder0" }.Contains(this.name))
			{
				gcode.register_mux_command("SET_PRESSURE_ADVANCE", "EXTRUDER", null, this.cmd_default_SET_PRESSURE_ADVANCE, desc: cmd_SET_PRESSURE_ADVANCE_help);
			}
			gcode.register_mux_command("SET_PRESSURE_ADVANCE", "EXTRUDER", this.name, this.cmd_SET_PRESSURE_ADVANCE, desc: cmd_SET_PRESSURE_ADVANCE_help);
		}

		public Heater get_heater()
		{
			return this.heater;
		}

		public override double set_active(double print_time, bool is_active)
		{
			return this.extrude_pos;
		}

		public string get_activate_gcode(bool is_active)
		{
			if (is_active)
			{
				return this.activate_gcode;
			}
			return this.deactivate_gcode;
		}

		public (bool, string) stats(double eventtime)
		{
			return this.heater.stats(eventtime);
		}

		public override void motor_off(double print_time)
		{
			this.stepper.motor_enable(print_time);
			this.need_motor_enable = true;
		}

		public override void check_move(Move move)
		{
			move.extrude_r = move.axes_d.W / move.move_d;
			move.extrude_max_corner_v = 0.0;
			if (!this.heater.can_extrude)
			{
				throw new Exception("Extrude below minimum temp\nSee the 'min_extrude_temp' config option for details");
			}
			if (!move.is_kinematic_move || move.extrude_r < 0.0)
			{
				// Extrude only move (or retraction move) - limit accel and velocity
				if (Math.Abs(move.axes_d.W) > this.max_e_dist)
				{
					throw new Exception($"Extrude only move too long ({move.axes_d.W:0.000}mm vs {this.max_e_dist:0.000}mm)See the 'max_extrude_only_distance' config option for details");
				}
				var inv_extrude_r = 1.0 / Math.Abs(move.extrude_r);
				move.limit_speed(this.max_e_velocity * inv_extrude_r, this.max_e_accel * inv_extrude_r);
			}
			else if (move.extrude_r > this.max_extrude_ratio)
			{
				if (move.axes_d.W <= this.nozzle_diameter * this.max_extrude_ratio)
				{
					// Permit extrusion if amount extruded is tiny
					move.extrude_r = this.max_extrude_ratio;
					return;
				}
				var area = move.axes_d.W * this.filament_area / move.move_d;
				logging.Debug("Overextrude: {0} vs {1} (area={2:0.000} dist={3:0.000})", move.extrude_r, this.max_extrude_ratio, area, move.move_d);
				throw new Exception($"Move exceeds maximum extrusion ({area:0.000}mm^2 vs {this.max_extrude_ratio * this.filament_area:0.000}mm^2) See the 'max_extrude_cross_section' config option for details");
			}
		}

		public override double calc_junction(Move prev_move, Move move)
		{
			var extrude = move.axes_d.W;
			var prev_extrude = prev_move.axes_d.W;
			if (extrude != 0 || prev_extrude != 0)
			{
				if (extrude == 0 || prev_extrude == 0)
				{
					// Extrude move to non-extrude move - disable lookahead
					return 0.0;
				}
				if ((move.extrude_r > prev_move.extrude_r * EXTRUDE_DIFF_IGNORE
					|| prev_move.extrude_r > move.extrude_r * EXTRUDE_DIFF_IGNORE)
					&& Math.Abs(move.move_d * prev_move.extrude_r - extrude) >= 0.001)
				{
					// Extrude ratio between moves is too different
					return 0.0;
				}
				move.extrude_r = prev_move.extrude_r;
			}
			return move.max_cruise_v2;
		}

		public override int lookahead(List<Move> moves, int flush_count, bool lazy)
		{
			var lookahead_t = this.pressure_advance_lookahead_time;
			if (this.pressure_advance == 0 || lookahead_t == 0)
			{
				return flush_count;
			}
			// Calculate max_corner_v - the speed the head will accelerate
			// to after cornering.
			for (int i = 0; i < flush_count; i++)
			{
				var move = moves[i];
				if (move.decel_t == 0)
				{
					continue;
				}
				var cruise_v = move.cruise_v;
				var max_corner_v = 0.0;
				var sum_t = lookahead_t;
				for (int j = i + 1; j < flush_count; j++)
				{
					var fmove = moves[j];
					if (fmove.max_start_v2 == 0)
					{
						break;
					}
					if (fmove.cruise_v > max_corner_v)
					{
						if (max_corner_v == 0 && fmove.accel_t == 0 && fmove.cruise_t == 0)
						{
							// Start timing after any full decel moves
							continue;
						}
						if (sum_t >= fmove.accel_t)
						{
							max_corner_v = fmove.cruise_v;
						}
						else
						{
							max_corner_v = Math.Max(max_corner_v, fmove.start_v + fmove.accel * sum_t);
						}
						if (max_corner_v >= cruise_v)
						{
							break;
						}
					}
					sum_t -= fmove.accel_t + fmove.cruise_t + fmove.decel_t;
					if (sum_t <= 0.0)
					{
						break;
					}
				}
				move.extrude_max_corner_v = max_corner_v;
			}
			return flush_count;
		}

		public override void move(double print_time, Move move)
		{
			double npd;
			if (this.need_motor_enable)
			{
				this.stepper.motor_enable(print_time, true);
				this.need_motor_enable = false;
			}
			var axis_d = (double)move.axes_d.W;
			var axis_r = axis_d / move.move_d;
			var accel = move.accel * axis_r;
			var start_v = move.start_v * axis_r;
			var cruise_v = move.cruise_v * axis_r;
			var accel_t = move.accel_t;
			var cruise_t = move.cruise_t;
			var decel_t = move.decel_t;
			// Update for pressure advance
			var extra_accel_v = 0.0;
			var start_pos = this.extrude_pos;
			double extra_decel_v = 0;
			if (axis_d >= 0.0 && (move.axes_d.X != 0 || move.axes_d.Y != 0) && this.pressure_advance != 0)
			{
				// Calculate extra_accel_v
				var pressure_advance = this.pressure_advance * move.extrude_r;
				var prev_pressure_d = start_pos - move.start_pos.W;
				if (accel_t != 0)
				{
					npd = move.cruise_v * pressure_advance;
					var extra_accel_d = npd - prev_pressure_d;
					if (extra_accel_d > 0.0)
					{
						extra_accel_v = extra_accel_d / accel_t;
						axis_d += extra_accel_d;
						prev_pressure_d += extra_accel_d;
					}
				}
				// Calculate extra_decel_v
				var emcv = move.extrude_max_corner_v;
				if (decel_t != 0 && emcv < move.cruise_v)
				{
					npd = Math.Max(emcv, move.end_v) * pressure_advance;
					var extra_decel_d = npd - prev_pressure_d;
					if (extra_decel_d < 0.0)
					{
						axis_d += extra_decel_d;
						extra_decel_v = extra_decel_d / decel_t;
					}
				}
			}
			// Generate steps
			this.extruder_move_fill(this.cmove, print_time, accel_t, cruise_t, decel_t, start_pos, start_v, cruise_v, accel, extra_accel_v, extra_decel_v);
			this.stepper.step_itersolve(this.cmove);
			this.extrude_pos = start_pos + axis_d;
		}

		public void cmd_default_SET_PRESSURE_ADVANCE(Dictionary<string, object> parameters)
		{
			var extruder = this.printer.lookup_object<ToolHead>("toolhead").get_extruder() as PrinterExtruder;
			extruder.cmd_SET_PRESSURE_ADVANCE(parameters);
		}

		public void cmd_SET_PRESSURE_ADVANCE(Dictionary<string, object> parameters)
		{
			this.printer.lookup_object<ToolHead>("toolhead").get_last_move_time();
			var gcode = this.printer.lookup_object<GCodeParser>("gcode");
			var pressure_advance = gcode.get_float("ADVANCE", parameters, this.pressure_advance, minval: 0.0);
			var pressure_advance_lookahead_time = gcode.get_float("ADVANCE_LOOKAHEAD_TIME", parameters, this.pressure_advance_lookahead_time, minval: 0.0);
			this.pressure_advance = pressure_advance;
			this.pressure_advance_lookahead_time = pressure_advance_lookahead_time;
			var msg = $"pressure_advance: {pressure_advance:0.000}\npressure_advance_lookahead_time: {pressure_advance_lookahead_time:0.000}";
			this.printer.set_rollover_info(this.name, $"{this.name}: {msg}");
			gcode.respond_info(msg);
		}


		public static List<PrinterExtruder> get_printer_extruders(Machine printer)
		{
			var @out = new List<PrinterExtruder>();
			for (int i = 0; i < 99; i++)
			{
				var extruder = printer.lookup_object<PrinterExtruder>($"extruder{i}", null);
				if (extruder == null)
				{
					break;
				}
				@out.Add(extruder);
			}
			return @out;
		}

		// Dummy extruder class used when a printer has no extruder at all
		public static void add_printer_objects(ConfigWrapper config)
		{
			var printer = config.get_printer();
			for (var i = 0; i < 99; i++)
			{
				var section = $"extruder{i}";
				if (!config.has_section(section))
				{
					if (i != 0 && config.has_section("extruder"))
					{
						var pe = new PrinterExtruder(config.getsection("extruder"), 0);
						printer.add_object("extruder0", pe);
						continue;
					}
					break;
				}
				printer.add_object(section, new PrinterExtruder(config.getsection(section), i));
			}
		}
	}

	public class DummyExtruder : BaseExtruder
	{

		public override double set_active(double print_time, bool is_active)
		{
			return 0.0;
		}

		public override void motor_off(double move_time)
		{
		}

		public override void check_move(Move move)
		{
			//throw homing.EndstopMoveError(move.end_pos, "Extrude when no extruder present");
			throw new Exception("Extrude when no extruder present");
		}

		public override double calc_junction(Move prev_move, Move move)
		{
			return move.max_cruise_v2;
		}

		public override int lookahead(List<Move> moves, int flush_count, bool lazy)
		{
			return flush_count;
		}

		public override void move(double print_time, Move move)
		{
			throw new NotImplementedException();
		}
	}

}
