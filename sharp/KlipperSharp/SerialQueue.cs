using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace KlipperSharp
{
	//
	// Message format
	// <1 byte length><1 byte sequence><n-byte content><2 byte crc><1 byte sync>
	//
	public unsafe class SerialQueue
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

		SerialPort serialPort;

		double retransmitTimer = PR_NEVER;
		double commandTimer = PR_NEVER;

		MemoryStream sendBuffer = new MemoryStream(
			new byte[Marshal.SizeOf<queue_message>()],
			0, Marshal.SizeOf<queue_message>(), true, true);
		Stopwatch time;
		double timeTicksToSec;

		// Input reading
		//struct pollreactor pr;
		//int serial_fd;
		//int pipe_fds[2];
		byte[] input_buf = new byte[4096];
		bool need_sync;
		int input_pos;
		// Threading
		object _lock = new object();
		//object cond = new object();
		Thread background_thread;
		bool processRead = true;
		volatile bool receive_waiting;
		// Baud / clock tracking
		int receive_window;
		double baud_adjust, idle_time;
		double est_freq, last_clock_time;
		ulong last_clock;
		double last_receive_sent_time;
		// Retransmit support
		ulong send_seq;
		ulong receive_seq; // ulong.MaxValue to stop Retransmit
		ulong ignore_nak_seq, last_ack_seq, retransmit_seq, rtt_sample_seq;
		Queue<queue_message> send_queue = new Queue<queue_message>();
		// Smooth round trip time
		double srtt;
		// Round trip time
		double rttvar;
		// Retransmission timeout
		double rto;
		// Pending transmission message queues
		List<command_queue> pending_queues = new List<command_queue>();
		int ready_bytes, stalled_bytes, need_ack_bytes, last_ack_bytes;
		ulong need_kick_clock;
		// Received messages
		ConcurrentQueue<queue_message> receive_queue = new ConcurrentQueue<queue_message>();
		// Debugging
		//list_head old_sent, old_receive;
		// Stats
		uint bytes_write, bytes_read, bytes_retransmit, bytes_invalid;
		volatile bool kick;
		// wait for first message from MCU
		bool waitFirstPacket = true;

		public SerialQueue(string port, int baud)
		{
			// Retransmit setup
			send_seq = 1;
			//if (write_only)
			//{
			//	receive_seq = ulong.MaxValue;
			//	rto = PR_NEVER;
			//}
			//else
			{
				receive_seq = 1;
				rto = MIN_RTO;
			}

			// Queues
			need_kick_clock = MAX_CLOCK;

			serialPort = new SerialPort(port, baud);
			serialPort.ReadTimeout = SerialPort.InfiniteTimeout;
			serialPort.WriteTimeout = SerialPort.InfiniteTimeout;
			serialPort.Encoding = Encoding.ASCII;
			serialPort.DtrEnable = true;
			this.background_thread = new Thread(_bg_thread)
			{
				Name = "SerialQueue",
				IsBackground = true,
				Priority = ThreadPriority.AboveNormal
			};
		}

		public void Start()
		{
			serialPort.Open();
			background_thread.Start();
		}

		// Request that the background thread exit
		public void Close()
		{
			//pollreactor_do_exit(&sq->pr);
			//kick_bg_thread(sq);
			//int ret = pthread_join(sq->tid, NULL);
			//if (ret)
			//    report_errno("pthread_join", ret);
			processRead = false;
			if (!background_thread.Join(1000))
			{
				background_thread.Abort();
			}
		}

		// Schedule the transmission of a message on the serial port at a
		// given time and priority.
		public void send(command_queue cq, byte[] msg, int len, ulong min_clock = 0, ulong req_clock = 0)
		{
			queue_message qm = queue_message.Create(msg, (byte)len);
			qm.min_clock = min_clock;
			qm.req_clock = req_clock;

			send_batch(cq, new[] { qm });
		}

		// Add a batch of messages to the given command_queue
		public void send_batch(command_queue cq, queue_message[] msgs)
		{
			// Make sure min_clock is set in list and calculate total bytes
			int len = 0;
			for (int i = 0; i < msgs.Length; i++)
			{
				ref var msg = ref msgs[i];
				if (msg.min_clock + (1L << 31) < msg.req_clock
					&& msg.req_clock != BACKGROUND_PRIORITY_CLOCK)
					msg.min_clock = msg.req_clock - (1L << 31);
				len += msg.len;
			}
			if (len == 0)
				return;
			var qm = msgs[0];

			bool mustwake = false;
			// Add list to cq->stalled_queue
			//pthread_mutex_lock(&sq->lock);
			lock (_lock)
			{
				if (cq.ready_queue.Count == 0 && cq.stalled_queue.Count == 0)
					pending_queues.Add(cq);

				for (int i = 0; i < msgs.Length; i++)
				{
					cq.stalled_queue.Enqueue(msgs[i]);
				}
				stalled_bytes += len;
				if (qm.min_clock < need_kick_clock)
				{
					need_kick_clock = 0;
					mustwake = true;
				}
			}
			//pthread_mutex_unlock(&sq->lock);

			// Wake the background thread if necessary
			if (mustwake)
				//	kick_bg_thread(sq);
				kick = true;
		}

		// Like serialqueue_send() but also builds the message to be sent
		public void encode_and_send(command_queue cq, uint[] data, int len, ulong min_clock = 0, ulong req_clock = 0)
		{
			queue_message qm = queue_message.CreateAndEncode(data, len);
			qm.min_clock = min_clock;
			qm.req_clock = req_clock;

			send_batch(cq, new[] { qm });
		}

		// Return a message read from the serial port (or wait for one if none
		// available)
		public void pull(out queue_message pqm)
		{
			//pthread_mutex_lock(&sq->lock);
			lock (_lock)
			{
				// Wait for message to be available
				while (receive_queue.Count == 0)
				{
					//if (pollreactor_is_exit(&sq->pr))
					//    goto exit;
					receive_waiting = true;
					Monitor.Wait(_lock);
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
			}
			//pthread_mutex_unlock(&sq->lock);
		}

		public void set_baud_adjust(double baud_adjust)
		{
			//pthread_mutex_lock(&sq->lock);
			lock (_lock)
			{
				this.baud_adjust = baud_adjust;
			}
			//pthread_mutex_unlock(&sq->lock);
		}

		public void set_receive_window(int receive_window)
		{
			//pthread_mutex_lock(&sq->lock);
			lock (_lock)
			{
				this.receive_window = receive_window;
			}
			//pthread_mutex_unlock(&sq->lock);
		}

		// Set the estimated clock rate of the mcu on the other end of the
		// serial port
		public void set_clock_est(double est_freq, double last_clock_time, ulong last_clock)
		{
			//pthread_mutex_lock(&sq->lock);
			lock (_lock)
			{
				this.est_freq = est_freq;
				this.last_clock_time = last_clock_time;
				this.last_clock = last_clock;
			}
			//pthread_mutex_unlock(&sq->lock);
		}

		double GetTime()
		{
			return time.ElapsedTicks * timeTicksToSec;
		}

		void _bg_thread(object obj)
		{
			time = Stopwatch.StartNew();
			timeTicksToSec = 1.0 / Stopwatch.Frequency;
			while (processRead)
			{
				double eventtime = GetTime();

				Input_event(eventtime);

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
				if (diff <= 0.001f)
					continue;
				else if (diff < 0.005f || send_queue.Count > 0)
					Thread.SpinWait(10);
				else if (diff < 0.015f)
					Thread.SpinWait(100);
				else
					Thread.Sleep(1);

				if (kick)
				{
					kick = false;
					commandTimer = PR_NOW;
				}
			}

			//pthread_mutex_lock(&sq->lock) ;
			//check_wake_receive(sq);
			//pthread_mutex_unlock(&sq->lock) ;
			check_wake_receive();
		}

		#region MyRegion

		void Input_event(double eventtime)
		{
			if ((!serialPort.IsOpen || serialPort.BytesToRead == 0) && !waitFirstPacket)
			{
				return;
			}
			waitFirstPacket = false;
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
					Handle_message(ret, eventtime);
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

		// Process a well formed input message
		void Handle_message(int length, double eventtime)
		{
			// Calculate receive sequence number
			ulong rseq = (receive_seq & ~MESSAGE_SEQ_MASK) | (input_buf[MESSAGE_POS_SEQ] & MESSAGE_SEQ_MASK);
			if (rseq < receive_seq)
				rseq += MESSAGE_SEQ_MASK + 1;
			//Console.WriteLine($"Msg seq:{rseq} l:{length}");
			if (rseq != receive_seq)
			{
				// New sequence number
				Update_receive_seq(eventtime, rseq);
			}
			if (length == MESSAGE_MIN)
			{
				//Console.WriteLine($"Ack/nak seq:{rseq}");
				// Ack/nak message
				if (last_ack_seq < rseq)
				{
					last_ack_seq = rseq;
				}
				else if (rseq > ignore_nak_seq && send_queue.Count != 0)
				{
					// Duplicate Ack is a Nak - do fast retransmit
					//pollreactor_update_timer(&sq->pr, SQPT_RETRANSMIT, PR_NOW);
					retransmitTimer = PR_NOW;
				}
			}
			if (length > MESSAGE_MIN)
			{
				// Add message to receive queue
				queue_message qm = queue_message.Create(input_buf, (byte)length);
				qm.sent_time = (rseq > retransmit_seq ? last_receive_sent_time : 0.0);
				qm.receive_time = GetTime();//get_monotonic(); // must be time post read()
				qm.receive_time -= baud_adjust * length;
				receive_queue.Enqueue(qm);
				check_wake_receive();
			}
		}

		// Update internal state when the receive sequence increases
		void Update_receive_seq(double eventtime, ulong rseq)
		{
			queue_message sent;
			// Remove from sent queue
			ulong sent_seq = receive_seq;
			while (true)
			{
				if (!send_queue.TryDequeue(out sent))
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

			if (!send_queue.TryDequeue(out sent))
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

		#endregion

		// Callback timer for when a retransmit should be done
		void Retransmit_event(double eventtime)
		{
			//?
			//serialPort.DiscardOutBuffer();

			//int ret = tcflush(sq->serial_fd, TCOFLUSH);
			//if (ret < 0)
			//	report_errno("tcflush", ret);

			//pthread_mutex_lock(&sq->lock) ;
			lock (_lock)
			{
				// Retransmit all pending messages
				byte[] buf = new byte[MESSAGE_MAX * MESSAGE_SEQ_MASK + 1];
				int buflen = 0, first_buflen = 0;
				buf[buflen++] = MESSAGE_SYNC;

				fixed (byte* pbuf = buf)
				{
					foreach (var qm in send_queue)
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
			}
			//pthread_mutex_unlock(&sq->lock) ;
			//return waketime;
		}

		#region MyRegion

		// Callback timer to send data to the serial port
		void Command_event(double eventtime)
		{
			//pthread_mutex_lock(&sq->lock);
			lock (_lock)
			{
				double waketime;
				while (true)
				{
					waketime = check_send_command(eventtime);
					if (waketime != PR_NOW)
						break;
					build_and_send_command(eventtime);
				}
				commandTimer = waketime;
			}
			//pthread_mutex_unlock(&sq->lock);
			//return waketime;
		}

		// Determine the time the next serial data should be sent
		double check_send_command(double eventtime)
		{
			if (send_seq - receive_seq >= MESSAGE_SEQ_MASK && receive_seq != ulong.MaxValue)
				// Need an ack before more messages can be sent
				return PR_NEVER;
			if (send_seq > receive_seq && receive_window != 0)
			{
				int need_ack_bytes = this.need_ack_bytes + MESSAGE_MAX;
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

		// Construct a block of data and send to the serial port
		/*
		void build_and_send_command(double eventtime)
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
					if (q.ready_queue.TryPeek(out m) && m.req_clock < min_clock)
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

				Buffer.MemoryCopy(qm.msg, output.msg + output.len, MESSAGE_MAX, qm.len);
				//memcpy(output.msg[output.len], qm.msg, qm.len);

				output.len += qm.len;
				ready_bytes -= qm.len;
			}

			// Fill header / trailer
			output.len += MESSAGE_TRAILER_SIZE;
			output.msg[MESSAGE_POS_LEN] = output.len;
			output.msg[MESSAGE_POS_SEQ] = (byte)(MESSAGE_DEST | (send_seq & MESSAGE_SEQ_MASK));
			int crc = SerialUtil.Crc16_ccitt(new ReadOnlySpan<byte>(output.msg, output.len - MESSAGE_TRAILER_SIZE));
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
			Console.WriteLine($"SEND MCU {send_seq - 1} - {eventtime}");
		}
		*/
		void build_and_send_command(double eventtime)
		{
			sendBuffer.SetLength(MESSAGE_HEADER_SIZE);
			sendBuffer.Position = 2;
			//queue_message output = new queue_message();
			//output.len = MESSAGE_HEADER_SIZE;

			while (ready_bytes != 0)
			{
				// Find highest priority message (message with lowest req_clock)
				ulong min_clock = MAX_CLOCK;
				command_queue cq = null;
				queue_message qm = new queue_message();
				foreach (var q in pending_queues)
				{
					queue_message m;
					if (q.ready_queue.TryPeek(out m) && m.req_clock < min_clock)
					{
						min_clock = m.req_clock;
						cq = q;
						qm = m;
					}
				}
				// Append message to outgoing command
				if ((int)sendBuffer.Length + qm.len > MESSAGE_PAYLOAD_MAX)
					break;
				cq.ready_queue.TryDequeue(out qm);
				if (cq.ready_queue.Count == 0 && cq.stalled_queue.Count == 0)
					pending_queues.Remove(cq);

				sendBuffer.Write(new ReadOnlySpan<byte>(qm.msg, qm.len));
				//Buffer.MemoryCopy(qm.msg, output.msg + output.len, MESSAGE_MAX, qm.len);
				//memcpy(output.msg[output.len], qm.msg, qm.len);

				//output.len += qm.len;
				ready_bytes -= qm.len;
			}

			// Fill header / trailer
			sendBuffer.Position = 0;
			sendBuffer.WriteByte((byte)(sendBuffer.Length + MESSAGE_TRAILER_SIZE));
			sendBuffer.WriteByte((byte)(MESSAGE_DEST | (send_seq & MESSAGE_SEQ_MASK)));

			int crc = SerialUtil.Crc16_ccitt(
				new ReadOnlySpan<byte>(sendBuffer.GetBuffer(), 0, (int)sendBuffer.Length));

			sendBuffer.Position = sendBuffer.Length;
			sendBuffer.WriteByte((byte)(crc >> 8));
			sendBuffer.WriteByte((byte)(crc & 0xff));
			sendBuffer.WriteByte(MESSAGE_SYNC);

			queue_message output;
			fixed (byte* p = sendBuffer.GetBuffer())
			{
				output = *(queue_message*)p;
			}

			// Send message
			serialPort.Write(sendBuffer.GetBuffer(), 0, (int)sendBuffer.Length);

			//int ret = write(sq->serial_fd, output->msg, output->len);
			//if (ret < 0)
			//	report_errno("write", ret);
			bytes_write += output.len;
			if (eventtime > idle_time)
				idle_time = eventtime;
			idle_time += output.len * baud_adjust;
			output.sent_time = eventtime;
			output.receive_time = idle_time;
			if (send_queue.Count == 0)
				//	pollreactor_update_timer(&sq->pr, SQPT_RETRANSMIT, sq->idle_time + sq->rto);
				retransmitTimer = idle_time + rto;
			if (rtt_sample_seq != 0)
				rtt_sample_seq = send_seq;
			send_seq++;
			need_ack_bytes += output.len;
			send_queue.Enqueue(output);
			Console.WriteLine($"sen {send_seq}");
		}

		#endregion

		// Wake up the receiver thread if it is waiting
		void check_wake_receive()
		{
			lock (_lock)
			{
				if (receive_waiting)
				{
					receive_waiting = false;
					Monitor.PulseAll(_lock);
					//pthread_cond_signal(&sq->cond);
				}
			}
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
			int crc = SerialUtil.Crc16_ccitt(new ReadOnlySpan<byte>(buf, 0, msglen - MESSAGE_TRAILER_SIZE));
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
	}

}
