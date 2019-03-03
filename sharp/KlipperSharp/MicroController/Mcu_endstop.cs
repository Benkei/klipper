using System;
using System.Collections.Generic;
using System.Linq;

namespace KlipperSharp.MicroController
{

	[Serializable]
	public class TimeoutEndstopException : Exception
	{
		public TimeoutEndstopException() { }
		public TimeoutEndstopException(string message) : base(message) { }
		public TimeoutEndstopException(string message, Exception inner) : base(message, inner) { }
		protected TimeoutEndstopException(
		 System.Runtime.Serialization.SerializationInfo info,
		 System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
	}

	public class Mcu_endstop
	{
		public const double RETRY_QUERY = 1.0;
		private Mcu _mcu;
		private List<Mcu_stepper> _steppers;
		private string _pin;
		private bool _pullup;
		private bool _invert;
		private int _oid;
		private bool _homing;
		private double _min_query_time;
		private double _next_query_print_time;
		private Dictionary<string, object> _last_state;
		private SerialCommand _home_cmd;
		private SerialCommand _query_cmd;

		public Mcu_endstop(Mcu mcu, PinParams pin_parameters)
		{
			_mcu = mcu;
			_steppers = new List<Mcu_stepper>();
			_pin = pin_parameters.pin;
			_pullup = pin_parameters.pullup;
			_invert = pin_parameters.invert;
			_oid = 0;
			_mcu.register_config_callback(_build_config);
			_homing = false;
			_min_query_time = 0.0;
			_next_query_print_time = 0.0;
			_last_state = new Dictionary<string, object>();
		}

		public Mcu get_mcu()
		{
			return _mcu;
		}

		public void add_stepper(Mcu_stepper stepper)
		{
			if (stepper.get_mcu() != _mcu)
			{
				throw new PinsException("Endstop and stepper must be on the same mcu");
			}
			if (_steppers.Contains(stepper))
			{
				return;
			}
			_steppers.Add(stepper);
		}

		public List<Mcu_stepper> get_steppers()
		{
			return _steppers;
		}

		public void _build_config()
		{
			_oid = _mcu.create_oid();
			_mcu.add_config_cmd($"config_end_stop oid={_oid} pin={_pin} pull_up={(_pullup ? 1 : 0)} stepper_count={_steppers.Count}");
			_mcu.add_config_cmd($"end_stop_home oid={_oid} clock=0 sample_ticks=0 sample_count=0 rest_ticks=0 pin_value=0", is_init: true);
			for (int i = 0; i < _steppers.Count; i++)
			{
				var s = _steppers[i];
				_mcu.add_config_cmd($"end_stop_set_stepper oid={_oid} pos={i} stepper_oid={s.get_oid()}", is_init: true);
			}
			var cmd_queue = _mcu.alloc_command_queue();
			_home_cmd = _mcu.lookup_command("end_stop_home oid=%c clock=%u sample_ticks=%u sample_count=%c rest_ticks=%u pin_value=%c", cq: cmd_queue);
			_query_cmd = _mcu.lookup_command("end_stop_query oid=%c", cq: cmd_queue);
			_mcu.register_msg(_handle_end_stop_state, "end_stop_state", _oid);
		}

		public void home_prepare()
		{
		}

		public void home_start(
			 double print_time,
			 double sample_time,
			 int sample_count,
			 double rest_time,
			 bool triggered = true)
		{
			var clock = _mcu.print_time_to_clock(print_time);
			var rest_ticks = (int)(rest_time * _mcu.get_adjusted_freq());
			_homing = true;
			_min_query_time = _mcu.monotonic();
			_next_query_print_time = print_time + RETRY_QUERY;
			_home_cmd.send(new object[] {
					 _oid,
					 clock,
					 _mcu.seconds_to_clock(sample_time),
					 sample_count,
					 rest_ticks,
					 triggered ^ _invert
				}, reqclock: (ulong)clock);
			foreach (var s in _steppers)
			{
				s.note_homing_start((ulong)clock);
			}
		}

		public void home_wait(double home_end_time)
		{
			double eventtime = _mcu.monotonic();
			while (_check_busy(eventtime, home_end_time))
			{
				eventtime = _mcu.pause(eventtime + 0.1);
			}
		}

		public void home_finalize()
		{
		}

		public void _handle_end_stop_state(Dictionary<string, object> parameters)
		{
			//logging.debug("end_stop_state %s", parameters);
			_last_state = parameters;
		}

		public bool _check_busy(double eventtime, double home_end_time = 0.0)
		{
			// Check if need to send an end_stop_query command
			var last_sent_time = _last_state.Get("#sent_time", -1.0);
			if (last_sent_time >= _min_query_time || _mcu.is_fileoutput())
			{
				if (!_homing)
				{
					return false;
				}
				if (!(bool)_last_state.Get("homing", false))
				{
					foreach (var s in _steppers)
					{
						s.note_homing_end(did_trigger: true);
					}
					_homing = false;
					return false;
				}
				var last_sent_print_time = _mcu.estimated_print_time(last_sent_time);
				if (last_sent_print_time > home_end_time)
				{
					// Timeout - disable endstop checking
					foreach (var s in _steppers)
					{
						s.note_homing_end();
					}
					_homing = false;
					_home_cmd.send(new object[] { _oid, 0, 0, 0, 0, 0 });
					throw new TimeoutEndstopException("Timeout during endstop homing");
				}
			}
			if (_mcu.is_shutdown())
			{
				throw new McuException("MCU is shutdown");
			}
			var est_print_time = _mcu.estimated_print_time(eventtime);
			if (est_print_time >= _next_query_print_time)
			{
				_next_query_print_time = est_print_time + RETRY_QUERY;
				_query_cmd.send(new object[] { _oid });
			}
			return true;
		}

		public void query_endstop(double print_time)
		{
			_homing = false;
			_min_query_time = _mcu.monotonic();
			_next_query_print_time = print_time;
		}

		public bool query_endstop_wait()
		{
			double eventtime = _mcu.monotonic();
			while (_check_busy(eventtime))
			{
				eventtime = _mcu.pause(eventtime + 0.1);
			}
			return _last_state.Get("pin", _invert) ^ _invert;
		}
	}
}
