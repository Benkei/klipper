using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;

namespace ConsoleTest
{
	unsafe class Program
	{
		public const double PR_NOW = 0.0;
		public const double PR_NEVER = 9999999999999999.0;

		public const long MAX_CLOCK = 0x7fffffffffffffffL;
		public const long BACKGROUND_PRIORITY_CLOCK = 0x7fffffff00000000L;
		public const int MESSAGE_MIN = 5;
		public const int MESSAGE_MAX = 64;
		public const int MESSAGE_HEADER_SIZE = 2;
		public const int MESSAGE_TRAILER_SIZE = 3;
		public const uint MESSAGE_POS_LEN = 0;
		public const uint MESSAGE_POS_SEQ = 1;
		public const uint MESSAGE_TRAILER_CRC = 3;
		public const uint MESSAGE_TRAILER_SYNC = 1;
		public const uint MESSAGE_PAYLOAD_MAX = (MESSAGE_MAX - MESSAGE_MIN);
		public const ulong MESSAGE_SEQ_MASK = 0x0f;
		public const ulong MESSAGE_DEST = 0x10;
		public const int MESSAGE_SYNC = 0x7E;

		public const int SQPF_SERIAL = 0;
		public const int SQPF_PIPE = 1;
		public const int SQPF_NUM = 2;
		public const int SQPT_RETRANSMIT = 0;
		public const int SQPT_COMMAND = 1;
		public const int SQPT_NUM = 2;
		public const double MIN_RTO = 0.025;
		public const double MAX_RTO = 5.000;
		public const double MIN_REQTIME_DELTA = 0.250;
		public const double MIN_BACKGROUND_DELTA = 0.005;
		public const double IDLE_QUERY_TIME = 1.0;
		public const int DEBUG_QUEUE_SENT = 100;
		public const int DEBUG_QUEUE_RECEIVE = 100;

		static SerialPort serialPort;

		static MemoryStream buffer = new MemoryStream();
		static int res = 0;


		static double retransmitTimer = PR_NEVER;
		static double commandTimer = PR_NEVER;

		static bool processRead = true;

		static byte[] sendBuffer = new byte[Marshal.SizeOf<queue_message>()];
		static double timeTicksToSec;

		// Input reading
		//struct pollreactor pr;
		//int serial_fd;
		//int pipe_fds[2];
		static byte[] input_buf = new byte[4096];
		static bool need_sync;
		static int input_pos;
		// Threading
		//pthread_t tid;
		//pthread_mutex_t lock; // protects variables below
		//pthread_cond_t cond;
		//bool receive_waiting;
		// Baud / clock tracking
		static int receive_window;
		static double baud_adjust, idle_time;
		static double est_freq, last_clock_time;
		static ulong last_clock;
		static double last_receive_sent_time;
		// Retransmit support
		static ulong send_seq = 1;
		static ulong receive_seq = 1; // ulong.MaxValue to stop Retransmit
		static ulong ignore_nak_seq, last_ack_seq, retransmit_seq, rtt_sample_seq;
		static Queue<queue_message> sent_queue = new Queue<queue_message>();
		// Smooth round trip time
		static double srtt;
		// Round trip time
		static double rttvar;
		// Retransmission timeout
		static double rto;
		// Pending transmission message queues
		static List<command_queue> pending_queues = new List<command_queue>();
		static int ready_bytes, stalled_bytes, need_ack_bytes, last_ack_bytes;
		static ulong need_kick_clock;
		// Received messages
		static Queue<queue_message> receive_queue = new Queue<queue_message>();
		// Debugging
		//list_head old_sent, old_receive;
		// Stats
		static uint bytes_write, bytes_read, bytes_retransmit, bytes_invalid;

		static void Main(string[] args)
		{
			Console.WriteLine($"GC: " + System.Runtime.GCSettings.LatencyMode);
			System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;

			serialPort = new SerialPort("COM5", 250000);
			serialPort.ReadTimeout = SerialPort.InfiniteTimeout;
			serialPort.WriteTimeout = SerialPort.InfiniteTimeout;
			serialPort.Encoding = Encoding.ASCII;
			serialPort.DtrEnable = true;
			serialPort.Open();

			ReaderThread();

			//Test();
		}

