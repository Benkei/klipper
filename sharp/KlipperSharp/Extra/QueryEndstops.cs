using KlipperSharp.MachineCodes;
using KlipperSharp.MicroController;
using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp.Extra
{
	public class QueryEndstops
	{
		public const string cmd_QUERY_ENDSTOPS_help = "Report on the status of each endstop";
		private Machine printer;
		private List<(Mcu_endstop endstop, string name)> endstops;

		public QueryEndstops(ConfigWrapper config)
		{
			this.printer = config.get_printer();
			this.endstops = new List<(Mcu_endstop endstop, string name)>();
			var gcode = this.printer.lookup_object<GCodeParser>("gcode");
			gcode.register_command("QUERY_ENDSTOPS", cmd_QUERY_ENDSTOPS, desc: cmd_QUERY_ENDSTOPS_help);
			gcode.register_command("M119", cmd_QUERY_ENDSTOPS);
		}

		public void register_endstop(Mcu_endstop mcu_endstop, string name)
		{
			this.endstops.Add((mcu_endstop, name));
		}

		public void cmd_QUERY_ENDSTOPS(Dictionary<string, object> parameters)
		{
			var toolhead = this.printer.lookup_object<ToolHead>("toolhead");
			var print_time = toolhead.get_last_move_time();
			// Query the endstops
			foreach (var item in this.endstops)
			{
				item.endstop.query_endstop(print_time);
			}
			var @out = new List<(string name, bool enable)>();
			foreach (var item in this.endstops)
			{
				@out.Add((item.name, item.endstop.query_endstop_wait()));
			}
			// Report results
			string msg = "";
			foreach (var item in @out)
			{
				var state = item.enable ? "TRIGGERED" : "open";
				msg += $"{item.name}:{state} ";
			}
			var gcode = this.printer.lookup_object<GCodeParser>("gcode");
			gcode.respond(msg);
		}

		public static QueryEndstops load_config(ConfigWrapper config)
		{
			return new QueryEndstops(config);
		}
	}
}
