using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp
{
	public class command_queue
	{
		public Queue<SerialQueue.RawMessage> stalled_queue = new Queue<SerialQueue.RawMessage>();
		public Queue<SerialQueue.RawMessage> ready_queue = new Queue<SerialQueue.RawMessage>();
	}
}
