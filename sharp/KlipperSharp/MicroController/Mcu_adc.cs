using System;
using System.Collections.Generic;

namespace KlipperSharp.MicroController
{
	public class Mcu_adc
	{
		private Mcu _mcu;
		private string _pin;
		private double _min_sample;
		private double _max_sample;
		private int _range_check_count;
		private double _sample_time;
		private int _sample_count;
		private int _report_clock;
		private int _oid;
		private double _inv_max_adc;
		private double _report_time;
		private Action<int, int> _callback;

		public Mcu_adc(Mcu mcu, PinParams pin_parameters)
		{
			this._mcu = mcu;
			this._pin = pin_parameters.pin;
			this._min_sample = 0.0;
			this._sample_time = 0.0;
			this._sample_count = 0;
			this._report_clock = 0;
			this._oid = 0;
			this._mcu.register_config_callback(this._build_config);
			this._inv_max_adc = 0.0;
		}

		public Mcu get_mcu()
		{
			return this._mcu;
		}

		public void setup_minmax(
			 int sample_time,
			 int sample_count,
			 double minval = 0.0,
			 double maxval = 1.0,
			 int range_check_count = 0)
		{
			this._sample_time = sample_time;
			this._sample_count = sample_count;
			this._min_sample = minval;
			this._max_sample = maxval;
			this._range_check_count = range_check_count;
		}

		public void setup_adc_callback(double report_time, Action<int, int> callback)
		{
			this._report_time = report_time;
			this._callback = callback;
		}

		public void _build_config()
		{
			if (this._sample_count != 0)
			{
				return;
			}
			this._oid = this._mcu.create_oid();
			this._mcu.add_config_cmd($"config_analog_in oid={this._oid} pin={this._pin}");
			var clock = this._mcu.get_query_slot(this._oid);
			var sample_ticks = this._mcu.seconds_to_clock(this._sample_time);
			var mcu_adc_max = this._mcu.get_constant_float("ADC_MAX");
			var max_adc = this._sample_count * mcu_adc_max;
			this._inv_max_adc = 1.0 / max_adc;
			this._report_clock = this._mcu.seconds_to_clock(this._report_time);
			var min_sample = Math.Max(0, Math.Min(65535, Convert.ToInt32(this._min_sample * max_adc)));
			var max_sample = Math.Max(0, Math.Min(65535, Convert.ToInt32(Math.Ceiling(this._max_sample * max_adc))));
			_mcu.add_config_cmd($"query_analog_in oid={_oid} clock={clock} sample_ticks={sample_ticks} sample_count={_sample_count} rest_ticks={_report_clock} min_value={min_sample} max_value={max_sample} range_check_count={_range_check_count}", is_init: true);
			this._mcu.register_msg(this._handle_analog_in_state, "analog_in_state", this._oid);
		}

		public void _handle_analog_in_state(Dictionary<string, object> parameters)
		{
			var last_value = (double)parameters["value"] * this._inv_max_adc;

			var next_clock = this._mcu.clock32_to_clock64((int)parameters["next_clock"]);
			var last_read_clock = next_clock - this._report_clock;
			var last_read_time = this._mcu.clock_to_print_time(last_read_clock);
			if (this._callback != null)
			{
				this._callback((int)last_read_time, (int)last_value);
			}
		}
	}
}
