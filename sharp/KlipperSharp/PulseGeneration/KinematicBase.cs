using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace KlipperSharp.PulseGeneration
{
	public abstract class KinematicBase
	{
		public double step_dist, commanded_pos;
		public stepcompress sc;

		public abstract double calc_position(ref move m, double move_time);

		public void itersolve_set_stepcompress(ref stepcompress sc, double step_dist)
		{
			this.sc = sc;
			this.step_dist = step_dist;
		}

		public double itersolve_calc_position_from_coord(double x, double y, double z)
		{
			move m = new move();
			m.fill(0.0, 0.0, 1.0, 0.0, x, y, z, 0.0, 1.0, 0.0, 0.0, 1.0, 0.0);
			return calc_position(ref m, 0.0);
		}

		public void itersolve_set_commanded_pos(double pos)
		{
			commanded_pos = pos;
		}

		public double itersolve_get_commanded_pos()
		{
			return commanded_pos;
		}

		// Find step using "false position" method
		private timepos itersolve_find_step(ref move m, ref timepos low, ref timepos high, double target)
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
				best_guess.position = calc_position(ref m, guess_time);
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
		public bool itersolve_gen_steps(ref move m)
		{
			stepcompress sc = this.sc;
			double half_step = 0.5 * step_dist;
			double mcu_freq = sc.get_mcu_freq();
			timepos last = new timepos(0.0, commanded_pos), low = last, high = last;
			double seek_time_delta = 0.000100;
			bool sdir = sc.get_step_dir();
			queue_append qa = sc.queue_append_start(m.print_time, 0.5);
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
					CalcPosition(ref m, last, ref low, ref high, ref seek_time_delta);
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
						CalcPosition(ref m, last, ref low, ref high, ref seek_time_delta);
						continue;
					}
					if (last.time >= low.time && high.time > last.time)
					{
						// Must seek new low range to avoid re-finding previous time
						high.time = (last.time + high.time) * .5;
						high.position = calc_position(ref m, high.time);
						continue;
					}
					ret = qa.append_set_next_step_dir(next_sdir);
					if (ret)
						return ret;
					sdir = next_sdir;
				}
				// Find step
				double target = last.position + (sdir ? half_step : -half_step);
				timepos next = itersolve_find_step(ref m, ref low, ref high, target);
				// Add step at given time
				ret = qa.append(next.time * mcu_freq);
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
					CalcPosition(ref m, last, ref low, ref high, ref seek_time_delta);
					continue;
				}
			}
			qa.queue_append_finish();
			commanded_pos = last.position;
			return false;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void CalcPosition(ref move m, timepos last, ref timepos low, ref timepos high, ref double seek_time_delta)
		{
			// Need to increase next step search range
			low = high;
			high.time = last.time + seek_time_delta;
			seek_time_delta += seek_time_delta;
			if (high.time > m.move_t)
				high.time = m.move_t;
			high.position = calc_position(ref m, high.time);
		}

	}

	//typedef double (* sk_callback) (struct stepper_kinematics * sk, struct move * m, double move_time);
	//public delegate double sk_callback(ref KinematicBase sk, ref move m, double move_time);
}
