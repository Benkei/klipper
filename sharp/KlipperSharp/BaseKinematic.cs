using KlipperSharp.MachineCodes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KlipperSharp
{
	public class KinematicFactory
	{
		public static BaseKinematic load_kinematics(string type, ToolHead toolhead, MachineConfig config)
		{
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

		public virtual List<double> calc_position()
		{
			return new List<double> { 0, 0, 0 };
		}

		public virtual void set_position(List<double> newpos, List<int> homing_axes)
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
