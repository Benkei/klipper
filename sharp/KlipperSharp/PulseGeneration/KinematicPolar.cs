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
				coord c = Itersolve.move_get_coord(ref m, move_time);
				return Math.Sqrt(c.x * c.x + c.y * c.y);
			}
		}
		class KinematicPolarA : KinematicBase
		{
			public override double calc_position(ref move m, double move_time)
			{
				coord c = Itersolve.move_get_coord(ref m, move_time);
				// XXX - handle x==y==0
				// XXX - handle angle wrapping
				return Math.Atan2(c.y, c.x);
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
