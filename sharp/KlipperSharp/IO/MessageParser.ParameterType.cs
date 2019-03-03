using System;
using System.IO;
using System.Text;

namespace KlipperSharp
{
	public abstract class PT_Type
	{
		public bool is_integer = false;
		public int max_length = 5;
		public bool signed = false;

		public abstract void encode(BinaryWriter output, object value);
		public abstract object parse(ReadOnlySpan<byte> text, ref int position);
	}
	public class PT_uint32 : PT_Type
	{
		public PT_uint32()
		{
			is_integer = true;
		}
		public override void encode(BinaryWriter output, object value)
		{
			var v = Convert.ToInt64(value);
			if (v >= 0xc000000 || v < -0x4000000) output.Write((byte)((v >> 28) & 0x7f | 0x80));
			if (v >= 0x180000 || v < -0x80000) output.Write((byte)((v >> 21) & 0x7f | 0x80));
			if (v >= 0x3000 || v < -0x1000) output.Write((byte)((v >> 14) & 0x7f | 0x80));
			if (v >= 0x60 || v < -0x20) output.Write((byte)((v >> 7) & 0x7f | 0x80));
			output.Write((byte)(v & 0x7f));
		}
		public override object parse(ReadOnlySpan<byte> data, ref int position)
		{
			var c = data[position++];
			uint v = c & 0x7fu;
			if ((c & 0x60) == 0x60)
				v |= unchecked((uint)-0x20);
			while ((c & 0x80) > 0)
			{
				c = data[position++];
				v = (v << 7) | (c & 0x7fu);
			}
			if (signed)
				return (int)v;
			else
				return v;
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
			max_length = SerialQueue.MESSAGE_MAX;
		}
		public override void encode(BinaryWriter output, object value)
		{
			output.Write((byte)((string)value).Length);
			output.Write((string)value);
		}
		public unsafe override object parse(ReadOnlySpan<byte> text, ref int position)
		{
			var l = text[position];
			byte* block = stackalloc byte[l];
			text[Range.Create(position + 1, position + 1 + l)].CopyTo(new Span<byte>(block, l));
			var txt = Encoding.ASCII.GetString(block, l);
			position += l + 1;
			return txt;
		}
	}
	public class PT_progmem_buffer : PT_buffer
	{ }
	public class PT_buffer : PT_Type
	{
		public PT_buffer()
		{
			is_integer = false;
			max_length = SerialQueue.MESSAGE_MAX;
		}
		public override void encode(BinaryWriter output, object value)
		{
			output.Write((byte)((byte[])value).Length);
			output.Write((byte[])value);
		}
		public override object parse(ReadOnlySpan<byte> text, ref int position)
		{
			var l = text[position];
			byte[] block = new byte[l];
			text[Range.Create(position + 1, position + 1 + l)].CopyTo(new Span<byte>(block));
			position += l + 1;
			return block;
		}
	}
}
