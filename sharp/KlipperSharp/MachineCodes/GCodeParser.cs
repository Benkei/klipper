using KlipperSharp.Extra;
using NLog;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace KlipperSharp.MachineCodes
{
	public interface ITransform
	{
		Vector4d get_position();
		void move(Vector4d newpos, double speed);
	}

	public class CommandStream : Stream
	{
		private byte[] readBuffer = new byte[1024];
		private int readBufferLen = 0;
		public Stream baseStream;

		public CommandStream(Stream baseStream)
		{
			this.baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
		}

		public override bool CanRead => baseStream.CanRead;

		public override bool CanSeek => baseStream.CanSeek;

		public override bool CanWrite => baseStream.CanWrite;

		public override long Length => baseStream.Length;

		public override long Position { get => baseStream.Position; set => baseStream.Position = value; }

		public override void Flush()
		{
			baseStream.Flush();
		}

		public bool ReadHasData()
		{
			if (readBufferLen > 0)
			{
				return true;
			}
			var read = baseStream.Read(readBuffer, readBufferLen, readBuffer.Length - readBufferLen);
			if (read >= 0)
			{
				readBufferLen = read;
			}
			return readBufferLen != 0;
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			readBuffer.AsSpan(0, readBufferLen).CopyTo(buffer.AsSpan(offset, count));
			var read = baseStream.Read(buffer.AsSpan(offset + readBufferLen, count - readBufferLen));
			read += readBufferLen;
			readBufferLen = 0;
			return read;
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			return baseStream.Seek(offset, origin);
		}

		public override void SetLength(long value)
		{
			baseStream.SetLength(value);
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			baseStream.Write(buffer, offset, count);
		}
	}

	[Serializable]
	public class GCodeException : Exception
	{
		public GCodeException() { }
		public GCodeException(string message) : base(message) { }
		public GCodeException(string message, Exception inner) : base(message, inner) { }
		protected GCodeException(
		 System.Runtime.Serialization.SerializationInfo info,
		 System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
	}

	public partial class GCodeParser
	{
		private static readonly Logger logging = LogManager.GetCurrentClassLogger();

		public readonly string cmd_RESTART_help = "Reload config file and restart host software";
		public readonly string cmd_FIRMWARE_RESTART_help = "Restart firmware, host, and reload config";
		public readonly string cmd_SET_GCODE_OFFSET_help = "Set a virtual offset to g-code positions";
		public readonly string cmd_STATUS_help = "Report the printer status";
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
		public string[] cmd_M18_aliases = new string[] { "M84" };
		public string[] cmd_IGNORE_aliases = new string[] { "G21", "M110", "M21" };
		public bool cmd_M105_when_not_ready = true;
		public bool cmd_M112_when_not_ready = true;
		public bool cmd_M115_when_not_ready = true;
		public bool cmd_IGNORE_when_not_ready = true;
		public bool cmd_GET_POSITION_when_not_ready = true;
		public bool cmd_RESTART_when_not_ready = true;
		public bool cmd_FIRMWARE_RESTART_when_not_ready = true;
		public bool cmd_ECHO_when_not_ready = true;
		public bool cmd_STATUS_when_not_ready = true;
		public bool cmd_HELP_when_not_ready = true;
		public bool cmd_M114_when_not_ready = true;


		private Machine printer;
		private CommandStream fd;
		private SelectReactor reactor;
		private bool is_processing_data;
		private bool is_fileinput;
		private List<string> pending_commands = new List<string>();
		private int bytes_read;
		private List<(double, string)> input_log = new List<(double, string)>(50);
		private bool is_printer_ready;
		private ConcurrentDictionary<string, Action<Dictionary<string, object>>> gcode_handlers;
		private ConcurrentDictionary<string, Action<Dictionary<string, object>>> base_gcode_handlers = new ConcurrentDictionary<string, Action<Dictionary<string, object>>>();
		private ConcurrentDictionary<string, Action<Dictionary<string, object>>> ready_gcode_handlers = new ConcurrentDictionary<string, Action<Dictionary<string, object>>>();
		private ConcurrentDictionary<string, (string, Dictionary<string, Action<Dictionary<string, object>>>)> mux_commands = new ConcurrentDictionary<string, (string, Dictionary<string, Action<Dictionary<string, object>>>)>();
		private ConcurrentDictionary<string, string> gcode_help = new ConcurrentDictionary<string, string>();
		private bool absolutecoord;
		private Vector4d base_position;
		private Vector4d last_position;
		private Vector4d homing_position;
		private double speed_factor;
		private double extrude_factor;
		private ITransform move_transform;
		private Action<Vector4d, double> move_with_transform;
		private Func<Vector4d> position_with_transform;
		private bool need_ack;
		private ToolHead toolhead;
		private PrinterHeaters heater;
		private double speed;
		private static Dictionary<string, int> axis2pos = new Dictionary<string, int> { { "X", 0 }, { "Y", 1 }, { "Z", 2 }, { "E", 3 } };

		private Fan fan;
		private PrinterExtruder extruder;
		private bool absoluteextrude;
		private ReactorFileHandler fd_handle;

		public GCodeParser(Machine printer, Stream fd)
		{
			this.printer = printer;
			this.fd = new CommandStream(fd);

			printer.register_event_handler("klippy:ready", this.handle_ready);
			printer.register_event_handler("klippy:shutdown", this.handle_shutdown);
			printer.register_event_handler("klippy:disconnect", this.handle_disconnect);
			// Input handling
			this.reactor = printer.get_reactor();
			this.is_processing_data = false;
			this.is_fileinput = printer.get_start_args().Get("debuginput") != null;
			this.fd_handle = null;
			if (!this.is_fileinput)
			{
				this.fd_handle = this.reactor.register_fd(this.fd, this.process_data);
			}
			this.bytes_read = 0;
			// Command handling
			this.is_printer_ready = false;
			foreach (var cmd in this.all_handlers)
			{
				var method = GetType().GetMethod("cmd_" + cmd);
				var fieldNotReady = GetType().GetField("cmd_" + cmd + "_when_not_ready");
				var fieldHelp = GetType().GetField("cmd_" + cmd + "_help");
				var fieldAliases = GetType().GetField("cmd_" + cmd + "_aliases");

				var func = (Action<Dictionary<string, object>>)method.CreateDelegate(typeof(Action<Dictionary<string, object>>), this);
				var wnr = fieldNotReady != null ? (bool)fieldNotReady.GetValue(this) : false;
				var desc = fieldHelp != null ? (string)fieldHelp.GetValue(this) : null;
				this.register_command(cmd, func, wnr, desc);

				if (fieldAliases != null)
				{
					var aliases = (string[])fieldAliases.GetValue(this);
					foreach (var a in aliases)
					{
						this.register_command(a, func, wnr);
					}
				}
			}
			// G-Code coordinate manipulation
			this.absolutecoord = true;
			this.base_position = Vector4d.Zero;
			this.last_position = Vector4d.Zero;
			this.homing_position = Vector4d.Zero;
			this.speed_factor = 1.0 / 60.0;
			this.extrude_factor = 1.0;
			this.move_transform = null;
			this.position_with_transform = () => Vector4d.Zero;
			// G-Code state
			this.need_ack = false;
			this.toolhead = null;
			this.heater = null;
			this.speed = 25.0 * 60.0;
		}

		public void register_command(string cmd, Action<Dictionary<string, object>> func, bool when_not_ready = false, string desc = null)
		{
			if (func == null)
			{
				this.ready_gcode_handlers.TryRemove(cmd, out var empty1);
				this.base_gcode_handlers.TryRemove(cmd, out var empty2);
				return;
			}
			if (this.ready_gcode_handlers.ContainsKey(cmd))
			{
				throw new ConfigException($"gcode command {cmd} already registered");
			}

			if (!(cmd.Length >= 2 && !char.IsUpper(cmd[0]) && char.IsDigit(cmd[1])))
			{
				var origfunc = func;
				func = parameters => origfunc(this.get_extended_params(parameters));
			}
			if (this.ready_gcode_handlers != null)
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
				throw new ConfigException($"mux command {cmd} {key} {value} may have only one key ({prev_key})");
			}
			if (prev_values.ContainsKey(value))
			{
				throw new ConfigException($"mux command {cmd} {key} {value} already registered ({prev_values})");
			}
			prev_values[value] = func;
		}

		public void set_move_transform(ITransform transform)
		{
			if (this.move_transform != null)
			{
				throw new ConfigException("G-Code move transform already specified");
			}
			this.move_transform = transform;
			this.move_with_transform = transform.move;
			this.position_with_transform = transform.get_position;
		}

		public (bool, string) stats(double eventtime)
		{
			return (false, $"gcodein={this.bytes_read}");
		}

		public Dictionary<string, object> get_status(double eventtime)
		{
			var busy = this.is_processing_data;
			return new Dictionary<string, object> {
					 { "speed_factor", this.speed_factor * 60.0 },
					 { "speed", this.speed },
					 { "extrude_factor", this.extrude_factor },
					 { "busy", busy },
					 { "last_xpos", this.last_position.X },
					 { "last_ypos", this.last_position.Y },
					 { "last_zpos", this.last_position.Z },
					 { "last_epos", this.last_position.W },
					 { "base_xpos", this.base_position.X },
					 { "base_ypos", this.base_position.Y },
					 { "base_zpos", this.base_position.Z },
					 { "base_epos", this.base_position.W },
					 { "homing_xpos", this.homing_position.X },
					 { "homing_ypos", this.homing_position.Y },
					 { "homing_zpos", this.homing_position.Z }
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
			this.last_position = this.position_with_transform();
		}

		public void dump_debug()
		{
			var @out = new List<string>();
			@out.Add($"Dumping gcode input {this.input_log.Count} blocks");
			foreach (var _tup_1 in this.input_log)
			{
				var eventtime = _tup_1.Item1;
				var data = _tup_1.Item2;
				@out.Add($"Read {eventtime}: {data}");
			}
			@out.Add($"gcode state: absolutecoord={this.absolutecoord} absoluteextrude={this.absoluteextrude} base_position={this.base_position} last_position={this.last_position} homing_position={this.homing_position} speed_factor={this.speed_factor} extrude_factor={this.extrude_factor} speed={this.speed}");
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
					// get none comment text
					line = line.Substring(0, cpos);
				}
				line = line.ToUpperInvariant();
				// Break command into parts
				var parts = args_r.Split(line).AsSpan(1);
				var parameters = new Dictionary<string, object>();
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

				if (!gcode_handlers.TryGetValue(cmd, out var handler))
				{
					handler = cmd_default;
				}
				try
				{
					handler(parameters);
				}
				catch (GCodeException ex)
				{
					respond_error(ex.ToString());
					reset_last_position();
					if (!need_ack)
					{
						throw;
					}
				}
				catch
				{
					var msg = $"Internal error on command:'{cmd}'";
					logging.Error(msg);
					printer.invoke_shutdown(msg);
					respond_error(msg);
					if (!need_ack)
					{
						throw;
					}
				}

				ack();
			}
		}

		StringBuilder cmdBuffer = new StringBuilder();
		List<string> lines = new List<string>(4);

		private int ReadData(List<string> lines)
		{
			var buffer = ArrayPool<byte>.Shared.Rent(4096);
			var charBuffer = ArrayPool<char>.Shared.Rent(4096);
			try
			{
				var read = fd.Read(buffer.AsSpan());
				var readChar = Encoding.UTF8.GetChars(buffer.AsSpan(0, read), charBuffer);

				cmdBuffer.Append(charBuffer.AsSpan(0, readChar));
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(buffer);
				ArrayPool<char>.Shared.Return(charBuffer);
			}

			int idx;
			int count = 0;
			while ((idx = cmdBuffer.IndexOf('\n')) != -1)
			{
				var str = cmdBuffer.ToString(0, idx);
				if (string.IsNullOrEmpty(str))
				{
					continue;
				}
				lines.Add(str);
				cmdBuffer.Remove(0, idx + 1);
				count++;
			}
			return count;
		}

		public void process_data(double eventtime)
		{
			// Read input, separate by newline, and add to pending_commands
			try
			{
				ReadData(lines);
			}
			catch
			{
				logging.Error("Read g-code");
				return;
			}

			for (int i = 0; i < lines.Count; i++)
			{
				this.input_log.Add((eventtime, lines[i]));
				this.bytes_read += lines[i].Length;
			}

			var pending_commands = this.pending_commands;
			pending_commands.AddRange(lines);
			lines.Clear();

			// Special handling for debug file input EOF
			//if (data == null && this.is_fileinput)
			//{
			//	if (!this.is_processing_data)
			//	{
			//		this.request_restart("exit");
			//	}
			//	pending_commands.Add("");
			//}

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
							this.cmd_M112(null);
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
			catch (GCodeException)
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
			var commands = new List<string>(script.Split('\n'));
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
				//if (msg != null)
				//{
				//	os.write(this.fd, String.Format("ok %s\n", msg));
				//}
				//else
				//{
				//	os.write(this.fd, "ok\n");
				//}
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
				//os.write(this.fd, msg + "\n");
			}
			catch
			{
				logging.Error("Write g-code response");
			}
		}

		public void respond_info(string msg)
		{
			logging.Debug(msg);
			var lines = (from l in (msg.Trim().Split('\n')) select l.Trim());
			this.respond("// " + string.Join("\n// ", lines));
		}

		public void respond_error(string msg)
		{
			logging.Warn(msg);
			var lines = msg.Trim().Split('\n');
			if (lines.Length > 1)
			{
				this.respond_info(string.Join('\n', lines));
			}
			this.respond($"!! {lines[0].Trim()}");
			if (this.is_fileinput)
			{
				this.printer.request_exit("error_exit");
			}
		}

		public void _respond_state(object state)
		{
			this.respond_info($"Klipper state: {state}");
		}

		// Parameter parsing helpers
		public string get_str(string name, Dictionary<string, object> parameters)
		{
			if (!parameters.ContainsKey(name))
			{
				throw new Exception($"Error on '{parameters.Get("#original")}': missing {name}");
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
				throw new ArgumentOutOfRangeException($"Error on '{parameters.Get("#original")}': {name} must have minimum of {minval}");
			}
			if (maxval != null && value > maxval)
			{
				throw new ArgumentOutOfRangeException($"Error on '{parameters.Get("#original")}': {name} must have maximum of {maxval}");
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
				throw new ArgumentOutOfRangeException($"Error on '{parameters.Get("#original")}': {name} must have minimum of {minval}");
			}
			if (maxval != null && value > maxval)
			{
				throw new ArgumentOutOfRangeException($"Error on '{parameters.Get("#original")}': {name} must have maximum of {maxval}");
			}
			if (above != null && value <= above)
			{
				throw new ArgumentOutOfRangeException($"Error on '{parameters.Get("#original")}': {name} must be above {above}");
			}
			if (below != null && value >= below)
			{
				throw new ArgumentOutOfRangeException($"Error on '{parameters.Get("#original")}': {name} must be below {below}");
			}
			return value;
		}

		public Dictionary<string, object> get_extended_params(Dictionary<string, object> parameters)
		{
			return parameters;
			//var m = extended_r.Match((string)parameters.Get("#original"));
			//if (m == null)
			//{
			//	// Not an "extended" command
			//	return parameters;
			//}
			//throw new NotImplementedException();
			//var eargs = m.Groups["args"];
			//try
			//{
			//	var eparams = (from earg in shlex.split(eargs.Value) select earg.split("=", 1)).ToList();
			//	eparams = eparams.ToDictionary(_tup_1 => _tup_1.Item1.upper(), _tup_1 => _tup_1.Item2);
			//	eparams.update(parameters.ToDictionary(k => k, k => parameters[k]));
			//	return eparams;
			//}
			//catch (Exception ex)
			//{
			//	throw new GCodeException($"Malformed command '{parameters["#original"]}'", ex);
			//}
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
						@out.Add($"{heater.gcode_id}:{cur} /{target}");
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
			Heater heater;
			if (is_bed)
			{
				heater = this.heater.get_heater_by_gcode_id("B");
			}
			else if (parameters.ContainsKey("T"))
			{
				var index = this.get_int("T", parameters, minval: 0);
				heater = this.heater.get_heater_by_gcode_id($"T{index}");
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
			catch (Exception ex)
			{
				throw new GCodeException("", ex);
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

	}

}
