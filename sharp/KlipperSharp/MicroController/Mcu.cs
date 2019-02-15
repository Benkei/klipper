using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KlipperSharp.MicroController
{
	public class Mcu : IPinSetup
	{
		private static readonly Logger logging = LogManager.GetCurrentClassLogger();

		private Machine _printer;
		private ClockSync _clocksync;
		private SelectReactor _reactor;
		private string _name;
		private string _serialport;
		private SerialReader _serial;
		private string _restart_method;
		private SerialCommand _reset_cmd;
		private SerialCommand _config_reset_cmd;
		private SerialCommand _emergency_stop_cmd;
		private bool _is_shutdown;
		private string _shutdown_msg;
		private int _oid_count;
		private List<Action> _config_callbacks;
		private List<string> _init_cmds;
		private List<string> _config_cmds;
		private string _pin_map;
		private string _custom;
		private double _mcu_freq;
		private double _stats_sumsq_base;
		private double _mcu_tick_avg;
		private double _mcu_tick_stddev;
		private double _mcu_tick_awake;
		private double _max_stepper_error;
		private int _move_count;
		private List<stepcompress> _stepqueues;
		private steppersync _steppersync;
		private bool _is_timeout;

		public Mcu(MachineConfig config, ClockSync clocksync)
		{
			this._printer = config.get_printer();
			this._clocksync = clocksync;
			this._reactor = _printer.get_reactor();
			this._name = config.get_name();
			if (this._name.StartsWith("mcu "))
			{
				this._name = this._name.Substring(4);
			}
			this._printer.register_event_handler("klippy:connect", this._connect);
			this._printer.register_event_handler("klippy:shutdown", this._shutdown);
			this._printer.register_event_handler("klippy:disconnect", this._disconnect);
			// Serial port
			this._serialport = config.get("serial", "/dev/ttyS0");
			var baud = 0;
			if (!(this._serialport.StartsWith("/dev/rpmsg_") || this._serialport.StartsWith("/tmp/klipper_host_")))
			{
				baud = config.getint("baud", 250000, minval: 2400);
			}
			this._serial = new SerialReader(this._serialport, baud);
			// Restarts
			this._restart_method = "command";
			if (baud != 0)
			{
				var rmethods = new List<string> { null, "arduino", "command", "rpi_usb" }.ToDictionary(m => m, m => m);
				this._restart_method = config.getchoice("restart_method", rmethods, null);
			}
			this._reset_cmd = null;
			this._emergency_stop_cmd = null;
			this._is_shutdown = false;
			this._shutdown_msg = "";
			// Config building
			this._printer.lookup_object<PrinterPins>("pins").register_chip(this._name, this);
			this._oid_count = 0;
			this._config_callbacks = new List<Action>();
			this._init_cmds = new List<string>();
			this._config_cmds = new List<string>();
			this._pin_map = config.get("pin_map", null);
			this._custom = config.get("custom", "");
			this._mcu_freq = 0.0;
			// Move command queuing
			this._max_stepper_error = config.getfloat("max_stepper_error", 2.5E-05, minval: 0.0);
			this._move_count = 0;
			this._stepqueues = new List<stepcompress>();
			this._steppersync = null;
			// Stats
			this._stats_sumsq_base = 0.0;
			this._mcu_tick_avg = 0.0;
			this._mcu_tick_stddev = 0.0;
			this._mcu_tick_awake = 0.0;
		}
		~Mcu()
		{
			this._disconnect();
		}

		public static Dictionary<string, string> Common_MCU_errors = new Dictionary<string, string> {
{ "Timer too close", @"This is generally indicative of an intermittent
communication failure between micro-controller and host."},
{ "No next step", @"This is generally indicative of an intermittent
communication failure between micro-controller and host."},
{ "Missed scheduling of next ", @"This is generally indicative of an intermittent
communication failure between micro-controller and host."},
{ "ADC out of range", @"This generally occurs when a heater temperature exceeds
its configured min_temp or max_temp."},
{ "Rescheduled timer in the past", @"This generally occurs when the micro-controller has been
requested to step at a rate higher than it is capable of
obtaining."},
{ "Stepper too far in past", @"This generally occurs when the micro-controller has been
requested to step at a rate higher than it is capable of
obtaining."},
{ "Command request", @"This generally occurs in response to an M112 G-Code command
or in response to an internal error in the host software."}
		};

		public static string error_help(string msg)
		{
			foreach (var item in Common_MCU_errors)
			{
				var prefixes = item.Key;
				var help_msg = item.Value;
				foreach (var prefix in prefixes)
				{
					if (msg.StartsWith(prefix))
					{
						return help_msg;
					}
				}
			}
			return "";
		}

		// Serial callbacks
		public void _handle_mcu_stats(Dictionary<string, object> parameters)
		{
			var count = (int)parameters["count"];
			var tick_sum = (int)parameters["sum"];
			var c = 1.0 / (count * this._mcu_freq);
			this._mcu_tick_avg = tick_sum * c;
			var tick_sumsq = (double)parameters["sumsq"] * this._stats_sumsq_base;

			this._mcu_tick_stddev = c * Math.Sqrt(count * tick_sumsq - Math.Pow(tick_sum, 2));

			this._mcu_tick_awake = tick_sum / this._mcu_freq;

		}

		public void _handle_shutdown(Dictionary<string, object> parameters)
		{
			if (this._is_shutdown)
				return;
			this._is_shutdown = true;
			var msg = this._shutdown_msg = (string)parameters["#msg"];
			logging.Info("MCU '%s' %s: %s\n%s\n%s", this._name, parameters["#name"], this._shutdown_msg, this._clocksync.dump_debug(), this._serial.dump_debug());
			var prefix = string.Format("MCU '%s' shutdown: ", this._name);
			if ((string)parameters["#name"] == "is_shutdown")
			{
				prefix = string.Format("Previous MCU '%s' shutdown: ", this._name);
			}

			this._printer.invoke_async_shutdown(prefix + msg + error_help(msg));
		}

		// Connection phase
		public void _check_restart(string reason)
		{
			var start_reason = this._printer.get_start_args().get("start_reason");
			if (start_reason == "firmware_restart")
			{
				return;
			}
			logging.Info("Attempting automated MCU '%s' restart: %s", this._name, reason);
			this._printer.request_exit("firmware_restart");
			this._reactor.pause(this._reactor.monotonic() + 2.0);
			throw new Exception($"Attempt MCU '{this._name}' restart failed");
		}

		public void _connect_file(bool pace = false)
		{
			object dict_fname;
			object out_fname;
			// In a debugging mode.  Open debug output file and read data dictionary
			var start_args = this._printer.get_start_args();
			if (this._name == "mcu")
			{
				out_fname = start_args.get("debugoutput");
				dict_fname = start_args.get("dictionary");
			}
			else
			{
				out_fname = start_args.get("debugoutput") + "-" + this._name;
				dict_fname = start_args.get("dictionary_" + this._name);
			}
			var outfile = open(out_fname, "wb");
			var dfile = open(dict_fname, "rb");
			var dict_data = dfile.read();
			dfile.close();
			this._serial.connect_file(outfile, dict_data);
			this._clocksync.connect_file(this._serial, pace);
			// Handle pacing
			if (!pace)
			{
				Func<object, object> dummy_estimated_print_time = eventtime =>
				{
					return 0.0;
				};
				this.estimated_print_time = dummy_estimated_print_time;
			}
		}

		public void _add_custom()
		{
			foreach (var item in this._custom.Split("\n"))
			{
				var line = item;
				line = line.Trim();
				var cpos = line.IndexOf('#');
				if (cpos >= 0)
				{
					line = line.Substring(0, cpos).Trim();
				}
				if (string.IsNullOrEmpty(line))
				{
					continue;
				}
				this.add_config_cmd(line);
			}
		}

		public void _send_config(int prev_crc)
		{
			// Build config commands
			foreach (var cb in this._config_callbacks)
			{
				cb();
			}
			this._add_custom();
			this._config_cmds.Insert(0, $"allocate_oids count={this._oid_count}");
			// Resolve pin names
			var mcu_type = this._serial.msgparser.get_constant("MCU");
			var pin_resolver = new PinResolver(mcu_type);
			if (this._pin_map != null)
			{
				pin_resolver.Update_aliases(this._pin_map);
			}
			for (int i = 0; i < _config_cmds.Count; i++)
			{
				this._config_cmds[i] = pin_resolver.Update_command(this._config_cmds[i]);
			}
			for (int i = 0; i < _init_cmds.Count; i++)
			{
				this._init_cmds[i] = pin_resolver.Update_command(this._init_cmds[i]);
			}
			// Calculate config CRC
			var bytes = Encoding.ASCII.GetBytes(string.Join('\n', this._config_cmds));
			int config_crc = (int)Crc32.Compute(bytes) & -1;
			this.add_config_cmd($"finalize_config crc={config_crc}");
			// Transmit config messages (if needed)
			if (prev_crc == 0)
			{
				logging.Info("Sending MCU '{0}' printer configuration...", this._name);
				foreach (var c in this._config_cmds)
				{
					this._serial.send(c);
				}
			}
			else if (config_crc != prev_crc)
			{
				this._check_restart("CRC mismatch");
				throw new Exception($"MCU '{this._name}' CRC does not match config");
			}
			// Transmit init messages
			foreach (var c in this._init_cmds)
			{
				this._serial.send(c);
			}
		}

		public Dictionary<string, object> _send_get_config()
		{
			var get_config_cmd = this.lookup_command("get_config");
			if (this.is_fileoutput())
			{
				return new Dictionary<string, object>
				{
					{"is_config", 0},
					{"move_count", 500},
					{"crc", 0}
				};
			}
			var config_parameters = get_config_cmd.send_with_response(null, "config");
			if (this._is_shutdown)
			{
				throw new Exception(String.Format("MCU '%s' error during config: %s", this._name, this._shutdown_msg));
			}
			if (config_parameters.Get("is_shutdown") == null)
			{
				throw new Exception(String.Format("Can not update MCU '%s' config as it is shutdown", this._name));
			}
			return config_parameters;
		}

		public void _check_config()
		{
			var config_parameters = this._send_get_config();
			if (config_parameters.Get("is_config") == null)
			{
				if (this._restart_method == "rpi_usb")
				{
					// Only configure mcu after usb power reset
					this._check_restart("full reset before config");
				}
				// Not configured - send config and issue get_config again
				this._send_config(0);
				config_parameters = this._send_get_config();
				if (config_parameters.Get("is_config") == null && !this.is_fileoutput())
				{
					throw new Exception(string.Format("Unable to configure MCU '%s'", this._name));
				}
			}
			else
			{
				var start_reason = this._printer.get_start_args().get("start_reason");
				if (start_reason == "firmware_restart")
				{
					throw new Exception(String.Format("Failed automated reset of MCU '%s'", this._name));
				}
				// Already configured - send init commands
				this._send_config((int)config_parameters.Get("crc"));
			}
			// Setup steppersync with the move_count returned by get_config
			this._move_count = (int)config_parameters.Get("move_count");
			this._steppersync = Stepcompress.steppersync_alloc(this._serial.serialqueue, this._stepqueues, this._stepqueues.Count, this._move_count);
			Stepcompress.steppersync_set_time(this._steppersync, 0.0, this._mcu_freq);
		}

		public void _connect()
		{
			if (this.is_fileoutput())
			{
				this._connect_file();
			}
			else
			{
				if (this._restart_method == "rpi_usb" && !System.IO.File.Exists(this._serialport))
				{
					// Try toggling usb power
					this._check_restart("enable power");
				}
				this._serial.Connect();
				this._clocksync.connect(this._serial);
			}
			var msgparser = this._serial.msgparser;
			var name = this._name;



			var log_info = new List<string> {
				$"Loaded MCU '{name}' {msgparser.messages_by_id.Count} commands ({msgparser.version} / {msgparser.build_versions})",
				$"MCU '{name}' config: {string.Join(" ", msgparser.config.Select((a) => $"{a.Key}={a.Value}"))}"
			};
			logging.Info(string.Join("\n", log_info));
			this._mcu_freq = this.get_constant_float("CLOCK_FREQ");
			this._stats_sumsq_base = this.get_constant_float("STATS_SUMSQ_BASE");
			this._emergency_stop_cmd = this.lookup_command("emergency_stop");
			this._reset_cmd = this.try_lookup_command("reset");
			this._config_reset_cmd = this.try_lookup_command("config_reset");
			if (this._restart_method == null
				&& (this._reset_cmd != null || this._config_reset_cmd != null)
				&& msgparser.get_constant("SERIAL_BAUD", null) == null)
			{
				this._restart_method = "command";
			}
			this.register_msg(this._handle_shutdown, "shutdown");
			this.register_msg(this._handle_shutdown, "is_shutdown");
			this.register_msg(this._handle_mcu_stats, "stats");
			this._check_config();
			var move_msg = $"Configured MCU '{name}' ({this._move_count} moves)";
			logging.Info(move_msg);
			log_info.Add(move_msg);
			this._printer.set_rollover_info(name, string.Join("\n", log_info), log: false);
		}

		// Config creation helpers
		public T setup_pin<T>(string pin_type, PinParams pin_params) where T : class
		{
			switch (pin_type)
			{
				case "stepper": return (T)(object)new Mcu_stepper(this, pin_params);
				case "endstop": return (T)(object)new Mcu_endstop(this, pin_params);
				case "digital_out": return (T)(object)new Mcu_digital_out(this, pin_params);
				case "pwm": return (T)(object)new Mcu_pwm(this, pin_params);
				case "adc": return (T)(object)new Mcu_adc(this, pin_params);
				default:
					throw new Exception($"pin type {pin_type} not supported on mcu");
			}
		}

		public int create_oid()
		{
			this._oid_count += 1;
			return this._oid_count - 1;
		}

		public void register_config_callback(Action cb)
		{
			this._config_callbacks.Add(cb);
		}

		public void add_config_cmd(string cmd, bool is_init = false)
		{
			if (is_init)
			{
				this._init_cmds.Add(cmd);
			}
			else
			{
				this._config_cmds.Add(cmd);
			}
		}

		public double get_query_slot(int oid)
		{
			var slot = this.seconds_to_clock(oid * 0.01);
			var t = Convert.ToInt32(this.estimated_print_time(this.monotonic()) + 1.5);
			return this.print_time_to_clock(t) + slot;
		}

		public void register_stepqueue(stepcompress stepqueue)
		{
			this._stepqueues.Add(stepqueue);
		}

		public int seconds_to_clock(double time)
		{
			return Convert.ToInt32(time * this._mcu_freq);
		}

		public double get_max_stepper_error()
		{
			return this._max_stepper_error;
		}

		// Wrapper functions
		public void register_msg(Action<Dictionary<string, object>> cb, string msg, int oid = 0)
		{
			this._serial.register_callback(cb, msg, oid);
		}

		public command_queue alloc_command_queue()
		{
			return new command_queue();//this._serial.alloc_command_queue();
		}

		public SerialCommand lookup_command(string msgformat, command_queue cq = null)
		{
			return this._serial.lookup_command(msgformat, cq);
		}

		public SerialCommand try_lookup_command(string msgformat)
		{
			try
			{
				return this.lookup_command(msgformat);
			}
			catch
			{
				return null;
			}
		}

		public int lookup_command_id(string msgformat)
		{
			return this._serial.msgparser.lookup_command(msgformat).Msgid;
		}

		public float get_constant_float(string name)
		{
			return this._serial.msgparser.get_constant_float(name);
		}

		public int print_time_to_clock(double print_time)
		{
			return this._clocksync.print_time_to_clock(print_time);
		}

		public double clock_to_print_time(double clock)
		{
			return this._clocksync.clock_to_print_time(clock);
		}

		public double estimated_print_time(double eventtime)
		{
			return this._clocksync.estimated_print_time(eventtime);
		}

		public double get_adjusted_freq()
		{
			return this._clocksync.get_adjusted_freq();
		}

		public long clock32_to_clock64(int clock32)
		{
			return this._clocksync.clock32_to_clock64(clock32);
		}

		public double pause(double waketime)
		{
			return this._reactor.pause(waketime);
		}

		public double monotonic()
		{
			return this._reactor.monotonic();
		}

		// Restarts
		public void _disconnect()
		{
			this._serial.disconnect();
			if (this._steppersync != null)
			{
				Stepcompress.steppersync_free(this._steppersync);
				this._steppersync = null;
			}
		}

		public void _shutdown()
		{
			_shutdown(false);
		}

		public void _shutdown(bool force)
		{
			if (this._emergency_stop_cmd == null || this._is_shutdown && !force)
			{
				return;
			}
			this._emergency_stop_cmd.send();
		}

		public void _restart_arduino()
		{
			logging.Info("Attempting MCU '%s' reset", this._name);
			this._disconnect();
			serialhdl.arduino_reset(this._serialport, this._reactor);
		}

		public void _restart_via_command()
		{
			if (this._reset_cmd == null && this._config_reset_cmd == null || !this._clocksync.is_active())
			{
				logging.Info("Unable to issue reset command on MCU '%s'", this._name);
				return;
			}
			if (this._reset_cmd == null)
			{
				// Attempt reset via config_reset command
				logging.Info("Attempting MCU '%s' config_reset command", this._name);
				this._is_shutdown = true;
				this._shutdown(force: true);
				this._reactor.pause(this._reactor.monotonic() + 0.015);
				this._config_reset_cmd.send();
			}
			else
			{
				// Attempt reset via reset command
				logging.Info("Attempting MCU '%s' reset command", this._name);
				this._reset_cmd.send();
			}
			this._reactor.pause(this._reactor.monotonic() + 0.015);
			this._disconnect();
		}

		public void _restart_rpi_usb()
		{
			logging.Info("Attempting MCU '%s' reset via rpi usb power", this._name);
			this._disconnect();
			//chelper.run_hub_ctrl(0);
			this._reactor.pause(this._reactor.monotonic() + 2.0);
			//chelper.run_hub_ctrl(1);
		}

		public void microcontroller_restart()
		{
			if (this._restart_method == "rpi_usb")
			{
				this._restart_rpi_usb();
			}
			else if (this._restart_method == "command")
			{
				this._restart_via_command();
			}
			else
			{
				this._restart_arduino();
			}
		}

		// Misc external commands
		public bool is_fileoutput()
		{
			return this._printer.get_start_args().get("debugoutput") != null;
		}

		public bool is_shutdown()
		{
			return this._is_shutdown;
		}

		public void flush_moves(double print_time)
		{
			if (this._steppersync == null)
			{
				return;
			}
			var clock = this.print_time_to_clock(print_time);
			if (clock < 0)
			{
				return;
			}
			var ret = Stepcompress.steppersync_flush(this._steppersync, (ulong)clock);
			if (ret != 0)
			{
				throw new Exception(String.Format("Internal error in MCU '%s' stepcompress", this._name));
			}
		}

		public void check_active(double print_time, double eventtime)
		{
			if (this._steppersync == null)
			{
				return;
			}
			var _tup_1 = this._clocksync.calibrate_clock(print_time, eventtime);
			var offset = _tup_1.Item1;
			var freq = _tup_1.Item2;
			Stepcompress.steppersync_set_time(this._steppersync, offset, freq);
			if (this._clocksync.is_active() || this.is_fileoutput() || this._is_timeout)
			{
				return;
			}
			this._is_timeout = true;
			logging.Info("Timeout with MCU '%s' (eventtime=%f)", this._name, eventtime);
			this._printer.invoke_shutdown(String.Format("Lost communication with MCU '%s'", this._name));
		}

		public object stats(double eventtime)
		{
			var msg = String.Format("%s: mcu_awake=%.03f mcu_task_avg=%.06f mcu_task_stddev=%.06f",
				this._name, this._mcu_tick_awake, this._mcu_tick_avg, this._mcu_tick_stddev);
			return (false, string.Join(" ", msg, this._serial.stats(eventtime), this._clocksync.stats(eventtime)));
		}

	}

}
