using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp.PulseGeneration
{
	// Cartesian kinematics stepper pulse time generation
	public class ItersolveCartesian
	{
		class ItersolveCartesianX : ItersolveBase
		{
			public override double calc_position(ref move m, double move_time)
			{
				return m.get_coord(move_time).X;
			}
		}
		class ItersolveCartesianY : ItersolveBase
		{
			public override double calc_position(ref move m, double move_time)
			{
				return m.get_coord(move_time).Y;
			}
		}
		class ItersolveCartesianZ : ItersolveBase
		{
			public override double calc_position(ref move m, double move_time)
			{
				return m.get_coord(move_time).Z;
			}
		}

		public static ItersolveBase cartesian_stepper_alloc(string axis)
		{
			if (axis == "x")
				return new ItersolveCartesianX();
			else if (axis == "y")
				return new ItersolveCartesianY();
			else if (axis == "z")
				return new ItersolveCartesianZ();

			throw new ArgumentOutOfRangeException();
		}

	}
}