		/*
		static void Test()
		{
			var ser = new SerialQueue("COM5", 250000);
			ser.Start();

			Thread.Sleep(100);

			MemoryStream buffer = new MemoryStream();
			int res = 0;

			var cq = new KlipperSharp.command_queue();
			MemoryStream msg = new MemoryStream(16);

			while (true)
			{
				KlipperSharp.queue_message qm;
				ser.pull(out qm);
				
				msg.Position = 0;
				msg.SetLength(0);
				for (var i = SerialQueue.MESSAGE_HEADER_SIZE;
					i < qm.len - SerialQueue.MESSAGE_TRAILER_SIZE; i++)
				{
					msg.WriteByte(qm.msg[i]);
				}
				msg.Position = 0;
				var code = msg.ReadByte();

				Console.WriteLine();
				Console.WriteLine($"MSG code:{code} {(qm.receive_time - qm.sent_time) * 1000}ms");

				if (code == 0)
				{
					Console.WriteLine("identify_response");
					var offset = msg.parse(false);
					var len = msg.ReadByte();
					if (len > 0)
					{
						var buf = new byte[len];
						var read = msg.Read(buf, 0, len);
						if (read != len)
							Console.WriteLine("Error");

						buffer.Write(buf, 0, read);
						res += read;

						Console.WriteLine($"Read bytes {buffer.Length}; {read}/{len}");
					}
					if (len < 40)
					{
						Console.WriteLine("download done");
						res = -1;

						//var final = ZlibDecompress(buffer.ToArray());
						//var jsonText = Encoding.ASCII.GetString(final);
						//var xml = XDocument.Load(JsonReaderWriterFactory.CreateJsonReader(Encoding.ASCII.GetBytes(jsonText), new XmlDictionaryReaderQuotas()));
						//xml.Save("identify.xml");

						//Assert.AreEqual(PROGMEM, buffer.ToArray());

						break;
					}
				}
				else
				{
					//Console.WriteLine($"{qm.receive_time * 1000} code:{code}");
				}
				if (res != -1)
				{
					Console.WriteLine($"identify: get data {res}");
					//{ 1, "identify offset=%u count=%c" }, uint32, byte
					msg.Position = 0;
					msg.SetLength(0);
					msg.WriteByte(1);
					msg.encode(res);
					msg.encode(40);
					ser.send(cq, msg.ToArray(), (int)msg.Length);
				}

			}
			Console.ReadKey();
		}
		*/

		// Return a message read from the serial port (or wait for one if none
		// available)
		static void serialqueue_pull(out queue_message pqm)
		{
			//pthread_mutex_lock(&sq->lock);
			// Wait for message to be available
			while (receive_queue.Count == 0)
			{
				//if (pollreactor_is_exit(&sq->pr))
				//    goto exit;
				//sq->receive_waiting = 1;
				//int ret = pthread_cond_wait(&sq->cond, &sq->lock);
				//if (ret)
				//    report_errno("pthread_cond_wait", ret);
			}

			// Remove message from queue
			queue_message qm;
			receive_queue.TryDequeue(out qm);

			// Copy message
			pqm = qm;
			//pqm = new queue_message();
			//memcpy(pqm->msg, qm->msg, qm->len);
			//pqm.len = qm.len;
			//pqm.sent_time = qm.sent_time;
			//pqm.receive_time = qm.receive_time;
			//debug_queue_add(&sq->old_receive, qm);

			//pthread_mutex_unlock(&sq->lock);
			return;

			//exit:
			//	pqm = new queue_message();
			//	pqm.len = -1;
			//pthread_mutex_unlock(&sq->lock);
		}

		static void serialqueue_set_baud_adjust(double baud_adjust)
		{
			//pthread_mutex_lock(&sq->lock);
			Program.baud_adjust = baud_adjust;
			//pthread_mutex_unlock(&sq->lock);
		}

		static void serialqueue_set_receive_window(int receive_window)
		{
			//pthread_mutex_lock(&sq->lock);
			Program.receive_window = receive_window;
			//pthread_mutex_unlock(&sq->lock);
		}

