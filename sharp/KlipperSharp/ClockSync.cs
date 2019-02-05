using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp
{
	public class ClockSync
	{
		public const double RTT_AGE = 1E-05 / (60.0 * 60.0);
		public const double DECAY = 1.0 / 30.0;
		public const double TRANSMIT_EXTRA = 0.001;
		protected object reactor;
		protected SerialReader serial;
		private object get_clock_timer;
		private object get_clock_cmd;
		private int queries_pending;
		protected internal double mcu_freq;
		private int last_clock;
		protected internal Tuple<double, double, double> clock_est;
		private double min_half_rtt;
		private double min_rtt_time;
		private double time_avg;
		private double clock_avg;
		private double prediction_variance;
		private double last_prediction_time;
		private int time_variance;
		private int clock_covariance;

		public ClockSync(object reactor)
		{
			this.reactor = reactor;
			this.serial = null;
			this.get_clock_timer = this.reactor.register_timer(this._get_clock_event);
			this.get_clock_cmd = null;
			this.queries_pending = 0;
			this.mcu_freq = 1.0;
			this.last_clock = 0;
			this.clock_est = Tuple.Create(0.0, 0.0, 0.0);
			// Minimum round-trip-time tracking
			this.min_half_rtt = 999999999.9;
			this.min_rtt_time = 0.0;
			// Linear regression of mcu clock and system sent_time
			this.time_avg = 0.0;
			this.clock_avg = 0.0;
			this.prediction_variance = 0.0;
			this.last_prediction_time = 0.0;
		}

		public virtual void connect(SerialReader serial)
		{
			this.serial = serial;
			this.mcu_freq = serial.msgparser.get_constant_float("CLOCK_FREQ");
			// Load initial clock and frequency
			var get_uptime_cmd = serial.lookup_command("get_uptime");
			var parameters = get_uptime_cmd.send_with_response(response: "uptime");
			this.last_clock = parameters["high"] << 32 | parameters["clock"];
			this.clock_avg = this.last_clock;
			this.time_avg = parameters["#sent_time"];
			this.clock_est = Tuple.Create(this.time_avg, this.clock_avg, this.mcu_freq);
			this.prediction_variance = Math.Pow(0.001 * this.mcu_freq, 2);
			// Enable periodic get_clock timer
			this.get_clock_cmd = serial.lookup_command("get_clock");
			for (int i = 0; i < 8; i++)
			{
				parameters = this.get_clock_cmd.send_with_response(response: "clock");
				this._handle_clock(parameters);
				this.reactor.pause(0.1);
			}
			serial.register_callback(this._handle_clock, "clock");
			this.reactor.update_timer(this.get_clock_timer, this.reactor.NOW);
		}

		public virtual void connect_file(object serial, bool pace = false)
		{
			this.serial = serial;
			this.mcu_freq = serial.msgparser.get_constant_float("CLOCK_FREQ");
			this.clock_est = Tuple.Create(0.0, 0.0, this.mcu_freq);
			var freq = 1000000000000.0;
			if (pace)
			{
				freq = this.mcu_freq;
			}
			serial.set_clock_est(freq, this.reactor.monotonic(), 0);
		}

		// MCU clock querying (_handle_clock is invoked from background thread)
		public double _get_clock_event(double eventtime)
		{
			this.get_clock_cmd.send();
			this.queries_pending += 1;
			// Use an unusual time for the next event so clock messages
			// don't resonate with other periodic events.
			return eventtime + 0.9839;
		}

		public void _handle_clock(object parameters)
		{
			this.queries_pending = 0;
			// Extend clock to 64bit
			var last_clock = this.last_clock;
			var clock = last_clock & ~-1 | parameters["clock"];
			if (clock < last_clock)
			{
				clock += 4294967296L;
			}
			this.last_clock = clock;
			// Check if this is the best round-trip-time seen so far
			var sent_time = parameters["#sent_time"];
			if (!sent_time)
			{
				return;
			}
			var receive_time = parameters["#receive_time"];
			var half_rtt = 0.5 * (receive_time - sent_time);
			var aged_rtt = (sent_time - this.min_rtt_time) * RTT_AGE;
			if (half_rtt < this.min_half_rtt + aged_rtt)
			{
				this.min_half_rtt = half_rtt;
				this.min_rtt_time = sent_time;
				//logging.debug("new minimum rtt %.3f: hrtt=%.6f freq=%d", sent_time, half_rtt, this.clock_est[2]);
			}
			// Filter out samples that are extreme outliers
			var exp_clock = (sent_time - this.time_avg) * this.clock_est.Item3 + this.clock_avg;
			var clock_diff2 = Math.Pow(clock - exp_clock, 2);
			if (clock_diff2 > 25.0 * this.prediction_variance && clock_diff2 > Math.Pow(0.0005 * this.mcu_freq, 2))
			{
				if (clock > exp_clock && sent_time < this.last_prediction_time + 10.0)
				{
					//logging.debug("Ignoring clock sample %.3f:\" freq=%d diff=%d stddev=%.3f\"", sent_time, this.clock_est[2], clock - exp_clock, math.sqrt(this.prediction_variance));
					return;
				}
				//logging.info("Resetting prediction variance %.3f:\" freq=%d diff=%d stddev=%.3f\"", sent_time, this.clock_est[2], clock - exp_clock, math.sqrt(this.prediction_variance));
				this.prediction_variance = Math.Pow(0.001 * this.mcu_freq, 2);
			}
			else
			{
				this.last_prediction_time = sent_time;
				this.prediction_variance = (1.0 - DECAY) * (this.prediction_variance + clock_diff2 * DECAY);
			}
			// Add clock and sent_time to linear regression
			var diff_sent_time = sent_time - this.time_avg;
			this.time_avg += DECAY * diff_sent_time;
			this.time_variance = (1.0 - DECAY) * (this.time_variance + Math.Pow(diff_sent_time, 2) * DECAY);
			var diff_clock = clock - this.clock_avg;
			this.clock_avg += DECAY * diff_clock;
			this.clock_covariance = (1.0 - DECAY) * (this.clock_covariance + diff_sent_time * diff_clock * DECAY);
			// Update prediction from linear regression
			var new_freq = this.clock_covariance / (double)this.time_variance;
			var pred_stddev = Math.Sqrt(this.prediction_variance);
			this.serial.set_clock_est(new_freq, this.time_avg + TRANSMIT_EXTRA, Convert.ToInt32(this.clock_avg - 3.0 * pred_stddev));
			this.clock_est = Tuple.Create(this.time_avg + this.min_half_rtt, this.clock_avg, new_freq);
			//logging.debug("regr %.3f: freq=%.3f d=%d(%.3f)",
			//              sent_time, new_freq, clock - exp_clock, pred_stddev)
			// clock frequency conversions
		}

		public virtual int print_time_to_clock(double print_time)
		{
			return Convert.ToInt32(print_time * this.mcu_freq);
		}

		public virtual double clock_to_print_time(double clock)
		{
			return clock / this.mcu_freq;
		}

		public virtual double get_adjusted_freq()
		{
			return this.mcu_freq;
		}

		// system time conversions
		public int get_clock(double eventtime)
		{
			var _tup_1 = this.clock_est;
			var sample_time = _tup_1.Item1;
			var clock = _tup_1.Item2;
			var freq = _tup_1.Item3;
			return Convert.ToInt32(clock + (eventtime - sample_time) * freq);
		}

		public double estimated_print_time(double eventtime)
		{
			return this.clock_to_print_time(this.get_clock(eventtime));
		}


		// misc commands
		public long clock32_to_clock64(int clock32)
		{
			var last_clock = this.last_clock;
			var clock_diff = last_clock - clock32 & -1;
			if ((clock_diff & -2147483648) != 0)
			{
				return last_clock + 4294967296L - clock_diff;
			}
			return last_clock - clock_diff;
		}

		public bool is_active()
		{
			return this.queries_pending <= 4;
		}

		public virtual string dump_debug()
		{
			var _tup_1 = this.clock_est;
			var sample_time = _tup_1.Item1;
			var clock = _tup_1.Item2;
			var freq = _tup_1.Item3;
			return String.Format($"clocksync state: mcu_freq=%d last_clock=%d\" clock_est=(%.3f %d %.3f) min_half_rtt=%.6f min_rtt_time=%.3f\"\" time_avg=%.3f(%.3f) clock_avg=%.3f(%.3f)\"\" pred_variance=%.3f\"",
			this.mcu_freq, this.last_clock, sample_time, clock, freq, this.min_half_rtt, this.min_rtt_time, this.time_avg,
			this.time_variance, this.clock_avg, this.clock_covariance, this.prediction_variance);
		}

		public virtual string stats(double eventtime)
		{
			var _tup_1 = this.clock_est;
			var sample_time = _tup_1.Item1;
			var clock = _tup_1.Item2;
			var freq = _tup_1.Item3;
			return $"freq={freq}";
		}

		public virtual (double, double) calibrate_clock(double print_time, double eventtime)
		{
			return (0.0, this.mcu_freq);
		}

	}


	public class SecondarySync : ClockSync
	{
		private ClockSync main_sync;
		private (double, double) clock_adj;
		private double last_sync_time;

		public SecondarySync(object reactor, ClockSync main_sync)
			 : base(reactor)
		{
			this.main_sync = main_sync;
			this.clock_adj = (0.0, 1.0);
			this.last_sync_time = 0.0;
		}

		public override void connect(SerialReader serial)
		{
			base.connect(serial);
			this.clock_adj = (0.0, this.mcu_freq);
			var curtime = this.reactor.monotonic();
			var main_print_time = this.main_sync.estimated_print_time(curtime);
			var local_print_time = this.estimated_print_time(curtime);
			this.clock_adj = (main_print_time - local_print_time, this.mcu_freq);
			this.calibrate_clock(0.0, curtime);
		}

		public override void connect_file(object serial, bool pace = false)
		{
			base.connect_file(serial, pace);
			this.clock_adj = (0.0, this.mcu_freq);
		}

		// clock frequency conversions
		public override int print_time_to_clock(double print_time)
		{
			var _tup_1 = this.clock_adj;
			var adjusted_offset = _tup_1.Item1;
			var adjusted_freq = _tup_1.Item2;
			return Convert.ToInt32((print_time - adjusted_offset) * adjusted_freq);
		}

		public override double clock_to_print_time(double clock)
		{
			var _tup_1 = this.clock_adj;
			var adjusted_offset = _tup_1.Item1;
			var adjusted_freq = _tup_1.Item2;
			return clock / adjusted_freq + adjusted_offset;
		}

		public override double get_adjusted_freq()
		{
			var _tup_1 = this.clock_adj;
			var adjusted_offset = _tup_1.Item1;
			var adjusted_freq = _tup_1.Item2;
			return adjusted_freq;
		}

		// misc commands
		public override string dump_debug()
		{
			var _tup_1 = this.clock_adj;
			var adjusted_offset = _tup_1.Item1;
			var adjusted_freq = _tup_1.Item2;
			return String.Format("%s clock_adj=(%.3f %.3f)", base.dump_debug(), adjusted_offset, adjusted_freq);
		}

		public override string stats(double eventtime)
		{
			var _tup_1 = this.clock_adj;
			var adjusted_offset = _tup_1.Item1;
			var adjusted_freq = _tup_1.Item2;
			return String.Format("%s adj=%d", base.stats(eventtime), adjusted_freq);
		}

		public override (double, double) calibrate_clock(double print_time, double eventtime)
		{
			// Calculate: est_print_time = main_sync.estimatated_print_time()
			var _tup_1 = this.main_sync.clock_est;
			var ser_time = _tup_1.Item1;
			var ser_clock = _tup_1.Item2;
			var ser_freq = _tup_1.Item3;
			var main_mcu_freq = this.main_sync.mcu_freq;
			var est_main_clock = (eventtime - ser_time) * ser_freq + ser_clock;
			var est_print_time = est_main_clock / main_mcu_freq;
			// Determine sync1_print_time and sync2_print_time
			var sync1_print_time = Math.Max(print_time, est_print_time);
			var sync2_print_time = Math.Max(Math.Max(sync1_print_time + 4.0, this.last_sync_time), print_time + 2.5 * (print_time - est_print_time));
			// Calc sync2_sys_time (inverse of main_sync.estimatated_print_time)
			var sync2_main_clock = sync2_print_time * main_mcu_freq;
			var sync2_sys_time = ser_time + (sync2_main_clock - ser_clock) / ser_freq;
			// Adjust freq so estimated print_time will match at sync2_print_time
			var sync1_clock = this.print_time_to_clock(sync1_print_time);
			var sync2_clock = this.get_clock(sync2_sys_time);
			var adjusted_freq = (sync2_clock - sync1_clock) / (sync2_print_time - sync1_print_time);
			var adjusted_offset = sync1_print_time - sync1_clock / adjusted_freq;
			// Apply new values
			this.clock_adj = (adjusted_offset, adjusted_freq);
			this.last_sync_time = sync2_print_time;
			return this.clock_adj;
		}
	}


}
