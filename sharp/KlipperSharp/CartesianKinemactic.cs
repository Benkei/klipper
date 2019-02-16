using KlipperSharp.MachineCodes;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;

namespace KlipperSharp
{
	public class CartesianKinemactic : BaseKinematic
	{
		public const string cmd_SET_DUAL_CARRIAGE_help = "Set which carriage is active";
		private Machine printer;
		private double max_z_velocity;
		private double max_z_accel;
		private bool need_motor_enable;
		private Vector2[] limits;
		private PrinterRail[] rails;
		private int dual_carriage_axis;
		private List<PrinterRail> dual_carriage_rails;

		public CartesianKinemactic(ToolHead toolhead, MachineConfig config)
		{
			this.printer = config.get_printer();
			// Setup axis rails
			var axis = new[] { "x", "y", "z" };
			rails = new PrinterRail[3];
			for (int i = 0; i < axis.Length; i++)
			{
				rails[i] = PrinterRail.LookupMultiRail(config.getsection("stepper_" + axis[i]));
				rails[i].setup_itersolve("cartesian_stepper_alloc", axis[i]);
			}
			// Setup boundary checks
			var _tup_2 = toolhead.get_max_velocity();
			var max_velocity = _tup_2.Item1;
			var max_accel = _tup_2.Item2;
			this.max_z_velocity = config.getfloat("max_z_velocity", max_velocity, above: 0.0, maxval: max_velocity);
			this.max_z_accel = config.getfloat("max_z_accel", max_accel, above: 0.0, maxval: max_accel);
			this.need_motor_enable = true;
			this.limits = new[] { new Vector2(1.0f, -1.0f), new Vector2(1.0f, -1.0f), new Vector2(1.0f, -1.0f) };
			// Setup stepper max halt velocity
			var max_halt_velocity = toolhead.get_max_axis_halt();
			this.rails[0].set_max_jerk(max_halt_velocity, max_accel);
			this.rails[1].set_max_jerk(max_halt_velocity, max_accel);
			this.rails[2].set_max_jerk(Math.Min(max_halt_velocity, this.max_z_velocity), max_accel);
			// Check for dual carriage support
			this.dual_carriage_axis = -1;
			this.dual_carriage_rails = new List<PrinterRail>();
			if (config.has_section("dual_carriage"))
			{
				var dc_config = config.getsection("dual_carriage");
				var dc_axis = (string)dc_config.getchoice("axis", new Dictionary<string, object> { { "x", "x" }, { "y", "y" } });
				this.dual_carriage_axis = new Dictionary<string, int> { { "x", 0 }, { "y", 1 } }[dc_axis];
				var dc_rail = PrinterRail.LookupMultiRail(dc_config);
				dc_rail.setup_itersolve("cartesian_stepper_alloc", dc_axis);
				dc_rail.set_max_jerk(max_halt_velocity, max_accel);
				this.dual_carriage_rails = new List<PrinterRail> { this.rails[this.dual_carriage_axis], dc_rail };
				this.printer.lookup_object<GCodeParser>("gcode")
					.register_command("SET_DUAL_CARRIAGE", cmd_SET_DUAL_CARRIAGE, desc: cmd_SET_DUAL_CARRIAGE_help);
			}
		}

		public override List<PrinterStepper> get_steppers(string flags = "")
		{
			if (flags == "Z")
			{
				return this.rails[2].get_steppers();
			}
			return (from rail in this.rails
					  from s in rail.get_steppers()
					  select s).ToList();
		}

		public override List<double> calc_position()
		{
			return (from rail in this.rails
					  select rail.get_commanded_position()).ToList();
		}

		public override void set_position(List<double> newpos, List<int> homing_axes)
		{
			for (int i = 0; i < this.rails.Length; i++)
			{
				var rail = this.rails[i];
				rail.set_position(newpos);
				if (homing_axes.Contains(i))
				{
					this.limits[i].X = (float)rail.get_range().Item1;
					this.limits[i].Y = (float)rail.get_range().Item2;
				}
			}
		}

		public void _home_axis(Homing homing_state, int axis, PrinterRail rail)
		{
			// Determine movement
			var _tup_1 = rail.get_range();
			var position_min = _tup_1.Item1;
			var position_max = _tup_1.Item2;
			var hi = rail.get_homing_info();
			var homepos = new List<double> { 0, 0, 0, 0 };
			homepos[axis] = hi.position_endstop;
			var forcepos = homepos.ToList();
			if ((bool)hi.positive_dir)
			{
				forcepos[axis] -= 1.5 * (hi.position_endstop - position_min);
			}
			else
			{
				forcepos[axis] += 1.5 * (position_max - hi.position_endstop);
			}
			// Perform homing
			double limit_speed = 0;
			if (axis == 2)
			{
				limit_speed = this.max_z_velocity;
			}
			homing_state.home_rails(new List<PrinterRail> { rail }, forcepos, homepos, limit_speed);
		}

