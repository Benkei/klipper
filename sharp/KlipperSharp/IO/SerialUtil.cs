using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace KlipperSharp
{
	public static class SerialUtil
	{
		public static int encode(this MemoryStream stream, int value)
		{
			var write = 1;
			var v = (int)value;
			if (v >= 0xc000000 || v < -0x4000000) { stream.WriteByte((byte)((v >> 28) & 0x7f | 0x80)); write++; }
			if (v >= 0x0180000 || v < -0x0080000) { stream.WriteByte((byte)((v >> 21) & 0x7f | 0x80)); write++; }
			if (v >= 0x0003000 || v < -0x0001000) { stream.WriteByte((byte)((v >> 14) & 0x7f | 0x80)); write++; }
			if (v >= 0x0000060 || v < -0x0000020) { stream.WriteByte((byte)((v >> 07) & 0x7f | 0x80)); write++; }
			stream.WriteByte((byte)(v & 0x7f));
			return write;
		}
		public static int parse(this MemoryStream stream, bool signed)
		{
			var c = stream.ReadByte();
			var v = c & 0x7f;
			if ((c & 0x60) == 0x60)
				v |= -0x20;
			while ((c & 0x80) > 0)
			{
				c = stream.ReadByte();
				v = (v << 7) | (c & 0x7f);
			}
			if (!signed)
				v = (int)(v & 0xffffffff);
			return v;
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

	}
}
