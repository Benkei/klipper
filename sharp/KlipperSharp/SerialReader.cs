using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Linq;

namespace KlipperSharp
{
	public class SerialReader
	{
		private static readonly Logger logging = LogManager.GetCurrentClassLogger();

		public const int BITS_PER_BYTE = 10;

		private SerialPort serialPort;
		private bool processRead = true;
		private object _lock = new object();
		private Thread background_thread;
		private Dictionary<ValueTuple<string, int>, Action<Dictionary<string, object>>> handlers;
		public SelectReactor reactor;

		public MessageParser msgparser;
		public SerialQueue serialqueue;

		public string Port;
		public int Baud;
		public command_queue default_cmd_queue = new command_queue();

		public SerialReader(SelectReactor reactor, string port, int baud)
		{
			this.reactor = reactor;
			Port = port;
			Baud = baud;
			msgparser = new MessageParser();
			// Message handlers
			handlers = new Dictionary<ValueTuple<string, int>, Action<Dictionary<string, object>>>()
			{
				{ ("#unknown", 0), handle_unknown },
				{ ("#output", 0), handle_output },
				{ ("shutdown", 0), handle_output },
				{ ("is_shutdown", 0), handle_output },
			};
		}
		~SerialReader()
		{
			disconnect();
		}

		private void _bg_thread()
		{
			QueueMessage response;
			while (processRead)
			{
				serialqueue.pull(out response);

				if (response.len == 0)
					continue;

				var parameter = msgparser.parse(ref response);
				parameter["#sent_time"] = response.sent_time;
				parameter["#receive_time"] = response.receive_time;
				var hdl = (parameter.Get<string>("#name"), parameter.Get<int>("oid"));

				logging.Info($"{hdl.Item1}:{hdl.Item2} - {(int)((response.receive_time - response.sent_time) * 1000)} : {(int)(response.sent_time * 1000)}/{(int)(response.receive_time * 1000)}");

				Action<Dictionary<string, object>> callback;
				lock (_lock)
				{
					callback = handlers.Get(hdl, handle_default);
				}
				try
				{
					callback(parameter);
				}
				catch (Exception ex)
				{
					logging.Error(ex, $"Exception in serial callback '{hdl.Item1}'");
				}
			}
		}

		public void Connect()
		{
			// Initial connection
			logging.Info("Starting serial connect");
			MemoryStream identify_data = null;
			while (true)
			{
				var starttime = reactor.monotonic();
				try
				{
					if (Baud != 0)
					{
						serialPort = new SerialPort(Port, Baud);
						serialPort.ReadTimeout = SerialPort.InfiniteTimeout;
						serialPort.WriteTimeout = SerialPort.InfiniteTimeout;
						serialPort.Encoding = Encoding.ASCII;
						serialPort.DtrEnable = true;
						serialPort.Open();
					}
					//else
					//{
					//	this.ser = open(this.serialport, "rb+");
					//}
				}
				catch (Exception ex)
				{
					logging.Warn(ex, $"Unable to open port: {Port}");
					//this.reactor.pause(starttime + 5.0);
					Thread.Sleep(5000);
					continue;
				}
				//if (this.Baud != 0)
				//{
				//	stk500v2_leave(this.ser, this.reactor);
				//}
				serialqueue = new SerialQueue(serialPort);
				serialqueue.Start();
				background_thread = new Thread(_bg_thread)
				{
					Name = nameof(SerialReader),
					IsBackground = true,
				};
				processRead = true;
				background_thread.Start();
				// Obtain and load the data dictionary from the firmware
				var sbs = new SerialBootStrap(this);
				identify_data = sbs.get_identify_data(starttime + 5.0);
				if (identify_data == null)
				{
					logging.Warn("Timeout on serial connect");
					disconnect();
					continue;
				}
				break;
			}
			msgparser = new MessageParser();
			msgparser.process_identify(identify_data);
			register_callback(handle_unknown, "#unknown");
			// Setup baud adjust
			var mcu_baud = msgparser.get_constant_float("SERIAL_BAUD");
			if (mcu_baud != 0)
			{
				var baud_adjust = BITS_PER_BYTE / mcu_baud;
				serialqueue.set_baud_adjust(baud_adjust);
				//this.ffi_lib.serialqueue_set_baud_adjust(this.serialqueue, baud_adjust);
			}
			var receive_window = (int)msgparser.get_constant_int("RECEIVE_WINDOW");
			if (receive_window != 0)
			{
				serialqueue.set_receive_window(receive_window);
				//this.ffi_lib.serialqueue_set_receive_window(this.serialqueue, receive_window);
			}
		}

