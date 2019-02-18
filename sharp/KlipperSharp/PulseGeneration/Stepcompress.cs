using System;
using System.Collections.Generic;
using System.Text;

// Stepper pulse schedule compression
//
// Copyright (C) 2016-2018  Kevin O'Connor <kevin@koconnor.net>
//
// This file may be distributed under the terms of the GNU GPLv3 license.
//
// The goal of this code is to take a series of scheduled stepper
// pulse times and compress them into a handful of commands that can
// be efficiently transmitted and executed on a microcontroller (mcu).
// The mcu accepts step pulse commands that take interval, count, and
// add parameters such that 'count' pulses occur, with each step event
// calculating the next step event time using:
//  next_wake_time = last_wake_time + interval; interval += add
// This code is written in C (instead of python) for processing
// efficiency - the repetitive integer math is vastly faster in C.

namespace KlipperSharp
{
	public unsafe class Stepcompress
	{
		const bool CHECK_LINES = true;
		const int QUEUE_START_SIZE = 1024;
		const int ERROR_RET = -1;

		// The maximum add delta between two valid quadratic sequences of the
		// form "add*count*(count-1)/2 + interval*count" is "(6 + 4*sqrt(2)) *
		// maxerror / (count*count)".  The "6 + 4*sqrt(2)" is 11.65685, but
		// using 11 works well in practice.
		const int QUADRATIC_DEV = 11;


		/****************************************************************
		 * Step compression
		 ****************************************************************/

		static int idiv_up(int n, int d)
		{
			return (n >= 0) ? DIV_ROUND_UP(n, d) : (n / d);
		}

		static int idiv_down(int n, int d)
		{
			return (n >= 0) ? (n / d) : (n - d + 1) / d;
		}

		static int DIV_ROUND_UP(int n, int d) => (n + d - 1) / d;

		// Given a requested step time, return the minimum and maximum
		// acceptable times
		static points minmax_point(ref stepcompress sc, uint* pos)
		{
			uint lsc = (uint)sc.last_step_clock, point = *pos - lsc;
			uint prevpoint = pos > sc.queue_pos ? *(pos - 1) - lsc : 0;
			uint max_error = (point - prevpoint) / 2;
			if (max_error > sc.max_error)
				max_error = sc.max_error;
			return new points { minp = (int)(point - max_error), maxp = (int)point };
		}

