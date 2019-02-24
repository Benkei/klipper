using KlipperSharp.Extra;
using NLog;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace KlipperSharp.MachineCodes
{
	public partial class GCodeParser
	{
		// G-Code special command handlers
		public void cmd_default(Dictionary<string, object> parameters)
		{
			if (!this.is_printer_ready)
			{
				this.respond_error(this.printer.get_state_message());
				return;
			}
			var cmd = parameters.Get("#command") as string;
			if (string.IsNullOrEmpty(cmd))
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
				gcode_handlers.TryGetValue("M117", out var handler);
				if (handler != null)
				{
					handler(parameters);
					return;
				}
			}
			this.respond_info($"Unknown command:'{cmd}'");
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
				throw new GCodeException(e.ToString(), ex);
			}
			this.extruder = e;
			this.reset_last_position();
			this.extrude_factor = 1.0;
			this.base_position.W = this.last_position.W;
			this.run_script_from_command(this.extruder.get_activate_gcode(true));
		}

		public void cmd_mux(Dictionary<string, object> parameters)
		{
			string key_param;
			var _tup_1 = this.mux_commands[parameters.Get<string>("#command")];
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
				throw new GCodeException($"The value '{key_param}' is not valid for {key}");
			}
			values[key_param](parameters);
		}

		public void cmd_G1(Dictionary<string, object> parameters)
		{
			// Move
			try
			{
				double v;
				object value;
				Vector4d vec = new Vector4d();
				if (parameters.TryGetValue("X", out value))
				{
					vec.X = float.Parse((string)value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
				}
				if (parameters.TryGetValue("Y", out value))
				{
					vec.Y = float.Parse((string)value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
				}
				if (parameters.TryGetValue("Z", out value))
				{
					vec.Z = float.Parse((string)value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
				}

				if (!this.absolutecoord)
				{
					// value relative to position of last move
					this.last_position += vec;
				}
				else
				{
					// value relative to base coordinate position
					this.last_position = vec + this.base_position;
				}

				if (parameters.TryGetValue("E", out value))
				{
					vec.W = float.Parse((string)value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
					vec.W *= extrude_factor;
				}

				if (!this.absolutecoord || !this.absoluteextrude)
				{
					// value relative to position of last move
					this.last_position.W += vec.W;
				}
				else
				{
					// value relative to base coordinate position
					this.last_position.W = vec.W + this.base_position.W;
				}

				if (parameters.TryGetValue("F", out value))
				{
					var speed = float.Parse((string)value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
					if (speed <= 0.0)
					{
						throw new GCodeException($"Invalid speed in '{parameters["#original"]}'");
					}
					this.speed = speed;
				}
			}
			catch (Exception ex)
			{
				throw new GCodeException($"Unable to parse move '{parameters["#original"]}'", ex);
			}
			try
			{
				this.move_with_transform(this.last_position, this.speed * this.speed_factor);
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
					axes.Add(axis2pos[axis.ToString()]);
				}
			}
			if (axes.Count != 0)
			{
				axes.Add(0);
				axes.Add(1);
				axes.Add(2);
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
			catch (Exception ex)
			{
				throw new GCodeException("", ex);
			}
			foreach (var axis in homing_state.get_axes())
			{
				this.base_position.Set(axis, this.homing_position.Get(axis));
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
			var offsets = axis2pos.ToDictionary(_tup_1 => _tup_1.Value, _tup_1 => this.get_float(_tup_1.Key, parameters));
			foreach (var _tup_2 in offsets)
			{
				var p = _tup_2.Key;
				var offset = _tup_2.Value;
				if (p == 3)
				{
					offset *= this.extrude_factor;
				}
				this.base_position.Set(p, last_position.Get(p) - offset);
			}
			if (offsets.Count == 0)
			{
				this.base_position = this.last_position;
			}
		}

		public void cmd_M114(Dictionary<string, object> parameters)
		{
			// Get Current Position
			var p = this.last_position - this.base_position;
			p.W /= this.extrude_factor;
			this.respond($"X:{p.X} Y:{p.Y} Z:{p.Z} E:{p.W}");
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
			var last_e_pos = this.last_position.W;
			var e_value = (last_e_pos - this.base_position.W) / this.extrude_factor;
			this.base_position.W = (last_e_pos - e_value * new_extrude_factor);
			this.extrude_factor = new_extrude_factor;
		}

		public void cmd_SET_GCODE_OFFSET(Dictionary<string, object> parameters)
		{
			double offset;
			foreach (var _tup_1 in axis2pos)
			{
				var axis = _tup_1.Key;
				var pos = _tup_1.Value;
				if (parameters.ContainsKey(axis))
				{
					offset = this.get_float(axis, parameters);
				}
				else if (parameters.ContainsKey(axis + "_ADJUST"))
				{
					offset = this.homing_position.Get(pos);
					offset += this.get_float(axis + "_ADJUST", parameters);
				}
				else
				{
					continue;
				}
				var delta = offset - this.homing_position.Get(pos);
				this.last_position.Set(pos, last_position.Get(pos) + delta);
				this.base_position.Set(pos, base_position.Get(pos) + delta);
				this.homing_position.Set(pos, offset);
			}
		}

		public void cmd_M206(Dictionary<string, object> parameters)
		{
			// Offset axes
			var offsets = "XYZ".ToDictionary(a => axis2pos[a.ToString()], a => this.get_float(a.ToString(), parameters));
			foreach (var item in offsets)
			{
				var p = item.Key;
				var offset = item.Value;
				this.base_position.Set(p, base_position.Get(p) - (this.homing_position.Get(p) + offset));
				this.homing_position.Set(p, homing_position.Get(p) - (-offset));
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
			var software_version = (string)this.printer.get_start_args().Get("software_version");
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
													  select $"{s.get_name()}:{s.get_mcu_position()}"));
			var stepper_pos = string.Join(" ", (from s in steppers
															select $"{s.get_name()}:{s.get_commanded_position()}"));
			var kinematic_pos = Vector3Format(kin.calc_position());
			var toolhead_pos = Vector4Format(toolhead.get_position());
			var gcode_pos = Vector4Format(last_position);
			var base_pos = Vector4Format(base_position);
			var homing_pos = Vector3Format(homing_position);
			this.respond_info($"mcu: {mcu_pos}\nstepper: {stepper_pos}\nkinematic: {kinematic_pos}\ntoolhead: {toolhead_pos}\ngcode: {gcode_pos}\ngcode base: {base_pos}\ngcode homing: {homing_pos}");
		}

		private static string Vector4Format(in Vector4d v)
		{
			return $"X:{v.X} Z:{v.Y} Z:{v.Z} E:{v.W}";
		}
		private static string Vector3Format(in Vector4d v)
		{
			return $"X:{v.X} Z:{v.Y} Z:{v.Z}";
		}
		private static string Vector3Format(in Vector3d v)
		{
			return $"X:{v.X} Z:{v.Y} Z:{v.Z}";
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
			this.respond_info(parameters.Get<string>("#original"));
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
			foreach (var cmd in this.gcode_handlers.OrderBy((a) => a.Key))
			{
				if (this.gcode_help.ContainsKey(cmd.Key))
				{
					cmdhelp.Add($"{cmd.Key}: {this.gcode_help[cmd.Key]}");
				}
			}
			this.respond_info(string.Join('\n', cmdhelp));
		}
	}

}
