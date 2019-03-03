using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp.PulseGeneration
{
	public class ItersolveCoreXY
	{
		class ItersolveCoreXYPlus : ItersolveBase
		{
			public override double calc_position(ref move m, double move_time)
			{
				Vector3d c = m.get_coord(move_time);
				return c.X + c.Y;
			}
		}
		class ItersolveCoreXYMinus : ItersolveBase
		{
			public override double calc_position(ref move m, double move_time)
			{
				Vector3d c = m.get_coord(move_time);
				return c.X - c.Y;
			}
		}

		public static ItersolveBase corexy_stepper_alloc(string type)
		{
			if (type == "+")
				return new ItersolveCoreXYPlus();
			else if (type == "-")
				return new ItersolveCoreXYMinus();

			throw new ArgumentOutOfRangeException();
		}
	}
}
