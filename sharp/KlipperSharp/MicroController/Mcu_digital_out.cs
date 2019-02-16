using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp.MicroController
{
	public interface IMcuDigitalOut
	{
		Mcu get_mcu();
		void setup_max_duration(double max_duration);
		void set_pwm(double print_time, double value);
	}

	public class Mcu_digital_out : IMcuDigitalOut
	{
		private Mcu _mcu;
		private int _oid;
		private string _pin;
		private bool _invert;
		private bool _start_value;
		private bool _is_static;
		private double _max_duration;
		private int _last_clock;
		private SerialCommand _set_cmd;
		private bool _shutdown_value;

		public Mcu_digital_out(Mcu mcu, PinParams pin_params)
		{
			_mcu = mcu;
			_oid = 0;
			_mcu.register_config_callback(_build_config);
			_pin = pin_params.pin;
			_invert = pin_params.invert;
			_start_value = _invert;
			_is_static = false;
			_max_duration = 2.0;
			_last_clock = 0;
		}

		public Mcu get_mcu()
		{
			return _mcu;
		}

		public void setup_max_duration(double max_duration)
		{
			_max_duration = max_duration;
		}

		public void setup_start_value(bool start_value, bool shutdown_value, bool is_static = false)
		{
			if (is_static && start_value != shutdown_value)
			{
				throw new Exception("Static pin can not have shutdown value");
			}
			_start_value = !start_value ^ _invert;
			_shutdown_value = !shutdown_value ^ _invert;
			_is_static = is_static;
		}

		public void _build_config()
		{
			if (_is_static)
			{
				_mcu.add_config_cmd($"set_digital_out pin={_pin} value={_start_value}");
				return;
			}
			_oid = _mcu.create_oid();
			_mcu.add_config_cmd(string.Format(
				"config_digital_out oid=%d pin=%s value=%d default_value=%d\" max_duration=%d\"",
				_oid,
				_pin,
				_start_value,
				_shutdown_value,
				_mcu.seconds_to_clock(_max_duration)));
			var cmd_queue = _mcu.alloc_command_queue();
			_set_cmd = _mcu.lookup_command("schedule_digital_out oid=%c clock=%u value=%c", cq: cmd_queue);
		}

		public void set_digital(double print_time, bool value)
		{
			var clock = _mcu.print_time_to_clock(print_time);
			_set_cmd.send(new object[] { _oid, clock, !!value ^ _invert }, (ulong)_last_clock, (ulong)clock);
			_last_clock = clock;
		}

		public void set_pwm(double print_time, double value)
		{
			set_digital(print_time, value >= 0.5);
		}
	}
}
