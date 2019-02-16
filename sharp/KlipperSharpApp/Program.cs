using CommandLine;
using KlipperSharp;
using NLog;
using NLog.Config;
using NLog.Targets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace KlipperSharpApp
{
	class Program
	{
		public class Options
		{
			[Option('i', "debuginput", Required = false, HelpText = "read commands from file instead of from tty port.")]
			public string Debuginput { get; set; }
			[Option('I', "input-tty", Required = false, HelpText = "input tty name (default is /tmp/printer).")]
			public bool Inputtty { get; set; }
			[Option('l', "logfile", Required = false, HelpText = "write log to file instead of stderr.")]
			public string Logfile { get; set; }
			[Option('v', "store_true", Required = false, HelpText = "enable debug messages.")]
			public bool Verbose { get; set; }
			[Option('o', "debugoutput", Required = false, HelpText = "write output to file instead of to serial port.")]
			public string Debugoutput { get; set; }
			[Option('d', "dictionary", Required = false, HelpText = "file to read for mcu protocol dictionary.")]
			public string Dictionary { get; set; }
		}


		static void Main(string[] args)
		{
			Options options = null;
			Parser.Default
				.ParseArguments<Options>(args)
				.WithParsed<Options>((opts) => options = opts)
				/*.WithNotParsed<Options>((errs) => HandleParseError(errs))*/;

			if (args.Length < 1)
			{
				//error
			}


			//var usage = "%prog [options] <config file>";
			//var opts = optparse.OptionParser(usage);
			//opts.add_option("-i", "--debuginput", dest: "debuginput", help: "read commands from file instead of from tty port");
			//opts.add_option("-I", "--input-tty", dest: "inputtty", @default: "/tmp/printer", help: "input tty name (default is /tmp/printer)");
			//opts.add_option("-l", "--logfile", dest: "logfile", help: "write log to file instead of stderr");
			//opts.add_option("-v", action: "store_true", dest: "verbose", help: "enable debug messages");
			//opts.add_option("-o", "--debugoutput", dest: "debugoutput", help: "write output to file instead of to serial port");
			//opts.add_option("-d", "--dictionary", dest: "dictionary", type: "string", action: "callback", callback: arg_dictionary, help: "file to read for mcu protocol dictionary");
			//var _tup_1 = opts.parse_args();
			//var options = _tup_1.Item1;
			//var args = _tup_1.Item2;
			//if (args.Length != 1)
			//{
			//	opts.error("Incorrect number of arguments");
			//}
			var start_args = new Dictionary<string, object> {
				{ "config_file", args[0] },
				{ "start_reason", "startup" }
			};
			Stream input_fd = null;
			var debuglevel = LogLevel.Info;
			object bglogger = null;
			if (options.Verbose)
			{
				debuglevel = LogLevel.Debug;
			}
			if (options.Debuginput != null)
			{
				start_args["debuginput"] = options.Debuginput;
				var debuginput = File.OpenRead(options.Debuginput);
				input_fd = debuginput;
			}
			else
			{
				//input_fd = util.create_pty(options.Inputtty);
			}
			if (options.Debugoutput != null)
			{
				start_args["debugoutput"] = options.Debugoutput;
				//start_args.update(options.Dictionary);
			}
			//if (options.Logfile != null)
			//{
			//	bglogger = queuelogger.setup_bg_logging(options.Logfile, debuglevel);
			//}

			var config = new LoggingConfiguration();
			var logfile = new FileTarget("logfile") { FileName = "file.txt" };
			var logconsole = new ConsoleTarget("logconsole");

			config.AddRule(LogLevel.Info, LogLevel.Fatal, logconsole);
			config.AddRule(LogLevel.Debug, LogLevel.Fatal, logfile);

			LogManager.Configuration = config;


			var logging = LogManager.GetCurrentClassLogger();
			logging.Info("Starting Klippy...");
			start_args["software_version"] = 1.0;//util.get_git_version();
			if (bglogger != null)
			{
				//var versions = string.Join("\n", new List<string> {
				//	 String.Format("Args: %s", sys.argv),
				//	 String.Format("Git version: %s", repr(start_args["software_version"])),
				//	 String.Format("CPU: %s", util.get_cpu_info()),
				//	 String.Format("Python: %s", repr(sys.version))
				//});
				//logging.Info(versions);
			}
			// Start Printer() class
			string res;
			while (true)
			{
				if (bglogger != null)
				{
					//bglogger.clear_rollover_info();
					//bglogger.set_rollover_info("versions", versions);
				}
				var printer = new Machine(input_fd, null/*bglogger*/, start_args);
				res = printer.run();
				if ("exit" == res || "error_exit" == res)
				{
					break;
				}
				Thread.Sleep(1000);
				logging.Info("Restarting printer");
				start_args["start_reason"] = res;
			}
			if (bglogger != null)
			{
				//bglogger.stop();
			}
			if (res == "error_exit")
			{
				Environment.ExitCode = -1;
			}
		}
	}
}
