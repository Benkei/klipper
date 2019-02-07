using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace KlipperSharp
{
	public delegate double ReactorAction(double eventtime);

	public class ReactorTimer
	{
		public readonly ReactorAction callback;
		public double waketime;

		public ReactorTimer(ReactorAction callback, double waketime)
		{
			this.callback = callback;
			this.waketime = waketime;
		}
	}

	public class ReactorCallback
	{
		private SelectReactor reactor;
		private ReactorTimer timer;
		private ReactorAction callback;

		public ReactorCallback(SelectReactor reactor, ReactorAction callback, double waketime)
		{
			this.reactor = reactor;
			this.timer = reactor.register_timer(this.invoke, waketime);
			this.callback = callback;
		}

		public double invoke(double eventtime)
		{
			this.reactor.unregister_timer(this.timer);
			this.callback(eventtime);
			return SelectReactor.NEVER;
		}
	}

	public class ReactorFileHandler
	{
		private object fd;
		private object callback;

		public ReactorFileHandler(object fd, object callback)
		{
			this.fd = fd;
			this.callback = callback;
		}

		public object fileno()
		{
			return fd;
		}
	}

	public class ReactorGreenlet //: greenlet.greenlet
	{

		public ReactorGreenlet(object run)
		//: base(run: run)
		{
			//this.timer = null;
		}
	}

	public class SelectReactor
	{
		public const double NOW = 0.0;
		public const double NEVER = 9999999999999999.0;

		protected bool _process;
		protected Func<double> monotonic;
		private List<ReactorTimer> _timers;
		private double _next_timer;
		private object _pipe_fds;
		private Queue<ReactorAction> _async_queue;
		private List<ReactorFileHandler> _fds;
		private object _g_dispatch;
		private List<object> _greenlets;

		public SelectReactor()
		{
			// Main code
			_process = false;
			monotonic = () => HighResolutionTime.Now; //chelper.get_ffi()[1].get_monotonic;
																	// Timers
			_timers = new List<ReactorTimer>();
			_next_timer = NEVER;
			// Callbacks
			_pipe_fds = null;
			_async_queue = new Queue<ReactorAction>();
			// File descriptors
			_fds = new List<ReactorFileHandler>();
			// Greenlets
			_g_dispatch = null;
			_greenlets = new List<object>();
		}

		~SelectReactor()
		{
			if (this._pipe_fds != null)
			{
				//os.close(this._pipe_fds[0]);
				//os.close(this._pipe_fds[1]);
				this._pipe_fds = null;
			}
		}

		// Timers
		public void _note_time(ReactorTimer t)
		{
			var nexttime = t.waketime;
			if (nexttime < this._next_timer)
			{
				_next_timer = nexttime;
			}
		}

		public void update_timer(ReactorTimer t, double nexttime)
		{
			t.waketime = nexttime;
			_note_time(t);
		}

		public ReactorTimer register_timer(ReactorAction callback, double waketime = NEVER)
		{
			var handler = new ReactorTimer(callback, waketime);
			var timers = _timers;
			timers.Add(handler);
			_timers = timers;
			_note_time(handler);
			return handler;
		}

		public void unregister_timer(ReactorTimer handler)
		{
			var timers = _timers;
			timers.Remove(handler);
			_timers = timers;
		}

		public double _check_timers(double eventtime)
		{
			if (eventtime < _next_timer)
			{
				return Math.Min(1.0, Math.Max(0.001, _next_timer - eventtime));
			}
			_next_timer = NEVER;
			var g_dispatch = _g_dispatch;
			foreach (var t in _timers)
			{
				if (eventtime >= t.waketime)
				{
					t.waketime = NEVER;
					t.waketime = t.callback(eventtime);
					if (g_dispatch != _g_dispatch)
					{
						_end_greenlet(g_dispatch);
						return 0.0;
					}
				}
				_note_time(t);
			}
			if (eventtime >= _next_timer)
			{
				return 0.0;
			}
			return Math.Min(1.0, Math.Max(0.001, _next_timer - monotonic()));
		}

		// Callbacks
		public void register_callback(ReactorAction callback, double waketime = NOW)
		{
			new ReactorCallback(this, callback, waketime);
		}

		public void register_async_callback(ReactorAction callback)
		{
			_async_queue.Enqueue(callback);
			try
			{
				//os.write(this._pipe_fds[1], ".");
			}
			catch
			{
			}
		}

		public void _got_pipe_signal(double eventtime)
		{
			try
			{
				//os.read(this._pipe_fds[0], 4096);
			}
			catch
			{
			}
			while (true)
			{
				ReactorAction callback;
				try
				{
					callback = _async_queue.Dequeue();
				}
				catch
				{
					break;
				}
				new ReactorCallback(this, callback, NOW);
			}
		}

		public void _setup_async_callbacks()
		{
			//_pipe_fds = os.pipe();
			//util.set_nonblock(_pipe_fds[0]);
			//util.set_nonblock(_pipe_fds[1]);
			//register_fd(_pipe_fds[0], _got_pipe_signal);
		}

		// Greenlets
		public double _sys_pause(double waketime)
		{
			// Pause using system sleep for when reactor not running
			var delay = waketime - monotonic();
			if (delay > 0.0)
			{
				Thread.Sleep(TimeSpan.FromSeconds(delay));
			}
			return monotonic();
		}

		public double pause(double waketime)
		{
			//object g_next;
			//var g = greenlet.getcurrent();
			//if (g != _g_dispatch)
			//{
			//	if (_g_dispatch == null)
			//	{
			//		return _sys_pause(waketime);
			//	}
			//	return _g_dispatch.@switch(waketime);
			//}
			//if (_greenlets)
			//{
			//	g_next = _greenlets.pop();
			//}
			//else
			//{
			//	g_next = new ReactorGreenlet(_dispatch_loop);
			//}
			//g_next.parent = g.parent;
			//g.timer = register_timer(g.@switch, waketime);
			//return g_next.@switch();
			throw new NotImplementedException();
		}

		public void _end_greenlet(object g_old)
		{
			//// Cache this greenlet for later use
			//_greenlets.Add(g_old);
			//this.unregister_timer(g_old.timer);
			//g_old.timer = null;
			//// Switch to existing dispatch
			//_g_dispatch.@switch(NEVER);
			//// This greenlet was reactivated - prepare for main processing loop
			//_g_dispatch = g_old;
			throw new NotImplementedException();
		}

		// File descriptors
		public virtual ReactorFileHandler register_fd(object fd, object callback)
		{
			var handler = new ReactorFileHandler(fd, callback);
			_fds.Add(handler);
			return handler;
		}

		public virtual void unregister_fd(ReactorFileHandler handler)
		{
			_fds.Remove(handler);
		}

		// Main loop
		public virtual void _dispatch_loop()
		{
			//var _g_dispatch = this.g_dispatch = greenlet.getcurrent();
			//var eventtime = monotonic();
			//while (_process)
			//{
			//	var timeout = _check_timers(eventtime);
			//	var res = select.select(_fds, new List<object>(), new List<object>(), timeout);
			//	eventtime = monotonic();
			//	foreach (var fd in res[0])
			//	{
			//		fd.callback(eventtime);
			//		if (g_dispatch != this.g_dispatch)
			//		{
			//			this._end_greenlet(g_dispatch);
			//			eventtime = monotonic();
			//			break;
			//		}
			//	}
			//}
			//_g_dispatch = null;
			throw new NotImplementedException();
		}

		public void run()
		{
			//if (_pipe_fds == null)
			//{
			//	_setup_async_callbacks();
			//}
			//_process = true;
			//var g_next = new ReactorGreenlet(new Action(_dispatch_loop));
			//g_next.@switch();
			throw new NotImplementedException();
		}

		public void end()
		{
			_process = false;
		}
	}
	/*
	public class PollReactor : SelectReactor
	{

		public PollReactor()
		{
			this._poll = select.poll();
			this._fds = new Dictionary<object, object>
			{
			};
		}

		// File descriptors
		public override ReactorFileHandler register_fd(object fd, object callback)
		{
			var handler = new ReactorFileHandler(fd, callback);
			var fds = _fds.copy();
			fds[fd] = callback;
			_fds = fds;
			_poll.register(handler, select.POLLIN | select.POLLHUP);
			return handler;
		}

		public override void unregister_fd(ReactorFileHandler handler)
		{
			this._poll.unregister(handler);
			var fds = this._fds.copy();
			fds.Remove(handler.fd);
			this._fds = fds;
		}

		// Main loop
		public override object _dispatch_loop()
		{
			var g_dispatch = this._g_dispatch = greenlet.getcurrent();
			var eventtime = monotonic();
			while (_process)
			{
				var timeout = _check_timers(eventtime);
				var res = this._poll.poll(Convert.ToInt32(Math.Ceiling(timeout * 1000.0)));
				eventtime = monotonic();
				foreach (var _tup_1 in res)
				{
					var fd = _tup_1.Item1;
					var @event = _tup_1.Item2;
					this._fds[fd](eventtime);
					if (g_dispatch != this._g_dispatch)
					{
						this._end_greenlet(g_dispatch);
						eventtime = monotonic();
						break;
					}
				}
			}
			this._g_dispatch = null;
		}
	}

	public class EPollReactor : SelectReactor
	{
		public EPollReactor()
		{
			this._epoll = select.epoll();
			this._fds = new Dictionary<object, object>
			{
			};
		}

		// File descriptors
		public override ReactorFileHandler register_fd(object fd, object callback)
		{
			var handler = new ReactorFileHandler(fd, callback);
			var fds = this._fds.copy();
			fds[fd] = callback;
			this._fds = fds;
			this._epoll.register(fd, select.EPOLLIN | select.EPOLLHUP);
			return handler;
		}

		public override void unregister_fd(ReactorFileHandler handler)
		{
			this._epoll.unregister(handler.fd);
			var fds = this._fds.copy();
			fds.Remove(handler.fd);
			this._fds = fds;
		}

		// Main loop
		public override object _dispatch_loop()
		{
			var _g_dispatch = this._g_dispatch = greenlet.getcurrent();
			var eventtime = monotonic();
			while (_process)
			{
				var timeout = _check_timers(eventtime);
				var res = this._epoll.poll(timeout);
				eventtime = monotonic();
				foreach (var _tup_1 in res)
				{
					var fd = _tup_1.Item1;
					var @event = _tup_1.Item2;
					this._fds[fd](eventtime);
					if (g_dispatch != this._g_dispatch)
					{
						this._end_greenlet(g_dispatch);
						eventtime = monotonic();
						break;
					}
				}
			}
			this._g_dispatch = null;
		}
	}
	*/
}
