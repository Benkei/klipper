using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp.PulseGeneration
{
	// Cartesian kinematics stepper pulse time generation
	public class KinematicCartesian
	{
		class KinematicCartesianX : KinematicBase
		{
			public override double calc_position(ref move m, double move_time)
			{
				return Itersolve.move_get_coord(ref m, move_time).x;
			}
		}
		class KinematicCartesianY : KinematicBase
		{
			public override double calc_position(ref move m, double move_time)
			{
				return Itersolve.move_get_coord(ref m, move_time).y;
			}
		}
		class KinematicCartesianZ : KinematicBase
		{
			public override double calc_position(ref move m, double move_time)
			{
				return Itersolve.move_get_coord(ref m, move_time).z;
			}
		}

		public static KinematicBase cartesian_stepper_alloc(string axis)
		{
			if (axis == "x")
				return new KinematicCartesianX();
			else if (axis == "y")
				return new KinematicCartesianY();
			else if (axis == "z")
				return new KinematicCartesianZ();

			throw new ArgumentOutOfRangeException();
		}

	}
}