		// Find a 'step_move' that covers a series of step times
		static step_move compress_bisect_add(ref stepcompress sc)
		{
			uint* qlast = sc.queue_next;
			if (qlast > sc.queue_pos + 65535)
				qlast = sc.queue_pos + 65535;
			points point = minmax_point(ref sc, sc.queue_pos);
			int outer_mininterval = point.minp, outer_maxinterval = point.maxp;
			int add = 0, minadd = -0x8000, maxadd = 0x7fff;
			int bestinterval = 0, bestcount = 1, bestadd = 1, bestreach = int.MinValue;
			int zerointerval = 0, zerocount = 0;

			int count, addfactor, nextaddfactor, c;
			while (true)
			{
				// Find longest valid sequence with the given 'add'
				points nextpoint;
				int nextmininterval = outer_mininterval;
				int nextmaxinterval = outer_maxinterval, interval = nextmaxinterval;
				int nextcount = 1;
				for (; ; )
				{
					nextcount++;
					if (&sc.queue_pos[nextcount - 1] >= qlast)
					{
						count = nextcount - 1;
						return new step_move() { interval = (uint)interval, count = (ushort)count, add = (short)add };
					}
					nextpoint = minmax_point(ref sc, sc.queue_pos + nextcount - 1);
					nextaddfactor = nextcount * (nextcount - 1) / 2;
					c = add * nextaddfactor;
					if (nextmininterval * nextcount < nextpoint.minp - c)
						nextmininterval = DIV_ROUND_UP(nextpoint.minp - c, nextcount);
					if (nextmaxinterval * nextcount > nextpoint.maxp - c)
						nextmaxinterval = (nextpoint.maxp - c) / nextcount;
					if (nextmininterval > nextmaxinterval)
						break;
					interval = nextmaxinterval;
				}

				// Check if this is the best sequence found so far
				count = nextcount - 1;
				addfactor = count * (count - 1) / 2;
				int reach = add * addfactor + interval * count;
				if (reach > bestreach
					 || (reach == bestreach && interval > bestinterval))
				{
					bestinterval = interval;
					bestcount = count;
					bestadd = add;
					bestreach = reach;
					if (add == 0)
					{
						zerointerval = interval;
						zerocount = count;
					}
					if (count > 0x200)
						// No 'add' will improve sequence; avoid integer overflow
						break;
				}

				// Check if a greater or lesser add could extend the sequence
				nextaddfactor = nextcount * (nextcount - 1) / 2;
				int nextreach = add * nextaddfactor + interval * nextcount;
				if (nextreach < nextpoint.minp)
				{
					minadd = add + 1;
					outer_maxinterval = nextmaxinterval;
				}
				else
				{
					maxadd = add - 1;
					outer_mininterval = nextmininterval;
				}

				// The maximum valid deviation between two quadratic sequences
				// can be calculated and used to further limit the add range.
				if (count > 1)
				{
					int errdelta = (int)sc.max_error * QUADRATIC_DEV / (count * count);
					if (minadd < add - errdelta)
						minadd = add - errdelta;
					if (maxadd > add + errdelta)
						maxadd = add + errdelta;
				}

				// See if next point would further limit the add range
				c = outer_maxinterval * nextcount;
				if (minadd * nextaddfactor < nextpoint.minp - c)
					minadd = idiv_up(nextpoint.minp - c, nextaddfactor);
				c = outer_mininterval * nextcount;
				if (maxadd * nextaddfactor > nextpoint.maxp - c)
					maxadd = idiv_down(nextpoint.maxp - c, nextaddfactor);

				// Bisect valid add range and try again with new 'add'
				if (minadd > maxadd)
					break;
				add = maxadd - (maxadd - minadd) / 4;
			}
			if (zerocount + zerocount / 16 >= bestcount)
				// Prefer add=0 if it's similar to the best found sequence
				return new step_move() { interval = (uint)zerointerval, count = (ushort)zerocount, add = 0 };
			return new step_move() { interval = (uint)bestinterval, count = (ushort)bestcount, add = (short)bestadd };
		}


		/****************************************************************
		 * Step compress checking
		 ****************************************************************/

		// Verify that a given 'step_move' matches the actual step times
		static int check_line(ref stepcompress sc, ref step_move move)
		{
			if (!CHECK_LINES) return 0;
			if (move.count == 0 || (move.interval == 0 && move.add == 0 && move.count > 1) || move.interval >= 0x80000000)
			{
				//errorf("stepcompress o=%d i=%d c=%d a=%d: Invalid sequence"
				//		 , sc->oid, move.interval, move.count, move.add);
				return ERROR_RET;
			}
			uint interval = move.interval, p = 0;
			ushort i;
			for (i = 0; i < move.count; i++)
			{
				points point = minmax_point(ref sc, sc.queue_pos + i);
				p += interval;
				if (p < point.minp || p > point.maxp)
				{
					//errorf("stepcompress o=%d i=%d c=%d a=%d: Point %d: %d not in %d:%d"
					//		 , sc.oid, move.interval, move.count, move.add
					//		 , i + 1, p, point.minp, point.maxp);
					return ERROR_RET;
				}
				if (interval >= 0x80000000)
				{
					//errorf("stepcompress o=%d i=%d c=%d a=%d:"+
					//		 " Point %d: interval overflow %d"
					//		 , sc.oid, move.interval, move.count, move.add
					//		 , i + 1, interval);
					return ERROR_RET;
				}
				interval += (ushort)move.add;
			}
			return 0;
		}

		/****************************************************************
		 * Step compress interface
		 ****************************************************************/

		// Allocate a new 'stepcompress' object
		public static stepcompress stepcompress_alloc(uint oid)
		{
			stepcompress sc = new stepcompress();
			sc.msg_queue = new Queue<SerialQueue.RawMessage>();
			sc.oid = oid;
			sc.sdir = -1;
			return sc;
		}

