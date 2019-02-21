﻿using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp.PulseGeneration
{
	public class KinematicDelta
	{
		class delta_stepper : KinematicBase
		{
			public double arm2, tower_x, tower_y;

			public override double calc_position(ref move m, double move_time)
			{
				coord c = Itersolve.move_get_coord(ref m, move_time);
				double dx = tower_x - c.x, dy = tower_y - c.y;
				return Math.Sqrt(arm2 - dx * dx - dy * dy) + c.z;
			}
		}

		public static KinematicBase delta_stepper_alloc(double arm2, double tower_x, double tower_y)
		{
			delta_stepper ds = new delta_stepper();
			ds.arm2 = arm2;
			ds.tower_x = tower_x;
			ds.tower_y = tower_y;
			return ds;
		}

	}
}