		// Set the estimated clock rate of the mcu on the other end of the
		// serial port
		static void serialqueue_set_clock_est(double est_freq, double last_clock_time, ulong last_clock)
		{
			//pthread_mutex_lock(&sq->lock);
			Program.est_freq = est_freq;
			Program.last_clock_time = last_clock_time;
			Program.last_clock = last_clock;
			//pthread_mutex_unlock(&sq->lock);
		}

		static double GetTime()
		{
			return Stopwatch.GetTimestamp() * timeTicksToSec;
		}

		static void ReaderThread()
		{
			timeTicksToSec = 1.0 / Stopwatch.Frequency;
			while (processRead)
			{
				double eventtime = GetTime();

				input_event(eventtime);

				if (eventtime >= retransmitTimer)
				{
					Retransmit_event(eventtime);
				}
				if (eventtime >= commandTimer)
				{
					Command_event(eventtime);
				}

				double diff = retransmitTimer - eventtime;
				double diff2 = commandTimer - eventtime;
				diff = diff < diff2 ? diff : diff2;
				if (diff <= 0.000f)
					continue;
				else if (diff < 0.001f)
					Thread.SpinWait(10);
				else if (diff < 0.005f)
					Thread.SpinWait(100);
				else
					Thread.Sleep(1);
			}
		}

		// Internal code to invoke timer callbacks
		//static int pollreactor_check_timers(pollreactor *pr, double eventtime)
		//{
		//    if (eventtime >= pr->next_timer) {
		//        pr->next_timer = PR_NEVER;
		//        int i;
		//        for (i=0; i<pr->num_timers; i++)
		//		{
		//            pollreactor_timer *timer = &pr->timers[i];
		//            double t = timer->waketime;
		//            if (eventtime >= t) {
		//                t = timer->callback(pr->callback_data, eventtime);
		//                timer->waketime = t;
		//            }
		//            if (t < pr->next_timer)
		//                pr->next_timer = t;
		//        }
		//        if (eventtime >= pr->next_timer)
		//            return 0;
		//    }
		//    double timeout = ceil((pr->next_timer - eventtime) * 1000.);
		//    return timeout < 1. ? 1 : (timeout > 1000. ? 1000 : (int)timeout);
		//}


		// Callback for input activity on the serial fd
		static void input_event(double eventtime)
		{
			if (serialPort.BytesToRead == 0)
			{
				return;
			}
			var ret = serialPort.Read(input_buf, input_pos, 4096 - input_pos);
			if (ret <= 0)
			{
				//report_errno("read", ret);
				//pollreactor_do_exit(&sq->pr);
				return;
			}

			input_pos += ret;
			while (true)
			{
				ret = Check_message(ref need_sync, input_buf, input_pos);
				if (ret == 0)
					// Need more data
					break;
				if (ret > 0)
				{
					// Received a valid message
					handle_message(ret, eventtime);
					bytes_read += (uint)ret;
				}
				else
				{
					// Skip bad data at beginning of input
					ret = -ret;
					bytes_invalid += (uint)ret;
				}
				input_pos -= ret;
				if (input_pos != 0)
				{
					//memmove(sq->input_buf, &sq->input_buf[ret], input_pos);
					Buffer.BlockCopy(input_buf, ret, input_buf, 0, 4096 - ret);
				}
			}
		}

