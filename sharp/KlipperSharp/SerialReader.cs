using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;

namespace KlipperSharp
{
	public class SerialReader
	{
		public const int BITS_PER_BYTE = 10;

		private SerialPort serialPort;
		private bool processRead = true;
		public MessageParser msgparser;
		private object _lock = new object();
		private Thread background_thread;
		private Dictionary<(string, int), Action<object>> handlers;

		public SerialQueue queue;

		public string Port;
		public int Baud;

		public SerialReader(string port, int baud)
		{
			Port = port;
			Baud = baud;
			msgparser = new MessageParser();

			// Threading
			this.background_thread = new Thread(_bg_thread);
			// Message handlers
			var handlers = new Dictionary<string, Action<object>>
			{
				{ "#unknown", this.handle_unknown },
				{ "#output", this.handle_output },
				{ "shutdown", this.handle_output },
				{ "is_shutdown", this.handle_output }
			};
			this.handlers = new Dictionary<(string, int), Action<object>>();
			foreach (var item in handlers)
			{
				var key = (item.Key, 0);
				this.handlers[key] = item.Value;
			}
		}
		~SerialReader()
		{
			this.disconnect();
		}

		private void _bg_thread()
		{
			while (processRead)
			{
				//var line = serialPort.Read();



				var parameter = this.msgparser.parse(line);
				parameter["#sent_time"] = response.sent_time;
				parameter["#receive_time"] = response.receive_time;
				var hdl = (parameter["#name"], parameter.get("oid"));
				lock (_lock)
				{
					hdl = this.handlers.get(hdl, this.handle_default);
				}
				try
				{
					hdl(parameter);
				}
				catch
				{
					//logging.exception("Exception in serial callback");
				}

				Thread.Sleep(0);
			}
		}

		public void Connect()
		{
			// Initial connection
			//logging.info("Starting serial connect");
			byte[] identify_data = null;
			while (true)
			{
				//var starttime = this.reactor.monotonic();
				try
				{
					if (this.Baud != 0)
					{
						serialPort = new SerialPort(Port, Baud);
						serialPort.ReadTimeout = SerialPort.InfiniteTimeout;
						serialPort.WriteTimeout = SerialPort.InfiniteTimeout;
						serialPort.Encoding = Encoding.ASCII;
						serialPort.Open();

					}
					//else
					//{
					//	this.ser = open(this.serialport, "rb+");
					//}
				}
				catch
				{
					//logging.warn("Unable to open port: %s", e);
					//this.reactor.pause(starttime + 5.0);
					continue;
				}
				//if (this.Baud != 0)
				//{
				//	stk500v2_leave(this.ser, this.reactor);
				//}
				//this.serialqueue = this.ffi_lib.serialqueue_alloc(this.ser.fileno(), 0);
				this.background_thread = new Thread(_bg_thread);
				this.background_thread.IsBackground = true;
				this.background_thread.Priority = ThreadPriority.AboveNormal;
				this.background_thread.Start();
				// Obtain and load the data dictionary from the firmware
				//var sbs = SerialBootStrap(this);
				//identify_data = sbs.get_identify_data(starttime + 5.0);
				//if (identify_data == null)
				//{
				//	logging.warn("Timeout on serial connect");
				//	this.disconnect();
				//	continue;
				//}
				break;
			}
			msgparser = new MessageParser();
			msgparser.process_identify(identify_data);
			this.register_callback(this.handle_unknown, "#unknown");
			// Setup baud adjust
			var mcu_baud = msgparser.get_constant_int("SERIAL_BAUD");
			if (mcu_baud != 0)
			{
				var baud_adjust = BITS_PER_BYTE / mcu_baud;
				serialPort.BaudRate = baud_adjust;
				//this.ffi_lib.serialqueue_set_baud_adjust(this.serialqueue, baud_adjust);
			}
			var receive_window = msgparser.get_constant_int("RECEIVE_WINDOW");
			if (receive_window != 0)
			{
				//serialPort.ReceivedBytesThreshold = receive_window;
				//this.ffi_lib.serialqueue_set_receive_window(this.serialqueue, receive_window);
			}
		}

