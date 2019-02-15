using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace KlipperSharp
{
	public class MachineConfig
	{
		Machine machine;
		XmlDocument config;

		public MachineConfig()
		{

		}


		XmlDocument _read_config_file(string filename)
		{
			try
			{
				var doc = new XmlDocument();
				using (var fs = File.OpenRead(filename))
				{
					doc.Load(fs);
				}
				return doc;
			}
			catch (Exception)
			{

				throw;
			}
		}




		private Machine printer;
		public string section;

		//public ConfigWrapper(Machine printer, object fileconfig, object access_tracking, string section)
		//{
		//	this.printer = printer;
		//	//this.fileconfig = fileconfig;
		//	//this.access_tracking = access_tracking;
		//	this.section = section;
		//}

		public Machine get_printer()
		{
			return this.printer;
		}

		public string get_name()
		{
			return this.section;
		}

		public object _get_wrapper(
			 object parser,
			 object option,
			 string @default,
			 object minval = null,
			 object maxval = null,
			 object above = null,
			 object below = null)
		{
			throw new NotImplementedException();
			//if (@default != sentinel && !this.fileconfig.has_option(this.section, option))
			//{
			//	return @default;
			//}
			//this.access_tracking[Tuple.Create(this.section.lower(), option.lower())] = 1;
			//try
			//{
			//	var v = parser(this.section, option);
			//}
			//catch
			//{
			//	throw;
			//}
			//catch
			//{
			//	throw error(String.Format("Unable to parse option '%s' in section '%s'", option, this.section));
			//}
			//if (minval != null && v < minval)
			//{
			//	throw error(String.Format("Option '%s' in section '%s' must have minimum of %s", option, this.section, minval));
			//}
			//if (maxval != null && v > maxval)
			//{
			//	throw error(String.Format("Option '%s' in section '%s' must have maximum of %s", option, this.section, maxval));
			//}
			//if (above != null && v <= above)
			//{
			//	throw error(String.Format("Option '%s' in section '%s' must be above %s", option, this.section, above));
			//}
			//if (below != null && v >= below)
			//{
			//	throw this.error(String.Format("Option '%s' in section '%s' must be below %s", option, this.section, below));
			//}
			//return v;
		}

		public string get(string option, string @default = null)
		{
			throw new NotImplementedException();
			//return this._get_wrapper(this.fileconfig.get, option, @default);
		}

		public int getint(string option, int? @default = null, int? minval = null, int? maxval = null)
		{
			throw new NotImplementedException();
			//return this._get_wrapper(this.fileconfig.getint, option, @default, minval, maxval);
		}

		public double getfloat(
			 string option,
			 double? @default = null,
			 double? minval = null,
			 double? maxval = null,
			 double? above = null,
			 double? below = null)
		{
			throw new NotImplementedException();
			//return this._get_wrapper(this.fileconfig.getfloat, option, @default, minval, maxval, above, below);
		}

		public bool? getboolean(string option, bool? @default = null)
		{
			throw new NotImplementedException();
			//return this._get_wrapper(this.fileconfig.getboolean, option, @default);
		}

		public T getchoice<T>(string option, Dictionary<string, T> choices, T @default = default(T))
		{
			throw new NotImplementedException();
			//var c = this.get(option, @default);
			//if (!choices.Contains(c))
			//{
			//	throw error(String.Format("Choice '%s' for option '%s' in section '%s'\" is not a valid choice\"", c, option, this.section));
			//}
			//return choices[c];
		}

		public MachineConfig getsection(string section)
		{
			throw new NotImplementedException();
			//return new ConfigWrapper(this.printer, this.fileconfig, this.access_tracking, section);
		}

		public bool has_section(string section)
		{
			throw new NotImplementedException();
			//return this.fileconfig.has_section(section);
		}

		public MachineConfig[] get_prefix_sections(string prefix)
		{
			throw new NotImplementedException();
			//return (from s in this.fileconfig.sections()
			//		  where s.startswith(prefix)
			//		  select this.getsection(s)).ToList();
		}

		//public virtual object get_prefix_options(object prefix)
		//{
		//	return (from o in this.fileconfig.options(this.section)
		//			  where o.startswith(prefix)
		//			  select o).ToList();
		//}
	}

	public class PrinterConfig
	{
		private static readonly Logger logging = LogManager.GetCurrentClassLogger();

		public static Regex comment_r = new Regex("[#;].*$");
		public static Regex value_r = new Regex("[^A-Za-z0-9_].*$");
		public const string cmd_SAVE_CONFIG_help = "Overwrite config file and restart";
		private Machine printer;

		public PrinterConfig(Machine printer)
		{
			//this.printer = printer;
			//this.autosave = null;
			//var gcode = this.printer.lookup_object("gcode");
			//gcode.register_command("SAVE_CONFIG", this.cmd_SAVE_CONFIG, desc: cmd_SAVE_CONFIG_help);
		}

		internal MachineConfig read_main_config()
		{
			throw new NotImplementedException();
		}

		internal void log_config(object config)
		{
			throw new NotImplementedException();
		}

		internal void check_unused_options(object config)
		{
			throw new NotImplementedException();
		}

	}

}
