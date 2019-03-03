using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace KlipperSharp
{
	public abstract class BaseFormat
	{
		public string Name;
		public int Msgid;
		public string Msgformat;
		public string Debugformat;
		public List<PT_Type> Param_types;
		public List<(string Name, PT_Type Type)> Param_names;

		public virtual void Encode(object[] parameters, BinaryWriter writer)
		{
			writer.Write((byte)Msgid);
			for (int i = 0; i < Param_types.Count; i++)
			{
				Param_types[i].encode(writer, parameters[i]);
			}
		}
		public virtual void Encode_by_name(Dictionary<string, object> parameters, BinaryWriter writer)
		{
			writer.Write((byte)Msgid);
			for (int i = 0; i < Param_names.Count; i++)
			{
				Param_names[i].Type.encode(writer, parameters[Param_names[i].Name]);
			}
		}
		public abstract Dictionary<string, object> Parse(ReadOnlySpan<byte> s, ref int pos);
		public abstract string Format_params(Dictionary<string, string> parameters);
	}
	public class MessageFormat : BaseFormat
	{
		public Dictionary<string, PT_Type> Name_to_type;

		public MessageFormat(int msgid, string msgformat)
		{
			Msgid = msgid;
			Msgformat = msgformat;
			Debugformat = MessageParser.Convert_msg_format(msgformat);
			Param_types = new List<PT_Type>();
			Param_names = new List<(string, PT_Type)>();
			Name_to_type = new Dictionary<string, PT_Type>();

			var match = MessageParser.MessageFormatRegex.Match(msgformat);
			while (match.Success)
			{
				var cmd = match.Groups["CMD"];
				var param = match.Groups["PARAM"];
				var value = match.Groups["VALUE"];
				if (cmd.Success)
				{
					Name = cmd.Value;
				}
				if (param.Success && value.Success)
				{
					var handle = MessageParser.MessageTypes[value.Value];
					Param_types.Add(handle);
					Param_names.Add((param.Value, handle));
					Name_to_type.Add(param.Value, handle);
				}
				match = match.NextMatch();
			}
		}

		public override Dictionary<string, object> Parse(ReadOnlySpan<byte> s, ref int pos)
		{
			pos++;
			var output = new Dictionary<string, object>(Param_names.Count);
			for (int i = 0; i < Param_names.Count; i++)
			{
				output[Param_names[i].Name] = Param_names[i].Type.parse(s, ref pos);
			}
			return output;
		}
		public override string Format_params(Dictionary<string, string> parameters)
		{
			var result = new List<object>(1 + Param_names.Count);
			for (int i = 0; i < Param_names.Count; i++)
			{
				var v = parameters[Param_names[i].Name];
				if (!Param_names[i].Type.is_integer)
				{
					// v = repr(v)
				}
				result.Add(v);
			}
			return string.Format(Debugformat, result.ToArray());
		}
	}
	public class OutputFormat : BaseFormat
	{
		public OutputFormat(int msgid, string msgformat)
		{
			Name = "#output";
			Msgid = msgid;
			Msgformat = msgformat;
			Debugformat = MessageParser.Convert_msg_format(msgformat);
			Param_types = new List<PT_Type>();

			var match = MessageParser.MessageFormatRegex.Match(msgformat);
			while (match.Success)
			{
				var cmd = match.Groups["CMD"];
				var param = match.Groups["PARAM"];
				var value = match.Groups["VALUE"];
				if (cmd.Success)
				{
					Name = cmd.Value;
				}
				if (param.Success && value.Success)
				{
					var handle = MessageParser.MessageTypes[value.Value];
					Param_types.Add(handle);
				}
				match = match.NextMatch();
			}
		}

		public override Dictionary<string, object> Parse(ReadOnlySpan<byte> s, ref int pos)
		{
			pos++;
			var output = new string[Param_types.Count];
			for (int i = 0; i < Param_types.Count; i++)
			{
				var data = Param_types[i].parse(s, ref pos);
				if (data == null)
					data = "null";
				output[i] = data as string;
			}
			var outmsg = string.Format(Debugformat, output);
			var dict = new Dictionary<string, object>(1) {
				{ "#msg", outmsg }
			};
			return dict;
		}
		public override string Format_params(Dictionary<string, string> parameters)
		{
			return $"#output {parameters["#msg"]}";
		}
	}
	public class UnknownFormat : BaseFormat
	{
		public UnknownFormat()
		{
			Name = "#unknown";
		}
		public unsafe override Dictionary<string, object> Parse(ReadOnlySpan<byte> s, ref int pos)
		{
			var msgid = s[pos];

			byte* block = stackalloc byte[s.Length];
			s.CopyTo(new Span<byte>(block, s.Length));
			var msg = Encoding.ASCII.GetString(block, s.Length);

			pos += s.Length - MessageParser.MESSAGE_TRAILER_SIZE;
			var output = new Dictionary<string, object>(2) {
				{ "#msgid", msgid },
				{ "#msg", msg }
			};
			return output;
		}
		public override string Format_params(Dictionary<string, string> parameters)
		{
			return $"#unknown {parameters["#msg"]}";
		}
	}
}