		static void handle_message(int length, double eventtime)
		{
			//Console.Write(eventtime + " ");
			//PrintByteArray(input_buf, length);

			// Calculate receive sequence number
			ulong rseq = (receive_seq & ~MESSAGE_SEQ_MASK) | (input_buf[MESSAGE_POS_SEQ] & MESSAGE_SEQ_MASK);
			if (rseq < receive_seq)
				rseq += MESSAGE_SEQ_MASK + 1;

			if (rseq != receive_seq)
			{
				// New sequence number
				update_receive_seq(eventtime, rseq);
			}
			if (length == MESSAGE_MIN)
			{
				// Ack/nak message
				if (last_ack_seq < rseq)
				{
					last_ack_seq = rseq;
				}
				else if (rseq > ignore_nak_seq && sent_queue.Count != 0)
				{
					// Duplicate Ack is a Nak - do fast retransmit
					//pollreactor_update_timer(&sq->pr, SQPT_RETRANSMIT, PR_NOW);
					retransmitTimer = PR_NOW;
				}
			}

			if (length > MESSAGE_MIN)
			{
				// Add message to receive queue
				queue_message qm = new queue_message(input_buf, (byte)length);
				qm.sent_time = (rseq > retransmit_seq ? last_receive_sent_time : 0.0);
				qm.receive_time = GetTime();//get_monotonic(); // must be time post read()
				qm.receive_time -= baud_adjust * length;
				receive_queue.Enqueue(qm);
				//check_wake_receive(sq);


				Console.WriteLine($"receive time {(qm.receive_time - qm.sent_time) * 1000}ms");

				//{ 0, "identify_response offset=%u data=%.*s" }, uint32, buffer
				MemoryStream msg = new MemoryStream(length);
				for (var i = MESSAGE_HEADER_SIZE; i < length - MESSAGE_TRAILER_SIZE; i++)
				{
					msg.WriteByte(input_buf[i]);
				}
				msg.Position = 0;
				var code = msg.ReadByte();

				if (code == 0)
				{
					Console.WriteLine("identify_response");
					var offset = msg.parse(false);
					var len = msg.ReadByte();
					if (len > 0)
					{
						var buf = new byte[len];
						var read = msg.Read(buf, 0, len);
						if (read != len)
							Console.WriteLine("Error");

						buffer.Write(buf, 0, read);
						res += read;

						Console.WriteLine($"Read bytes {buffer.Length}; {read}/{len}");
					}
					if (len < 40)
					{
						Console.WriteLine("download done");
						res = -1;

						var final = ZlibDecompress(buffer.ToArray());
						var jsonText = Encoding.ASCII.GetString(final);
						//System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(jsonText);
						var xml = XDocument.Load(JsonReaderWriterFactory.CreateJsonReader(Encoding.ASCII.GetBytes(jsonText), new XmlDictionaryReaderQuotas()));
						xml.Save("identify.xml");
						//Console.WriteLine(Encoding.ASCII.GetString(final));
					}
				}
				else
				{
					Console.WriteLine($"{eventtime * 1000} code:{code}; seq:{rseq}; len:{length}");
					var text = Encoding.ASCII.GetString(input_buf, 1 + MESSAGE_HEADER_SIZE, length - MESSAGE_TRAILER_SIZE);
					Console.WriteLine(text);
				}
			}

			if (length > MESSAGE_MIN && res != -1)
			{
				Console.WriteLine(eventtime + " identify get data " + res + " seq:" + send_seq);
				//{ 1, "identify offset=%u count=%c" }, uint32, byte
				MemoryStream msg = new MemoryStream(16);
				msg.WriteByte(1);
				msg.encode(res);
				msg.encode(40);
				Send(msg);
			}
		}

		//// Process a well formed input message
		//void handle_message(double eventtime, int len)
		//{
		//	// Calculate receive sequence number
		//	ulong rseq = ((receive_seq & ~MESSAGE_SEQ_MASK) | (input_buf[MESSAGE_POS_SEQ] & MESSAGE_SEQ_MASK));
		//	if (rseq < receive_seq)
		//		rseq += MESSAGE_SEQ_MASK + 1;
		//	if (rseq != receive_seq)
		//		// New sequence number
		//		update_receive_seq(eventtime, rseq);
		//	if (len == MESSAGE_MIN)
		//	{
		//		// Ack/nak message
		//		if (last_ack_seq < rseq)
		//			last_ack_seq = rseq;
		//		else if (rseq > ignore_nak_seq && !sent_queue.IsEmpty)
		//			// Duplicate Ack is a Nak - do fast retransmit
		//			pollreactor_update_timer(&sq->pr, SQPT_RETRANSMIT, PR_NOW);
		//	}
		//	if (len > MESSAGE_MIN)
		//	{
		//		// Add message to receive queue
		//		queue_message qm = new queue_message(input_buf, len)
		//		{
		//			sent_time = (rseq > retransmit_seq ? last_receive_sent_time : 0.0),
		//			receive_time = get_monotonic() - baud_adjust * len // must be time post read()
		//		};
		//		receive_queue.Push(qm);
		//		//list_add_tail(&qm->node, receive_queue);
		//		check_wake_receive();
		//	}
		//}



