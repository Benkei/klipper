using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp.PulseGeneration
{
	public class KinematicPolar
	{
		class KinematicPolarR : KinematicBase
		{
			public override double calc_position(ref move m, double move_time)
			{
				Vector3d c = m.get_coord(move_time);
				return Math.Sqrt(c.X * c.X + c.Y * c.Y);
			}
		}
		class KinematicPolarA : KinematicBase
		{
			public override double calc_position(ref move m, double move_time)
			{
				Vector3d c = m.get_coord(move_time);
				// XXX - handle x==y==0
				// XXX - handle angle wrapping
				return Math.Atan2(c.Y, c.X);
			}
		}

		public static KinematicBase polar_stepper_alloc(string type)
		{
			if (type == "r")
				return new KinematicPolarR();
			else if (type == "a")
				return new KinematicPolarA();

			throw new ArgumentOutOfRangeException();
		}

	}
}
