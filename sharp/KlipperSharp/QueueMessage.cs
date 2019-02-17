using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace KlipperSharp
{
	[StructLayout(LayoutKind.Explicit)]
	public unsafe struct QueueMessage
	{
		[FieldOffset(0)]
		public fixed byte msg[SerialQueue.MESSAGE_MAX];
		[FieldOffset(0 + SerialQueue.MESSAGE_MAX)]
		public byte len;
		[FieldOffset(1 + SerialQueue.MESSAGE_MAX)]
		public double sent_time;
		[FieldOffset(1 + SerialQueue.MESSAGE_MAX + 8)]
		public double receive_time;
	}
}