		// Fill message id information
		public static void stepcompress_fill(ref stepcompress sc, uint max_error
								, uint invert_sdir, uint queue_step_msgid
								, uint set_next_step_dir_msgid)
		{
			sc.max_error = max_error;
			sc.invert_sdir = invert_sdir == 0 ? 1 : 0;
			sc.queue_step_msgid = queue_step_msgid;
			sc.set_next_step_dir_msgid = set_next_step_dir_msgid;
		}

		// Free memory associated with a 'stepcompress' object
		void stepcompress_free(ref stepcompress sc)
		{
			//if (!sc)
			//	return;
			free(sc.queue);
			//message_queue_free(&sc->msg_queue);
			//free(sc);
		}

		// Convert previously scheduled steps into commands for the mcu
		static int stepcompress_flush(ref stepcompress sc, ulong move_clock)
		{
			if (sc.queue_pos >= sc.queue_next)
				return 0;
			while (sc.last_step_clock < move_clock)
			{
				step_move move = compress_bisect_add(ref sc);
				int ret = check_line(ref sc, ref move);
				if (ret != 0)
					return ret;

				uint* msg = stackalloc uint[] { sc.queue_step_msgid, sc.oid, move.interval, move.count, (uint)move.add };
				var qm = SerialQueue.RawMessage.CreateAndEncode(new ReadOnlySpan<uint>(msg, 5));
				qm.min_clock = qm.req_clock = sc.last_step_clock;
				int addfactor = move.count * (move.count - 1) / 2;
				ulong ticks = (ulong)(move.add * addfactor + move.interval * move.count);
				sc.last_step_clock += ticks;
				if (sc.homing_clock != 0)
					// When homing, all steps should be sent prior to homing_clock
					qm.min_clock = qm.req_clock = sc.homing_clock;
				sc.msg_queue.Enqueue(qm);

				if (sc.queue_pos + move.count >= sc.queue_next)
				{
					sc.queue_pos = sc.queue_next = sc.queue;
					break;
				}
				sc.queue_pos += move.count;
			}
			return 0;
		}

		// Generate a queue_step for a step far in the future from the last step
		static int stepcompress_flush_far(ref stepcompress sc, ulong abs_step_clock)
		{
			uint* msg = stackalloc uint[] { sc.queue_step_msgid, sc.oid, (uint)(abs_step_clock - sc.last_step_clock), 1, 0 };
			var qm = SerialQueue.RawMessage.CreateAndEncode(new ReadOnlySpan<uint>(msg, 5));
			qm.min_clock = sc.last_step_clock;
			sc.last_step_clock = qm.req_clock = abs_step_clock;
			if (sc.homing_clock != 0)
				// When homing, all steps should be sent prior to homing_clock
				qm.min_clock = qm.req_clock = sc.homing_clock;
			sc.msg_queue.Enqueue(qm);
			return 0;
		}

		// Send the set_next_step_dir command
		static int set_next_step_dir(ref stepcompress sc, int sdir)
		{
			if (sc.sdir == sdir)
				return 0;
			sc.sdir = sdir;
			int ret = stepcompress_flush(ref sc, ulong.MaxValue);
			if (ret != 0)
				return ret;
			uint* msg = stackalloc uint[] { sc.set_next_step_dir_msgid, sc.oid, (uint)sdir ^ (uint)sc.invert_sdir };
			var qm = SerialQueue.RawMessage.CreateAndEncode(new ReadOnlySpan<uint>(msg, 3));
			qm.req_clock = sc.homing_clock != 0 ? 0 : sc.last_step_clock;
			sc.msg_queue.Enqueue(qm);
			return 0;
		}

		// Reset the internal state of the stepcompress object
		public static int stepcompress_reset(ref stepcompress sc, ulong last_step_clock)
		{
			int ret = stepcompress_flush(ref sc, ulong.MaxValue);
			if (ret != 0)
				return ret;
			sc.last_step_clock = last_step_clock;
			sc.sdir = -1;
			return 0;
		}

