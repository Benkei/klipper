using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp.PulseGeneration
{
	public class ItersolveStepper
	{
		class IterStepper : ItersolveBase
		{
			public override double calc_position(ref move m, double move_time)
			{
				return m.start_pos.X + m.get_distance(move_time);
			}
		}

		public static ItersolveBase extruder_stepper_alloc()
		{
			return new IterStepper();
		}
	}
}