		// Determine the time the next serial data should be sent
		static double check_send_command(double eventtime)
		{
			if (send_seq - receive_seq >= MESSAGE_SEQ_MASK && receive_seq != ulong.MaxValue)
				// Need an ack before more messages can be sent
				return PR_NEVER;
			if (send_seq > receive_seq && receive_window != 0)
			{
				int need_ack_bytes = Program.need_ack_bytes + MESSAGE_MAX;
				if (last_ack_seq < receive_seq)
					need_ack_bytes += last_ack_bytes;
				if (need_ack_bytes > receive_window)
					// Wait for ack from past messages before sending next message
					return PR_NEVER;
			}

			// Check for stalled messages now ready
			double idletime = eventtime > idle_time ? eventtime : idle_time;
			idletime += MESSAGE_MIN * baud_adjust;
			double timedelta = idletime - last_clock_time;
			ulong ack_clock = (ulong)(timedelta * est_freq) + last_clock;
			ulong min_stalled_clock = MAX_CLOCK;
			ulong min_ready_clock = MAX_CLOCK;

			foreach (var cq in pending_queues)
			{
				queue_message qm;
				// Move messages from the stalled_queue to the ready_queue
				while (cq.stalled_queue.TryDequeue(out qm))
				{
					if (ack_clock < qm.min_clock)
					{
						if (qm.min_clock < min_stalled_clock)
							min_stalled_clock = qm.min_clock;
						break;
					}
					cq.ready_queue.Enqueue(qm);
					stalled_bytes -= qm.len;
					ready_bytes += qm.len;
				}
				// Update min_ready_clock
				if (cq.ready_queue.TryPeek(out qm))
				{
					ulong req_clock = qm.req_clock;
					if (req_clock == BACKGROUND_PRIORITY_CLOCK)
						req_clock = (ulong)((idle_time - last_clock_time + MIN_REQTIME_DELTA + MIN_BACKGROUND_DELTA) * est_freq) + last_clock;
					if (req_clock < min_ready_clock)
						min_ready_clock = req_clock;
				}
			}

			// Check for messages to send
			if (ready_bytes >= MESSAGE_PAYLOAD_MAX)
				return PR_NOW;
			if (est_freq != 0)
			{
				if (ready_bytes != 0)
					return PR_NOW;
				need_kick_clock = MAX_CLOCK;
				return PR_NEVER;
			}
			ulong reqclock_delta = (ulong)(MIN_REQTIME_DELTA * est_freq);
			if (min_ready_clock <= ack_clock + reqclock_delta)
				return PR_NOW;
			ulong wantclock = min_ready_clock - reqclock_delta;
			if (min_stalled_clock < wantclock)
				wantclock = min_stalled_clock;
			need_kick_clock = wantclock;
			return idletime + (wantclock - ack_clock) / est_freq;
		}

