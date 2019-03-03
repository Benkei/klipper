using NLog;
using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp
{
	public class ClockSync
	{
		private static readonly Logger logging = LogManager.GetCurrentClassLogger();

		public const double RTT_AGE = 0.000010 / (60.0 * 60.0);
		public const double DECAY = 1.0 / 30.0;
		public const double TRANSMIT_EXTRA = 0.001;

		protected SelectReactor reactor;
		protected SerialReader serial;
		private ReactorTimer get_clock_timer;
		private SerialCommand get_clock_cmd;
		private int queries_pending;
		protected internal double mcu_freq;
		private ulong last_clock;
		protected internal (double time_avg, double clock_avg, double mcu_freq) clock_est;
		private double min_half_rtt;
		private double min_rtt_time;
		private double time_avg;
		private double clock_avg;
		private double prediction_variance;
		private double last_prediction_time;
		private double time_variance;
		private double clock_covariance;

		public ClockSync(SelectReactor reactor)
		{
			this.reactor = reactor;
			serial = null;
			get_clock_timer = this.reactor.register_timer(this._get_clock_event);
			get_clock_cmd = null;
			queries_pending = 0;
			mcu_freq = 1.0;
			last_clock = 0;
			clock_est = (0.0, 0.0, 0.0);
			// Minimum round-trip-time tracking
			min_half_rtt = 999999999.9;
			min_rtt_time = 0.0;
			// Linear regression of mcu clock and system sent_time
			time_avg = 0.0;
			clock_avg = 0.0;
			prediction_variance = 0.0;
			last_prediction_time = 0.0;
		}

		public virtual void connect(SerialReader serial)
		{
			this.serial = serial;
			mcu_freq = serial.msgparser.get_constant_float("CLOCK_FREQ");
			// Load initial clock and frequency
			var get_uptime_cmd = serial.lookup_command("get_uptime");
			var parameters = get_uptime_cmd.send_with_response(response: "uptime");
			last_clock = parameters.Get<ulong>("high") << 32 | parameters.Get<ulong>("clock");
			clock_avg = last_clock;
			time_avg = parameters.Get<double>("#sent_time");
			clock_est = (time_avg, clock_avg, mcu_freq);
			prediction_variance = Math.Pow(0.001 * mcu_freq, 2);
			// Enable periodic get_clock timer
			get_clock_cmd = serial.lookup_command("get_clock");
			for (int i = 0; i < 8; i++)
			{
				parameters = get_clock_cmd.send_with_response(response: "clock");
				this._handle_clock(parameters);
				this.reactor.pause(0.1);
			}
			serial.register_callback(this._handle_clock, "clock");
			this.reactor.update_timer(this.get_clock_timer, SelectReactor.NOW);
		}

		public virtual void connect_file(SerialReader serial, bool pace = false)
		{
			this.serial = serial;
			this.mcu_freq = serial.msgparser.get_constant_float("CLOCK_FREQ");
			this.clock_est = (0.0, 0.0, this.mcu_freq);
			var freq = 1000000000000.0;
			if (pace)
			{
				freq = this.mcu_freq;
			}
			serial.set_clock_est(freq, this.reactor.monotonic(), 0);
		}

		// MCU clock querying (_handle_clock is invoked from background thread)
		double _get_clock_event(double eventtime)
		{
			this.get_clock_cmd.send();
			this.queries_pending += 1;

			// Use an unusual time for the next event so clock messages
			// don't resonate with other periodic events.
			return eventtime + 0.9839;
		}

		void _handle_clock(Dictionary<string, object> parameters)
		{
			this.queries_pending = 0;
			// Extend clock to 64bit
			var last_clock = this.last_clock;
			var clock = last_clock & ~0xffffffffUL | parameters.Get<ulong>("clock");
			if (clock < last_clock)
			{
				clock += 0x100000000L;
			}
			this.last_clock = clock;
			// Check if this is the best round-trip-time seen so far
			var sent_time = parameters.Get<double>("#sent_time");
			if (sent_time == 0)
			{
				return;
			}
			var receive_time = parameters.Get<double>("#receive_time");
			var half_rtt = 0.5 * (receive_time - sent_time);
			var aged_rtt = (sent_time - min_rtt_time) * RTT_AGE;
			if (half_rtt < min_half_rtt + aged_rtt)
			{
				min_half_rtt = half_rtt;
				min_rtt_time = sent_time;
				logging.Debug("new minimum rtt {0:0.000}: hrtt={1:0.000000} freq={2:0}", sent_time, half_rtt, this.clock_est.mcu_freq);
			}
			// Filter out samples that are extreme outliers
			var exp_clock = (sent_time - time_avg) * clock_est.mcu_freq + clock_avg;
			var clock_diff2 = Math.Pow(clock - exp_clock, 2);
			if (clock_diff2 > 25.0 * prediction_variance && clock_diff2 > Math.Pow(0.0005 * mcu_freq, 2))
			{
				if (clock > exp_clock && sent_time < last_prediction_time + 10.0)
				{
					logging.Debug("Ignoring clock sample {0:0.000}: freq={1:0} diff={2:0} stddev={3:0.000}",
						sent_time, this.clock_est.mcu_freq, clock - exp_clock, Math.Sqrt(this.prediction_variance));
					return;
				}
				logging.Info("Resetting prediction variance {0:0.000}: freq={1:0} diff={2:0} stddev={3:0.000}",
					sent_time, this.clock_est.mcu_freq, clock - exp_clock, Math.Sqrt(this.prediction_variance));
				prediction_variance = Math.Pow(0.001 * mcu_freq, 2);
			}
			else
			{
				last_prediction_time = sent_time;
				prediction_variance = (1.0 - DECAY) * (prediction_variance + clock_diff2 * DECAY);
			}
			// Add clock and sent_time to linear regression
			var diff_sent_time = sent_time - time_avg;
			time_avg += DECAY * diff_sent_time;
			time_variance = (1.0 - DECAY) * (time_variance + Math.Pow(diff_sent_time, 2) * DECAY);
			var diff_clock = clock - clock_avg;
			clock_avg += DECAY * diff_clock;
			clock_covariance = (1.0 - DECAY) * (clock_covariance + diff_sent_time * diff_clock * DECAY);
			// Update prediction from linear regression
			var new_freq = clock_covariance / time_variance;
			var pred_stddev = Math.Sqrt(prediction_variance);
			serial.set_clock_est(new_freq, time_avg + TRANSMIT_EXTRA, (ulong)(clock_avg - 3.0 * pred_stddev));
			clock_est = (time_avg + min_half_rtt, clock_avg, new_freq);
			logging.Debug("regr {0:0.000}: freq={1:0.000} d={2:0}({3:0.000})", sent_time, new_freq, clock - exp_clock, pred_stddev);
			// clock frequency conversions
		}

		public virtual uint print_time_to_clock(double print_time)
		{
			return (uint)(print_time * this.mcu_freq);
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
			var clock_est = this.clock_est;
			return (int)(clock_est.clock_avg + (eventtime - clock_est.time_avg) * clock_est.mcu_freq);
		}

		public double estimated_print_time(double eventtime)
		{
			return this.clock_to_print_time(this.get_clock(eventtime));
		}


		// misc commands
		public ulong clock32_to_clock64(uint clock32)
		{
			var last_clock = this.last_clock;
			var clock_diff = (last_clock - clock32) & 0xffffffffUL;
			if ((clock_diff & 0x80000000UL) != 0)
			{
				return last_clock + 0x100000000UL - clock_diff;
			}
			return last_clock - clock_diff;
		}

		public bool is_active()
		{
			return this.queries_pending <= 4;
		}

		public virtual string dump_debug()
		{
			var clock_est = this.clock_est;
			var sample_time = clock_est.time_avg;
			var clock = clock_est.clock_avg;
			var freq = clock_est.mcu_freq;
			return $"clocksync state: mcu_freq={this.mcu_freq:0} last_clock={this.last_clock:0} clock_est=({sample_time:0.000} {clock:0} {freq:0.000}) min_half_rtt={this.min_half_rtt:0.000000} min_rtt_time={this.min_rtt_time:0.000} time_avg={this.time_avg:0.000}({this.time_variance:0.000}) clock_avg={this.clock_avg:0.000}({this.clock_covariance:0.000}) pred_variance={this.prediction_variance:0.000}";
		}

		public virtual string stats(double eventtime)
		{
			return $"freq={this.clock_est.mcu_freq}";
		}

		public virtual (double, double) calibrate_clock(double print_time, double eventtime)
		{
			return (0.0, this.mcu_freq);
		}

	}


	public class SecondarySync : ClockSync
	{
		private ClockSync main_sync;
		private (double adjusted_offset, double adjusted_freq) clock_adj;
		private double last_sync_time;

		public SecondarySync(SelectReactor reactor, ClockSync main_sync)
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

		public override void connect_file(SerialReader serial, bool pace = false)
		{
			base.connect_file(serial, pace);
			this.clock_adj = (0.0, this.mcu_freq);
		}

		// clock frequency conversions
		public override uint print_time_to_clock(double print_time)
		{
			return (uint)((print_time - this.clock_adj.adjusted_offset) * this.clock_adj.adjusted_freq);
		}

		public override double clock_to_print_time(double clock)
		{
			return clock / this.clock_adj.adjusted_freq + this.clock_adj.adjusted_offset;
		}

		public override double get_adjusted_freq()
		{
			return this.clock_adj.adjusted_freq;
		}

		// misc commands
		public override string dump_debug()
		{
			return $"{base.dump_debug()} clock_adj=({this.clock_adj.adjusted_offset} {this.clock_adj.adjusted_freq})";
		}

		public override string stats(double eventtime)
		{
			return $"{base.stats(eventtime)} adj={this.clock_adj.Item2}";
		}

		public override (double, double) calibrate_clock(double print_time, double eventtime)
		{
			// Calculate: est_print_time = main_sync.estimatated_print_time()
			var clock_est = this.main_sync.clock_est;
			var main_mcu_freq = this.main_sync.mcu_freq;
			var est_main_clock = (eventtime - clock_est.time_avg) * clock_est.mcu_freq + clock_est.clock_avg;
			var est_print_time = est_main_clock / main_mcu_freq;
			// Determine sync1_print_time and sync2_print_time
			var sync1_print_time = Math.Max(print_time, est_print_time);
			var sync2_print_time = Math.Max(Math.Max(sync1_print_time + 4.0, this.last_sync_time), print_time + 2.5 * (print_time - est_print_time));
			// Calc sync2_sys_time (inverse of main_sync.estimatated_print_time)
			var sync2_main_clock = sync2_print_time * main_mcu_freq;
			var sync2_sys_time = clock_est.time_avg + (sync2_main_clock - clock_est.clock_avg) / clock_est.mcu_freq;
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
