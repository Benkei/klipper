using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace KlipperSharp.Kinematics
{
	class WinchKinematic : BaseKinematic
	{
		private List<PrinterStepper> steppers;
		private List<Vector3> anchors;
		private bool need_motor_enable;

		public WinchKinematic(ToolHead toolhead, ConfigWrapper config)
		{
			// Setup steppers at each anchor
			this.steppers = new List<PrinterStepper>();
			this.anchors = new List<Vector3>();
			for (int i = 0; i < 26; i++)
			{
				var name = "stepper_" + (char)('a' + i);
				if (i >= 3 && !config.has_section(name))
				{
					break;
				}
				var stepper_config = config.getsection(name);
				var s = new PrinterStepper(stepper_config);
				this.steppers.Add(s);
				var anchor = new Vector3(
					(float)stepper_config.getfloat("anchor_x"),
					(float)stepper_config.getfloat("anchor_y"),
					(float)stepper_config.getfloat("anchor_z"));
				this.anchors.Add(anchor);
				s.setup_itersolve(KinematicType.winch, new object[] { anchor.X, anchor.Y, anchor.Z });
			}
			// Setup stepper max halt velocity
			var _tup_1 = toolhead.get_max_velocity();
			var max_velocity = _tup_1.Item1;
			var max_accel = _tup_1.Item2;
			var max_halt_velocity = toolhead.get_max_axis_halt();
			foreach (var s in this.steppers)
			{
				s.set_max_jerk(max_halt_velocity, max_accel);
			}
			// Setup boundary checks
			this.need_motor_enable = true;
			this.set_position(Vector3.Zero, null);
		}

		public override List<PrinterStepper> get_steppers(string flags = "")
		{
			return this.steppers;
		}

		public override Vector3 calc_position()
		{
			// Use only first three steppers to calculate cartesian position
			var lenX = steppers[0].get_commanded_position();
			var lenY = steppers[1].get_commanded_position();
			var lenZ = steppers[2].get_commanded_position();
			lenX *= lenX;
			lenY *= lenY;
			lenZ *= lenZ;
			return MathUtil.trilateration(anchors[0], anchors[1], anchors[2], lenX, lenY, lenZ);
		}

		public override void set_position(Vector3 newpos, List<int> homing_axes)
		{
			foreach (var s in this.steppers)
			{
				s.set_position(newpos);
			}
		}

		public override void home(Homing homing_state)
		{
			// XXX - homing not implemented
			homing_state.set_axes(new List<int> { 0, 1, 2 });
			homing_state.set_homed_position((0.0, 0.0, 0.0, null));
		}

		public override void motor_off(double print_time)
		{
			foreach (var s in this.steppers)
			{
				s.motor_enable(print_time, false);
			}
			this.need_motor_enable = true;
		}

		void _check_motor_enable(double print_time)
		{
			foreach (var s in this.steppers)
			{
				s.motor_enable(print_time, true);
			}
			this.need_motor_enable = false;
		}

		public override void check_move(Move move)
		{
			// XXX - boundary checks and speed limits not implemented
		}

		public override void move(double print_time, Move move)
		{
			if (this.need_motor_enable)
			{
				this._check_motor_enable(print_time);
			}
			foreach (var s in this.steppers)
			{
				s.step_itersolve(move.cmove);
			}
		}

	}
}