		static void build_and_send_command(double eventtime)
		{
			queue_message output = new queue_message();
			output.len = MESSAGE_HEADER_SIZE;

			while (ready_bytes != 0)
			{
				// Find highest priority message (message with lowest req_clock)
				ulong min_clock = MAX_CLOCK;
				command_queue cq = null;
				queue_message qm = new queue_message();
				foreach (var q in pending_queues)
				{
					queue_message m;
					if (!q.ready_queue.TryPeek(out m) && m.req_clock < min_clock)
					{
						min_clock = m.req_clock;
						cq = q;
						qm = m;
					}
				}
				// Append message to outgoing command
				if (output.len + qm.len > MESSAGE_MAX - MESSAGE_TRAILER_SIZE)
					break;
				cq.ready_queue.TryDequeue(out qm);
				if (cq.ready_queue.Count == 0 && cq.stalled_queue.Count == 0)
					pending_queues.Remove(cq);

				Buffer.MemoryCopy(qm.msg, &output.msg[output.len], MESSAGE_MAX, qm.len);
				//memcpy(output.msg[output.len], qm.msg, qm.len);

				output.len += qm.len;
				ready_bytes -= qm.len;
			}

			// Fill header / trailer
			output.len += MESSAGE_TRAILER_SIZE;
			output.msg[MESSAGE_POS_LEN] = output.len;
			output.msg[MESSAGE_POS_SEQ] = (byte)(MESSAGE_DEST | (send_seq & MESSAGE_SEQ_MASK));
			int crc = Crc16_ccitt(new ReadOnlySpan<byte>(output.msg, output.len - MESSAGE_TRAILER_SIZE));
			output.msg[output.len - MESSAGE_TRAILER_CRC] = (byte)(crc >> 8);
			output.msg[output.len - MESSAGE_TRAILER_CRC + 1] = (byte)(crc & 0xff);
			output.msg[output.len - MESSAGE_TRAILER_SYNC] = MESSAGE_SYNC;

			// Send message
			fixed (byte* psendBuffer = sendBuffer)
			{
				*((queue_message*)psendBuffer) = output;
			}
			serialPort.Write(sendBuffer, 0, output.len);
			//int ret = write(sq->serial_fd, output->msg, output->len);
			//if (ret < 0)
			//	report_errno("write", ret);
			bytes_write += output.len;
			if (eventtime > idle_time)
				idle_time = eventtime;
			idle_time += output.len * baud_adjust;
			output.sent_time = eventtime;
			output.receive_time = idle_time;
			if (sent_queue.Count == 0)
				//	pollreactor_update_timer(&sq->pr, SQPT_RETRANSMIT, sq->idle_time + sq->rto);
				retransmitTimer = idle_time + rto;
			if (rtt_sample_seq != 0)
				rtt_sample_seq = send_seq;
			send_seq++;
			need_ack_bytes += output.len;
			sent_queue.Enqueue(output);
		}


		// Callback timer to send data to the serial port
		static void Command_event(double eventtime)
		{
			//pthread_mutex_lock(&sq->lock);
			double waketime;
			while (true)
			{
				waketime = check_send_command(eventtime);
				if (waketime != PR_NOW)
					break;
				build_and_send_command(eventtime);
			}
			commandTimer = waketime;
			//pthread_mutex_unlock(&sq->lock);
			//return waketime;
		}


		private static byte[] ZlibDecompress(byte[] data)
		{
			// ignore first two and the last bytes
			var readMs = new MemoryStream(data, 2, data.Length - 2 - 1);
			readMs.Position = 0;
			var ms = new MemoryStream(512);
			using (var s = new DeflateStream(readMs, CompressionMode.Decompress))
			{
				var buffer = new byte[512];
				int read;
				do
				{
					read = s.Read(buffer, 0, 512);
					ms.Write(buffer, 0, read);
				} while (read > 0);
			}
			data = ms.ToArray();
			return data;
		}

		static void Send(MemoryStream msgContent)
		{
			MemoryStream msg = new MemoryStream(sendBuffer, true);
			msg.SetLength(0);
			msg.WriteByte((byte)(MESSAGE_HEADER_SIZE + msgContent.Length + MESSAGE_TRAILER_SIZE));
			msg.WriteByte((byte)(MESSAGE_DEST | (send_seq & MESSAGE_SEQ_MASK)));
			//msg.WriteByte((byte)(send_seq));

			msgContent.Position = 0;
			msgContent.CopyTo(msg);

			msg.Position = 0;
			int crc = Crc16_ccitt(msg);

			msg.WriteByte((byte)(crc >> 8));
			msg.WriteByte((byte)(crc & 0xff));
			msg.WriteByte((byte)MESSAGE_SYNC);

			send_seq++;

			serialPort.Write(sendBuffer, 0, (int)msg.Length);
		}