		public override void home(Homing homing_state)
		{
			// Each axis is homed independently and in order
			foreach (var axis in homing_state.get_axes())
			{
				if (axis == this.dual_carriage_axis)
				{
					var dc1 = this.dual_carriage_rails[0];
					var dc2 = this.dual_carriage_rails[1];
					var altc = this.rails[axis] == dc2;
					this._activate_carriage(0);
					this._home_axis(homing_state, axis, dc1);
					this._activate_carriage(1);
					this._home_axis(homing_state, axis, dc2);
					this._activate_carriage(altc ? 1 : 0);
				}
				else
				{
					this._home_axis(homing_state, axis, this.rails[axis]);
				}
			}
		}

		public override void motor_off(double print_time)
		{
			this.limits = new[] { new Vector2(1.0f, -1.0f), new Vector2(1.0f, -1.0f), new Vector2(1.0f, -1.0f) };
			foreach (var rail in this.rails)
			{
				rail.motor_enable(print_time, false);
			}
			foreach (var rail in this.dual_carriage_rails)
			{
				rail.motor_enable(print_time, false);
			}
			this.need_motor_enable = true;
		}

		public void _check_motor_enable(double print_time, Move move)
		{
			var need_motor_enable = false;
			for (int i = 0; i < this.rails.Length; i++)
			{
				var rail = this.rails[i];
				if (move.axes_d[i] != 0)
				{
					rail.motor_enable(print_time, true);
				}
				need_motor_enable |= !rail.is_motor_enabled();
			}
			this.need_motor_enable = need_motor_enable;
		}

		public void _check_endstops(Move move)
		{
			var end_pos = move.end_pos;
			for (int i = 0; i < 3; i++)
			{
				if (move.axes_d[i] != 0 && (end_pos[i] < this.limits[i].X || end_pos[i] > this.limits[i].Y))
				{
					if (this.limits[i].X > this.limits[i].Y)
					{
						throw new Exception("Must home axis first");
						//throw homing.EndstopMoveError(end_pos, "Must home axis first");
					}
					throw new Exception();
					//throw homing.EndstopMoveError(end_pos);
				}
			}
		}

		public override void check_move(Move move)
		{
			var limits = this.limits;
			var xpos = move.end_pos[0];
			var ypos = move.end_pos[1];
			if (xpos < limits[0].X || xpos > limits[0].Y || ypos < limits[1].X || ypos > limits[1].Y)
			{
				this._check_endstops(move);
			}
			if (move.axes_d[2] == 0)
			{
				// Normal XY move - use defaults
				return;
			}
			// Move with Z - update velocity and accel for slower Z axis
			this._check_endstops(move);
			var z_ratio = move.move_d / Math.Abs(move.axes_d[2]);
			move.limit_speed(this.max_z_velocity * z_ratio, this.max_z_accel * z_ratio);
		}

		public override void move(double print_time, Move move)
		{
			if (this.need_motor_enable)
			{
				this._check_motor_enable(print_time, move);
			}
			for (int i = 0; i < rails.Length; i++)
			{
				var rail = rails[i];
				if (move.axes_d[i] != 0)
				{
					rail.step_itersolve(move.cmove);
				}
			}
		}

		// Dual carriage support
		public void _activate_carriage(int carriage)
		{
			var toolhead = this.printer.lookup_object<ToolHead>("toolhead");
			toolhead.get_last_move_time();
			var dc_rail = this.dual_carriage_rails[carriage];
			var dc_axis = this.dual_carriage_axis;
			this.rails[dc_axis] = dc_rail;
			var extruder_pos = toolhead.get_position()[3];
			toolhead.set_position(new List<double>(this.calc_position()) { extruder_pos });
			if (this.limits[dc_axis].X <= this.limits[dc_axis].Y)
			{
				this.limits[dc_axis].X = (float)dc_rail.get_range().Item1;
				this.limits[dc_axis].Y = (float)dc_rail.get_range().Item2;
			}
			this.need_motor_enable = true;
		}

		public void cmd_SET_DUAL_CARRIAGE(Dictionary<string, object> parameters)
		{
			var gcode = this.printer.lookup_object<GCodeParser>("gcode");
			var carriage = gcode.get_int("CARRIAGE", parameters, minval: 0, maxval: 1);
			this._activate_carriage(carriage);
			gcode.reset_last_position();
		}
	}
}
