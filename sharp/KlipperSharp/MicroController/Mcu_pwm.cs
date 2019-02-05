using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp.MicroController
{
	public class Mcu_pwm
	{
		private Mcu _mcu;
		private bool _hardware_pwm;
		private double _cycle_time;
		private double _max_duration;
		private int _oid;
		private int _pin;
		private int _invert;
		private double _start_value;
		private double _shutdown_value;
		private bool _is_static;
		private int _last_clock;
		private double _pwm_max;
		private object _set_cmd;

		public Mcu_pwm(Mcu mcu, object pin_params)
		{
			this._mcu = mcu;
			this._hardware_pwm = false;
			this._cycle_time = 0.1;
			this._max_duration = 2.0;
			this._oid = 0;
			this._mcu.register_config_callback(this._build_config);
			this._pin = pin_params["pin"];
			this._invert = pin_params["invert"];
			this._start_value = (double)this._invert;
			this._is_static = false;
			this._last_clock = 0;
			this._pwm_max = 0.0;
			this._set_cmd = null;
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
			if (_invert != 0)
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
					this._mcu.add_config_cmd(String.Format("set_pwm_out pin=%s cycle_ticks=%d value=%d", this._pin, cycle_ticks, this._start_value * this._pwm_max));
					return;
				}
				this._oid = this._mcu.create_oid();
				this._mcu.add_config_cmd(String.Format("config_pwm_out oid=%d pin=%s cycle_ticks=%d value=%d\" default_value=%d max_duration=%d\"", this._oid, this._pin, cycle_ticks, this._start_value * this._pwm_max, this._shutdown_value * this._pwm_max, this._mcu.seconds_to_clock(this._max_duration)));
				this._set_cmd = this._mcu.lookup_command("schedule_pwm_out oid=%c clock=%u value=%hu", cq: cmd_queue);
			}
			else
			{
				if (!new List<object> {
						  0.0,
						  1.0
					 }.Contains(this._start_value) || !new List<object> {
						  0.0,
						  1.0
					 }.Contains(this._shutdown_value))
				{
					throw new Exception("start and shutdown values must be 0.0 or 1.0 on soft pwm");
				}
				this._pwm_max = this._mcu.get_constant_float("SOFT_PWM_MAX");
				if (this._is_static)
				{
					this._mcu.add_config_cmd(String.Format("set_digital_out pin=%s value=%d", this._pin, this._start_value >= 0.5));
					return;
				}
				this._oid = this._mcu.create_oid();
				this._mcu.add_config_cmd(String.Format("config_soft_pwm_out oid=%d pin=%s cycle_ticks=%d value=%d\" default_value=%d max_duration=%d\"", this._oid, this._pin, cycle_ticks, this._start_value >= 0.5, this._shutdown_value >= 0.5, this._mcu.seconds_to_clock(this._max_duration)));
				this._set_cmd = this._mcu.lookup_command("schedule_soft_pwm_out oid=%c clock=%u value=%hu", cq: cmd_queue);
			}
		}

		public void set_pwm(double print_time, double value)
		{
			var clock = this._mcu.print_time_to_clock(print_time);
			if (this._invert != 0)
			{
				value = 1.0 - value;
			}
			value = Convert.ToInt32(Math.Max(0.0, Math.Min(1.0, value)) * this._pwm_max + 0.5);
			this._set_cmd.send(new List<object> {
					 this._oid,
					 clock,
					 value
				}, minclock: this._last_clock, reqclock: clock);
			this._last_clock = clock;
		}
	}
}
