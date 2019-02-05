using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace KlipperSharp.MachineCodes
{
	public class MachineCodeParser
	{
		public static Regex CodeRegex = new Regex(
			@"(?<IGNORE>[\;])|(^[N](?<LINENUMBER>[0-9]*))|(?<CMD>(?<CMDPREFIX>[A-Z]+)\s*(?<CMDCODE>[+-]?[0-9]*\.?[0-9]+))",
			RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture);

		public void Process(string line, MachineCode result)
		{
			result.Linenumber = 0;
			result.Command = new Command();
			result.Parameters.Clear();
			var match = CodeRegex.Match(line);
			while (match.Success)
			{
				if (match.Groups["IGNORE"].Success)
				{
					break;
				}
				if (match.Groups["LINENUMBER"].Success)
				{
					int.TryParse(match.Groups["LINENUMBER"].Value, out result.Linenumber);
				}
				if (match.Groups["CMD"].Success)
				{
					Command cmd;
					cmd.Code = match.Groups["CMDPREFIX"].Value.ToUpperInvariant();
					double.TryParse(match.Groups["CMDCODE"].Value,
						System.Globalization.NumberStyles.Float,
						System.Globalization.CultureInfo.InvariantCulture,
						out cmd.Value);

					if (result.Command.Code == null)
					{
						result.Command = cmd;
					}
					else
					{
						result.Parameters.Add(cmd);
					}
				}

				match = match.NextMatch();
			}
		}
	}

	public struct Command
	{
		public string Code;
		public double Value;
	}

	public class MachineCode
	{
		public int Linenumber;
		public Command Command;
		public List<Command> Parameters = new List<Command>();
	}
}
