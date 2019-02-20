using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using IniParser;
using IniParser.Parser;
using IniParser.Model.Configuration;
using IniParser.Model;
using System.Linq;
using KlipperSharp.MachineCodes;
using System.Globalization;

namespace KlipperSharp
{

	[Serializable]
	public class ConfigException : Exception
	{
		public ConfigException() { }
		public ConfigException(string message) : base(message) { }
		public ConfigException(string message, Exception inner) : base(message, inner) { }
		protected ConfigException(
		 System.Runtime.Serialization.SerializationInfo info,
		 System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
	}

	public class ConfigWrapper
	{
		private Machine printer;
		public IniData fileconfig;
		public HashSet<(string, string)> access_tracking;
		private string section;
		private KeyDataCollection options;

		public ConfigWrapper(Machine printer, IniData fileconfig, HashSet<(string, string)> access_tracking, string section)
		{
			this.printer = printer;
			this.fileconfig = fileconfig;
			this.access_tracking = access_tracking;
			this.section = section;
			this.options = fileconfig.Sections[section];
		}

		public Machine get_printer()
		{
			return this.printer;
		}

		public string get_name()
		{
			return this.section;
		}

		string _get_wrapper(string option, string @default)
		{
			bool contKey = options != null && options.ContainsKey(option);
			if (@default != null && !contKey)
			{
				return @default;
			}
			this.access_tracking.Add((this.section.ToLowerInvariant(), option.ToLowerInvariant()));
			return options?[option];
		}

		public string get(string option, string @default = null)
		{
			return this._get_wrapper(option, @default);
		}

		public long getint(string option, long? @default = null, long? minval = null, long? maxval = null)
		{
			var raw = this._get_wrapper(option, null);
			if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v))
			{
				if (@default.HasValue)
				{
					v = @default.Value;
				}
				else
				{
					throw new Exception($"Unable to parse option '{option}' in section '{this.section}'");
				}
			}
			if (minval != null && v < minval)
			{
				throw new Exception($"Option '{option}' in section '{this.section}' must have minimum of {minval}");
			}
			if (maxval != null && v > maxval)
			{
				throw new Exception($"Option '{option}' in section '{this.section}' must have maximum of {maxval}");
			}
			return v;
		}

		public double getfloat(
			 string option,
			 double? @default = null,
			 double? minval = null,
			 double? maxval = null,
			 double? above = null,
			 double? below = null)
		{
			var raw = this._get_wrapper(option, null);
			if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
			{
				if (@default.HasValue)
				{
					v = @default.Value;
				}
				else
				{
					throw new Exception($"Unable to parse option '{option}' in section '{this.section}'");
				}
			}
			if (minval != null && v < minval)
			{
				throw new Exception($"Option '{option}' in section '{this.section}' must have minimum of {minval}");
			}
			if (maxval != null && v > maxval)
			{
				throw new Exception($"Option '{option}' in section '{this.section}' must have maximum of {maxval}");
			}
			if (above != null && v <= above)
			{
				throw new Exception($"Option '{option}' in section '{this.section}' must be above {above}");
			}
			if (below != null && v >= below)
			{
				throw new Exception($"Option '{option}' in section '{this.section}' must be below {below}");
			}
			return v;
		}

		public bool getboolean(string option, bool? @default = null)
		{
			var raw = this._get_wrapper(option, null);
			return raw == "1";
		}
		public TEnum getEnum<TEnum>(string option, TEnum? @default = null) where TEnum : struct
		{
			var c = this.get(option, null);
			TEnum v;
			if (!Enum.TryParse(c, true, out v))
			{
				if (@default.HasValue)
				{
					v = @default.Value;
				}
				else
				{
					throw new ArgumentException($"Enum '{c}' for option '{option}' in section '{this.section}' is not a valid enum");
				}
			}
			return v;
		}

		public T getchoice<T>(string option, Dictionary<string, T> choices, string @default = null)
		{
			var c = this.get(option, @default);
			if (!choices.ContainsKey(c))
			{
				throw new Exception($"Choice '{c}' for option '{option}' in section '{this.section}' is not a valid choice");
			}
			return choices[c];
		}

		public ConfigWrapper getsection(string section)
		{
			return new ConfigWrapper(this.printer, this.fileconfig, this.access_tracking, section);
		}

		public bool has_section(string section)
		{
			return this.fileconfig.Sections.ContainsSection(section);
		}

		public ConfigWrapper[] get_prefix_sections(string prefix)
		{
			return (from s in this.fileconfig.Sections
					  where s.SectionName.StartsWith(prefix)
					  select this.getsection(s.SectionName)).ToArray();
		}

