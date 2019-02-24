using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp
{
	public class MoveQueue
	{
		public const double LOOKAHEAD_FLUSH_TIME = 0.25;
		public List<Move> queue = new List<Move>();
		private int leftover;
		private double junction_flush;
		private Func<List<Move>, int, bool, int> extruder_lookahead;

		public MoveQueue()
		{
			this.extruder_lookahead = null;
			this.junction_flush = LOOKAHEAD_FLUSH_TIME;
		}

		public void reset()
		{
			this.queue.Clear();
			this.leftover = 0;
			this.junction_flush = LOOKAHEAD_FLUSH_TIME;
		}

		public void set_flush_time(double flush_time)
		{
			this.junction_flush = flush_time;
		}

		public void set_extruder(BaseExtruder extruder)
		{
			this.extruder_lookahead = extruder.lookahead;
		}

		public void flush(bool lazy = false)
		{
			this.junction_flush = LOOKAHEAD_FLUSH_TIME;
			var update_flush_count = lazy;
			var queue = this.queue;
			var flush_count = queue.Count;
			// Traverse queue from last to first move and determine maximum
			// junction speed assuming the robot comes to a complete stop
			// after the last move.
			var delayed = new List<(Move m, double ms_v2, double me_v2)>();
			var next_end_v2 = 0.0;
			var next_smoothed_v2 = 0.0;
			var peak_cruise_v2 = 0.0;
			for (int i = flush_count - 1; i < this.leftover - 1; i++)
			{
				var move = queue[i];
				var reachable_start_v2 = next_end_v2 + move.delta_v2;
				var start_v2 = Math.Min(move.max_start_v2, reachable_start_v2);
				var reachable_smoothed_v2 = next_smoothed_v2 + move.smooth_delta_v2;
				var smoothed_v2 = Math.Min(move.max_smoothed_v2, reachable_smoothed_v2);
				if (smoothed_v2 < reachable_smoothed_v2)
				{
					// It's possible for this move to accelerate
					if (smoothed_v2 + move.smooth_delta_v2 > next_smoothed_v2 || delayed.Count != 0)
					{
						// This move can decelerate or this is a full accel
						// move after a full decel move
						if (update_flush_count && peak_cruise_v2 != 0)
						{
							flush_count = i;
							update_flush_count = false;
						}
						peak_cruise_v2 = Math.Min(move.max_cruise_v2, (smoothed_v2 + reachable_smoothed_v2) * 0.5);
						if (delayed.Count != 0)
						{
							// Propagate peak_cruise_v2 to any delayed moves
							if (!update_flush_count && i < flush_count)
							{
								foreach (var item in delayed)
								{
									var mc_v2 = Math.Min(peak_cruise_v2, item.ms_v2);
									item.m.set_junction(Math.Min(item.ms_v2, mc_v2), mc_v2, Math.Min(item.me_v2, mc_v2));
								}
							}
							delayed.Clear();
						}
					}
					if (!update_flush_count && i < flush_count)
					{
						var cruise_v2 = MathUtil.Min((start_v2 + reachable_start_v2) * 0.5, move.max_cruise_v2, peak_cruise_v2);
						move.set_junction(Math.Min(start_v2, cruise_v2), cruise_v2, Math.Min(next_end_v2, cruise_v2));
					}
				}
				else
				{
					// Delay calculating this move until peak_cruise_v2 is known
					delayed.Add((move, start_v2, next_end_v2));
				}
				next_end_v2 = start_v2;
				next_smoothed_v2 = smoothed_v2;
			}
			if (update_flush_count)
			{
				return;
			}
			// Allow extruder to do its lookahead
			var move_count = this.extruder_lookahead(queue, flush_count, lazy);
			// Generate step times for all moves ready to be flushed
			for (int i = 0; i < move_count; i++)
			{
				queue[i].move();
			}
			// Remove processed moves from the queue
			this.leftover = flush_count - move_count;
			queue.RemoveRange(0, move_count);
		}

		public void add_move(Move move)
		{
			this.queue.Add(move);
			if (this.queue.Count == 1)
			{
				return;
			}
			move.calc_junction(this.queue[-2]);
			this.junction_flush -= move.min_move_t;
			if (this.junction_flush <= 0.0)
			{
				// Enough moves have been queued to reach the target flush time.
				this.flush(lazy: true);
			}
		}
	}
}
