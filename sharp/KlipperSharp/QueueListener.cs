using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp
{
	public class QueueListener
	{
		public QueueListener(object filename)
			 //: base(filename, when: "midnight", backupCount: 5)
		{
			//this.bg_queue = Queue.Queue();
			//this.bg_thread = threading.Thread(target: this._bg_thread);
			//this.bg_thread.start();
			//this.rollover_info = new Dictionary<object, object>
			//{
			//};
		}

		//private void _bg_thread()
		//{
		//	while (1)
		//	{
		//		var record = this.bg_queue.get(true);
		//		if (record == null)
		//		{
		//			break;
		//		}
		//		this.handle(record);
		//	}
		//}

		public void stop()
		{
			//this.bg_queue.put_nowait(null);
			//this.bg_thread.join();
		}

		public void set_rollover_info(string name, string info)
		{
			//this.rollover_info[name] = info;
		}

		public void clear_rollover_info()
		{
			//this.rollover_info.clear();
		}

		public void doRollover()
		{
			//logging.handlers.TimedRotatingFileHandler.doRollover(this);
			//var lines = (from name in this.rollover_info.OrderBy(_p_1 => _p_1).ToList()
			//				 select this.rollover_info[name]).ToList();
			//lines.append(String.Format("=============== Log rollover at %s ===============", time.asctime()));
			//this.emit(logging.makeLogRecord(new Dictionary<object, object> {
			//		 {
			//			  "msg",
			//			  "\n".join(lines)},
			//		 {
			//			  "level",
			//			  logging.INFO}}));
		}
	}
}
