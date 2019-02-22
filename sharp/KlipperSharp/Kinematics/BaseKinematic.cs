using KlipperSharp.MachineCodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace KlipperSharp.Kinematics
{
	public class KinematicFactory
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

	public class BaseKinematic
	{
		//public BaseKinematic(ToolHead toolhead, MachineConfig config)
		//{
		//}

		public virtual List<PrinterStepper> get_steppers(string flags = "")
		{
			return new List<PrinterStepper>();
		}

		public virtual Vector3 calc_position()
		{
			return Vector3.Zero;
		}

		public virtual void set_position(Vector3 newpos, List<int> homing_axes)
		{
		}

		public virtual void home(Homing homing_state)
		{
		}

		public virtual void motor_off(double print_time)
		{
		}

		public virtual void check_move(Move move)
		{
		}

		public virtual void move(double print_time, Move move)
		{
		}
	}
}
