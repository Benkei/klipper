using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace KlipperSharp.MachineCodes
{
	public interface ITransform
	{
		List<double> get_position();
		void move(List<double> newpos, double speed);
	}

	public partial class GCodeParser
	{
		private static readonly Logger logging = LogManager.GetCurrentClassLogger();

		public const double RETRY_TIME = 0.1;
		public static Regex args_r = new Regex(@"([A-Z_]+|[A-Z*/])");
		public static Regex m112_r = new Regex(@"^(?:[nN][0-9]+)?\s*[mM]112(?:\s|$)");
		public static Regex extended_r = new Regex(@"(^\s*(?:N[0-9]+\s*)?)|((?<cmd>[a-zA-Z_][a-zA-Z_]+)(?:\s+|$))|((?<args>[^#*;]*?))|(\s*(?:[#*;].*)?$)");
		//extended_r = re.compile(
		//        r'^\s*(?:N[0-9]+\s*)?'
		//        r'(?P<cmd>[a-zA-Z_][a-zA-Z_]+)(?:\s+|$)'
		//        r'(?P<args>[^#*;]*?)'
		//        r'\s*(?:[#*;].*)?$')

		public string[] all_handlers = new string[] {
				"G1",
				"G4",
				"G28",
				"M18",
				"M400",
				"G20",
				"M82",
				"M83",
				"G90",
				"G91",
				"G92",
				"M114",
				"M220",
				"M221",
				"SET_GCODE_OFFSET",
				"M206",
				"M105",
				"M104",
				"M109",
				"M140",
				"M190",
				"M106",
				"M107",
				"M112",
				"M115",
				"IGNORE",
				"GET_POSITION",
				"RESTART",
				"FIRMWARE_RESTART",
				"ECHO",
				"STATUS",
				"HELP"
		  };
		public string[] cmd_G1_aliases = new string[] { "G0" };
		public bool cmd_M105_when_not_ready = true;
		public bool cmd_M112_when_not_ready = true;
		public bool cmd_M115_when_not_ready = true;
		public bool cmd_IGNORE_when_not_ready = true;
		public string[] cmd_IGNORE_aliases = new string[] { "G21", "M110", "M21" };
		public bool cmd_GET_POSITION_when_not_ready = true;
		public bool cmd_RESTART_when_not_ready = true;
		public const string cmd_RESTART_help = "Reload config file and restart host software";
		public bool cmd_FIRMWARE_RESTART_when_not_ready = true;
		public const string cmd_FIRMWARE_RESTART_help = "Restart firmware, host, and reload config";
		public bool cmd_ECHO_when_not_ready = true;
		public bool cmd_STATUS_when_not_ready = true;
		public const string cmd_STATUS_help = "Report the printer status";
		public bool cmd_HELP_when_not_ready = true;
		public const string cmd_SET_GCODE_OFFSET_help = "Set a virtual offset to g-code positions";


		private Machine printer;
		private object fd;
		private SelectReactor reactor;
		private bool is_processing_data;
		private bool is_fileinput;
		public bool cmd_M114_when_not_ready = true;
		private string partial_input;
		private List<string> pending_commands = new List<string>();
		private int bytes_read;
		private List<(double, object)> input_log = new List<(double, object)>(50);
		private bool is_printer_ready;
		private Dictionary<object, Action<Dictionary<string, object>>> gcode_handlers;
		private Dictionary<object, Action<Dictionary<string, object>>> base_gcode_handlers = new Dictionary<object, Action<Dictionary<string, object>>>();
		private Dictionary<object, Action<Dictionary<string, object>>> ready_gcode_handlers = new Dictionary<object, Action<Dictionary<string, object>>>();
		private Dictionary<string, (string, Dictionary<string, Action<Dictionary<string, object>>>)> mux_commands = new Dictionary<string, (string, Dictionary<string, Action<Dictionary<string, object>>>)>();
		private Dictionary<object, object> gcode_help = new Dictionary<object, object>();
		private bool absolutecoord;
		private double[] base_position;
		private double[] last_position;
		private double[] homing_position;
		private double speed_factor;
		private double extrude_factor;
		private ITransform move_transform;
		private Action<List<double>, double> move_with_transform;
		private Func<List<double>> position_with_transform;
		private bool need_ack;
		private ToolHead toolhead;
		private PrinterHeaters heater;
		private double speed;
		private Dictionary<string, int> axis2pos;

		public string[] cmd_M18_aliases = new string[] { "M84" };
		private Fan fan;
		private PrinterExtruder extruder;
		private bool absoluteextrude;
		private ReactorFileHandler fd_handle;

		public GCodeParser(Machine printer, object fd)
		{
			this.printer = printer;
			this.fd = fd;
			printer.register_event_handler("klippy:ready", this.handle_ready);
			printer.register_event_handler("klippy:shutdown", this.handle_shutdown);
			printer.register_event_handler("klippy:disconnect", this.handle_disconnect);
			// Input handling
			this.reactor = printer.get_reactor();
			this.is_processing_data = false;
			this.is_fileinput = printer.get_start_args().get("debuginput") != null;
			this.fd_handle = null;
			if (!this.is_fileinput)
			{
				this.fd_handle = this.reactor.register_fd(this.fd, this.process_data);
			}
			this.partial_input = "";
			this.bytes_read = 0;
			// Command handling
			this.is_printer_ready = false;
			foreach (var cmd in this.all_handlers)
			{
				var func = getattr(this, "cmd_" + cmd);
				var wnr = getattr(this, "cmd_" + cmd + "_when_not_ready", false);
				var desc = getattr(this, "cmd_" + cmd + "_help", null);
				this.register_command(cmd, func, wnr, desc);
				foreach (var a in getattr(this, "cmd_" + cmd + "_aliases", new List<object>()))
				{
					this.register_command(a, func, wnr);
				}
			}
			// G-Code coordinate manipulation
			this.absolutecoord = true;
			this.base_position = new double[] { 0.0, 0.0, 0.0, 0.0 };
			this.last_position = new double[] { 0.0, 0.0, 0.0, 0.0 };
			this.homing_position = new double[] { 0.0, 0.0, 0.0, 0.0 };
			this.speed_factor = 1.0 / 60.0;
			this.extrude_factor = 1.0;
			this.move_transform = null;
			this.position_with_transform = () => new List<double> { 0.0, 0.0, 0.0, 0.0 };
			// G-Code state
			this.need_ack = false;
			this.toolhead = null;
			this.heater = null;
			this.speed = 25.0 * 60.0;
			this.axis2pos = new Dictionary<string, int> {
				{ "X", 0 },
				{ "Y", 1 },
				{ "Z", 2 },
				{ "E", 3 }
			};
		}

		public void register_command(string cmd, Action<Dictionary<string, object>> func, bool when_not_ready = false, object desc = null)
		{
			if (func == null)
			{
				if (this.ready_gcode_handlers.ContainsKey(cmd))
				{
					this.ready_gcode_handlers.Remove(cmd);
				}
				if (this.base_gcode_handlers.ContainsKey(cmd))
				{
					this.base_gcode_handlers.Remove(cmd);
				}
				return;
			}
			if (this.ready_gcode_handlers.ContainsKey(cmd))
			{
				throw new Exception(String.Format("gcode command %s already registered", cmd));
			}

			if (!(cmd.Length >= 2 && !char.IsUpper(cmd[0]) && char.IsDigit(cmd[1])))
			{
				var origfunc = func;
				func = parameters => origfunc(this.get_extended_params(parameters));
			}
			this.ready_gcode_handlers[cmd] = func;
			if (when_not_ready)
			{
				this.base_gcode_handlers[cmd] = func;
			}
			if (desc != null)
			{
				this.gcode_help[cmd] = desc;
			}
		}

		public void register_mux_command(
			 string cmd,
			 string key,
			 string value,
			 Action<Dictionary<string, object>> func,
			 string desc = null)
		{
			(string, Dictionary<string, Action<Dictionary<string, object>>>) prev;
			if (this.mux_commands.TryGetValue(cmd, out prev))
			{
				this.register_command(cmd, this.cmd_mux, desc: desc);
				this.mux_commands[cmd] = (key, new Dictionary<string, Action<Dictionary<string, object>>>());
			}
			var _tup_1 = prev;
			var prev_key = _tup_1.Item1;
			var prev_values = _tup_1.Item2;
			if (prev_key != key)
			{
				throw new Exception(String.Format("mux command %s %s %s may have only one key (%s)", cmd, key, value, prev_key));
			}
			if (prev_values.ContainsKey(value))
			{
				throw new Exception(String.Format("mux command %s %s %s already registered (%s)", cmd, key, value, prev_values));
			}
			prev_values[value] = func;
		}

		public void set_move_transform(ITransform transform)
		{
			if (this.move_transform != null)
			{
				//throw this.printer.config_error("G-Code move transform already specified");
				throw new Exception("G-Code move transform already specified");
			}
			this.move_transform = transform;
			this.move_with_transform = transform.move;
			this.position_with_transform = transform.get_position;
		}

		public (bool, string) stats(double eventtime)
		{
			return (false, string.Format("gcodein=%d", this.bytes_read));
		}

		public Dictionary<string, object> get_status(double eventtime)
		{
			var busy = this.is_processing_data;
			return new Dictionary<string, object> {
					 { "speed_factor", this.speed_factor * 60.0},
					 { "speed", this.speed},
					 { "extrude_factor", this.extrude_factor},
					 { "busy", busy},
					 { "last_xpos", this.last_position[0]},
					 { "last_ypos", this.last_position[1]},
					 { "last_zpos", this.last_position[2]},
					 { "last_epos", this.last_position[3]},
					 { "base_xpos", this.base_position[0]},
					 { "base_ypos", this.base_position[1]},
					 { "base_zpos", this.base_position[2]},
					 { "base_epos", this.base_position[3]},
					 { "homing_xpos", this.homing_position[0]},
					 { "homing_ypos", this.homing_position[1]},
					 { "homing_zpos", this.homing_position[2]}
			};
		}

		public void handle_shutdown()
		{
			if (!this.is_printer_ready)
			{
				return;
			}
			this.is_printer_ready = false;
			this.gcode_handlers = this.base_gcode_handlers;
			this.dump_debug();
			if (this.is_fileinput)
			{
				this.printer.request_exit("error_exit");
			}
			this._respond_state("Shutdown");
		}

		public void handle_disconnect()
		{
			this._respond_state("Disconnect");
		}

		public void handle_ready()
		{
			this.is_printer_ready = true;
			this.gcode_handlers = this.ready_gcode_handlers;
			// Lookup printer components
			this.heater = this.printer.lookup_object<PrinterHeaters>("heater");
			this.toolhead = this.printer.lookup_object<ToolHead>("toolhead");
			if (this.move_transform == null)
			{
				this.move_with_transform = this.toolhead.move;
				this.position_with_transform = this.toolhead.get_position;
			}
			var extruders = PrinterExtruder.get_printer_extruders(this.printer);
			if (extruders.Count != 0)
			{
				this.extruder = extruders[0];
				this.toolhead.set_extruder(this.extruder);
			}
			this.fan = this.printer.lookup_object<Fan>("fan", null);
			if (this.is_fileinput && this.fd_handle == null)
			{
				this.fd_handle = this.reactor.register_fd(this.fd, this.process_data);
			}
			this._respond_state("Ready");
		}

		public void reset_last_position()
		{
			this.last_position = this.position_with_transform().ToArray();
		}

		public void dump_debug()
		{
			var @out = new List<string>();
			@out.Add(String.Format("Dumping gcode input %d blocks", this.input_log.Count));
			foreach (var _tup_1 in this.input_log)
			{
				var eventtime = _tup_1.Item1;
				var data = _tup_1.Item2;
				@out.Add(String.Format("Read %f: %s", eventtime, data));
			}
			@out.Add(String.Format("gcode state: absolutecoord=%s absoluteextrude=%s\" base_position=%s last_position=%s homing_position=%s\"\" speed_factor=%s extrude_factor=%s speed=%s\"",
				this.absolutecoord, this.absoluteextrude, this.base_position, this.last_position, this.homing_position, this.speed_factor, this.extrude_factor, this.speed));
			logging.Info(string.Join("\n", @out));
		}

		public void process_commands(List<string> commands, bool need_ack = true)
		{
			foreach (var item in commands)
			{
				// Ignore comments and leading/trailing spaces
				string origline;
				var line = origline = item.Trim();
				var cpos = line.IndexOf(";");
				if (cpos >= 0)
				{
					line = line.Substring(0, cpos);
				}
				// Break command into parts
				var parts = (new Span<string>(args_r.Split(line.ToUpper()))).Slice(1);
				var parameters = new Dictionary<string, object>();// = range(0, parts.Count, 2).ToDictionary(i => parts[i], i => parts[i + 1].strip());
				for (int i = 0; i < parts.Length; i += 2)
				{
					parameters[parts[i]] = parts[i + 1].Trim();
				}
				parameters["#original"] = origline;
				if (parts.Length != 0 && parts[0] == "N")
				{
					// Skip line number at start of command
					parts.Slice(2);
				}
				if (parts.IsEmpty)
				{
					// Treat empty line as empty command
					parts = new string[] { "", "" };
				}
				string cmd;
				parameters["#command"] = cmd = parts[0] + parts[1].Trim();
				// Invoke handler for command
				this.need_ack = need_ack;

				var handler = this.gcode_handlers.Get(cmd, this.cmd_default);
				try
				{
					handler(parameters);
				}
				catch (Exception ex)
				{
					this.respond_error(ex.ToString());
					this.reset_last_position();
					if (!need_ack)
					{
						throw;
					}
				}
				catch
				{
					var msg = String.Format("Internal error on command:\"%s\"", cmd);
					logging.Error(msg);

					this.printer.invoke_shutdown(msg);

					this.respond_error(msg);
					if (!need_ack)
					{
						throw;
					}
				}

				this.ack();

			}
		}

		public void process_data(double eventtime)
		{
			// Read input, separate by newline, and add to pending_commands
			string data;
			try
			{
				data = os.read(this.fd, 4096);
			}
			catch
			{
				logging.Error("Read g-code");
				return;
			}
			this.input_log.Add((eventtime, data));
			this.bytes_read += data.Length;
			var lines = new List<string>(data.Split("\n"));
			lines[0] = this.partial_input + lines[0];
			this.partial_input = lines[lines.Count - 1];
			lines.RemoveAt(lines.Count - 1);
			var pending_commands = this.pending_commands;
			pending_commands.AddRange(lines);
			// Special handling for debug file input EOF
			if (data == null && this.is_fileinput)
			{
				if (!this.is_processing_data)
				{
					this.request_restart("exit");
				}
				pending_commands.Add("");
			}
			// Handle case where multiple commands pending
			if (this.is_processing_data || pending_commands.Count > 1)
			{
				if (pending_commands.Count < 20)
				{
					// Check for M112 out-of-order
					foreach (var line in lines)
					{
						if (m112_r.IsMatch(line))
						{
							this.cmd_M112(new Dictionary<string, object>());
						}
					}
				}
				if (this.is_processing_data)
				{
					if (pending_commands.Count >= 20)
					{
						// Stop reading input
						this.reactor.unregister_fd(this.fd_handle);
						this.fd_handle = null;
					}
					return;
				}
			}
			// Process commands
			this.is_processing_data = true;
			this.pending_commands = new List<string>();
			this.process_commands(pending_commands);
			if (this.pending_commands.Count != 0)
			{
				this.process_pending();
			}
			this.is_processing_data = false;
		}

		public void process_pending()
		{
			var pending_commands = this.pending_commands;
			while (pending_commands.Count != 0)
			{
				this.pending_commands = new List<string>();
				this.process_commands(pending_commands);
				pending_commands = this.pending_commands;
			}
			if (this.fd_handle == null)
			{
				this.fd_handle = this.reactor.register_fd(this.fd, this.process_data);
			}
		}

		public bool process_batch(List<string> commands)
		{
			if (this.is_processing_data)
			{
				return false;
			}
			this.is_processing_data = true;
			try
			{
				this.process_commands(commands, need_ack: false);
			}
			catch (Exception)
			{
				if (this.pending_commands.Count != 0)
				{
					this.process_pending();
				}
				this.is_processing_data = false;
				throw;
			}
			if (this.pending_commands.Count != 0)
			{
				this.process_pending();
			}
			this.is_processing_data = false;
			return true;
		}

		public void run_script_from_command(string script)
		{
			var prev_need_ack = this.need_ack;
			try
			{
				this.process_commands(new List<string>(script.Split('\n')), need_ack: false);
			}
			finally
			{
				this.need_ack = prev_need_ack;
			}
		}

		public void run_script(string script)
		{
			var commands = new List<string>(script.Split("\n"));
			double curtime = 0;
			while (true)
			{
				var res = this.process_batch(commands);
				if (res)
				{
					break;
				}
				if (curtime == 0)
				{
					curtime = this.reactor.monotonic();
				}
				curtime = this.reactor.pause(curtime + 0.1);
			}
		}

		// Response handling
		public void ack(string msg = null)
		{
			if (!this.need_ack || this.is_fileinput)
			{
				return;
			}
			try
			{
				if (msg != null)
				{
					os.write(this.fd, String.Format("ok %s\n", msg));
				}
				else
				{
					os.write(this.fd, "ok\n");
				}
			}
			catch
			{
				logging.Error("Write g-code ack");
			}
			this.need_ack = false;
		}

		public void respond(string msg)
		{
			if (this.is_fileinput)
			{
				return;
			}
			try
			{
				os.write(this.fd, msg + "\n");
			}
			catch
			{
				logging.Error("Write g-code response");
			}
		}

		public void respond_info(string msg)
		{
			logging.Debug(msg);
			var lines = (from l in (msg.Trim().Split("\n")) select l.Trim());
			this.respond("// " + string.Join("\n// ", lines));
		}

		public void respond_error(string msg)
		{
			logging.Warn(msg);
			var lines = msg.Trim().Split("\n");
			if (lines.Length > 1)
			{
				this.respond_info(string.Join("\n", lines));
			}
			this.respond(String.Format("!! %s", lines[0].Trim()));
			if (this.is_fileinput)
			{
				this.printer.request_exit("error_exit");
			}
		}

		public void _respond_state(object state)
		{
			this.respond_info(String.Format("Klipper state: %s", state));
		}

		// Parameter parsing helpers
		public string get_str(string name, Dictionary<string, object> parameters)
		{
			if (!parameters.ContainsKey(name))
			{
				throw new Exception(String.Format("Error on '%s': missing %s", parameters.Get("#original"), name));
			}
			return parameters[name] as string;
		}
		public string get_str(string name, Dictionary<string, object> parameters, string @default)
		{
			if (!parameters.ContainsKey(name))
			{
				return @default;
			}
			return parameters[name] as string;
		}

		public int get_int(string name, Dictionary<string, object> parameters,
			int? @default = null, int? minval = null, int? maxval = null)
		{
			var rawValue = this.get_str(name, parameters);
			int value;
			if (!int.TryParse(rawValue, out value) && @default.HasValue)
			{
				value = (int)@default;
			}
			if (minval != null && value < minval)
			{
				throw new ArgumentOutOfRangeException(String.Format("Error on '%s': %s must have minimum of %s", parameters.Get("#original"), name, minval));
			}
			if (maxval != null && value > maxval)
			{
				throw new ArgumentOutOfRangeException(String.Format("Error on '%s': %s must have maximum of %s", parameters.Get("#original"), name, maxval));
			}
			return value;
		}

		public double get_float(string name, Dictionary<string, object> parameters,
			 double? @default = null,
			 double? minval = null, double? maxval = null,
			 double? above = null, double? below = null)
		{
			var rawValue = this.get_str(name, parameters);
			double value;
			if (!double.TryParse(rawValue, out value) && @default.HasValue)
			{
				value = (double)@default;
			}
			if (minval != null && value < minval)
			{
				throw new ArgumentOutOfRangeException(String.Format("Error on '%s': %s must have minimum of %s", parameters.Get("#original"), name, minval));
			}
			if (maxval != null && value > maxval)
			{
				throw new ArgumentOutOfRangeException(String.Format("Error on '%s': %s must have maximum of %s", parameters.Get("#original"), name, maxval));
			}
			if (above != null && value <= above)
			{
				throw new ArgumentOutOfRangeException(String.Format("Error on '%s': %s must be above %s", parameters.Get("#original"), name, above));
			}
			if (below != null && value >= below)
			{
				throw new ArgumentOutOfRangeException(String.Format("Error on '%s': %s must be below %s", parameters.Get("#original"), name, below));
			}
			return value;
		}

		public Dictionary<string, object> get_extended_params(Dictionary<string, object> parameters)
		{
			var m = extended_r.Match(parameters["#original"] as string);
			if (m == null)
			{
				// Not an "extended" command
				return parameters;
			}
			var eargs = m.Groups["args"];
			try
			{
				var eparams = (from earg in shlex.split(eargs.Value) select earg.split("=", 1)).ToList();
				eparams = eparams.ToDictionary(_tup_1 => _tup_1.Item1.upper(), _tup_1 => _tup_1.Item2);
				eparams.update(parameters.ToDictionary(k => k, k => parameters[k]));
				return eparams;
			}
			catch (Exception ex)
			{
				throw new Exception(String.Format("Malformed command '%s'", parameters["#original"]), ex);
			}
		}

		// Temperature wrappers
		public string get_temp(double eventtime)
		{
			// Tn:XXX /YYY B:XXX /YYY
			var @out = new List<string>();
			if (this.heater != null)
			{
				foreach (var heater in this.heater.get_all_heaters())
				{
					if (heater != null)
					{
						var _tup_1 = heater.get_temp(eventtime);
						var cur = _tup_1.Item1;
						var target = _tup_1.Item2;
						@out.Add(String.Format("%s:%.1f /%.1f", heater.gcode_id, cur, target));
					}
				}
			}
			if (@out.Count == 0)
			{
				return "T:0";
			}
			return string.Join(" ", @out);
		}

		public void bg_temp(Heater heater)
		{
			if (this.is_fileinput)
			{
				return;
			}
			var eventtime = this.reactor.monotonic();
			while (this.is_printer_ready && heater.check_busy(eventtime))
			{
				var print_time = this.toolhead.get_last_move_time();
				this.respond(this.get_temp(eventtime));
				eventtime = this.reactor.pause(eventtime + 1.0);
			}
		}

		public void set_temp(Dictionary<string, object> parameters, bool is_bed = false, bool wait = false)
		{
			var temp = this.get_float("S", parameters, 0.0);
			Heater heater = null;
			if (is_bed)
			{
				heater = this.heater.get_heater_by_gcode_id("B");
			}
			else if (parameters.ContainsKey("T"))
			{
				var index = this.get_int("T", parameters, minval: 0);
				heater = this.heater.get_heater_by_gcode_id(String.Format("T%d", index));
			}
			else
			{
				heater = this.heater.get_heater_by_gcode_id("T0");
			}
			if (heater == null)
			{
				if (temp > 0.0)
				{
					this.respond_error("Heater not configured");
				}
				return;
			}
			var print_time = this.toolhead.get_last_move_time();
			try
			{
				heater.set_temp(print_time, temp);
			}
			catch
			{
				throw;
			}
			if (wait && temp != 0)
			{
				this.bg_temp(heater);
			}
		}

		public void set_fan_speed(double speed)
		{
			if (this.fan == null)
			{
				if (speed != 0 && !this.is_fileinput)
				{
					this.respond_info("Fan not configured");
				}
				return;
			}
			var print_time = this.toolhead.get_last_move_time();
			this.fan.set_speed(print_time, speed);
		}

		// G-Code special command handlers
		public void cmd_default(Dictionary<string, object> parameters)
		{
			if (!this.is_printer_ready)
			{
				this.respond_error(this.printer.get_state_message());
				return;
			}
			var cmd = parameters.Get("#command") as string;
			if (cmd == null)
			{
				logging.Debug(parameters["#original"]);
				return;
			}
			if (cmd[0] == 'T' && cmd.Length > 1 && char.IsDigit(cmd[1]))
			{
				// Tn command has to be handled specially
				this.cmd_Tn(parameters);
				return;
			}
			else if (cmd.StartsWith("M117 "))
			{
				// Handle M117 gcode with numeric and special characters
				var handler = this.gcode_handlers.Get("M117", null);
				if (handler != null)
				{
					handler(parameters);
					return;
				}
			}
			this.respond_info(String.Format("Unknown command:\"%s\"", cmd));
		}

		public void cmd_Tn(Dictionary<string, object> parameters)
		{
			// Select Tool
			var extruders = PrinterExtruder.get_printer_extruders(this.printer);
			var index = this.get_int("T", parameters, minval: 0, maxval: extruders.Count - 1);
			var e = extruders[index];
			if (object.ReferenceEquals(this.extruder, e))
			{
				return;
			}
			this.run_script_from_command(this.extruder.get_activate_gcode(false));
			try
			{
				this.toolhead.set_extruder(e);
			}
			catch (Exception ex)
			{
				throw new Exception(e.ToString(), ex);
			}
			this.extruder = e;
			this.reset_last_position();
			this.extrude_factor = 1.0;
			this.base_position[3] = this.last_position[3];
			this.run_script_from_command(this.extruder.get_activate_gcode(true));
		}

		public void cmd_mux(Dictionary<string, object> parameters)
		{
			string key_param;
			var _tup_1 = this.mux_commands[parameters["#command"] as string];
			var key = _tup_1.Item1;
			var values = _tup_1.Item2;
			if (values.ContainsKey(null))
			{
				key_param = this.get_str(key, parameters, null);
			}
			else
			{
				key_param = this.get_str(key, parameters);
			}
			if (!values.ContainsKey(key_param))
			{
				throw new Exception(String.Format("The value '%s' is not valid for %s", key_param, key));
			}
			values[key_param](parameters);
		}

		public void cmd_G1(Dictionary<string, object> parameters)
		{
			double v;
			// Move
			try
			{
				foreach (var axis in "XYZ")
				{
					if (parameters.ContainsKey(axis.ToString()))
					{
						v = double.Parse((string)parameters[axis.ToString()]);
						var pos = this.axis2pos[axis.ToString()];
						if (!this.absolutecoord)
						{
							// value relative to position of last move
							this.last_position[pos] += v;
						}
						else
						{
							// value relative to base coordinate position
							this.last_position[pos] = v + this.base_position[pos];
						}
					}
				}
				if (parameters.ContainsKey("E"))
				{
					v = double.Parse((string)parameters["E"]) * this.extrude_factor;
					if (!this.absolutecoord || !this.absoluteextrude)
					{
						// value relative to position of last move
						this.last_position[3] += v;
					}
					else
					{
						// value relative to base coordinate position
						this.last_position[3] = v + this.base_position[3];
					}
				}
				if (parameters.ContainsKey("F"))
				{
					var speed = double.Parse((string)parameters["F"]);
					if (speed <= 0.0)
					{
						throw new Exception(String.Format("Invalid speed in '%s'", parameters["#original"]));
					}
					this.speed = speed;
				}
			}
			catch (Exception ex)
			{
				throw new Exception(String.Format("Unable to parse move '%s'", parameters["#original"]), ex);
			}
			try
			{
				this.move_with_transform(new List<double>(this.last_position), this.speed * this.speed_factor);
			}
			catch (Exception)
			{
				throw;
			}
		}

		public void cmd_G4(Dictionary<string, object> parameters)
		{
			double delay;
			// Dwell
			if (parameters.ContainsKey("S"))
			{
				delay = this.get_float("S", parameters, minval: 0.0);
			}
			else
			{
				delay = this.get_float("P", parameters, 0.0, minval: 0.0) / 1000.0;
			}
			this.toolhead.dwell(delay);
		}

		public void cmd_G28(Dictionary<string, object> parameters)
		{
			// Move to origin
			var axes = new List<int>();
			foreach (var axis in "XYZ")
			{
				if (parameters.ContainsKey(axis.ToString()))
				{
					axes.Add(this.axis2pos[axis.ToString()]);
				}
			}
			if (axes.Count != 0)
			{
				axes = new List<int> { 0, 1, 2 };
			}
			var homing_state = new Homing(this.printer);
			if (this.is_fileinput)
			{
				homing_state.set_no_verify_retract();
			}
			try
			{
				homing_state.home_axes(axes);
			}
			catch
			{
				throw;
			}
			foreach (var axis in homing_state.get_axes())
			{
				this.base_position[axis] = this.homing_position[axis];
			}
			this.reset_last_position();
		}

		public void cmd_M18(Dictionary<string, object> parameters)
		{
			// Turn off motors
			this.toolhead.motor_off();
		}

		public void cmd_M400(Dictionary<string, object> parameters)
		{
			// Wait for current moves to finish
			this.toolhead.wait_moves();
		}

		// G-Code coordinate manipulation
		public void cmd_G20(Dictionary<string, object> parameters)
		{
			// Set units to inches
			this.respond_error("Machine does not support G20 (inches) command");
		}

		public void cmd_M82(Dictionary<string, object> parameters)
		{
			// Use absolute distances for extrusion
			this.absoluteextrude = true;
		}

		public void cmd_M83(Dictionary<string, object> parameters)
		{
			// Use relative distances for extrusion
			this.absoluteextrude = false;
		}

		public void cmd_G90(Dictionary<string, object> parameters)
		{
			// Use absolute coordinates
			this.absolutecoord = true;
		}

		public void cmd_G91(Dictionary<string, object> parameters)
		{
			// Use relative coordinates
			this.absolutecoord = false;
		}

		public void cmd_G92(Dictionary<string, object> parameters)
		{
			// Set position
			var offsets = this.axis2pos.ToDictionary(_tup_1 => _tup_1.Value, _tup_1 => this.get_float(_tup_1.Key, parameters));
			foreach (var _tup_2 in offsets)
			{
				var p = _tup_2.Key;
				var offset = _tup_2.Value;
				if (p == 3)
				{
					offset *= this.extrude_factor;
				}
				this.base_position[p] = this.last_position[p] - offset;
			}
			if (offsets.Count == 0)
			{
				this.last_position.CopyTo(this.base_position, 0);
				//this.base_position = this.last_position.ToList();
			}
		}

		public void cmd_M114(Dictionary<string, object> parameters)
		{
			// Get Current Position
			var p = (from item in this.last_position.Zip(this.base_position, (lp, bp) => (lp, bp))
						select (item.lp - item.bp)).ToList();
			p[3] /= this.extrude_factor;
			this.respond(String.Format("X:%.3f Y:%.3f Z:%.3f E:%.3f", p[0], p[1], p[2], p[3]));
		}

		public void cmd_M220(Dictionary<string, object> parameters)
		{
			// Set speed factor override percentage
			var value = this.get_float("S", parameters, 100.0, above: 0.0) / (60.0 * 100.0);
			this.speed_factor = value;
		}

		public void cmd_M221(Dictionary<string, object> parameters)
		{
			// Set extrude factor override percentage
			var new_extrude_factor = this.get_float("S", parameters, 100.0, above: 0.0) / 100.0;
			var last_e_pos = this.last_position[3];
			var e_value = (last_e_pos - this.base_position[3]) / this.extrude_factor;
			this.base_position[3] = last_e_pos - e_value * new_extrude_factor;
			this.extrude_factor = new_extrude_factor;
		}

		public void cmd_SET_GCODE_OFFSET(Dictionary<string, object> parameters)
		{
			double offset;
			foreach (var _tup_1 in this.axis2pos)
			{
				var axis = _tup_1.Key;
				var pos = _tup_1.Value;
				if (parameters.ContainsKey(axis))
				{
					offset = this.get_float(axis, parameters);
				}
				else if (parameters.ContainsKey(axis + "_ADJUST"))
				{
					offset = this.homing_position[pos];
					offset += this.get_float(axis + "_ADJUST", parameters);
				}
				else
				{
					continue;
				}
				var delta = offset - this.homing_position[pos];
				this.last_position[pos] += delta;
				this.base_position[pos] += delta;
				this.homing_position[pos] = offset;
			}
		}

		public void cmd_M206(Dictionary<string, object> parameters)
		{
			// Offset axes
			var offsets = "XYZ".ToDictionary(a => this.axis2pos[a.ToString()], a => this.get_float(a.ToString(), parameters));
			foreach (var item in offsets)
			{
				var p = item.Key;
				var offset = item.Value;
				this.base_position[p] -= this.homing_position[p] + offset;
				this.homing_position[p] = -offset;
			}
		}

		public void cmd_M105(Dictionary<string, object> parameters)
		{
			// Get Extruder Temperature
			this.ack(this.get_temp(this.reactor.monotonic()));
		}

		public void cmd_M104(Dictionary<string, object> parameters)
		{
			// Set Extruder Temperature
			this.set_temp(parameters);
		}

		public void cmd_M109(Dictionary<string, object> parameters)
		{
			// Set Extruder Temperature and Wait
			this.set_temp(parameters, wait: true);
		}

		public void cmd_M140(Dictionary<string, object> parameters)
		{
			// Set Bed Temperature
			this.set_temp(parameters, is_bed: true);
		}

		public void cmd_M190(Dictionary<string, object> parameters)
		{
			// Set Bed Temperature and Wait
			this.set_temp(parameters, is_bed: true, wait: true);
		}

		public void cmd_M106(Dictionary<string, object> parameters)
		{
			// Set fan speed
			this.set_fan_speed(this.get_float("S", parameters, 255.0, minval: 0.0) / 255.0);
		}

		public void cmd_M107(Dictionary<string, object> parameters)
		{
			// Turn fan off
			this.set_fan_speed(0.0);
		}

		public void cmd_M112(Dictionary<string, object> parameters)
		{
			// Emergency Stop
			this.printer.invoke_shutdown("Shutdown due to M112 command");
		}

		public void cmd_M115(Dictionary<string, object> parameters)
		{
			// Get Firmware Version and Capabilities
			var software_version = this.printer.get_start_args().get("software_version");
			this.ack($"FIRMWARE_NAME:Klipper FIRMWARE_VERSION:{software_version}");
		}

		public void cmd_IGNORE(Dictionary<string, object> parameters)
		{
			// Commands that are just silently accepted
		}

		public void cmd_GET_POSITION(Dictionary<string, object> parameters)
		{
			if (this.toolhead == null)
			{
				this.cmd_default(parameters);
				return;
			}
			var kin = this.toolhead.get_kinematics();
			var steppers = kin.get_steppers();

			var mcu_pos = string.Join(" ", (from s in steppers
													  select String.Format("%s:%d", s.get_name(), s.get_mcu_position())));
			var stepper_pos = string.Join(" ", (from s in steppers
															select String.Format("%s:%.6f", s.get_name(), s.get_commanded_position())));
			var kinematic_pos = string.Join(" ", (from _tup_1 in "XYZE".Zip(kin.calc_position(), (a, v) => (a, v))
															  let a = _tup_1.Item1
															  let v = _tup_1.Item2
															  select String.Format("%s:%.6f", a, v)));
			var toolhead_pos = string.Join(" ", (from _tup_2 in "XYZE".Zip(this.toolhead.get_position(), (a, v) => (a, v))
															 let a = _tup_2.Item1
															 let v = _tup_2.Item2
															 select String.Format("%s:%.6f", a, v)));
			var gcode_pos = string.Join(" ", (from _tup_3 in "XYZE".Zip(this.last_position, (a, v) => (a, v))
														 let a = _tup_3.Item1
														 let v = _tup_3.Item2
														 select String.Format("%s:%.6f", a, v)));
			var base_pos = string.Join(" ", (from _tup_4 in "XYZE".Zip(this.base_position, (a, v) => (a, v))
														let a = _tup_4.Item1
														let v = _tup_4.Item2
														select String.Format("%s:%.6f", a, v)));
			var homing_pos = string.Join(" ", (from _tup_5 in "XYZ".Zip(this.homing_position, (a, v) => (a, v))
														  let a = _tup_5.Item1
														  let v = _tup_5.Item2
														  select String.Format("%s:%.6f", a, v)));
			this.respond_info(String.Format("mcu: %s\n\"stepper: %s\n\"\"kinematic: %s\n\"\"toolhead: %s\n\"\"gcode: %s\n\"\"gcode base: %s\n\"\"gcode homing: %s\"", mcu_pos, stepper_pos, kinematic_pos, toolhead_pos, gcode_pos, base_pos, homing_pos));
		}

		public void request_restart(string result)
		{
			if (this.is_printer_ready)
			{
				this.toolhead.motor_off();
				var print_time = this.toolhead.get_last_move_time();
				if (this.heater != null)
				{
					foreach (var heater in this.heater.get_all_heaters())
					{
						if (heater != null)
						{
							heater.set_temp(print_time, 0.0);
						}
					}
				}
				if (this.fan != null)
				{
					this.fan.set_speed(print_time, 0.0);
				}
				this.toolhead.dwell(0.5);
				this.toolhead.wait_moves();
			}
			this.printer.request_exit(result);
		}

		public void cmd_RESTART(Dictionary<string, object> parameters)
		{
			this.request_restart("restart");
		}

		public void cmd_FIRMWARE_RESTART(Dictionary<string, object> parameters)
		{
			this.request_restart("firmware_restart");
		}

		public void cmd_ECHO(Dictionary<string, object> parameters)
		{
			this.respond_info(parameters["#original"] as string);
		}

		public void cmd_STATUS(Dictionary<string, object> parameters)
		{
			if (this.is_printer_ready)
			{
				this._respond_state("Ready");
				return;
			}
			var msg = this.printer.get_state_message();
			msg = msg.TrimStart() + "\nKlipper state: Not ready";
			this.respond_error(msg);
		}

		public void cmd_HELP(Dictionary<string, object> parameters)
		{
			var cmdhelp = new List<string>();
			if (!this.is_printer_ready)
			{
				cmdhelp.Add("Printer is not ready - not all commands available.");
			}
			cmdhelp.Add("Available extended commands:");
			foreach (var cmd in this.gcode_handlers.OrderBy(_p_1 => _p_1))
			{
				if (this.gcode_help.ContainsKey(cmd))
				{
					cmdhelp.Add(String.Format("%-10s: %s", cmd, this.gcode_help[cmd]));
				}
			}
			this.respond_info(string.Join("\n", cmdhelp));
		}
	}

}
