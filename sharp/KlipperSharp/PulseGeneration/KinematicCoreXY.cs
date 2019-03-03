using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp.PulseGeneration
{
	public class KinematicCoreXY
	{
		class KinematicCoreXYPlus : KinematicBase
		{
			public override double calc_position(ref move m, double move_time)
			{
				Vector3d c = m.get_coord(move_time);
				return c.X + c.Y;
			}
		}
		class KinematicCoreXYMinus : KinematicBase
		{
			public override double calc_position(ref move m, double move_time)
			{
				Vector3d c = m.get_coord(move_time);
				return c.X - c.Y;
			}
		}

		public static KinematicBase corexy_stepper_alloc(string type)
		{
			if (type == "+")
				return new KinematicCoreXYPlus();
			else if (type == "-")
				return new KinematicCoreXYMinus();

			throw new ArgumentOutOfRangeException();
		}
	}
}