		public void connect_file(object debugoutput, byte[] dictionary, bool pace = false)
		{
			//this.ser = debugoutput;
			//this.msgparser.process_identify(dictionary, decompress: false);
			//this.serialqueue = this.ffi_lib.serialqueue_alloc(this.ser.fileno(), 1);
		}

		public void set_clock_est(object freq, object last_time, object last_clock)
		{
			//this.ffi_lib.serialqueue_set_clock_est(this.serialqueue, freq, last_time, last_clock);
		}

		public void disconnect()
		{
			//if (this.serialqueue != null)
			//{
			//	this.ffi_lib.serialqueue_exit(this.serialqueue);
			//	if (this.background_thread != null)
			//	{
			//		this.background_thread.join();
			//	}
			//	this.ffi_lib.serialqueue_free(this.serialqueue);
			//	this.background_thread = null;
			//}
			//if (this.ser != null)
			//{
			//	this.ser.close();
			//	this.ser = null;
			//}
		}

		public string stats(object eventtime)
		{
			//if (this.serialqueue == null)
			//{
			//	return "";
			//}
			//this.ffi_lib.serialqueue_get_stats(this.serialqueue, this.stats_buf, this.stats_buf.Count);
			//return this.ffi_main.@string(this.stats_buf);
			return null;
		}

		// Serial response callbacks
		public void register_callback(Action<object> callback, string name, int oid = 0)
		{
			lock (_lock)
			{
				//this.handlers[name, oid] = callback;
			}
		}

		public void unregister_callback(string name, int oid = 0)
		{
			lock (_lock)
			{
				//this.handlers.Remove([name, oid]);
			}
		}

		// Command sending
		public void raw_send(object cmd, int minclock, int reqclock, object cmd_queue)
		{
			//this.ffi_lib.serialqueue_send(this.serialqueue, cmd_queue, cmd, cmd.Count, minclock, reqclock);
		}

		public void send(object msg, int minclock = 0, int reqclock = 0)
		{
			//var cmd = this.msgparser.create_command(msg);
			//this.raw_send(cmd, minclock, reqclock, this.default_cmd_queue);
		}

		public SerialCommand lookup_command(string msgformat, object cq = null)
		{
			if (cq == null)
			{
				cq = this.default_cmd_queue;
			}
			var cmd = this.msgparser.lookup_command(msgformat);
			return new SerialCommand(this, cq, cmd);
		}

		public object alloc_command_queue()
		{
			//return this.ffi_main.gc(this.ffi_lib.serialqueue_alloc_commandqueue(), this.ffi_lib.serialqueue_free_commandqueue);
			return null;
		}


		// Dumping debug lists
		public byte[] dump_debug()
		{
			//object cmds;
			//object msg;
			//var @out = new List<object>();
			//@out.append(String.Format("Dumping serial stats: %s", this.stats(this.reactor.monotonic())));
			//var sdata = this.ffi_main.@new("struct pull_queue_message[1024]");
			//var rdata = this.ffi_main.@new("struct pull_queue_message[1024]");
			//var scount = this.ffi_lib.serialqueue_extract_old(this.serialqueue, 1, sdata, sdata.Count);
			//var rcount = this.ffi_lib.serialqueue_extract_old(this.serialqueue, 0, rdata, rdata.Count);
			//@out.append(String.Format("Dumping send queue %d messages", scount));
			//foreach (var i in range(scount))
			//{
			//	msg = sdata[i];
			//	cmds = this.msgparser.dump(msg.msg[0::msg.len]);
			//	@out.append(String.Format("Sent %d %f %f %d: %s", i, msg.receive_time, msg.sent_time, msg.len, ", ".join(cmds)));
			//}
			//@out.append(String.Format("Dumping receive queue %d messages", rcount));
			//foreach (var i in range(rcount))
			//{
			//	msg = rdata[i];
			//	cmds = this.msgparser.dump(msg.msg[0::msg.len]);
			//	@out.append(String.Format("Receive: %d %f %f %d: %s", i, msg.receive_time, msg.sent_time, msg.len, ", ".join(cmds)));
			//}
			//return "\n".join(@out);
			return null;
		}




