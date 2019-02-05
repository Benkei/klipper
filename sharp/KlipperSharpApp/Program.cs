using CommandLine;
using System;

namespace KlipperSharpApp
{
	class Program
	{
		public class Options
		{
			[Option('i', "debuginput", Required = false, HelpText = "read commands from file instead of from tty port.")]
			public bool Debuginput { get; set; }
			[Option('I', "input-tty", Required = false, HelpText = "input tty name (default is /tmp/printer).")]
			public bool Inputtty { get; set; }
			[Option('l', "logfile", Required = false, HelpText = "write log to file instead of stderr.")]
			public bool Logfile { get; set; }
			[Option('v', "store_true", Required = false, HelpText = "enable debug messages.")]
			public bool Verbose { get; set; }
			[Option('o', "debugoutput", Required = false, HelpText = "write output to file instead of to serial port.")]
			public bool Debugoutput { get; set; }
			[Option('d', "dictionary", Required = false, HelpText = "file to read for mcu protocol dictionary.")]
			public bool Dictionary { get; set; }
		}


		static void Main(string[] args)
		{
			//CommandLine.Parser.Default
			//	.ParseArguments<Options>(args)
			//	.WithParsed<Options>(opts => RunOptionsAndReturnExitCode(opts))
			//	.WithNotParsed<Options>((errs) => HandleParseError(errs));

			if (args.Length < 1)
			{
				//error
			}

		}
	}
}
