using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp.PulseGeneration
{
	public class ItersolveWinch
	{
		class winch_stepper : ItersolveBase
		{
			public Vector3d anchor;

			public override double calc_position(ref move m, double move_time)
			{
				Vector3d c = m.get_coord(move_time);
				double dx = anchor.X - c.X, dy = anchor.Y - c.Y;
				double dz = anchor.Z - c.Z;
				return Math.Sqrt(dx * dx + dy * dy + dz * dz);
			}
		}

		public static ItersolveBase winch_stepper_alloc(double anchor_x, double anchor_y, double anchor_z)
		{
			winch_stepper hs = new winch_stepper();
			hs.anchor.X = anchor_x;
			hs.anchor.Y = anchor_y;
			hs.anchor.Z = anchor_z;
			return hs;
		}

	}
}
