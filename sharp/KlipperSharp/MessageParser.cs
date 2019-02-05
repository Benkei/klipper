using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.IO;
using System.IO.Compression;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;

namespace KlipperSharp
{
	public abstract class PT_Type
	{
		public bool is_integer = false;
		public int max_length = 5;
		public bool signed = false;

		public abstract void encode(BinaryWriter output, object value);
		public abstract (object v, int position) parse(byte[] text, int position);
	}
	public class PT_uint32 : PT_Type
	{
		public PT_uint32()
		{
			is_integer = true;
		}
		public override void encode(BinaryWriter output, object value)
		{
			var v = (int)value;
			if (v >= 0xc000000 || v < -0x4000000) output.Write((byte)((v >> 28) & 0x7f | 0x80));
			if (v >= 0x180000 || v < -0x80000) output.Write((byte)((v >> 21) & 0x7f | 0x80));
			if (v >= 0x3000 || v < -0x1000) output.Write((byte)((v >> 14) & 0x7f | 0x80));
			if (v >= 0x60 || v < -0x20) output.Write((byte)((v >> 7) & 0x7f | 0x80));
			output.Write((byte)(v & 0x7f));
		}
		public override (object v, int position) parse(byte[] text, int position)
		{
			var c = text[position];
			position++;
			var v = c & 0x7f;
			if ((c & 0x60) == 0x60)
				v |= -0x20;
			while ((c & 0x80) > 0)
			{
				c = text[position];
				position++;
				v = (v << 7) | (c & 0x7f);
			}
			if (!signed)
				v = (int)(v & 0xffffffff);
			return (v, position);
		}
	}
	public class PT_int32 : PT_uint32
	{
		public PT_int32()
		{
			signed = true;
		}
	}
	public class PT_uint16 : PT_uint32
	{
		public PT_uint16()
		{
			max_length = 3;
		}
	}
	public class PT_int16 : PT_int32
	{
		public PT_int16()
		{
			signed = true;
			max_length = 3;
		}
	}
	public class PT_byte : PT_uint32
	{
		public PT_byte()
		{
			max_length = 2;
		}
	}
	public class PT_string : PT_Type
	{
		public PT_string()
		{
			is_integer = false;
			max_length = 64;
		}
		public override void encode(BinaryWriter output, object value)
		{
			output.Write((byte)((string)value).Length);
			output.Write((string)value);
		}
		public override (object v, int position) parse(byte[] text, int position)
		{
			var l = text[position];
			var txt = Encoding.ASCII.GetString(text, position + 1, position + 1 + l);
			return (txt, position + l + 1);
		}
	}
	public class PT_progmem_buffer : PT_string
	{ }
	public class PT_buffer : PT_string
	{ }

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
			writer.Write(Msgid);
			for (int i = 0; i < Param_types.Count; i++)
			{
				Param_types[i].encode(writer, parameters[i]);
			}
		}
		public virtual void Encode_by_name(Dictionary<string, object> parameters, BinaryWriter writer)
		{
			writer.Write(Msgid);
			for (int i = 0; i < Param_names.Count; i++)
			{
				Param_names[i].Type.encode(writer, parameters[Param_names[i].Name]);
			}
		}
		public abstract (Dictionary<string, object> msg, int pos) Parse(byte[] s, int pos);
		public abstract string Format_params(Dictionary<string, string> parameters);
	}
	public class MessageFormat : BaseFormat
	{
		internal static Regex format = new Regex(@"(^\s*(?<CMD>[\w]+))|(\s*(?<PARAM>[\w]+)=(?<VALUE>[\w\%\.]+)\s*)",
			RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture);

		public Dictionary<string, PT_Type> Name_to_type;

		public MessageFormat(int msgid, string msgformat)
		{
			Msgid = msgid;
			Msgformat = msgformat;
			Debugformat = MessageParser.Convert_msg_format(msgformat);
			Param_types = new List<PT_Type>();
			Param_names = new List<(string, PT_Type)>();
			Name_to_type = new Dictionary<string, PT_Type>();

			var match = format.Match(msgformat);
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

			//var parts = msgformat.Split(' ');
			//Name = parts[0];
			//List<string> argparts = new List<string>();
			//for (int i = 1; i < parts.Length; i++)
			//{
			//	argparts.AddRange(parts[i].Split('='));
			//}
			//for (int i = 0; i < argparts.Count; i += 2)
			//{
			//	var handle = MessageParser.MessageTypes[argparts[i + 1]];
			//	Param_types.Add(handle);
			//	Param_names.Add((argparts[i], handle));
			//	Name_to_type.Add(argparts[i], handle);
			//}
		}

		public override (Dictionary<string, object> msg, int pos) Parse(byte[] s, int pos)
		{
			pos++;
			var output = new Dictionary<string, object>();
			for (int i = 0; i < Param_names.Count; i++)
			{
				output[Param_names[i].Name] = Param_names[i].Type.parse(s, pos);
			}
			return (output, pos);
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
			return string.Format(Debugformat, "");
		}
	}
	public class OutputFormat : BaseFormat
	{
		public OutputFormat(int msgid, string msgformat)
		{
			Name = "#output";
			this.Msgid = msgid;
			this.Msgformat = msgformat;
			this.Debugformat = MessageParser.Convert_msg_format(msgformat);
			this.Param_types = new List<PT_Type>();
			var args = msgformat;
			while (true)
			{
				var pos = args.IndexOf('%');
				if (pos < 0)
					break;
				if (pos + 1 >= args.Length || args[pos + 1] != '%')
				{
					for (int i = 0; i < 4; i++)
					{
						PT_Type t;
						if (MessageParser.MessageTypes.TryGetValue(args.Substring(pos, ((pos + 1) + i)), out t))
						{
							this.Param_types.Add(t);
							break;
						}
					}
				}
				args = args.Substring(pos + 1);
			}
		}

		public override (Dictionary<string, object> msg, int pos) Parse(byte[] s, int pos)
		{
			pos++;
			var output = new List<int>();
			for (int i = 0; i < Param_types.Count; i++)
			{
				var data = Param_types[i].parse(s, pos);
				//if (!Param_types[i].is_integer)
				//	v = repr(v);
				//output.append(v);
			}
			//var outmsg = Debugformat % tuple(out);
			//return ((outmsg), pos);
			return (null, 0);
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
		public override (Dictionary<string, object> msg, int pos) Parse(byte[] s, int pos)
		{
			var msgid = s[pos];
			var msg = s;// str(bytearray(s));
			return (/*{ "#msgid": msgid, "#msg": msg}*/null, s.Length - MessageParser.MESSAGE_TRAILER_SIZE);
		}
		public override string Format_params(Dictionary<string, string> parameters)
		{
			return $"#unknown {parameters["#msg"]}";
		}
	}

	public class MessageParser
	{
		public const int MESSAGE_MIN = 5;
		public const int MESSAGE_MAX = 64;
		public const int MESSAGE_HEADER_SIZE = 2;
		public const int MESSAGE_TRAILER_SIZE = 3;
		public const int MESSAGE_POS_LEN = 0;
		public const int MESSAGE_POS_SEQ = 1;
		public const int MESSAGE_TRAILER_CRC = 3;
		public const int MESSAGE_TRAILER_SYNC = 1;
		public const int MESSAGE_PAYLOAD_MAX = MESSAGE_MAX - MESSAGE_MIN;
		public const int MESSAGE_SEQ_MASK = 0x0f;
		public const int MESSAGE_DEST = 0x10;
		public const byte MESSAGE_SYNC = (byte)'\x7E'; // ^

		public static Dictionary<int, string> DefaultMessages = new Dictionary<int, string>
		{
			{ 0, "identify_response offset=%u data=%.*s" },
			{ 1, "identify offset=%u count=%c" }
		};
		public static Dictionary<string, PT_Type> MessageTypes = new Dictionary<string, PT_Type>
		{
			{"%u" , new PT_uint32() }, {"%i",  new PT_int32() },
			{"%hu", new PT_uint16() }, {"%hi", new PT_int16() },
			{"%c" , new PT_byte()   },
			{"%s", new PT_string()  }, {"%.*s", new PT_progmem_buffer() }, {"%*s", new PT_buffer() }
		};
		private UnknownFormat unknown = new UnknownFormat();
		private List<int> command_ids = new List<int>();
		public Dictionary<int, BaseFormat> messages_by_id = new Dictionary<int, BaseFormat>();
		public Dictionary<string, BaseFormat> messages_by_name = new Dictionary<string, BaseFormat>();
		public Dictionary<int, string> static_strings = new Dictionary<int, string>();
		public Dictionary<string, string> config = new Dictionary<string, string>();
		public string version = "";
		public string build_versions = "";
		private byte[] raw_identify_data = null;

		[DataContract]
		public class IdentifyData
		{
			[DataMember]
			public Dictionary<int, string> messages;
			[DataMember]
			public List<int> commands;
			[DataMember]
			public List<int> responses;
			[DataMember]
			public Dictionary<int, string> static_strings;
			[DataMember]
			public Dictionary<string, string> config;
			[DataMember]
			public string version;
			[DataMember]
			public string build_versions;
		}

		public static int Crc16_ccitt(ReadOnlySpan<byte> buff)
		{
			int crc = 0xffff;
			for (int i = 0; i < buff.Length; i++)
			{
				int data = buff[i];
				data ^= crc & 0xff;
				data ^= (data & 0x0f) << 4;
				crc = ((data << 8) | (crc >> 8)) ^ (data >> 4) ^ (data << 3);
			}
			return crc;
		}

		// Update the message format to be compatible with python's % operator
		public static string Convert_msg_format(string msgformat)
		{
			var mf = msgformat.Replace("%c", "%u").Replace("%.*s", "%s").Replace("%*s", "%s");
			return mf;
		}


		public MessageParser()
		{
			this._init_messages(DefaultMessages, DefaultMessages.Keys.ToList());
		}

		public void _init_messages(Dictionary<int, string> messages, List<int> parsers)
		{
			foreach (var _tup_1 in messages)
			{
				var msgid = _tup_1.Key;
				var msgformat = _tup_1.Value;
				if (!parsers.Contains(msgid))
				{
					this.messages_by_id[msgid] = new OutputFormat(msgid, msgformat);
					continue;
				}
				var msg = new MessageFormat(msgid, msgformat);
				this.messages_by_id[msgid] = msg;
				this.messages_by_name[msg.Name] = msg;
			}
		}

		public object check_packet(byte[] s)
		{
			if (s.Length < MESSAGE_MIN)
			{
				return 0;
			}
			var msglen = s[MESSAGE_POS_LEN];
			if (msglen < MESSAGE_MIN || msglen > MESSAGE_MAX)
			{
				return -1;
			}
			var msgseq = s[MESSAGE_POS_SEQ];
			if ((msgseq & ~MESSAGE_SEQ_MASK) != MESSAGE_DEST)
			{
				return -1;
			}
			if (s.Length < msglen)
			{
				// Need more data
				return 0;
			}
			if (s[msglen - MESSAGE_TRAILER_SYNC] != MESSAGE_SYNC)
			{
				return -1;
			}
			var msgcrc = BitConverter.ToInt16(s, msglen - MESSAGE_TRAILER_CRC);
			var crc = Crc16_ccitt(new ReadOnlySpan<byte>(s, 0, msglen - MESSAGE_TRAILER_SIZE));
			if (crc != msgcrc)
			{
				//logging.debug("got crc %s vs %s", repr(crc), repr(msgcrc))
				return -1;
			}
			return msglen;
		}
		public List<object> dump(byte[] s)
		{
			var msgseq = s[MESSAGE_POS_SEQ];
			var @out = new List<string> { $"seq: {msgseq:X}" };
			var pos = MESSAGE_HEADER_SIZE;
			while (true)
			{
				var msgid = s[pos];
				BaseFormat mid = this.messages_by_id.Get(msgid, unknown);
				var _tup_1 = mid.Parse(s, pos);
				var parameter = _tup_1.Item1;
				pos = _tup_1.Item2;
				@out.Add(mid.Format_params(parameter));
				if (pos >= s.Length - MESSAGE_TRAILER_SIZE)
					break;
			}
			return @out;
		}

		public object format_params(object parameter)
		{
			//var name = parameter.get("#name");
			//var mid = this.messages_by_name.get(name);
			//if (mid != null)
			//{
			//	return mid.format_params(parameter);
			//}
			//var msg = parameter.get("#msg");
			//if (msg != null)
			//{
			//	return String.Format("%s %s", name, msg);
			//}
			//return parameter;
			return null;
		}

		public object parse(byte[] s)
		{
			var msgid = s[MESSAGE_HEADER_SIZE];
			BaseFormat mid;
			if (!this.messages_by_id.TryGetValue(msgid, out mid))
				mid = this.unknown;
			//var _tup_1 = mid.Parse(s, MESSAGE_HEADER_SIZE);
			//var parameter = _tup_1.Item1;
			//var pos = _tup_1.Item2;
			//if (pos != s.Length - MESSAGE_TRAILER_SIZE)
			//{
			//	throw new Exception("Extra data at end of message");
			//}
			//parameter["#name"] = mid.name;
			//var static_string_id = parameter.get("static_string_id");
			//if (static_string_id != null)
			//{
			//	parameter["#msg"] = this.static_strings.get(static_string_id, "?");
			//}
			//return parameter;
			return null;
		}

		public byte[] encode(int seq, byte[] cmd)
		{
			var msglen = MESSAGE_MIN + cmd.Length;
			seq = seq & MESSAGE_SEQ_MASK | MESSAGE_DEST;
			var @out = new List<byte> {
				(byte)msglen,
				(byte)seq
			};
			@out.AddRange(cmd);
			@out.AddRange(BitConverter.GetBytes((short)Crc16_ccitt(@out.ToArray())));
			@out.Add(MESSAGE_SYNC);
			return @out.ToArray();
		}

		public List<byte> _parse_buffer(string value)
		{
			if (value == null)
			{
				return new List<byte>();
			}
			//var tval = Convert.ToInt32(value, 16);
			//var @out = new List<object>();
			//foreach (var i in range(value.Count / 2))
			//{
			//	@out.append(tval & 255);
			//	tval >>= 8;
			//}
			//@out.reverse();
			//return @out;
			return null;
		}

		public BaseFormat lookup_command(string msgformat)
		{
			var parts = msgformat.Trim().Split();
			var msgname = parts[0];
			BaseFormat mp;
			if (!messages_by_name.TryGetValue(msgname, out mp))
			{
				throw new Exception($"Unknown command: {msgname}");
			}
			if (msgformat != mp.Msgformat)
			{
				throw new Exception($"Command format mismatch: {msgformat} vs {mp.Msgformat}");
			}
			return mp;
		}

		public void create_command(string msg, BinaryWriter writer)
		{
			if (string.IsNullOrWhiteSpace(msg))
				return;

			string msgName;
			Dictionary<string, string> msgParams = ParseMsgFormat(msg, out msgName);

			if (msgName == null || msgParams.Count == 0)
				return;

			BaseFormat mp;
			if (!messages_by_name.TryGetValue(msgName, out mp))
				throw new Exception($"Unknown command: {msgName}");

			Dictionary<string, object> argParts = new Dictionary<string, object>(msgParams.Count);
			try
			{
				foreach (var item in msgParams)
				{
					object tval;
					var t = ((MessageFormat)mp).Name_to_type[item.Key];
					if (t.is_integer)
					{
						tval = Convert.ToInt32(item.Value);
					}
					else
					{
						tval = this._parse_buffer(item.Value);
					}
					argParts[item.Key] = tval;
				}
			}
			catch
			{
				//traceback.print_exc()
				throw new Exception($"Unable to extract params from: {msgName}");
			}
			try
			{
				((MessageFormat)mp).Encode_by_name(argParts, writer);
			}
			catch
			{
				//traceback.print_exc()
				throw new Exception($"Unable to encode: {msgName}");
			}
		}

		private static Dictionary<string, string> ParseMsgFormat(string msg, out string msgName)
		{
			msgName = null;
			var msgParams = new Dictionary<string, string>();
			var match = MessageFormat.format.Match(msg);
			while (match.Success)
			{
				var cmd = match.Groups["CMD"];
				var param = match.Groups["PARAM"];
				var value = match.Groups["VALUE"];
				if (cmd.Success)
				{
					msgName = cmd.Value;
				}
				if (param.Success && value.Success)
				{
					msgParams.Add(param.Value, value.Value);
				}
				match = match.NextMatch();
			}
			return msgParams;
		}

		public void process_identify(byte[] data, bool decompress = true)
		{
			try
			{
				if (decompress)
				{
					data = ZlibDecompress(data);
					//data = zlib.decompress(data);
				}
				this.raw_identify_data = data;
				var identify_data = JsonLoad<IdentifyData>(data);

				//data = json.loads(data);
				var messages = identify_data.messages;
				var commands = identify_data.commands;
				var responses = identify_data.responses;
				this.command_ids = commands;
				this.static_strings = identify_data.static_strings ?? new Dictionary<int, string>();
				this.config = identify_data.config ?? new Dictionary<string, string>();
				this.version = identify_data.version ?? "";
				this.build_versions = identify_data.build_versions ?? "";

				var cmds = new List<int>();
				cmds.AddRange(commands);
				cmds.AddRange(responses);
				this._init_messages(messages, cmds);
			}
			catch (Exception e)
			{
				//logging.exception("process_identify error");
				throw new Exception($"Error during identify: {e}");
			}
		}

		private static byte[] ZlibDecompress(byte[] data)
		{
			var ms = new MemoryStream(512);
			using (var s = new GZipStream(new MemoryStream(data), CompressionLevel.Optimal))
			{
				var buffer = new byte[512];
				int read;
				do
				{
					read = s.Read(buffer, 0, 512);
					ms.Write(buffer, 0, read);
				} while (read > 0);
			}
			data = ms.ToArray();
			return data;
		}

		private static T JsonLoad<T>(byte[] data)
		{
			var ser = new DataContractJsonSerializer(typeof(T));
			return (T)ser.ReadObject(new MemoryStream(data));
		}

		public string get_constant(string name, string @default = "", object parser = null)
		{
			string value;
			if (!config.TryGetValue(name, out value))
			{
				if (@default == "")
				{
					throw new Exception($"Firmware constant {name}' not found");
				}
				value = @default;
			}
			return value;
		}

		public float get_constant_float(string name, float @default = 0)
		{
			var result = this.get_constant(name, "0");
			float value;
			if (!float.TryParse(result, out value))
				value = @default;
			return value;
		}

		public int get_constant_int(string name, int @default = 0)
		{
			var result = this.get_constant(name, "0");
			int value;
			if (!int.TryParse(result, out value))
				value = @default;
			return value;
		}




	}
}
