using System;
using System.Collections.Generic;
using System.Linq;

namespace KlipperSharp.MicroController
{
	public class Mcu_endstop
	{
		public class TimeoutError : Exception
		{
			public TimeoutError(string msg) : base(msg)
			{
			}
		}

		public const double RETRY_QUERY = 1.0;
		private Mcu _mcu;
		private List<Mcu_stepper> _steppers;
		private int _pin;
		private int _pullup;
		private int _invert;
		private int _oid;
		private bool _homing;
		private double _min_query_time;
		private double _next_query_print_time;
		private Dictionary<object, object> _last_state;
		private object _home_cmd;
		private object _query_cmd;

		public Mcu_endstop(Mcu mcu, object pin_parameters)
		{
			_mcu = mcu;
			_steppers = new List<Mcu_stepper>();
			_pin = pin_parameters["pin"];
			_pullup = pin_parameters["pullup"];
			_invert = pin_parameters["invert"];
			_oid = 0;
			_mcu.register_config_callback(_build_config);
			_homing = false;
			_min_query_time = 0.0;
			_next_query_print_time = 0.0;
			_last_state = new Dictionary<object, object>();
		}

		public Mcu get_mcu()
		{
			return _mcu;
		}

		public void add_stepper(Mcu_stepper stepper)
		{
			if (stepper.get_mcu() != _mcu)
			{
				throw new Exception("Endstop and stepper must be on the same mcu");
			}
			if (_steppers.Contains(stepper))
			{
				return;
			}
			_steppers.Add(stepper);
		}

		public List<Mcu_stepper> get_steppers()
		{
			return _steppers.ToList();
		}

		public void _build_config()
		{
			_oid = _mcu.create_oid();
			_mcu.add_config_cmd(String.Format("config_end_stop oid=%d pin=%s pull_up=%d stepper_count=%d", _oid, _pin, _pullup, _steppers.Count));
			_mcu.add_config_cmd(String.Format("end_stop_home oid=%d clock=0 sample_ticks=0 sample_count=0\" rest_ticks=0 pin_value=0\"", _oid), is_init: true);
			foreach (var _tup_1 in _steppers.Select((_p_1, _p_2) => Tuple.Create(_p_2, _p_1)))
			{
				var i = _tup_1.Item1;
				var s = _tup_1.Item2;
				_mcu.add_config_cmd(String.Format("end_stop_set_stepper oid=%d pos=%d stepper_oid=%d", _oid, i, s.get_oid()), is_init: true);
			}
			var cmd_queue = _mcu.alloc_command_queue();
			_home_cmd = _mcu.lookup_command("end_stop_home oid=%c clock=%u sample_ticks=%u sample_count=%c\" rest_ticks=%u pin_value=%c\"", cq: cmd_queue);
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
			var rest_ticks = Convert.ToInt32(rest_time * _mcu.get_adjusted_freq());
			_homing = true;
			_min_query_time = _mcu.monotonic();
			_next_query_print_time = print_time + RETRY_QUERY;
			_home_cmd.send(new List<object> {
					 _oid,
					 clock,
					 _mcu.seconds_to_clock(sample_time),
					 sample_count,
					 rest_ticks,
					 triggered ^ _invert
				}, reqclock: clock);
			foreach (var s in _steppers)
			{
				s.note_homing_start(clock);
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

		public void _handle_end_stop_state(object parameters)
		{
			//logging.debug("end_stop_state %s", parameters);
			_last_state = parameters;
		}

		public bool _check_busy(double eventtime, double home_end_time = 0.0)
		{
			// Check if need to send an end_stop_query command
			var last_sent_time = _last_state.get("#sent_time", -1.0);
			if (last_sent_time >= _min_query_time || _mcu.is_fileoutput())
			{
				if (!_homing)
				{
					return false;
				}
				if (!_last_state.get("homing", 0))
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
					_home_cmd.send(new List<object> {
								_oid,
								0,
								0,
								0,
								0,
								0
						  });
					throw new TimeoutError("Timeout during endstop homing");
				}
			}
			if (_mcu.is_shutdown())
			{
				throw new Exception("MCU is shutdown");
			}
			var est_print_time = _mcu.estimated_print_time(eventtime);
			if (est_print_time >= _next_query_print_time)
			{
				_next_query_print_time = est_print_time + RETRY_QUERY;
				_query_cmd.send(new List<object> { _oid });
			}
			return true;
		}

		public void query_endstop(double print_time)
		{
			_homing = false;
			_min_query_time = _mcu.monotonic();
			_next_query_print_time = print_time;
		}

		public void query_endstop_wait()
		{
			double eventtime = _mcu.monotonic();
			while (_check_busy(eventtime))
			{
				eventtime = _mcu.pause(eventtime + 0.1);
			}
			return _last_state.get("pin", _invert) ^ _invert;
		}
	}
}
