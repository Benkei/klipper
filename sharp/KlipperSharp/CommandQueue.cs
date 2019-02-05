using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp
{
	public class command_queue
	{
		public Queue<queue_message> stalled_queue = new Queue<queue_message>();
		public Queue<queue_message> ready_queue = new Queue<queue_message>();
	}
}
