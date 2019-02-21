using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp.PulseGeneration
{
	public class KinematicWinch
	{
		class winch_stepper : KinematicBase
		{
			public coord anchor;

			public override double calc_position(ref move m, double move_time)
			{
				coord c = Itersolve.move_get_coord(ref m, move_time);
				double dx = anchor.x - c.x, dy = anchor.y - c.y;
				double dz = anchor.z - c.z;
				return Math.Sqrt(dx * dx + dy * dy + dz * dz);
			}
		}

		public static KinematicBase winch_stepper_alloc(double anchor_x, double anchor_y, double anchor_z)
		{
			winch_stepper hs = new winch_stepper();
			hs.anchor.x = anchor_x;
			hs.anchor.y = anchor_y;
			hs.anchor.z = anchor_z;
			return hs;
		}

	}
}
