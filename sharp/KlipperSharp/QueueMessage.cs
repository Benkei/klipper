using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace KlipperSharp
{
	[StructLayout(LayoutKind.Explicit)]
	public unsafe struct queue_message
	{
		[FieldOffset(0)]
		public byte len;
		[FieldOffset(1)]
		public fixed byte msg[SerialQueue.MESSAGE_MAX];

		[FieldOffset(1 + SerialQueue.MESSAGE_MAX)]
		// Filled when on a command queue
		public ulong min_clock;
		[FieldOffset(1 + SerialQueue.MESSAGE_MAX + 8)]
		public ulong req_clock;

		// Filled when in sent/receive queues
		[FieldOffset(1 + SerialQueue.MESSAGE_MAX)]
		public double sent_time;
		[FieldOffset(1 + SerialQueue.MESSAGE_MAX + 8)]
		public double receive_time;

		public static queue_message Create(byte[] data, byte len)
		{
			queue_message qm = new queue_message();
			fixed (byte* pData = data)
				Buffer.MemoryCopy(pData, qm.msg, SerialQueue.MESSAGE_MAX, len);
			qm.len = len;
			return qm;
		}
		public static queue_message Create(byte* data, byte len)
		{
			queue_message qm = new queue_message();
			Buffer.MemoryCopy(data, qm.msg, SerialQueue.MESSAGE_MAX, len);
			qm.len = len;
			return qm;
		}
		// Allocate a queue_message and fill it with a series of encoded vlq integers
		public static queue_message CreateAndEncode(uint[] data, int len)
		{
			queue_message qm = new queue_message();
			int i;
			byte* p = qm.msg;
			for (i = 0; i < len; i++)
			{
				p = Encode_int(p, (int)data[i]);
				if (p > &qm.msg[SerialQueue.MESSAGE_PAYLOAD_MAX])
					goto fail;
			}
			qm.len = (byte)(p - qm.msg);
			return qm;

		fail:
			//errorf("Encode error");
			qm.len = 0;
			return qm;
		}

		// Encode an integer as a variable length quantity (vlq)
		static byte* Encode_int(byte* p, int v)
		{
			int sv = v;
			if (sv < (3L << 5) && sv >= -(1L << 5)) goto f4;
			if (sv < (3L << 12) && sv >= -(1L << 12)) goto f3;
			if (sv < (3L << 19) && sv >= -(1L << 19)) goto f2;
			if (sv < (3L << 26) && sv >= -(1L << 26)) goto f1;
			*p++ = (byte)((v >> 28) | 0x80);
		f1: *p++ = (byte)(((v >> 21) & 0x7f) | 0x80);
		f2: *p++ = (byte)(((v >> 14) & 0x7f) | 0x80);
		f3: *p++ = (byte)(((v >> 7) & 0x7f) | 0x80);
		f4: *p++ = (byte)(v & 0x7f);
			return p;
		}

	}
}
