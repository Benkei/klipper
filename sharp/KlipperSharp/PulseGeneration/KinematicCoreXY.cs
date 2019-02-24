﻿using System;
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
				coord c = Itersolve.move_get_coord(ref m, move_time);
				return c.x + c.y;
			}
		}
		class KinematicCoreXYMinus : KinematicBase
		{
			public override double calc_position(ref move m, double move_time)
			{
				coord c = Itersolve.move_get_coord(ref m, move_time);
				return c.x - c.y;
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