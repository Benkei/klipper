using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp.MicroController
{
	public class Mcu_pwm : IMcuDigitalOut
	{
		private Mcu _mcu;
		private bool _hardware_pwm;
		private double _cycle_time;
		private double _max_duration;
		private int _oid;
		private string _pin;
		private bool _invert;
		private double _start_value;
		private double _shutdown_value;
		private bool _is_static;
		private uint _last_clock;
		private double _pwm_max;
		private SerialCommand _set_cmd;

		public Mcu_pwm(Mcu mcu, PinParams pin_params)
		{
			_mcu = mcu;
			_hardware_pwm = false;
			_cycle_time = 0.1;
			_max_duration = 2.0;
			_oid = 0;
			_mcu.register_config_callback(this._build_config);
			_pin = pin_params.pin;
			_invert = pin_params.invert;
			_start_value = this._shutdown_value = pin_params.invert ? 1 : 0;
			_is_static = false;
			_last_clock = 0;
			_pwm_max = 0.0;
			_set_cmd = null;
		}

		public Mcu get_mcu()
		{
			return this._mcu;
		}

		public void setup_max_duration(double max_duration)
		{
			this._max_duration = max_duration;
		}

		public void setup_cycle_time(double cycle_time, bool hardware_pwm = false)
		{
			this._cycle_time = cycle_time;
			this._hardware_pwm = hardware_pwm;
		}

		public void setup_start_value(double start_value, double shutdown_value, bool is_static = false)
		{
			if (is_static && start_value != shutdown_value)
			{
				throw new Exception("Static pin can not have shutdown value");
			}
			if (_invert)
			{
				start_value = 1.0 - start_value;
				shutdown_value = 1.0 - shutdown_value;
			}
			this._start_value = Math.Max(0.0, Math.Min(1.0, start_value));
			this._shutdown_value = Math.Max(0.0, Math.Min(1.0, shutdown_value));
			this._is_static = is_static;
		}

		public void _build_config()
		{
			var cmd_queue = this._mcu.alloc_command_queue();
			var cycle_ticks = this._mcu.seconds_to_clock(this._cycle_time);
			if (this._hardware_pwm)
			{
				this._pwm_max = this._mcu.get_constant_float("PWM_MAX");
				if (this._is_static)
				{
					this._mcu.add_config_cmd($"set_pwm_out pin={this._pin} cycle_ticks={cycle_ticks} value={this._start_value * this._pwm_max}");
					return;
				}
				this._oid = this._mcu.create_oid();
				this._mcu.add_config_cmd($"config_pwm_out oid={this._oid} pin={this._pin} cycle_ticks={cycle_ticks} value={this._start_value * this._pwm_max} default_value={this._shutdown_value * this._pwm_max} max_duration={this._mcu.seconds_to_clock(this._max_duration)}");
				this._set_cmd = this._mcu.lookup_command("schedule_pwm_out oid=%c clock=%u value=%hu", cq: cmd_queue);
			}
			else
			{
				if (!(this._start_value == 0.0 || this._start_value == 1.0)
					&& !(this._shutdown_value == 0.0 || this._shutdown_value == 1.0))
				{
					throw new Exception("start and shutdown values must be 0.0 or 1.0 on soft pwm");
				}
				this._pwm_max = this._mcu.get_constant_float("SOFT_PWM_MAX");
				if (this._is_static)
				{
					this._mcu.add_config_cmd($"set_digital_out pin={this._pin} value={(this._start_value >= 0.5 ? 1 : 0)}");
					return;
				}
				this._oid = this._mcu.create_oid();
				this._mcu.add_config_cmd($"config_soft_pwm_out oid={this._oid} pin={this._pin} cycle_ticks={cycle_ticks} value={(this._start_value >= 0.5 ? 1 : 0)} default_value={(this._shutdown_value >= 0.5 ? 1 : 0)} max_duration={this._mcu.seconds_to_clock(this._max_duration)}");
				this._set_cmd = this._mcu.lookup_command("schedule_soft_pwm_out oid=%c clock=%u value=%hu", cq: cmd_queue);
			}
		}

		public void set_pwm(double print_time, double value)
		{
			var clock = this._mcu.print_time_to_clock(print_time);
			if (this._invert)
			{
				value = 1.0 - value;
			}
			value = (int)(Math.Max(0.0, Math.Min(1.0, value)) * this._pwm_max + 0.5);
			this._set_cmd.send(new object[] { this._oid, clock, value }, minclock: (ulong)this._last_clock, reqclock: (ulong)clock);
			this._last_clock = clock;
		}
	}
}
