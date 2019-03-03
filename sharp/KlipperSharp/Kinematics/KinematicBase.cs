using KlipperSharp.MachineCodes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KlipperSharp.Kinematics
{
	public abstract class KinematicBase
	{
		public abstract List<PrinterStepper> get_steppers(string flags = "");

		public abstract Vector3d calc_position();

		public abstract void set_position(Vector3d newpos, List<int> homing_axes);

		public abstract void home(Homing homing_state);

		public abstract void motor_off(double print_time);

		public abstract void check_move(Move move);

		public abstract void move(double print_time, Move move);
	}
}