		// Indicate the stepper is in homing mode (or done homing if zero)
		public static int stepcompress_set_homing(ref stepcompress sc, ulong homing_clock)
		{
			int ret = stepcompress_flush(ref sc, ulong.MaxValue);
			if (ret != 0)
				return ret;
			sc.homing_clock = homing_clock;
			return 0;
		}

		// Queue an mcu command to go out in order with stepper commands
		public static int stepcompress_queue_msg(ref stepcompress sc, uint* data, int len)
		{
			int ret = stepcompress_flush(ref sc, ulong.MaxValue);
			if (ret != 0)
				return ret;

			var qm = SerialQueue.RawMessage.CreateAndEncode(new ReadOnlySpan<uint>(data, len));
			qm.req_clock = sc.homing_clock != 0 ? 0 : sc.last_step_clock;
			sc.msg_queue.Enqueue(qm);
			return 0;
		}

		// Set the conversion rate of 'print_time' to mcu clock
		static void stepcompress_set_time(ref stepcompress sc, double time_offset, double mcu_freq)
		{
			sc.mcu_time_offset = time_offset;
			sc.mcu_freq = mcu_freq;
		}

		public static double stepcompress_get_mcu_freq(ref stepcompress sc)
		{
			return sc.mcu_freq;
		}

		uint stepcompress_get_oid(ref stepcompress sc)
		{
			return sc.oid;
		}

		public static bool stepcompress_get_step_dir(ref stepcompress sc)
		{
			return sc.sdir > 0;
		}


		/****************************************************************
		 * Queue management
		 ****************************************************************/

		// Maximium clock delta between messages in the queue
		const long CLOCK_DIFF_MAX = (3 << 28);

		// Create a cursor for inserting clock times into the queue
		public static queue_append queue_append_start(ref stepcompress sc, double print_time, double adjust)
		{
			double print_clock = (print_time - sc.mcu_time_offset) * sc.mcu_freq;
			return new queue_append()
			{
				sc = sc,
				qnext = sc.queue_next,
				qend = sc.queue_end,
				last_step_clock_32 = (uint)sc.last_step_clock,
				clock_offset = (print_clock - (double)sc.last_step_clock) + adjust
			};
		}

		// Finalize a cursor created with queue_append_start()
		public static void queue_append_finish(ref queue_append qa)
		{
			qa.sc.queue_next = qa.qnext;
		}

		// Slow path for queue_append()
		static int queue_append_slow(ref stepcompress sc, double rel_sc)
		{
			ulong abs_step_clock = (ulong)(rel_sc + sc.last_step_clock);
			if (abs_step_clock >= sc.last_step_clock + CLOCK_DIFF_MAX)
			{
				// Avoid integer overflow on steps far in the future
				int ret = stepcompress_flush(ref sc, abs_step_clock - CLOCK_DIFF_MAX + 1);
				if (ret != 0)
					return ret;

				if (abs_step_clock >= sc.last_step_clock + CLOCK_DIFF_MAX)
					return stepcompress_flush_far(ref sc, abs_step_clock);
			}

			if (sc.queue_next - sc.queue_pos > 65535 + 2000)
			{
				// No point in keeping more than 64K steps in memory
				uint flush = *(sc.queue_next - 65535) - (uint)sc.last_step_clock;
				int ret = stepcompress_flush(ref sc, sc.last_step_clock + flush);
				if (ret != 0)
					return ret;
			}

			if (sc.queue_next >= sc.queue_end)
			{
				// Make room in the queue
				long in_use = sc.queue_next - sc.queue_pos;
				if (sc.queue_pos > sc.queue)
				{
					// Shuffle the internal queue to avoid having to allocate more ram
					memmove(sc.queue, sc.queue_pos, in_use * sizeof(uint));
				}
				else
				{
					// Expand the internal queue of step times
					long alloc = sc.queue_end - sc.queue;
					if (alloc == 0)
						alloc = QUEUE_START_SIZE;
					while (in_use >= alloc)
						alloc *= 2;
					sc.queue = (uint*)realloc(sc.queue, alloc * sizeof(uint));
					sc.queue_end = sc.queue + alloc;
				}
				sc.queue_pos = sc.queue;
				sc.queue_next = sc.queue + in_use;
			}

			*sc.queue_next++ = (uint)abs_step_clock;
			return 0;
		}

