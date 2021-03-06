﻿using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp.Kinematics
{
	public class KinematicNone : KinematicBase
	{
		public override List<PrinterStepper> get_steppers(string flags = "")
		{
			return new List<PrinterStepper>();
		}

		public override Vector3d calc_position()
		{
			return Vector3d.Zero;
		}

		public override void set_position(Vector3d newpos, List<int> homing_axes)
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
