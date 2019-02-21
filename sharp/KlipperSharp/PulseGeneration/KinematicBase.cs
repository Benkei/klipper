using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp.PulseGeneration
{
	public abstract class KinematicBase
	{
		public double step_dist, commanded_pos;
		public stepcompress sc;
		//public sk_callback calc_position;
		public abstract double calc_position(ref move m, double move_time);
	}

	//typedef double (* sk_callback) (struct stepper_kinematics * sk, struct move * m, double move_time);
	//public delegate double sk_callback(ref KinematicBase sk, ref move m, double move_time);
}
