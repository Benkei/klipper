using KlipperSharp.MachineCodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace KlipperSharp.Kinematics
{
	public static class KinematicFactory
	{
		public static BaseKinematic load_kinematics(KinematicType type, ToolHead toolhead, ConfigWrapper config)
		{
			switch (type)
			{
				case KinematicType.none: break;
				case KinematicType.cartesian: return new CartesianKinemactic(toolhead, config);
				case KinematicType.corexy: break;
				case KinematicType.delta: break;
				case KinematicType.extruder: break;
				case KinematicType.polar: break;
				case KinematicType.winch: break;
			}
			//return DeltaKinematics(toolhead, config);
			throw new NotImplementedException();
		}
	}

	public abstract class BaseKinematic
	{
		public abstract List<PrinterStepper> get_steppers(string flags = "");

		public abstract Vector3 calc_position();

		public abstract void set_position(Vector3 newpos, List<int> homing_axes);

		public abstract void home(Homing homing_state);

		public abstract void motor_off(double print_time);

		public abstract void check_move(Move move);

		public abstract void move(double print_time, Move move);
	}
}