		static void* memmove(void* destination, void* source, long num)
		{
			Buffer.MemoryCopy(source, destination, num, num);
			return destination;
		}

		static void* realloc(void* ptr, long size)
		{
			if ((IntPtr)ptr == IntPtr.Zero)
				ptr = (void*)System.Runtime.InteropServices.Marshal.AllocHGlobal((IntPtr)size);
			else
				ptr = (void*)System.Runtime.InteropServices.Marshal.ReAllocHGlobal((IntPtr)ptr, (IntPtr)size);
			return ptr;
		}

		static void free(void* ptr)
		{
			System.Runtime.InteropServices.Marshal.FreeHGlobal((IntPtr)ptr);
		}


		// Add a clock time to the queue (flushing the queue if needed)
		public static bool queue_append(ref queue_append qa, double step_clock)
		{
			double rel_sc = step_clock + qa.clock_offset;
			if (!(qa.qnext >= qa.qend || rel_sc >= (double)CLOCK_DIFF_MAX))
			{
				*qa.qnext++ = qa.last_step_clock_32 + (uint)rel_sc;
				return false;
			}
			// Call queue_append_slow() to handle queue expansion and integer overflow
			stepcompress sc = qa.sc;
			ulong old_last_step_clock = sc.last_step_clock;
			sc.queue_next = qa.qnext;
			int ret = queue_append_slow(ref sc, rel_sc);
			if (ret != 0)
				return ret > 0;
			qa.qnext = sc.queue_next;
			qa.qend = sc.queue_end;
			qa.last_step_clock_32 = (uint)sc.last_step_clock;
			qa.clock_offset -= sc.last_step_clock - old_last_step_clock;
			return false;
		}

		public static bool queue_append_set_next_step_dir(ref queue_append qa, bool sdir)
		{
			stepcompress sc = qa.sc;
			ulong old_last_step_clock = sc.last_step_clock;
			sc.queue_next = qa.qnext;
			int ret = set_next_step_dir(ref sc, sdir ? 1 : 0);
			if (ret != 0)
				return ret > 0;
			qa.qnext = sc.queue_next;
			qa.qend = sc.queue_end;
			qa.last_step_clock_32 = (uint)sc.last_step_clock;
			qa.clock_offset -= sc.last_step_clock - old_last_step_clock;
			return false;
		}

		/****************************************************************
		 * Step compress synchronization
		 ****************************************************************/

		// Allocate a new 'steppersync' object
		public static steppersync steppersync_alloc(SerialQueue sq, List<stepcompress> sc_list, int sc_num, int move_num)
		{
			steppersync ss = new steppersync();
			ss.sq = sq;
			ss.cq = new command_queue();

			ss.sc_list = sc_list;
			ss.sc_num = sc_num;

			ss.move_clocks = new ulong[move_num];
			ss.num_move_clocks = move_num;

			return ss;
		}

		// Free memory associated with a 'steppersync' object
		public static void steppersync_free(steppersync ss)
		{
			//if (!ss)
			//	return;
			//free(ss.sc_list);
			//free(ss.move_clocks);
			//serialqueue_free_commandqueue(ss.cq);
			//free(ss);
		}

		// Set the conversion rate of 'print_time' to mcu clock
		public static void steppersync_set_time(steppersync ss, double time_offset, double mcu_freq)
		{
			int i;
			for (i = 0; i < ss.sc_num; i++)
			{
				stepcompress sc = ss.sc_list[i];
				stepcompress_set_time(ref sc, time_offset, mcu_freq);
			}
		}

