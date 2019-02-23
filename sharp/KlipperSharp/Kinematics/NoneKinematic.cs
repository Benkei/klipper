using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace KlipperSharp.Kinematics
{
	public class NoneKinematic : BaseKinematic
	{
		public override List<PrinterStepper> get_steppers(string flags = "")
		{
			return new List<PrinterStepper>();
		}

		public override Vector3 calc_position()
		{
			return Vector3.Zero;
		}

		public override void set_position(Vector3 newpos, List<int> homing_axes)
		{
		}

		public override void home(Homing homing_state)
		{
		}

		public override void motor_off(double print_time)
		{
		}

		public override void check_move(Move move)
		{
		}

		public override void move(double print_time, Move move)
		{
		}
	}
}
