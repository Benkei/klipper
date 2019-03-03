using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp.PulseGeneration
{
	public class ItersolvePolar
	{
		class ItersolvePolarR : ItersolveBase
		{
			public override double calc_position(ref move m, double move_time)
			{
				Vector3d c = m.get_coord(move_time);
				return Math.Sqrt(c.X * c.X + c.Y * c.Y);
			}
		}
		class ItersolvePolarA : ItersolveBase
		{
			public override double calc_position(ref move m, double move_time)
			{
				Vector3d c = m.get_coord(move_time);
				// XXX - handle x==y==0
				// XXX - handle angle wrapping
				return Math.Atan2(c.Y, c.X);
			}
		}

		public static ItersolveBase polar_stepper_alloc(string type)
		{
			if (type == "r")
				return new ItersolvePolarR();
			else if (type == "a")
				return new ItersolvePolarA();

			throw new ArgumentOutOfRangeException();
		}

	}
}