		// Default message handlers
		public void handle_unknown(object parameter)
		{
			//logging.warn("Unknown message type %d: %s", parameter["#msgid"], repr(parameter["#msg"]));
		}

		public void handle_output(object parameter)
		{
			//logging.info("%s: %s", params["#parameter"], parameter["#msg"]);
		}

		public void handle_default(object parameter)
		{
			//logging.warn("got %s", parameter);
		}

	}

	public class SerialCommand
	{
		private SerialReader serial;
		private object cmd_queue;
		private BaseFormat cmd;

		public SerialCommand(SerialReader serial, object cmd_queue, BaseFormat cmd)
		{
			this.serial = serial;
			this.cmd_queue = cmd_queue;
			this.cmd = cmd;
		}

		public void send(object data/* = Tuple.Create("<Empty>")*/, int minclock = 0, int reqclock = 0)
		{
			var buffer = new MemoryStream();
			var writer = new BinaryWriter(buffer, Encoding.ASCII);
			this.cmd.Encode(data, writer);
			this.serial.raw_send(cmd, minclock, reqclock, this.cmd_queue);
		}

		public object send_with_response(object data /*= Tuple.Create("<Empty>")*/, object response = null, int response_oid = 0)
		{
			var buffer = new MemoryStream();
			var writer = new BinaryWriter(buffer, Encoding.ASCII);
			this.cmd.Encode(data, writer);
			var src = new SerialRetryCommand(this.serial, cmd, response, response_oid);
			return src.get_response();
		}
	}

	public class SerialRetryCommand
	{
		public const double TIMEOUT_TIME = 5.0;
		public const double RETRY_TIME = 0.5;
		private SerialReader serial;
		private object cmd;
		private string name;
		private int oid;
		private object response;
		private double min_query_time;
		private object send_timer;

		public SerialRetryCommand(SerialReader serial, object cmd, string name, int oid = 0)
		{
			this.serial = serial;
			this.cmd = cmd;
			this.name = name;
			this.oid = oid;
			this.response = null;
			this.min_query_time = this.serial.queue.get_monotonic();//this.serial.reactor.monotonic();
			this.serial.register_callback(this.handle_callback, this.name, this.oid);
			//this.send_timer = this.serial.reactor.register_timer(this.send_event, this.serial.reactor.NOW);
		}

		public void unregister()
		{
			this.serial.unregister_callback(this.name, this.oid);
			//this.serial.reactor.unregister_timer(this.send_timer);
		}

		public double send_event(double eventtime)
		{
			if (this.response != null)
			{
				return this.serial.reactor.NEVER;
			}
			this.serial.raw_send(this.cmd, 0, 0, this.serial.default_cmd_queue);
			return eventtime + RETRY_TIME;
		}

		public void handle_callback(object parameter)
		{
			double last_sent_time = parameter["#sent_time"];
			if (last_sent_time >= this.min_query_time)
			{
				this.response = parameter;
			}
		}

		public object get_response()
		{
			double eventtime = this.serial.reactor.monotonic();
			while (this.response == null)
			{
				eventtime = this.serial.reactor.pause(eventtime + 0.05);
				if (eventtime > this.min_query_time + TIMEOUT_TIME)
				{
					this.unregister();
					throw new Exception($"Timeout on wait for '{this.name}' response");
				}
			}
			this.unregister();
			return this.response;
		}
	}

	public class SerialBootStrap
	{
		public const double RETRY_TIME = 0.5;
		private bool is_done;
		private SerialReader serial;
		private MemoryStream identify_data;
		private SerialCommand identify_cmd;
		private object send_timer;

