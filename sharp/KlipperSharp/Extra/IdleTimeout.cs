using KlipperSharp.MachineCodes;
using NLog;
using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp.Extra
{
	[Extension("idle_timeout")]
	public class IdleTimeout
	{
		private static readonly Logger logging = LogManager.GetCurrentClassLogger();

		public const string DEFAULT_IDLE_GCODE = @"TURN_OFF_HEATERS
M84";
		public const double PIN_MIN_TIME = 0.1;

		public const double READY_TIMEOUT = 0.5;
		private Machine printer;
		private SelectReactor reactor;
		private GCodeParser gcode;
		private ToolHead toolhead;
		private ReactorTimer timeout_timer;
		private string state;
		private double idle_timeout;
		private string[] idle_gcode;

		[ExtensionGenerator]
		public static object LoadConfig(ConfigWrapper config)
		{
			return new IdleTimeout(config);
		}

		public IdleTimeout(ConfigWrapper config)
		{
			this.printer = config.get_printer();
			this.reactor = this.printer.get_reactor();
			this.gcode = this.printer.lookup_object<GCodeParser>("gcode");
			this.toolhead = null;
			this.printer.register_event_handler("klippy:ready", handle_ready);
			this.state = "Idle";
			this.idle_timeout = config.getfloat("timeout", 600.0, above: 0.0);
			this.idle_gcode = config.get("gcode", DEFAULT_IDLE_GCODE).Split("\n");
		}

		public void handle_ready()
		{
			this.toolhead = this.printer.lookup_object<ToolHead>("toolhead");
			this.timeout_timer = this.reactor.register_timer(this.timeout_handler);
			this.printer.register_event_handler("toolhead:sync_print_time", (Delegate)(Action<double, double, double>)this.handle_sync_print_time);
		}

		public double transition_idle_state(double eventtime)
		{
			this.state = "Printing";
			bool res;
			try
			{
				res = this.gcode.process_batch(new List<string>(this.idle_gcode));
			}
			catch
			{
				logging.Error("idle timeout gcode execution");
				return eventtime + 1.0;
			}
			if (!res)
			{
				// Raced with incoming g-code commands
				return eventtime + 1.0;
			}
			var print_time = this.toolhead.get_last_move_time();
			this.state = "Idle";
			this.printer.send_event("idle_timeout:idle", print_time);
			return SelectReactor.NEVER;
		}

		public double check_idle_timeout(double eventtime)
		{
			// Make sure toolhead class isn't busy
			var _tup_1 = this.toolhead.check_busy(eventtime);
			var print_time = _tup_1.Item1;
			var est_print_time = _tup_1.Item2;
			var lookahead_empty = _tup_1.Item3;
			var idle_time = est_print_time - print_time;
			if (lookahead_empty == 0 || idle_time < 1.0)
			{
				// Toolhead is busy
				return eventtime + this.idle_timeout;
			}
			if (idle_time < this.idle_timeout)
			{
				// Wait for idle timeout
				return eventtime + this.idle_timeout - idle_time;
			}
			if (!this.gcode.process_batch(null))
			{
				// Gcode class busy
				return eventtime + 1.0;
			}
			// Idle timeout has elapsed
			return this.transition_idle_state(eventtime);
		}

		public double timeout_handler(double eventtime)
		{
			if (this.state == "Ready")
			{
				return this.check_idle_timeout(eventtime);
			}
			// Check if need to transition to "ready" state
			var _tup_1 = this.toolhead.check_busy(eventtime);
			var print_time = _tup_1.Item1;
			var est_print_time = _tup_1.Item2;
			var lookahead_empty = _tup_1.Item3;
			var buffer_time = Math.Min(2.0, print_time - est_print_time);
			if (lookahead_empty == 0)
			{
				// Toolhead is busy
				return eventtime + READY_TIMEOUT + Math.Max(0.0, buffer_time);
			}
			if (buffer_time > -READY_TIMEOUT)
			{
				// Wait for ready timeout
				return eventtime + READY_TIMEOUT + buffer_time;
			}
			if (!this.gcode.process_batch(null))
			{
				// Gcode class busy
				return eventtime + READY_TIMEOUT;
			}
			// Transition to "ready" state
			this.state = "Ready";
			this.printer.send_event("idle_timeout:ready", est_print_time + PIN_MIN_TIME);
			return eventtime + this.idle_timeout;
		}

		public void handle_sync_print_time(double curtime, double print_time, double est_print_time)
		{
			if (this.state == "Printing")
			{
				return;
			}
			// Transition to "printing" state
			this.state = "Printing";
			var check_time = READY_TIMEOUT + print_time - est_print_time;
			this.reactor.update_timer(this.timeout_timer, curtime + check_time);
			this.printer.send_event("idle_timeout:printing", est_print_time + PIN_MIN_TIME);
		}
	}
}