		// Implement a binary heap algorithm to track when the next available
		// 'struct move' in the mcu will be available
		static void heap_replace(steppersync ss, ulong req_clock)
		{
			ulong[] mc = ss.move_clocks;
			int nmc = ss.num_move_clocks, pos = 0;
			while (true)
			{
				int child1_pos = 2 * pos + 1, child2_pos = 2 * pos + 2;
				ulong child2_clock = child2_pos < nmc ? mc[child2_pos] : ulong.MaxValue;
				ulong child1_clock = child1_pos < nmc ? mc[child1_pos] : ulong.MaxValue;
				if (req_clock <= child1_clock && req_clock <= child2_clock)
				{
					mc[pos] = req_clock;
					break;
				}
				if (child1_clock < child2_clock)
				{
					mc[pos] = child1_clock;
					pos = child1_pos;
				}
				else
				{
					mc[pos] = child2_clock;
					pos = child2_pos;
				}
			}
		}

		// Find and transmit any scheduled steps prior to the given 'move_clock'
		public static int steppersync_flush(steppersync ss, ulong move_clock)
		{
			// Flush each stepcompress to the specified move_clock
			int i;
			for (i = 0; i < ss.sc_num; i++)
			{
				var sc = ss.sc_list[i];
				int ret = stepcompress_flush(ref sc, move_clock);
				if (ret != 0)
					return ret;
			}

			// Order commands by the reqclock of each pending command
			List<SerialQueue.RawMessage> msgs = new List<SerialQueue.RawMessage>();
			while (true)
			{
				// Find message with lowest reqclock
				ulong req_clock = SerialQueue.MAX_CLOCK;
				var qm = new SerialQueue.RawMessage();
				var scFound = new stepcompress();
				bool found = false;
				for (i = 0; i < ss.sc_num; i++)
				{
					stepcompress sc = ss.sc_list[i];
					if (sc.msg_queue.Count != 0)
					{
						var m = sc.msg_queue.Peek();
						if (m.req_clock < req_clock)
						{
							found = true;
							scFound = sc;
							qm = m;
							req_clock = m.req_clock;
						}
					}
				}
				if (found || (qm.min_clock != 0 && req_clock > move_clock))
					break;

				ulong next_avail = ss.move_clocks[0];
				if (qm.min_clock != 0)
					// The qm.min_clock field is overloaded to indicate that
					// the command uses the 'move queue' and to store the time
					// that move queue item becomes available.
					heap_replace(ss, qm.min_clock);
				// Reset the min_clock to its normal meaning (minimum transmit time)
				qm.min_clock = next_avail;

				qm = scFound.msg_queue.Dequeue();
				// Batch this command
				msgs.Add(qm);
			}

			// Transmit commands
			if (msgs.Count != 0)
				ss.sq.send_batch(ss.cq, msgs.ToArray());

			return 0;
		}



	}


	public struct points
	{
		public int minp, maxp;
	}

	public unsafe struct queue_append
	{
		public stepcompress sc;
		public uint* qnext, qend;
		public uint last_step_clock_32;
		public double clock_offset;
	}

	public unsafe struct stepcompress
	{
		// Buffer management
		public uint* queue, queue_end, queue_pos, queue_next;
		// Internal tracking
		public uint max_error;
		public double mcu_time_offset, mcu_freq;
		// Message generation
		public ulong last_step_clock, homing_clock;
		public Queue<SerialQueue.RawMessage> msg_queue;
		public uint queue_step_msgid, set_next_step_dir_msgid, oid;
		public int sdir, invert_sdir;
		public stepcompress(uint oid) : this()
		{
			this.oid = oid;
			sdir = -1;
		}
	}

	public struct step_move
	{
		public uint interval;
		public ushort count;
		public short add;
	}

	// The steppersync object is used to synchronize the output of mcu
	// step commands.  The mcu can only queue a limited number of step
	// commands - this code tracks when items on the mcu step queue become
	// free so that new commands can be transmitted.  It also ensures the
	// mcu step queue is ordered between steppers so that no stepper
	// starves the other steppers of space in the mcu step queue.

	public unsafe class steppersync
	{
		// Serial port
		public SerialQueue sq;
		public command_queue cq;
		// Storage for associated stepcompress objects
		public List<stepcompress> sc_list;
		public int sc_num;
		// Storage for list of pending move clocks
		public ulong[] move_clocks;
		public int num_move_clocks;
	}

}
