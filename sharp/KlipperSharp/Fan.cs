using KlipperSharp.MicroController;
using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp
{
	public class Fan
	{
		public const double FAN_MIN_TIME = 0.1;

		private double last_fan_value;
		private double last_fan_time;
		private double max_power;
		private double kick_start_time;
		private Mcu_pwm mcu_fan;

		public Fan(MachineConfig config, double default_shutdown_speed = 0.0)
		{
			this.last_fan_value = 0.0;
			this.last_fan_time = 0.0;
			this.max_power = config.getfloat("max_power", 1.0, above: 0.0, maxval: 1.0);
			this.kick_start_time = config.getfloat("kick_start_time", 0.1, minval: 0.0);
			var ppins = config.get_printer().lookup_object<PrinterPins>("pins");
			this.mcu_fan = ppins.setup_pin<Mcu_pwm>("pwm", config.get("pin")) as Mcu_pwm;
			this.mcu_fan.setup_max_duration(0.0);
			var cycle_time = config.getfloat("cycle_time", 0.01, above: 0.0);
			var hardware_pwm = (bool)config.getboolean("hardware_pwm", false);
			this.mcu_fan.setup_cycle_time(cycle_time, hardware_pwm);
			var shutdown_speed = config.getfloat("shutdown_speed", default_shutdown_speed, minval: 0.0, maxval: 1.0);
			this.mcu_fan.setup_start_value(0.0, Math.Max(0.0, Math.Min(this.max_power, shutdown_speed)));
		}

		public void set_speed(double print_time, double value)
		{
			value = Math.Max(0.0, Math.Min(this.max_power, value * this.max_power));
			if (value == this.last_fan_value)
			{
				return;
			}
			print_time = Math.Max(this.last_fan_time + FAN_MIN_TIME, print_time);
			if (value != 0 && value < this.max_power && this.last_fan_value == 0 && this.kick_start_time != 0)
			{
				// Run fan at full speed for specified kick_start_time
				this.mcu_fan.set_pwm(print_time, this.max_power);
				print_time += this.kick_start_time;
			}
			this.mcu_fan.set_pwm(print_time, value);
			this.last_fan_time = print_time;
			this.last_fan_value = value;
		}

		public Dictionary<string, object> get_status(double eventtime)
		{
			return new Dictionary<string, object> { { "speed", this.last_fan_value } };
		}
	}
}