		// Update internal state when the receive sequence increases
		static void update_receive_seq(double eventtime, ulong rseq)
		{
			queue_message sent;
			// Remove from sent queue
			ulong sent_seq = receive_seq;
			while (true)
			{
				if (!sent_queue.TryDequeue(out sent))
				{
					// Got an ack for a message not sent; must be connection init
					send_seq = rseq;
					last_receive_sent_time = 0.0;
					break;
				}
				need_ack_bytes -= sent.len;
				//debug_queue_add(&sq->old_sent, sent);
				sent_seq++;
				if (rseq == sent_seq)
				{
					// Found sent message corresponding with the received sequence
					last_receive_sent_time = sent.receive_time;
					last_ack_bytes = sent.len;
					break;
				}
			}
			receive_seq = rseq;
			//pollreactor_update_timer(&sq->pr, SQPT_COMMAND, PR_NOW);
			commandTimer = PR_NOW;

			// Update retransmit info
			if (rtt_sample_seq != 0 && rseq > rtt_sample_seq && last_receive_sent_time != 0)
			{
				// RFC6298 rtt calculations
				double delta = eventtime - last_receive_sent_time;
				if (srtt != 0)
				{
					rttvar = delta / 2.0;
					srtt = delta * 10.0; // use a higher start default
				}
				else
				{
					rttvar = (3.0 * rttvar + Math.Abs(srtt - delta)) / 4.0;
					srtt = (7.0 * srtt + delta) / 8.0;
				}
				double rttvar4 = rttvar * 4.0;
				if (rttvar4 < 0.001)
					rttvar4 = 0.001;
				rto = srtt + rttvar4;
				if (rto < MIN_RTO)
					rto = MIN_RTO;
				else if (rto > MAX_RTO)
					rto = MAX_RTO;
				rtt_sample_seq = 0;
			}

			if (!sent_queue.TryDequeue(out sent))
			{
				// stop RETRANSMIT
				//pollreactor_update_timer(&sq->pr, SQPT_RETRANSMIT, PR_NEVER);
				retransmitTimer = PR_NEVER;
			}
			else
			{
				// set timer RETRANSMIT
				double nr = eventtime + rto + sent.len * baud_adjust;
				//pollreactor_update_timer(&sq->pr, SQPT_RETRANSMIT, nr);
				retransmitTimer = nr;
			}
		}


		// Callback timer for when a retransmit should be done
		static void Retransmit_event(double eventtime)
		{
			//?
			//serialPort.DiscardOutBuffer();

			//int ret = tcflush(sq->serial_fd, TCOFLUSH);
			//if (ret < 0)
			//	report_errno("tcflush", ret);

			//pthread_mutex_lock(&sq->lock) ;

			// Retransmit all pending messages
			byte[] buf = new byte[MESSAGE_MAX * MESSAGE_SEQ_MASK + 1];
			int buflen = 0, first_buflen = 0;
			buf[buflen++] = MESSAGE_SYNC;

			fixed (byte* pbuf = buf)
			{
				foreach (var qm in sent_queue)
				{
					byte* ppbuf = pbuf + buflen;
					for (int i = 0; i < qm.len; i++)
					{
						ppbuf[i] = qm.msg[i];
					}

					buflen += qm.len;
					if (first_buflen == 0)
						first_buflen = qm.len + 1;
				}
			}
			//queue_message* qm;
			//list_for_each_entry(qm, sent_queue, node) {
			//	memcpy(&buf[buflen], qm->msg, qm->len);
			//	buflen += qm->len;
			//	if (first_buflen == 0)
			//		first_buflen = qm->len + 1;
			//}
			//ret = write(sq->serial_fd, buf, buflen);
			//if (ret < 0)
			//	report_errno("retransmit write", ret);
			serialPort.Write(buf, 0, buflen);
			bytes_retransmit += (uint)buflen;

			// Update rto
			if (/*pollreactor_get_timer(&sq->pr, SQPT_RETRANSMIT)*/ retransmitTimer == PR_NOW)
			{
				// Retransmit due to nak
				ignore_nak_seq = receive_seq;
				if (receive_seq < retransmit_seq)
					// Second nak for this retransmit - don't allow third
					ignore_nak_seq = retransmit_seq;
			}
			else
			{
				// Retransmit due to timeout
				rto *= 2.0;
				if (rto > MAX_RTO)
					rto = MAX_RTO;
				ignore_nak_seq = send_seq;
			}
			retransmit_seq = send_seq;
			rtt_sample_seq = 0;
			idle_time = eventtime + buflen * baud_adjust;
			double waketime = eventtime + first_buflen * baud_adjust + rto;
			retransmitTimer = waketime;

			//pthread_mutex_unlock(&sq->lock) ;
			//return waketime;
		}


