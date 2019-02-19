using KlipperSharp.MachineCodes;
using KlipperSharp.MicroController;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KlipperSharp
{
	public delegate void temperature_callback(double read_time, double temp);
	public interface ISensor
	{
		void setup_minmax(double min_temp, double max_temp);
		void setup_callback(temperature_callback callback);
		double get_report_time_delta();
	}

	public class Heater
	{
		private static readonly Logger logging = LogManager.GetCurrentClassLogger();

		public const double KELVIN_TO_CELCIUS = -273.15;
		public const double MAX_HEAT_TIME = 5.0;
		private string name;
		public string gcode_id;
		private ISensor sensor;
		private double min_temp;
		private double max_temp;
		private double pwm_delay;
		private double min_extrude_temp;
		public bool can_extrude;
		private double max_power;
		private double smooth_time;
		private double inv_smooth_time;
		private object _lock;
		private double target_temp;
		private double time_diff;
		private double last_temp;
		private double last_temp_time;
		private double next_pwm_time;
		private double last_pwm_value;
		private IMcuDigitalOut mcu_pwm;
		private double smoothed_temp;
		private BaseControl control;

		public Heater(ConfigWrapper config, ISensor sensor, string gcode_id)
		{
			var printer = config.get_printer();
			this.name = config.get_name();
			this.gcode_id = gcode_id;
			// Setup sensor
			this.sensor = sensor;
			this.min_temp = config.getfloat("min_temp", minval: KELVIN_TO_CELCIUS);
			this.max_temp = config.getfloat("max_temp", above: this.min_temp);
			this.sensor.setup_minmax(this.min_temp, this.max_temp);
			this.sensor.setup_callback(this.temperature_callback);
			this.pwm_delay = this.sensor.get_report_time_delta();
			// Setup temperature checks
			this.min_extrude_temp = config.getfloat("min_extrude_temp", 170.0, minval: this.min_temp, maxval: this.max_temp);
			var is_fileoutput = printer.get_start_args().Get("debugoutput") != null;
			this.can_extrude = this.min_extrude_temp <= 0.0 || is_fileoutput;
			this.max_power = config.getfloat("max_power", 1.0, above: 0.0, maxval: 1.0);
			this.smooth_time = config.getfloat("smooth_time", 2.0, above: 0.0);
			this.inv_smooth_time = 1.0 / this.smooth_time;
			this._lock = new object();
			this.last_temp = 0.0;
			this.last_temp_time = 0.0;
			// pwm caching
			this.next_pwm_time = 0.0;
			this.last_pwm_value = 0.0;
			// Setup control algorithm sub-class
			//var algos = new Dictionary<string, object> { { "watermark", ControlBangBang }, { "pid", ControlPID } };
			//var algo = config.getchoice("control", algos);
			switch (config.get("control"))
			{
				case "watermark": this.control = new ControlBangBang(this, config); break;
				case "pid": this.control = new ControlPID(this, config); break;
			}
			// Setup output heater pin
			var heater_pin = config.get("heater_pin");
			var ppins = printer.lookup_object<PrinterPins>("pins");
			if (control is ControlBangBang && this.max_power == 1.0)
			{
				this.mcu_pwm = ppins.setup_pin<Mcu_digital_out>("digital_out", heater_pin);
			}
			else
			{
				var pwm = ppins.setup_pin<Mcu_pwm>("pwm", heater_pin);
				var pwm_cycle_time = config.getfloat("pwm_cycle_time", 0.1, above: 0.0, maxval: this.pwm_delay);
				pwm.setup_cycle_time(pwm_cycle_time);
				this.mcu_pwm = pwm;
			}
			this.mcu_pwm.setup_max_duration(MAX_HEAT_TIME);
			// Load additional modules
			printer.try_load_module(config, $"verify_heater {this.name}");
			printer.try_load_module(config, "pid_calibrate");
		}

		public void set_pwm(double read_time, double value)
		{
			if (this.target_temp <= 0.0)
			{
				value = 0.0;
			}
			if ((read_time < this.next_pwm_time || this.last_pwm_value != 0) && Math.Abs(value - this.last_pwm_value) < 0.05)
			{
				// No significant change in value - can suppress update
				return;
			}
			var pwm_time = read_time + this.pwm_delay;
			this.next_pwm_time = pwm_time + 0.75 * MAX_HEAT_TIME;
			this.last_pwm_value = value;
			logging.Debug("{0}: pwm={1:0.000}@{2:0.000} (from {3:0.000}@{4:0.000} [{5:0.000}])", this.name, value, pwm_time, this.last_temp, this.last_temp_time, this.target_temp);
			this.mcu_pwm.set_pwm(pwm_time, value);
		}

		public void temperature_callback(double read_time, double temp)
		{
			lock (_lock)
			{
				time_diff = read_time - this.last_temp_time;
				this.last_temp = temp;
				this.last_temp_time = read_time;
				this.control.temperature_update(read_time, temp, this.target_temp);
				var temp_diff = temp - this.smoothed_temp;
				var adj_time = Math.Min(time_diff * this.inv_smooth_time, 1.0);
				this.smoothed_temp += temp_diff * adj_time;
				this.can_extrude = this.smoothed_temp >= this.min_extrude_temp;
				//logging.debug("temp: %.3f %f = %f", read_time, temp)
				// External commands
			}
		}

		public double get_pwm_delay()
		{
			return this.pwm_delay;
		}

		public double get_max_power()
		{
			return this.max_power;
		}

		public double get_smooth_time()
		{
			return this.smooth_time;
		}

		public void set_temp(double print_time, double degrees)
		{
			if (degrees != 0 && (degrees < this.min_temp || degrees > this.max_temp))
			{
				throw new Exception($"Requested temperature ({degrees:0.00}) out of range ({this.min_temp:0.00}:{this.max_temp:0.00})");
			}
			lock (_lock)
			{
				this.target_temp = degrees;
			}
		}

		public (double, double) get_temp(double eventtime)
		{
			var print_time = this.mcu_pwm.get_mcu().estimated_print_time(eventtime) - 5.0;
			lock (_lock)
			{
				if (this.last_temp_time < print_time)
				{
					return (0.0, this.target_temp);
				}
				return (this.smoothed_temp, this.target_temp);
			}
		}

		public bool check_busy(double eventtime)
		{
			lock (_lock)
			{
				return this.control.check_busy(eventtime, this.smoothed_temp, this.target_temp);
			}
		}

		public BaseControl set_control(BaseControl control)
		{
			lock (_lock)
			{
				var old_control = this.control;
				this.control = control;
				this.target_temp = 0.0;
				return old_control;
			}
		}

		public void alter_target(double target_temp)
		{
			if (target_temp != 0)
			{
				target_temp = Math.Max(this.min_temp, Math.Min(this.max_temp, target_temp));
			}
			this.target_temp = target_temp;
		}

		public (bool, string) stats(double eventtime)
		{
			lock (_lock)
			{
				var target_temp = this.target_temp;
				var last_temp = this.last_temp;
				var last_pwm_value = this.last_pwm_value;
				var is_active = target_temp != 0 || last_temp > 50.0;
				return (is_active, $"{this.name}: target={target_temp:0} temp={last_temp:0.00} pwm={last_pwm_value:0.000}");
			}
		}

		public Dictionary<string, object> get_status(double eventtime)
		{
			lock (_lock)
			{
				var target_temp = this.target_temp;
				var smoothed_temp = this.smoothed_temp;
				return new Dictionary<string, object> { { "temperature", smoothed_temp }, { "target", target_temp } };
			}
		}
	}

	public abstract class BaseControl
	{
		public abstract void temperature_update(double read_time, double temp, double target_temp);
		public abstract bool check_busy(double eventtime, double smoothed_temp, double target_temp);
	}

	public class ControlBangBang : BaseControl
	{
		private readonly Heater heater;
		private readonly double heater_max_power;
		private readonly double max_delta;
		private bool heating;

		public ControlBangBang(Heater heater, ConfigWrapper config)
		{
			this.heater = heater;
			this.heater_max_power = heater.get_max_power();
			this.max_delta = config.getfloat("max_delta", 2.0, above: 0.0);
			this.heating = false;
		}

		public override void temperature_update(double read_time, double temp, double target_temp)
		{
			if (this.heating && temp >= target_temp + this.max_delta)
			{
				this.heating = false;
			}
			else if (!this.heating && temp <= target_temp - this.max_delta)
			{
				this.heating = true;
			}
			if (this.heating)
			{
				this.heater.set_pwm(read_time, this.heater_max_power);
			}
			else
			{
				this.heater.set_pwm(read_time, 0.0);
			}
		}

		public override bool check_busy(double eventtime, double smoothed_temp, double target_temp)
		{
			return smoothed_temp < target_temp - this.max_delta;
		}
	}

	public class ControlPID : BaseControl
	{
		public const double PID_SETTLE_DELTA = 1.0;
		public const double PID_SETTLE_SLOPE = 0.1;
		public const double PID_PARAM_BASE = 255.0;
		public const double AMBIENT_TEMP = 25.0;

		private Heater heater;
		private double heater_max_power;

		private double Kp;
		private double Ki;
		private double Kd;
		private double min_deriv_time;
		private double temp_integ_max;
		private double prev_temp;
		private double prev_temp_time;
		private double prev_temp_deriv;
		private double prev_temp_integ;

		public ControlPID(Heater heater, ConfigWrapper config)
		{
			this.heater = heater;
			this.heater_max_power = heater.get_max_power();
			this.Kp = config.getfloat("pid_Kp") / PID_PARAM_BASE;
			this.Ki = config.getfloat("pid_Ki") / PID_PARAM_BASE;
			this.Kd = config.getfloat("pid_Kd") / PID_PARAM_BASE;
			this.min_deriv_time = heater.get_smooth_time();
			var imax = config.getfloat("pid_integral_max", this.heater_max_power, minval: 0.0);
			this.temp_integ_max = imax / this.Ki;
			this.prev_temp = AMBIENT_TEMP;
			this.prev_temp_time = 0.0;
			this.prev_temp_deriv = 0.0;
			this.prev_temp_integ = 0.0;
		}

		public override void temperature_update(double read_time, double temp, double target_temp)
		{
			double temp_deriv;
			var time_diff = read_time - this.prev_temp_time;
			// Calculate change of temperature
			var temp_diff = temp - this.prev_temp;
			if (time_diff >= this.min_deriv_time)
			{
				temp_deriv = temp_diff / time_diff;
			}
			else
			{
				temp_deriv = (this.prev_temp_deriv * (this.min_deriv_time - time_diff) + temp_diff) / this.min_deriv_time;
			}
			// Calculate accumulated temperature "error"
			var temp_err = target_temp - temp;
			var temp_integ = this.prev_temp_integ + temp_err * time_diff;
			temp_integ = Math.Max(0.0, Math.Min(this.temp_integ_max, temp_integ));
			// Calculate output
			var co = this.Kp * temp_err + this.Ki * temp_integ - this.Kd * temp_deriv;
			//logging.debug("pid: %f@%.3f -> diff=%f deriv=%f err=%f integ=%f co=%d",
			//    temp, read_time, temp_diff, temp_deriv, temp_err, temp_integ, co)
			var bounded_co = Math.Max(0.0, Math.Min(this.heater_max_power, co));
			this.heater.set_pwm(read_time, bounded_co);
			// Store state for next measurement
			this.prev_temp = temp;
			this.prev_temp_time = read_time;
			this.prev_temp_deriv = temp_deriv;
			if (co == bounded_co)
			{
				this.prev_temp_integ = temp_integ;
			}
		}

		public override bool check_busy(double eventtime, double smoothed_temp, double target_temp)
		{
			var temp_diff = target_temp - smoothed_temp;
			return Math.Abs(temp_diff) > PID_SETTLE_DELTA || Math.Abs(this.prev_temp_deriv) > PID_SETTLE_SLOPE;
		}
	}




	public class PrinterHeaters
	{
		public const string cmd_TURN_OFF_HEATERS_help = "Turn off all heaters";
		private Machine printer;
		private Dictionary<string, Func<ConfigWrapper, ISensor>> sensors = new Dictionary<string, Func<ConfigWrapper, ISensor>>();
		private Dictionary<string, Heater> heaters = new Dictionary<string, Heater>();
		private Dictionary<string, string> heaters_gcode_id = new Dictionary<string, string>();

		public PrinterHeaters(ConfigWrapper config)
		{
			this.printer = config.get_printer();
			// Register TURN_OFF_HEATERS command
			var gcode = this.printer.lookup_object<GCodeParser>("gcode");
			gcode.register_command("TURN_OFF_HEATERS", this.cmd_TURN_OFF_HEATERS, desc: cmd_TURN_OFF_HEATERS_help);
		}

		public void add_sensor(string sensor_type, Func<ConfigWrapper, ISensor> sensor_factory)
		{
			this.sensors[sensor_type] = sensor_factory;
		}

		public Heater setup_heater(ConfigWrapper config, string gcode_id)
		{
			var heater_name = config.get_name();
			if (heater_name == "extruder")
			{
				heater_name = "extruder0";
			}
			if (this.heaters.ContainsKey(heater_name))
			{
				throw new Exception($"Heater {heater_name} already registered");
			}
			// Setup sensor
			var sensor = this.setup_sensor(config);
			// Create heater
			var heater = new Heater(config, sensor, gcode_id);
			this.heaters[heater_name] = heater;
			this.heaters_gcode_id[heater.gcode_id] = heater_name;
			return heater;
		}

		public Heater lookup_heater(string heater_name)
		{
			if (heater_name == "extruder")
			{
				heater_name = "extruder0";
			}
			if (!this.heaters.ContainsKey(heater_name))
			{
				throw new Exception($"Unknown heater '{heater_name}'");
			}
			return this.heaters[heater_name];
		}

		public ISensor setup_sensor(ConfigWrapper config)
		{
			this.printer.try_load_module(config, "thermistor");
			this.printer.try_load_module(config, "adc_temperature");
			this.printer.try_load_module(config, "spi_temperature");
			var sensor_type = config.get("sensor_type");
			if (!this.sensors.ContainsKey(sensor_type))
			{
				throw new Exception($"Unknown temperature sensor '{sensor_type}'");
			}
			return this.sensors[sensor_type](config);
		}

		public void cmd_TURN_OFF_HEATERS(object parameters)
		{
			var print_time = this.printer.lookup_object<ToolHead>("toolhead").get_last_move_time();
			foreach (var heater in this.heaters.Values)
			{
				heater.set_temp(print_time, 0.0);
			}
		}

		public Heater[] get_all_heaters()
		{
			return this.heaters.Values.ToArray();
		}

		public Heater get_heater_by_gcode_id(string gcode_id)
		{
			if (this.heaters_gcode_id.ContainsKey(gcode_id))
			{
				var heater_name = this.heaters_gcode_id[gcode_id];
				return this.heaters[heater_name];
			}
			return null;
		}
	}

}
