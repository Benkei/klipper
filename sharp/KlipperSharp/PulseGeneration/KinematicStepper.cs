using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp.PulseGeneration
{
	public class KinematicStepper
	{
		class KinStepper : KinematicBase
		{
			public override double calc_position(ref move m, double move_time)
			{
				return m.start_pos.X + Itersolve.move_get_distance(ref m, move_time);
			}
		}

		public static KinematicBase extruder_stepper_alloc()
		{
			return new KinStepper();
		}

		// Populate a 'struct move' with an extruder velocity trapezoid
		public static void extruder_move_fill(move m, double print_time
								 , double accel_t, double cruise_t, double decel_t
								 , double start_pos
								 , double start_v, double cruise_v, double accel
								 , double extra_accel_v, double extra_decel_v)
		{
			// Setup velocity trapezoid
			m.print_time = print_time;
			m.move_t = accel_t + cruise_t + decel_t;
			m.accel_t = accel_t;
			m.cruise_t = cruise_t;
			m.cruise_start_d = accel_t * (0.5 * (cruise_v + start_v) + extra_accel_v);
			m.decel_start_d = m.cruise_start_d + cruise_t * cruise_v;

			// Setup for accel/cruise/decel phases
			m.cruise_v = cruise_v;
			m.accel.c1 = start_v + extra_accel_v;
			m.accel.c2 = 0.5 * accel;
			m.decel.c1 = cruise_v + extra_decel_v;
			m.decel.c2 = -m.accel.c2;

			// Setup start distance
			m.start_pos.X = start_pos;
		}

	}
}