		public object get_prefix_options(string prefix)
		{
			return (from o in options
					  where o.KeyName.StartsWith(prefix)
					  select o).ToList();
		}
	}


	public class MachineConfig
	{
		private static readonly Logger logging = LogManager.GetCurrentClassLogger();

		public const string cmd_SAVE_CONFIG_help = "Overwrite config file and restart";
		public const string AUTOSAVE_HEADER = @"#*# <---------------------- SAVE_CONFIG ---------------------->
#*# DO NOT EDIT THIS BLOCK OR BELOW. The contents are auto-generated.
#*#";
		public static Regex comment_r = new Regex("[#;].*$");
		public static Regex value_r = new Regex("[^A-Za-z0-9_].*$");
		private Machine printer;
		private ConfigWrapper autosave;

		static FileIniDataParser fileIniParser;

		static MachineConfig()
		{
			var parserConfiguration = new IniParserConfiguration()
			{
				CaseInsensitive = false,
				CommentString = "#",
				SectionStartChar = '[',
				SectionEndChar = ']',
				KeyValueAssigmentChar = ':',
			};
			var iniParser = new IniDataParser(parserConfiguration);
			fileIniParser = new FileIniDataParser(iniParser);
		}
		public MachineConfig(Machine printer)
		{
			this.printer = printer;
			var gcode = this.printer.lookup_object<GCodeParser>("gcode");
			gcode.register_command("SAVE_CONFIG", cmd_SAVE_CONFIG, desc: cmd_SAVE_CONFIG_help);
		}

		string _read_config_file(string filename)
		{
			string data;
			try
			{
				data = File.ReadAllText(filename);
			}
			catch
			{
				var msg = $"Unable to open config file {filename}";
				logging.Error(msg);
				throw new Exception(msg);
			}
			return data.Replace("\r\n", "\n");
		}

		(string, string) _find_autosave_data(string data)
		{
			var regular_data = data;
			var autosave_data = "";
			var pos = data.IndexOf(AUTOSAVE_HEADER);
			if (pos >= 0)
			{
				regular_data = data.Substring(0, pos);
				autosave_data = data.Substring(pos + AUTOSAVE_HEADER.Length).Trim();
			}
			// Check for errors and strip line prefixes
			if (regular_data.Contains("\n#*# "))
			{
				logging.Warn("Can't read autosave from config file\" - autosave state corrupted\"");
				return (data, "");
			}
			var @out = new List<object> { "" };
			foreach (var line in autosave_data.Split("\n"))
			{
				if ((!line.StartsWith("#*#") || line.Length >= 4 && !line.StartsWith("#*# ")) && autosave_data != null)
				{
					logging.Warn("Can't read autosave from config file\" - modifications after header\"");
					return (data, "");
				}
				@out.Add(line.Substring(4));
			}
			@out.Add("");
			return (regular_data, string.Join("\n", @out));
		}

		public string _strip_duplicates(string data, ConfigWrapper config)
		{
			var fileconfig = config.fileconfig;
			// Comment out fields in 'data' that are defined in 'config'
			var lines = data.Split("\n");
			string section = null;
			var is_dup_field = false;
			for (int lineno = 0; lineno < lines.Length; lineno++)
			{
				var line = lines[lineno];
				var pruned_line = comment_r.Match(line).Value.TrimEnd();
				if (string.IsNullOrEmpty(pruned_line))
				{
					continue;
				}
				if (char.IsWhiteSpace(pruned_line[0]))
				{
					if (is_dup_field)
					{
						lines[lineno] = "#" + lines[lineno];
					}
					continue;
				}
				is_dup_field = false;
				if (pruned_line[0] == '[')
				{
					section = pruned_line.Substring(1, pruned_line.Length - 2).Trim();
					continue;
				}
				var field = value_r.Match(pruned_line).Value;
				if (config.fileconfig[section].ContainsKey(field))
				{
					is_dup_field = true;
					lines[lineno] = "#" + lines[lineno];
				}
			}
			return string.Join("\n", lines);
		}

		ConfigWrapper _build_config_wrapper(string data)
		{
			var fileconfig = fileIniParser.Parser.Parse(data);
			return new ConfigWrapper(this.printer, fileconfig, new HashSet<(string, string)>(), "printer");
		}

		string _build_config_string(ConfigWrapper config)
		{
			MemoryStream ms = new MemoryStream();
			fileIniParser.WriteData(new StreamWriter(ms), config.fileconfig);
			ms.Position = 0;
			var data = new StreamReader(ms).ReadToEnd();
			return data.Trim();
		}

		public ConfigWrapper read_config(string filename)
		{
			return this._build_config_wrapper(this._read_config_file(filename));
		}

		public ConfigWrapper read_main_config()
		{
			var filename = (string)this.printer.get_start_args().Get("config_file");
			var data = this._read_config_file(filename);
			var _tup_1 = this._find_autosave_data(data);
			var regular_data = _tup_1.Item1;
			var autosave_data = _tup_1.Item2;
			var regular_config = this._build_config_wrapper(regular_data);
			autosave_data = this._strip_duplicates(autosave_data, regular_config);
			this.autosave = this._build_config_wrapper(autosave_data);
			return this._build_config_wrapper(regular_data + autosave_data);
		}

		public void check_unused_options(ConfigWrapper config)
		{
			var fileconfig = config.fileconfig;
			var objects = this.printer.lookup_objects<object>().ToDictionary((a) => a.name, (b) => b.modul);
			// Determine all the fields that have been accessed
			var access_tracking = new HashSet<(string, string)>(config.access_tracking);
			foreach (var section in this.autosave.fileconfig.Sections)
			{
				foreach (var option in section.Keys)
				{
					access_tracking.Add((section.SectionName.ToLowerInvariant(), option.KeyName.ToLowerInvariant()));
				}
			}
			// Validate that there are no undefined parameters in the config file
			var valid_sections = access_tracking.Select((a) => a.Item1).ToHashSet();
			foreach (var section in fileconfig.Sections)
			{
				var sectionName = section.SectionName.ToLowerInvariant();
				if (!valid_sections.Contains(sectionName) && !objects.ContainsKey(sectionName))
				{
					throw new Exception($"Section '{sectionName}' is not a valid config section");
				}
				foreach (var option in section.Keys)
				{
					var optionName = option.KeyName.ToLowerInvariant();
					if (!access_tracking.Contains((sectionName, optionName)))
					{
						throw new Exception($"Option '{option}' is not valid in section '{sectionName}'");
					}
				}
			}
		}

		public void log_config(ConfigWrapper config)
		{
			var lines = $"===== Config file =====\n{this._build_config_string(config)}\n=======================";
			this.printer.set_rollover_info("config", lines);
		}

		// Autosave functions
		public void set(string section, string option, string value)
		{
			if (!this.autosave.fileconfig.Sections.ContainsSection(section))
			{
				this.autosave.fileconfig.Sections.AddSection(section);
			}
			this.autosave.fileconfig.Sections[section][option] = value;
			logging.Info("save_config: set [{0}] {1} = {2}", section, option, value);
		}

		public void remove_section(string section)
		{
			this.autosave.fileconfig.Sections.RemoveSection(section);
		}

		public void cmd_SAVE_CONFIG(Dictionary<string, object> parameters)
		{
			string msg;
			string regular_data;
			string data;
			if (this.autosave.fileconfig.Sections.Count == 0)
			{
				return;
			}
			var gcode = this.printer.lookup_object<GCodeParser>("gcode");
			// Create string containing autosave data
			var autosave_data = this._build_config_string(this.autosave);
			var lines = (from l in autosave_data.Split("\n")
							 select ("#*# " + l).TrimEnd()).ToList();
			lines.Insert(0, "\n" + AUTOSAVE_HEADER);
			lines.Add("");
			autosave_data = string.Join("\n", lines);
			// Read in and validate current config file
			var cfgname = (string)this.printer.get_start_args().Get("config_file");
			try
			{
				data = this._read_config_file(cfgname);
				var _tup_1 = this._find_autosave_data(data);
				regular_data = _tup_1.Item1;
				var old_autosave_data = _tup_1.Item2;
				var config = this._build_config_wrapper(regular_data);
			}
			catch (Exception ex)
			{
				msg = "Unable to parse existing config on SAVE_CONFIG";
				logging.Error(msg);
				throw new Exception(msg, ex);
			}
			regular_data = this._strip_duplicates(regular_data, this.autosave);
			data = regular_data.Trim() + autosave_data;
			// Determine filenames
			var datestr = DateTime.Now.ToLongDateString();// ("-%Y%m%d_%H%M%S");
			var backup_name = cfgname + datestr;
			var temp_name = cfgname + "_autosave";
			if (cfgname.EndsWith(".cfg"))
			{
				backup_name = cfgname.Substring(0, cfgname.Length - 4) + datestr + ".cfg";
				temp_name = cfgname.Substring(0, cfgname.Length - 4) + "_autosave.cfg";
			}
			// Create new config file with temporary name and swap with main config
			logging.Info("SAVE_CONFIG to '{0}' (backup in '{1}')", cfgname, backup_name);
			try
			{
				File.WriteAllText(temp_name, data);
				File.Move(cfgname, backup_name, true);
				File.Move(temp_name, cfgname, true);
			}
			catch (Exception ex)
			{
				msg = "Unable to write config file during SAVE_CONFIG";
				logging.Error(msg);
				throw new Exception(msg, ex);
			}
			gcode.request_restart("restart");
		}
	}

}
