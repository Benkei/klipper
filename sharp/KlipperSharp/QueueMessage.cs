using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace KlipperSharp
{
	public unsafe struct QueueMessage
	{
		public fixed byte msg[SerialQueue.MESSAGE_MAX];
		public byte len;
		public double sent_time;
		public double receive_time;
	}
}
