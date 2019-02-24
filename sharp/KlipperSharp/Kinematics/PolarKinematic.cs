using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KlipperSharp.Kinematics
{
	public class PolarKinematic : BaseKinematic
	{
		private PrinterRail[] rails;
		private List<PrinterStepper> steppers = new List<PrinterStepper>();
		private double max_z_velocity;
		private double max_z_accel;
		private bool need_motor_enable;
		private Vector2d limit_z;
		private double limit_xy2;

		public PolarKinematic(ToolHead toolhead, ConfigWrapper config)
		{
			// Setup axis steppers
			var stepper_bed = new PrinterStepper(config.getsection("stepper_bed"));
			var rail_arm = new PrinterRail(config.getsection("stepper_arm"));
			var rail_z = PrinterRail.LookupMultiRail(config.getsection("stepper_z"));
			stepper_bed.setup_itersolve(KinematicType.polar, new object[] { "a" });
			rail_arm.setup_itersolve(KinematicType.polar, "r");
			rail_z.setup_itersolve(KinematicType.cartesian, "z");
			this.rails = new PrinterRail[] {
				rail_arm,
				rail_z
			};
			this.steppers.Add(stepper_bed);
			this.steppers.AddRange(rail_arm.get_steppers());
			this.steppers.AddRange(rail_z.get_steppers());

			// Setup boundary checks
			var _tup_1 = toolhead.get_max_velocity();
			var max_velocity = _tup_1.Item1;
			var max_accel = _tup_1.Item2;
			this.max_z_velocity = config.getfloat("max_z_velocity", max_velocity, above: 0.0, maxval: max_velocity);
			this.max_z_accel = config.getfloat("max_z_accel", max_accel, above: 0.0, maxval: max_accel);
			this.need_motor_enable = true;
			this.limit_z = new Vector2d(1, -1);
			this.limit_xy2 = -1.0;
			// Setup stepper max halt velocity
			var max_halt_velocity = toolhead.get_max_axis_halt();
			stepper_bed.set_max_jerk(max_halt_velocity, max_accel);
			rail_arm.set_max_jerk(max_halt_velocity, max_accel);
			rail_z.set_max_jerk(max_halt_velocity, max_accel);
		}

		public override List<PrinterStepper> get_steppers(string flags = "")
		{
			if (flags == "Z")
			{
				return this.rails[1].get_steppers();
			}
			return this.steppers;
		}

		public override Vector3d calc_position()
		{
			var bed_angle = this.steppers[0].get_commanded_position();
			var arm_pos = this.rails[0].get_commanded_position();
			var z_pos = this.rails[1].get_commanded_position();
			return new Vector3d(
				(Math.Cos(bed_angle) * arm_pos),
				(Math.Sin(bed_angle) * arm_pos),
				z_pos
			);
		}

		public override void set_position(Vector3d newpos, List<int> homing_axes)
		{
			foreach (var s in this.steppers)
			{
				s.set_position(newpos);
			}
			if (homing_axes.Contains(2))
			{
				this.limit_z = this.rails[1].get_range();
			}
			if (homing_axes.Contains(0) && homing_axes.Contains(1))
			{
				this.limit_xy2 = Math.Pow(this.rails[0].get_range().Y, 2);
			}
		}

		void _home_axis(Homing homing_state, int axis, PrinterRail rail)
		{
			// Determine movement
			var _tup_1 = rail.get_range();
			var position_min = _tup_1.X;
			var position_max = _tup_1.Y;
			var hi = rail.get_homing_info();
			var homepos = new List<double?> { null, null, null, null };
			homepos[axis] = hi.position_endstop;
			if (axis == 0)
			{
				homepos[1] = 0.0;
			}
			var forcepos = homepos.ToList();
			if (hi.positive_dir ?? false)
			{
				forcepos[axis] -= hi.position_endstop - position_min;
			}
			else
			{
				forcepos[axis] += position_max - hi.position_endstop;
			}
			// Perform homing
			double? limit_speed = null;
			if (axis == 2)
			{
				limit_speed = this.max_z_velocity;
			}
			homing_state.home_rails(new List<PrinterRail> { rail },
				(forcepos[0], forcepos[1], forcepos[2], forcepos[3]),
				(homepos[0], homepos[1], homepos[2], homepos[3]),
				limit_speed);
		}

		public override void home(Homing homing_state)
		{
			// Always home XY together
			var homing_axes = homing_state.get_axes();
			var home_xy = homing_axes.Contains(0) || homing_axes.Contains(1);
			var home_z = homing_axes.Contains(2);
			var updated_axes = new List<int>();
			if (home_xy)
			{
				updated_axes.Add(0);
				updated_axes.Add(1);
			}
			if (home_z)
			{
				updated_axes.Add(2);
			}
			homing_state.set_axes(updated_axes);
			// Do actual homing
			if (home_xy)
			{
				this._home_axis(homing_state, 0, this.rails[0]);
			}
			if (home_z)
			{
				this._home_axis(homing_state, 2, this.rails[1]);
			}
		}

		public override void motor_off(double print_time)
		{
			this.limit_z = new Vector2d(1, -1);
			this.limit_xy2 = -1.0;
			foreach (var s in this.steppers)
			{
				s.motor_enable(print_time, false);
			}
			this.need_motor_enable = true;
		}

		void _check_motor_enable(double print_time, Move move)
		{
			if (move.axes_d.X != 0 || move.axes_d.Y != 0)
			{
				this.steppers[0].motor_enable(print_time, true);
				this.rails[0].motor_enable(print_time, true);
			}
			if (move.axes_d.Z != 0)
			{
				this.rails[1].motor_enable(print_time, true);
			}
			var need_motor_enable = !this.steppers[0].is_motor_enabled();
			foreach (var rail in this.rails)
			{
				need_motor_enable |= !rail.is_motor_enabled();
			}
			this.need_motor_enable = need_motor_enable;
		}

		public override void check_move(Move move)
		{
			var end_pos = move.end_pos;
			var xy2 = Math.Pow(end_pos.X, 2) + Math.Pow(end_pos.Y, 2);
			if (xy2 > this.limit_xy2)
			{
				if (this.limit_xy2 < 0.0)
				{
					throw EndstopException.EndstopMoveError(end_pos, "Must home axis first");
				}
				throw EndstopException.EndstopMoveError(end_pos);
			}
			if (move.axes_d.Z != 0)
			{
				if (end_pos.Z < this.limit_z.X || end_pos.Z > this.limit_z.Y)
				{
					if (this.limit_z.X > this.limit_z.Y)
					{
						throw EndstopException.EndstopMoveError(end_pos, "Must home axis first");
					}
					throw EndstopException.EndstopMoveError(end_pos);
				}
				// Move with Z - update velocity and accel for slower Z axis
				var z_ratio = move.move_d / Math.Abs(move.axes_d.Z);
				move.limit_speed(this.max_z_velocity * z_ratio, this.max_z_accel * z_ratio);
			}
		}

		public override void move(double print_time, Move move)
		{
			if (this.need_motor_enable)
			{
				this._check_motor_enable(print_time, move);
			}
			var axes_d = move.axes_d;
			var cmove = move.cmove;
			if (axes_d.X != 0 || axes_d.Y != 0)
			{
				this.steppers[0].step_itersolve(cmove);
				this.rails[0].step_itersolve(cmove);
			}
			if (axes_d.Z != 0)
			{
				this.rails[1].step_itersolve(cmove);
			}
		}

	}
}
