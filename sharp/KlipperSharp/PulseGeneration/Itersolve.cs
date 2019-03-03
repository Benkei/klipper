using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp.PulseGeneration
{
	public delegate void move_fill_callback(ref move m, double print_time, double accel_t, double cruise_t, double decel_t, double start_pos_x,
													  double start_pos_y, double start_pos_z, double axes_d_x, double axes_d_y, double axes_d_z, double start_v,
													  double cruise_v, double accel);

	public class Itersolve
	{
		/****************************************************************
		 * Kinematic moves
		 ****************************************************************/

		public static move move_alloc()
		{
			//move *m = malloc(sizeof(*m));
			//memset(m, 0, sizeof(*m));
			//return m;
			return new move();
		}

		// Populate a 'struct move' with a velocity trapezoid
		public static void move_fill(ref move m, double print_time
					 , double accel_t, double cruise_t, double decel_t
					 , double start_pos_x, double start_pos_y, double start_pos_z
					 , double axes_d_x, double axes_d_y, double axes_d_z
					 , double start_v, double cruise_v, double accel)
		{
			// Setup velocity trapezoid
			m.print_time = print_time;
			m.move_t = accel_t + cruise_t + decel_t;
			m.accel_t = accel_t;
			m.cruise_t = cruise_t;
			m.cruise_start_d = accel_t * 0.5 * (cruise_v + start_v);
			m.decel_start_d = m.cruise_start_d + cruise_t * cruise_v;

			// Setup for accel/cruise/decel phases
			m.cruise_v = cruise_v;
			m.accel.c1 = start_v;
			m.accel.c2 = 0.5 * accel;
			m.decel.c1 = cruise_v;
			m.decel.c2 = -m.accel.c2;

			// Setup for move_get_coord()
			m.start_pos.X = start_pos_x;
			m.start_pos.Y = start_pos_y;
			m.start_pos.Z = start_pos_z;
			double inv_move_d = 1.0 / Math.Sqrt(axes_d_x * axes_d_x + axes_d_y * axes_d_y + axes_d_z * axes_d_z);
			m.axes_r.X = axes_d_x * inv_move_d;
			m.axes_r.Y = axes_d_y * inv_move_d;
			m.axes_r.Z = axes_d_z * inv_move_d;
		}

		// Find the distance travel during acceleration/deceleration
		//public static double move_eval_accel(ref move_accel ma, double move_time)
		//{
		//	return (ma.c1 + ma.c2 * move_time) * move_time;
		//}

		// Return the distance moved given a time in a move
		public static double move_get_distance(ref move m, double move_time)
		{
			if (move_time < m.accel_t)
				// Acceleration phase of move
				return m.accel.eval_accel(move_time);
			move_time -= m.accel_t;
			if (move_time <= m.cruise_t)
				// Cruising phase
				return m.cruise_start_d + m.cruise_v * move_time;
			// Deceleration phase
			move_time -= m.cruise_t;
			return m.decel_start_d + m.decel.eval_accel(move_time);
		}

		// Return the XYZ coordinates given a time in a move
		public static Vector3d move_get_coord(ref move m, double move_time)
		{
			double move_dist = move_get_distance(ref m, move_time);
			return new Vector3d()
			{
				X = m.start_pos.X + m.axes_r.X * move_dist,
				Y = m.start_pos.Y + m.axes_r.Y * move_dist,
				Z = m.start_pos.Z + m.axes_r.Z * move_dist
			};
		}


		/****************************************************************
		 * Iterative solver
		 ****************************************************************/

		// Find step using "false position" method
		static timepos itersolve_find_step(ref KinematicBase sk, ref move m, ref timepos low, ref timepos high, double target)
		{
			timepos best_guess = high;
			low.position -= target;
			high.position -= target;
			if (high.position == 0)
				// The high range was a perfect guess for the next step
				return best_guess;
			int high_sign = Math.Sign(high.position);
			if (high_sign == Math.Sign(low.position))
				// The target is not in the low/high range - return low range
				return new timepos(low.time, target);
			while (true)
			{
				double guess_time = (low.time * high.position - high.time * low.position) / (high.position - low.position);
				if (Math.Abs(guess_time - best_guess.time) <= 0.000000001)
					break;
				best_guess.time = guess_time;
				best_guess.position = sk.calc_position(ref m, guess_time);
				double guess_position = best_guess.position - target;
				int guess_sign = Math.Sign(guess_position);
				if (guess_sign == high_sign)
				{
					high.time = guess_time;
					high.position = guess_position;
				}
				else
				{
					low.time = guess_time;
					low.position = guess_position;
				}
			}
			return best_guess;
		}

		// Generate step times for a stepper during a move
		public static bool itersolve_gen_steps(ref KinematicBase sk, ref move m)
		{
			stepcompress sc = sk.sc;
			double half_step = 0.5 * sk.step_dist;
			double mcu_freq = Stepcompress.stepcompress_get_mcu_freq(ref sc);
			timepos last = new timepos(0.0, sk.commanded_pos), low = last, high = last;
			double seek_time_delta = 0.000100;
			bool sdir = Stepcompress.stepcompress_get_step_dir(ref sc);
			queue_append qa = Stepcompress.queue_append_start(ref sc, m.print_time, 0.5);
			bool ret;
			while (true)
			{
				// Determine if next step is in forward or reverse direction
				double dist = high.position - last.position;
				if (Math.Abs(dist) < half_step)
				{
					if (high.time >= m.move_t)
						// At end of move
						break;
					NewMethod(sk, ref m, last, ref low, ref high, ref seek_time_delta);
					continue;
				}
				bool next_sdir = dist > 0.0;
				if (next_sdir != sdir)
				{
					// Direction change
					if (Math.Abs(dist) < half_step + .000000001)
					{
						// Only change direction if going past midway point
						if (high.time >= m.move_t)
							// At end of move
							break;
						NewMethod(sk, ref m, last, ref low, ref high, ref seek_time_delta);
						continue;
					}
					if (last.time >= low.time && high.time > last.time)
					{
						// Must seek new low range to avoid re-finding previous time
						high.time = (last.time + high.time) * .5;
						high.position = sk.calc_position(ref m, high.time);
						continue;
					}
					ret = Stepcompress.queue_append_set_next_step_dir(ref qa, next_sdir);
					if (ret)
						return ret;
					sdir = next_sdir;
				}
				// Find step
				double target = last.position + (sdir ? half_step : -half_step);
				timepos next = itersolve_find_step(ref sk, ref m, ref low, ref high, target);
				// Add step at given time
				ret = Stepcompress.queue_append(ref qa, next.time * mcu_freq);
				if (ret)
					return ret;
				seek_time_delta = next.time - last.time;
				if (seek_time_delta < 0.000000001)
					seek_time_delta = 0.000000001;
				last.position = target + (sdir ? half_step : -half_step);
				last.time = next.time;
				low = next;
				if (last.time >= high.time)
				{
					// The high range is no longer valid - recalculate it
					if (high.time >= m.move_t)
						// At end of move
						break;
					NewMethod(sk, ref m, last, ref low, ref high, ref seek_time_delta);
					continue;
				}
			}
			Stepcompress.queue_append_finish(ref qa);
			sk.commanded_pos = last.position;
			return false;
		}

		private static void NewMethod(KinematicBase sk,
			ref move m, timepos last,
			ref timepos low, ref timepos high, ref double seek_time_delta)
		{
			// Need to increase next step search range
			low = high;
			high.time = last.time + seek_time_delta;
			seek_time_delta += seek_time_delta;
			if (high.time > m.move_t)
				high.time = m.move_t;
			high.position = sk.calc_position(ref m, high.time);
		}

		public static void itersolve_set_stepcompress(ref KinematicBase sk, ref stepcompress sc, double step_dist)
		{
			sk.sc = sc;
			sk.step_dist = step_dist;
		}

		public static double itersolve_calc_position_from_coord(ref KinematicBase sk, double x, double y, double z)
		{
			move m = new move();
			move_fill(ref m, 0.0, 0.0, 1.0, 0.0, x, y, z, 0.0, 1.0, 0.0, 0.0, 1.0, 0.0);
			return sk.calc_position(ref m, 0.0);
		}

		public static void itersolve_set_commanded_pos(ref KinematicBase sk, double pos)
		{
			sk.commanded_pos = pos;
		}

		public static double itersolve_get_commanded_pos(ref KinematicBase sk)
		{
			return sk.commanded_pos;
		}

	}

	//public struct coord
	//{
	//	public double x;
	//	public double y;
	//	public double z;
	//}

	public struct move_accel
	{
		public double c1;
		public double c2;

		// Find the distance travel during acceleration/deceleration
		public double eval_accel(double move_time)
		{
			return (c1 + c2 * move_time) * move_time;
		}
	}

	public ref struct timepos
	{
		public double time, position;

		public timepos(double time, double position)
		{
			this.time = time;
			this.position = position;
		}
	}

	public class move
	{
		public double print_time, move_t;
		public double accel_t, cruise_t;
		public double cruise_start_d, decel_start_d;
		public double cruise_v;
		public move_accel accel, decel;
		public Vector3d start_pos, axes_r;
	}


}