		static void PrintByteArray(byte* bytes, int length)
		{
			var sb = new StringBuilder("{ ");
			for (int i = 0; i < length; i++)
			{
				sb.Append(bytes[i]);
				sb.Append(", ");
			}
			sb.Append("}");
			Console.WriteLine(sb.ToString());
		}

		// Verify a buffer starts with a valid mcu message

		static int Check_message(ref bool need_sync, byte[] buf, int buf_len)
		{
			if (buf_len < MESSAGE_MIN)
				// Need more data
				return 0;
			if (need_sync)
				goto error;
			byte msglen = buf[MESSAGE_POS_LEN];
			if (msglen < MESSAGE_MIN || msglen > MESSAGE_MAX)
				goto error;
			byte msgseq = buf[MESSAGE_POS_SEQ];
			if ((msgseq & ~MESSAGE_SEQ_MASK) != MESSAGE_DEST)
				goto error;
			if (buf_len < msglen)
				// Need more data
				return 0;
			if (buf[msglen - MESSAGE_TRAILER_SYNC] != MESSAGE_SYNC)
				goto error;
			int msgcrc = (buf[msglen - MESSAGE_TRAILER_CRC] << 8) | buf[msglen - MESSAGE_TRAILER_CRC + 1];
			int crc = Crc16_ccitt(new ReadOnlySpan<byte>(buf, 0, msglen - MESSAGE_TRAILER_SIZE));
			if (crc != msgcrc)
				goto error;
			return msglen;

		error:
			// Discard bytes until next SYNC found
			var nextSyncIdx = Array.IndexOf(buf, (byte)MESSAGE_SYNC, 0, buf_len);
			if (nextSyncIdx >= 0)
			{
				need_sync = false;
				return -(nextSyncIdx + 1);
			}
			need_sync = true;
			return -buf_len;
		}

		static int Crc16_ccitt(ReadOnlySpan<byte> buff)
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
		static int Crc16_ccitt(MemoryStream buff)
		{
			int crc = 0xffff;
			int data;
			while ((data = buff.ReadByte()) != -1)
			{
				data ^= crc & 0xff;
				data ^= (data & 0x0f) << 4;
				crc = ((data << 8) | (crc >> 8)) ^ (data >> 4) ^ (data << 3);
			}
			return crc;
		}



		public class command_queue
		{
			public Queue<queue_message> stalled_queue = new Queue<queue_message>();
			public Queue<queue_message> ready_queue = new Queue<queue_message>();
		}

		[StructLayout(LayoutKind.Explicit)]
		public unsafe struct queue_message
		{
			[FieldOffset(0)]
			public byte len;
			[FieldOffset(1)]
			public fixed byte msg[MESSAGE_MAX];

			[FieldOffset(1 + MESSAGE_MAX)]
			// Filled when on a command queue
			public ulong min_clock;
			[FieldOffset(1 + MESSAGE_MAX + 8)]
			public ulong req_clock;

			// Filled when in sent/receive queues
			[FieldOffset(1 + MESSAGE_MAX)]
			public double sent_time;
			[FieldOffset(1 + MESSAGE_MAX + 8)]
			public double receive_time;

			public queue_message(byte[] data, byte len) : this()
			{
				fixed (byte* pData = data)
				fixed (byte* pMsg = msg)
					Buffer.MemoryCopy(pData, pMsg, MESSAGE_MAX, len);
				this.len = len;
			}
			public queue_message(byte* data, byte len) : this()
			{
				fixed (byte* pMsg = msg)
					Buffer.MemoryCopy(data, pMsg, MESSAGE_MAX, len);
				this.len = len;
			}
		}

	}


	static class Extension
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
	}
}