		public void connect_file(object debugoutput, byte[] dictionary, bool pace = false)
		{
			//this.ser = debugoutput;
			//this.msgparser.process_identify(dictionary, decompress: false);
			//this.serialqueue = this.ffi_lib.serialqueue_alloc(this.ser.fileno(), 1);
		}

		public void set_clock_est(double freq, double last_time, ulong last_clock)
		{
			serialqueue.set_clock_est(freq, last_time, last_clock);
			//this.ffi_lib.serialqueue_set_clock_est(this.serialqueue, freq, last_time, last_clock);
		}

		public void disconnect()
		{
			processRead = false;
			if (serialqueue != null)
			{
				serialqueue.Close();
				serialqueue = null;
			}
			if (background_thread != null && !background_thread.Join(5000))
			{
				background_thread.Abort();
				background_thread = null;
			}
			if (serialPort != null)
			{
				serialPort.Close();
				serialPort = null;
			}
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

		public string stats(double eventtime)
		{
			//if (this.serialqueue == null)
			//{
			//	return "";
			//}
			//this.ffi_lib.serialqueue_get_stats(this.serialqueue, this.stats_buf, this.stats_buf.Count);
			//return this.ffi_main.@string(this.stats_buf);
			if (serialqueue == null)
			{
				return "";
			}
			return serialqueue.GetStats();
		}

		// Serial response callbacks
		public void register_callback(Action<Dictionary<string, object>> callback, string name, int oid = 0)
		{
			lock (_lock)
			{
				handlers[(name, oid)] = callback;
			}
		}

		public void unregister_callback(string name, int oid = 0)
		{
			lock (_lock)
			{
				handlers.Remove((name, oid));
			}
		}

		// Command sending
		public void raw_send(byte[] cmd, ulong minclock, ulong reqclock, command_queue cmd_queue)
		{
			//this.ffi_lib.serialqueue_send(this.serialqueue, cmd_queue, cmd, cmd.Count, minclock, reqclock);
			serialqueue.send(cmd_queue, cmd, cmd.Length, minclock, reqclock);
		}

		public void send(string msg, ulong minclock = 0, ulong reqclock = 0)
		{
			var buffer = new MemoryStream();
			var writer = new BinaryWriter(buffer, Encoding.ASCII);
			msgparser.create_command(msg, writer);
			raw_send(buffer.ToArray(), minclock, reqclock, default_cmd_queue);
		}

		public SerialCommand lookup_command(string msgformat, command_queue cq = null)
		{
			if (cq == null)
			{
				cq = default_cmd_queue;
			}
			var cmd = msgparser.lookup_command(msgformat);
			return new SerialCommand(this, cq, cmd);
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
		public void handle_unknown(Dictionary<string, object> parameter)
		{
			logging.Warn("Unknown message type {0}: {1}", parameter["#msgid"], parameter["#msg"]);
		}

		public void handle_output(Dictionary<string, object> parameter)
		{
			logging.Info("{0}: {1}", parameter["#name"], parameter["#msg"]);
		}

		public void handle_default(Dictionary<string, object> parameter)
		{
			logging.Warn("got {0}", string.Join("; ", parameter.Select((a) => a.Key + ":" + a.Value)));
		}

	}

	public class SerialCommand
	{
		private SerialReader serial;
		private command_queue cmd_queue;
		private BaseFormat cmd;

		public SerialCommand(SerialReader serial, command_queue cmd_queue, BaseFormat cmd)
		{
			this.serial = serial;
			this.cmd_queue = cmd_queue;
			this.cmd = cmd;
		}

		public void send(object[] data = null/* = Tuple.Create("<Empty>")*/, ulong minclock = 0, ulong reqclock = 0)
		{
			var buffer = new MemoryStream();
			var writer = new BinaryWriter(buffer, Encoding.ASCII);
			cmd.Encode(data, writer);
			serial.raw_send(buffer.ToArray(), minclock, reqclock, cmd_queue);
		}

		public Dictionary<string, object> send_with_response(object[] data = null /*= Tuple.Create("<Empty>")*/, string response = null, int response_oid = 0)
		{
			var buffer = new MemoryStream();
			var writer = new BinaryWriter(buffer, Encoding.ASCII);
			cmd.Encode(data, writer);
			var src = new SerialRetryCommand(serial, buffer.ToArray(), response, response_oid);
			return src.get_response();
		}
	}

	public class SerialRetryCommand
	{
		private static readonly Logger logging = LogManager.GetCurrentClassLogger();

		public const double TIMEOUT_TIME = 5.0;
		public const double RETRY_TIME = 0.5;
		private SerialReader serial;
		private byte[] cmd;
		private string name;
		private int oid;
		private Dictionary<string, object> response;
		private double min_query_time;
		private ReactorTimer send_timer;

		public SerialRetryCommand(SerialReader serial, byte[] cmd, string name, int oid = 0)
		{
			this.serial = serial;
			this.cmd = cmd;
			this.name = name;
			this.oid = oid;
			min_query_time = this.serial.reactor.monotonic();
			this.serial.register_callback(handle_callback, this.name, this.oid);
			this.send_timer = this.serial.reactor.register_timer(this.send_event, SelectReactor.NOW);
		}

		public void unregister()
		{
			serial.unregister_callback(name, oid);
			this.serial.reactor.unregister_timer(this.send_timer);
		}

		public double send_event(double eventtime)
		{
			if (response != null)
			{
				return SelectReactor.NEVER;
			}
			serial.raw_send(cmd, 0, 0, serial.default_cmd_queue);
			logging.Info($"send {name}");
			return eventtime + RETRY_TIME;
		}

		public void handle_callback(Dictionary<string, object> parameter)
		{
			double last_sent_time = parameter.Get<double>("#sent_time");
			if (last_sent_time >= min_query_time)
			{
				response = parameter;
			}
			logging.Info($"handle {name} {last_sent_time} {min_query_time}");
		}

		public Dictionary<string, object> get_response()
		{
			double eventtime = serial.reactor.monotonic();
			while (response == null)
			{
				eventtime = serial.reactor.pause(eventtime + 0.05);
				if (eventtime > min_query_time + TIMEOUT_TIME)
				{
					unregister();
					throw new Exception($"Timeout on wait for '{name}' response");
				}
			}
			unregister();
			return response;
		}
	}

	public class SerialBootStrap
	{
		private static readonly Logger logging = LogManager.GetCurrentClassLogger();

		public const double RETRY_TIME = 0.5;
		private bool is_done;
		private SerialReader serial;
		private MemoryStream identify_data;
		private SerialCommand identify_cmd;
		private ReactorTimer send_timer;

		public SerialBootStrap(SerialReader serial)
		{
			logging.Info("start load identify_data");
			this.serial = serial;
			identify_data = new MemoryStream();
			identify_cmd = this.serial.lookup_command("identify offset=%u count=%c");
			this.serial.register_callback(handle_identify, "identify_response");
			this.serial.register_callback(handle_unknown, "#unknown");
			this.send_timer = this.serial.reactor.register_timer(this.send_event, SelectReactor.NOW);
		}

		public MemoryStream get_identify_data(double timeout)
		{
			var eventtime = serial.reactor.monotonic();
			while (!is_done && eventtime <= timeout)
			{
				eventtime = serial.reactor.pause(eventtime + 0.05);
			}
			serial.unregister_callback("identify_response");
			serial.reactor.unregister_timer(send_timer);
			if (!is_done)
			{
				return null;
			}
			identify_data.Position = 0;
			return identify_data;
		}

		public void handle_identify(Dictionary<string, object> parameters)
		{
			if (is_done || parameters.Get<int>("offset") != identify_data.Length)
			{
				return;
			}
			byte[] msgdata = parameters.Get<byte[]>("data");
			if (msgdata == null || msgdata.Length < 40)
			{
				if (msgdata != null)
					identify_data.Write(msgdata);
				is_done = true;
				logging.Info("finish load identify_data " + identify_data.Length);
				return;
			}
			identify_data.Write(msgdata);
			identify_cmd.send(new object[] { (int)identify_data.Length, 40 });
		}

		public double send_event(double eventtime)
		{
			if (is_done)
			{
				return SelectReactor.NEVER;
			}
			//logging.Info("Load identify_data " + identify_data.Length);
			identify_cmd.send(new object[] { (int)identify_data.Length, 40 });
			return eventtime + RETRY_TIME;
		}

		public void handle_unknown(Dictionary<string, object> parameter)
		{
			logging.Debug("Unknown message {0] (len {1}) while identifying", parameter.Get<int>("#msgid"), parameter.Get<string>("#msg").Length);
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
