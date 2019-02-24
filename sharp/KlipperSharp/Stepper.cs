using KlipperSharp.MicroController;
using KlipperSharp.PulseGeneration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KlipperSharp
{
	// Tracking of shared stepper enable pins
	public class StepperEnablePin
	{
		private Mcu_digital_out mcu_enable;
		private int enable_count;

		public StepperEnablePin(Mcu_digital_out mcu_enable, int enable_count = 0)
		{
			this.mcu_enable = mcu_enable;
			this.enable_count = enable_count;
		}

		public void set_enable(double print_time, bool enable)
		{
			if (enable)
			{
				if (this.enable_count == 0)
				{
					this.mcu_enable.set_digital(print_time, true);
				}
				this.enable_count += 1;
			}
			else
			{
				this.enable_count -= 1;
				if (this.enable_count == 0)
				{
					this.mcu_enable.set_digital(print_time, false);
				}
			}
		}
		public static StepperEnablePin lookup_enable_pin(PrinterPins ppins, string pin)
		{
			if (pin == null)
			{
				return new StepperEnablePin(null, 9999);
			}
			var pin_params = ppins.lookup_pin(pin, can_invert: true, share_type: "stepper_enable");
			if (pin_params.classPin == null)
			{
				var mcu_enable = pin_params.chip.setup_pin<Mcu_digital_out>("digital_out", pin_params);
				mcu_enable.setup_max_duration(0.0);
				pin_params.classPin = new StepperEnablePin(mcu_enable);
			}
			return pin_params.classPin;
		}
	}

	// Code storing the definitions for a stepper motor
	public class PrinterStepper
	{
		private string name;
		private bool need_motor_enable;
		private Mcu_stepper mcu_stepper;
		public Func<double> get_step_dist;
		public Action<move> step_itersolve;
		public Action<KinematicType, object[]> setup_itersolve;
		private Func<KinematicBase, KinematicBase> set_stepper_kinematics;
		private Func<bool, object> set_ignore_move;
		private Func<Vector3d, double> calc_position_from_coord;
		public Action<Vector3d> set_position;
		public Func<double> get_commanded_position;
		public Action<double> set_commanded_position;
		public Func<int> get_mcu_position;
		private StepperEnablePin enable;

		public PrinterStepper(ConfigWrapper config)
		{
			var printer = config.get_printer();
			this.name = config.get_name();
			this.need_motor_enable = true;
			// Stepper definition
			var ppins = printer.lookup_object<PrinterPins>("pins");
			var step_pin = config.get("step_pin");
			this.mcu_stepper = ppins.setup_pin<Mcu_stepper>("stepper", step_pin);
			var dir_pin = config.get("dir_pin");
			var dir_pin_params = ppins.lookup_pin(dir_pin, can_invert: true);
			mcu_stepper.setup_dir_pin(dir_pin_params);
			var step_dist = config.getfloat("step_distance", above: 0.0);
			mcu_stepper.setup_step_distance(step_dist);
			this.enable = StepperEnablePin.lookup_enable_pin(ppins, config.get("enable_pin", null));
			// Register STEPPER_BUZZ command
			//var force_move = printer.try_load_module(config, "force_move");
			//force_move.register_stepper(this);
			// Wrappers
			this.step_itersolve = mcu_stepper.step_itersolve;
			this.setup_itersolve = mcu_stepper.setup_itersolve;
			this.set_stepper_kinematics = mcu_stepper.set_stepper_kinematics;
			this.set_ignore_move = mcu_stepper.set_ignore_move;
			this.calc_position_from_coord = mcu_stepper.calc_position_from_coord;
			this.set_position = mcu_stepper.set_position;
			this.get_commanded_position = mcu_stepper.get_commanded_position;
			this.set_commanded_position = mcu_stepper.set_commanded_position;
			this.get_mcu_position = mcu_stepper.get_mcu_position;
			this.get_step_dist = mcu_stepper.get_step_dist;
		}

		public string get_name(bool shortName = false)
		{
			if (shortName && this.name.StartsWith("stepper_"))
			{
				return this.name.Substring(8);
			}
			return this.name;
		}

		public void add_to_endstop(Mcu_endstop mcu_endstop)
		{
			mcu_endstop.add_stepper(this.mcu_stepper);
		}

		public double _dist_to_time(double dist, double start_velocity, double accel)
		{
			// Calculate the time it takes to travel a distance with constant accel
			var time_offset = start_velocity / accel;
			return Math.Sqrt(2.0 * dist / accel + Math.Pow(time_offset, 2)) - time_offset;
		}

		public void set_max_jerk(double max_halt_velocity, double max_accel)
		{
			// Calculate the firmware's maximum halt interval time
			var step_dist = this.get_step_dist();
			var last_step_time = this._dist_to_time(step_dist, max_halt_velocity, max_accel);
			var second_last_step_time = this._dist_to_time(2.0 * step_dist, max_halt_velocity, max_accel);
			var min_stop_interval = second_last_step_time - last_step_time;
			this.mcu_stepper.setup_min_stop_interval(min_stop_interval);
		}

		public void motor_enable(double print_time, bool enable = false)
		{
			if (this.need_motor_enable != !enable)
			{
				this.enable.set_enable(print_time, enable);
			}
			this.need_motor_enable = !enable;
		}

		public bool is_motor_enabled()
		{
			return !this.need_motor_enable;
		}
	}


	//#####################################################################
	// Stepper controlled rails
	//#####################################################################

	public interface ICustomEndstop
	{
		double get_position_endstop();
	}

	// A motor control "rail" with one (or more) steppers and one (or more)
	// endstops.
	public class PrinterRail
	{
		private List<PrinterStepper> steppers;
		private string name;
		public Action<move> step_itersolve;
		private double position_endstop;
		private double position_min;
		private double position_max;
		private double homing_speed;
		private double second_homing_speed;
		private double homing_retract_dist;
		private bool? homing_positive_dir;
		private List<(Mcu_endstop endstop, string name)> endstops;
		public Func<double> get_commanded_position;
		public Func<bool> is_motor_enabled;

		public PrinterRail(ConfigWrapper config, bool need_position_minmax = true, double? default_position_endstop = null)
		{
			// Primary stepper
			var stepper = new PrinterStepper(config);
			this.steppers = new List<PrinterStepper> { stepper };
			this.name = stepper.get_name(shortName: true);
			this.step_itersolve = stepper.step_itersolve;
			this.get_commanded_position = stepper.get_commanded_position;
			this.is_motor_enabled = stepper.is_motor_enabled;
			// Primary endstop and its position
			var printer = config.get_printer();
			var ppins = printer.lookup_object<PrinterPins>("pins");
			var mcu_endstop = ppins.setup_pin<Mcu_endstop>("endstop", config.get("endstop_pin"));
			this.endstops = new List<(Mcu_endstop endstop, string name)> { (mcu_endstop, this.name) };
			stepper.add_to_endstop(mcu_endstop);
			if (mcu_endstop is ICustomEndstop)
			{
				this.position_endstop = ((ICustomEndstop)mcu_endstop).get_position_endstop();
			}
			else if (default_position_endstop == null)
			{
				this.position_endstop = config.getfloat("position_endstop");
			}
			else
			{
				this.position_endstop = config.getfloat("position_endstop", default_position_endstop);
			}
			//var query_endstops = printer.try_load_module(config, "query_endstops");
			//query_endstops.register_endstop(mcu_endstop, this.name);
			// Axis range
			if (need_position_minmax)
			{
				this.position_min = config.getfloat("position_min", 0.0);
				this.position_max = config.getfloat("position_max", above: this.position_min);
			}
			else
			{
				this.position_min = 0.0;
				this.position_max = this.position_endstop;
			}
			if (this.position_endstop < this.position_min || this.position_endstop > this.position_max)
			{
				throw new Exception($"position_endstop in section '{config.get_name()}' must be between position_min and position_max");
			}
			// Homing mechanics
			this.homing_speed = config.getfloat("homing_speed", 5.0, above: 0.0);
			this.second_homing_speed = config.getfloat("second_homing_speed", this.homing_speed / 2.0, above: 0.0);
			this.homing_retract_dist = config.getfloat("homing_retract_dist", 5.0, minval: 0.0);
			this.homing_positive_dir = config.getboolean("homing_positive_dir", null);
			if (this.homing_positive_dir == null)
			{
				var axis_len = this.position_max - this.position_min;
				if (this.position_endstop <= this.position_min + axis_len / 4.0)
				{
					this.homing_positive_dir = false;
				}
				else if (this.position_endstop >= this.position_max - axis_len / 4.0)
				{
					this.homing_positive_dir = true;
				}
				else
				{
					throw new Exception($"Unable to infer homing_positive_dir in section '{config.get_name()}'");
				}
			}
		}

		public Vector2d get_range()
		{
			return new Vector2d(this.position_min, this.position_max);
		}

		public struct Homing_info
		{
			public double speed;
			public double position_endstop;
			public double retract_dist;
			public bool? positive_dir;
			public double second_homing_speed;
		}

		public Homing_info get_homing_info()
		{
			return new Homing_info()
			{
				speed = this.homing_speed,
				position_endstop = this.position_endstop,
				retract_dist = this.homing_retract_dist,
				positive_dir = this.homing_positive_dir,
				second_homing_speed = this.second_homing_speed
			};
		}

		public List<PrinterStepper> get_steppers()
		{
			return this.steppers;
		}

		public List<(Mcu_endstop endstop, string name)> get_endstops()
		{
			return this.endstops;
		}

		public void add_extra_stepper(ConfigWrapper config)
		{
			var stepper = new PrinterStepper(config);
			this.steppers.Add(stepper);
			this.step_itersolve = this.step_multi_itersolve;
			var mcu_endstop = this.endstops[0].endstop;
			var endstop_pin = config.get("endstop_pin", null);
			if (endstop_pin != null)
			{
				var printer = config.get_printer();
				var ppins = printer.lookup_object<PrinterPins>("pins");
				mcu_endstop = ppins.setup_pin<Mcu_endstop>("endstop", endstop_pin);
				var name = stepper.get_name(shortName: true);
				this.endstops.Add((mcu_endstop, name));
				//var query_endstops = printer.try_load_module(config, "query_endstops");
				//query_endstops.register_endstop(mcu_endstop, name);
			}
			stepper.add_to_endstop(mcu_endstop);
		}

		public void add_to_endstop(Mcu_endstop mcu_endstop)
		{
			foreach (var stepper in this.steppers)
			{
				stepper.add_to_endstop(mcu_endstop);
			}
		}

		public void step_multi_itersolve(move cmove)
		{
			foreach (var stepper in this.steppers)
			{
				stepper.step_itersolve(cmove);
			}
		}

		public void setup_itersolve(KinematicType type, params object[] parameters)
		{
			foreach (var stepper in this.steppers)
			{
				stepper.setup_itersolve(type, parameters);
			}
		}

		public void set_max_jerk(double max_halt_velocity, double max_accel)
		{
			foreach (var stepper in this.steppers)
			{
				stepper.set_max_jerk(max_halt_velocity, max_accel);
			}
		}

		public void set_commanded_position(double pos)
		{
			foreach (var stepper in this.steppers)
			{
				stepper.set_commanded_position(pos);
			}
		}

		public void set_position(Vector3d coord)
		{
			foreach (var stepper in this.steppers)
			{
				stepper.set_position(coord);
			}
		}

		public void motor_enable(double print_time, bool enable = false)
		{
			foreach (var stepper in this.steppers)
			{
				stepper.motor_enable(print_time, enable);
			}
		}


		// Wrapper for dual stepper motor support
		public static PrinterRail LookupMultiRail(ConfigWrapper config)
		{
			var rail = new PrinterRail(config);
			for (int i = 0; i < 99; i++)
			{
				if (!config.has_section(config.get_name() + i))
				{
					break;
				}
				rail.add_extra_stepper(config.getsection(config.get_name() + i));
			}
			return rail;
		}
	}

}