		public SerialBootStrap(SerialReader serial)
		{
			this.serial = serial;
			this.identify_data = new MemoryStream();
			this.identify_cmd = this.serial.lookup_command("identify offset=%u count=%c");
			this.serial.register_callback(this.handle_identify, "identify_response");
			this.serial.register_callback(this.handle_unknown, "#unknown");
			//this.send_timer = this.serial.reactor.register_timer(this.send_event, this.serial.reactor.NOW);
		}

		public byte[] get_identify_data(object timeout)
		{
			var eventtime = this.serial.reactor.monotonic();
			while (!this.is_done && eventtime <= timeout)
			{
				eventtime = this.serial.reactor.pause(eventtime + 0.05);
			}
			this.serial.unregister_callback("identify_response");
			this.serial.reactor.unregister_timer(this.send_timer);
			if (!this.is_done)
			{
				return null;
			}
			return this.identify_data;
		}

		public void handle_identify(object parameters)
		{
			if (this.is_done || parameters["offset"] != this.identify_data.Length)
			{
				return;
			}
			byte[] msgdata = parameters["data"];
			if (msgdata == null)
			{
				this.is_done = true;
				return;
			}
			this.identify_data.Write(msgdata);
			this.identify_cmd.send(new List<int> { (int)this.identify_data.Length, 40 });
		}

		public double send_event(double eventtime)
		{
			if (this.is_done)
			{
				return this.serial.reactor.NEVER;
			}
			this.identify_cmd.send(new List<int> { (int)this.identify_data.Length, 40 });
			return eventtime + RETRY_TIME;
		}

		public void handle_unknown(object parameters)
		{
			//logging.debug("Unknown message %d (len %d) while identifying", parameters["#msgid"], parameters["#msg"].Count);
		}
	}


	public static class serialhdl
	{
		// Serial port management for firmware communication
		//
		// Copyright (C) 2016,2017  Kevin O'Connor <kevin@koconnor.net>
		//
		// This file may be distributed under the terms of the GNU GPLv3 license.
		// Wrapper around command sending
		// Class to retry sending of a query command until a given response is received
		// Code to start communication and download message type dictionary
		// Attempt to place an AVR stk500v2 style programmer into normal mode
		public static void stk500v2_leave(SerialPort ser, object reactor)
		{
			////logging.debug("Starting stk500v2 leave programmer sequence");
			//util.clear_hupcl(ser.fileno());
			//var origbaud = ser.BaudRate;
			//// Request a dummy speed first as this seems to help reset the port
			//ser.BaudRate = 2400;
			//ser.ReadByte();
			//// Send stk500v2 leave programmer sequence
			//ser.BaudRate = 115200;
			//reactor.pause(reactor.monotonic() + 0.1);
			//var b = new byte[4096];
			//ser.Read(b, 0, 4096);
			//ser.Write("\x1b\x01\x00\x01\x0e\x11\x04");
			//reactor.pause(reactor.monotonic() + 0.05);
			//var res = ser.Read(b, 0, 4096);
			//logging.debug("Got %s from stk500v2", repr(res));
			//ser.BaudRate = origbaud;
		}

		// Attempt an arduino style reset on a serial port
		public static void arduino_reset(string serialport, object reactor)
		{
			//// First try opening the port at a different baud
			//var ser = new SerialPort(serialport, 2400);
			//ser.ReadTimeout = SerialPort.InfiniteTimeout;
			//ser.WriteTimeout = SerialPort.InfiniteTimeout;
			//ser.ReadByte();
			//reactor.pause(reactor.monotonic() + 0.1);
			//// Then toggle DTR
			//ser.DtrEnable = true;
			//reactor.pause(reactor.monotonic() + 0.1);
			//ser.DtrEnable = false;
			//reactor.pause(reactor.monotonic() + 0.1);
			//ser.Close();
		}
	}


}
