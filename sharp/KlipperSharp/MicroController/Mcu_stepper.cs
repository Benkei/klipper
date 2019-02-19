using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp.MicroController
{
	public class Mcu_stepper
	{
		private Mcu _mcu;
		private int _oid;
		private string _step_pin;
		private bool _invert_step;
		private string _dir_pin;
		private bool _invert_dir;
		private double _mcu_position_offset;
		private double _step_dist;
		private double _min_stop_interval;
		private int _reset_cmd_id;
		private stepcompress _stepqueue;
		private stepper_kinematics _stepper_kinematics;
		private itersolve_gen_steps_callback _itersolve_gen_steps;
		private SerialCommand _get_position_cmd;

		public delegate bool itersolve_gen_steps_callback(ref stepper_kinematics sk, ref move m);

		public Mcu_stepper(Mcu mcu, PinParams pin_parameters)
		{
			this._mcu = mcu;
			this._oid = this._mcu.create_oid();
			this._mcu.register_config_callback(this._build_config);
			this._step_pin = pin_parameters.pin;
			this._invert_step = pin_parameters.invert;
			this._dir_pin = null;
			this._mcu_position_offset = 0.0;
			this._step_dist = 0.0;
			this._min_stop_interval = 0.0;
			this._reset_cmd_id = 0;
			this._get_position_cmd = null;
			//var _tup_1 = chelper.get_ffi();
			//var ffi_main = _tup_1.Item1;
			//this._ffi_lib = _tup_1.Item2;
			//this._stepqueue = ffi_main.gc(this._ffi_lib.stepcompress_alloc(this._oid), this._ffi_lib.stepcompress_free);
			this._stepqueue = Stepcompress.stepcompress_alloc((uint)this._oid);
			this._mcu.register_stepqueue(this._stepqueue);
			this._stepper_kinematics = null;
			this.set_ignore_move(false);
		}

		public Mcu get_mcu()
		{
			return this._mcu;
		}

		public void setup_dir_pin(PinParams pin_parameters)
		{
			if (pin_parameters.chip != this._mcu)
			{
				throw new Exception("Stepper dir pin must be on same mcu as step pin");
			}
			this._dir_pin = pin_parameters.pin;
			this._invert_dir = pin_parameters.invert;
		}

		public void setup_min_stop_interval(double min_stop_interval)
		{
			this._min_stop_interval = min_stop_interval;
		}

		public void setup_step_distance(double step_dist)
		{
			this._step_dist = step_dist;
		}

		public void setup_itersolve(string alloc_func, params object[] parameters)
		{
			//stepper_kinematics sk = alloc_func(parameters);
			stepper_kinematics sk = new stepper_kinematics();
			this.set_stepper_kinematics(sk);
		}

		public void _build_config()
		{
			var max_error = this._mcu.get_max_stepper_error();
			var min_stop_interval = Math.Max(0.0, this._min_stop_interval - max_error);
			this._mcu.add_config_cmd($"config_stepper oid={this._oid} step_pin={this._step_pin} dir_pin={this._dir_pin} min_stop_interval={this._mcu.seconds_to_clock(min_stop_interval)} invert_step={(this._invert_step ? 1 : 0)}");
			this._mcu.add_config_cmd($"reset_step_clock oid={this._oid} clock=0", is_init: true);
			var step_cmd_id = this._mcu.lookup_command_id("queue_step oid=%c interval=%u count=%hu add=%hi");
			var dir_cmd_id = this._mcu.lookup_command_id("set_next_step_dir oid=%c dir=%c");
			this._reset_cmd_id = this._mcu.lookup_command_id("reset_step_clock oid=%c clock=%u");
			this._get_position_cmd = this._mcu.lookup_command("stepper_get_position oid=%c");
			Stepcompress.stepcompress_fill(ref this._stepqueue, (uint)this._mcu.seconds_to_clock(max_error),
				this._invert_dir ? 1u : 0u, (uint)step_cmd_id, (uint)dir_cmd_id);
		}

		public int get_oid()
		{
			return this._oid;
		}

		public double get_step_dist()
		{
			return this._step_dist;
		}

		public double calc_position_from_coord(List<double> coord)
		{
			return Itersolve.itersolve_calc_position_from_coord(ref this._stepper_kinematics, coord[0], coord[1], coord[2]);
			//return this._ffi_lib.itersolve_calc_position_from_coord(this._stepper_kinematics, coord[0], coord[1], coord[2]);
		}

		public void set_position(List<double> coord)
		{
			this.set_commanded_position(this.calc_position_from_coord(coord));
		}

		public double get_commanded_position()
		{
			return Itersolve.itersolve_get_commanded_pos(ref this._stepper_kinematics);
			//return this._ffi_lib.itersolve_get_commanded_pos(this._stepper_kinematics);
		}

		public void set_commanded_position(double pos)
		{
			this._mcu_position_offset += this.get_commanded_position() - pos;
			Itersolve.itersolve_set_commanded_pos(ref this._stepper_kinematics, pos);
			//this._ffi_lib.itersolve_set_commanded_pos(this._stepper_kinematics, pos);
		}

		public int get_mcu_position()
		{
			var mcu_pos_dist = this.get_commanded_position() + this._mcu_position_offset;
			var mcu_pos = mcu_pos_dist / this._step_dist;
			if (mcu_pos >= 0.0)
			{
				return (int)(mcu_pos + 0.5);
			}
			return (int)(mcu_pos - 0.5);
		}

		public stepper_kinematics set_stepper_kinematics(stepper_kinematics sk)
		{
			var old_sk = this._stepper_kinematics;
			this._stepper_kinematics = sk;
			if (sk != null)
			{
				Itersolve.itersolve_set_stepcompress(ref sk, ref this._stepqueue, this._step_dist);
				//this._ffi_lib.itersolve_set_stepcompress(sk, this._stepqueue, this._step_dist);
			}
			return old_sk;
		}

		public virtual object set_ignore_move(bool ignore_move)
		{
			var was_ignore = this._itersolve_gen_steps != Itersolve.itersolve_gen_steps;
			//var was_ignore = this._itersolve_gen_steps != this._ffi_lib.itersolve_gen_steps;
			if (ignore_move)
			{
				this._itersolve_gen_steps = (ref stepper_kinematics sk, ref move m) => false;
			}
			else
			{
				this._itersolve_gen_steps = Itersolve.itersolve_gen_steps;
			}
			return was_ignore;
		}

		public void note_homing_start(ulong homing_clock)
		{
			var ret = Stepcompress.stepcompress_set_homing(ref this._stepqueue, homing_clock) > 0;
			//var ret = this._ffi_lib.stepcompress_set_homing(this._stepqueue, homing_clock);
			if (ret)
			{
				throw new Exception("Internal error in stepcompress");
			}
		}

		public unsafe void note_homing_end(bool did_trigger = false)
		{
			var ret = Stepcompress.stepcompress_set_homing(ref this._stepqueue, 0) > 0;
			//var ret = this._ffi_lib.stepcompress_set_homing(this._stepqueue, 0);
			if (ret)
			{
				throw new Exception("Internal error in stepcompress");
			}
			ret = Stepcompress.stepcompress_reset(ref this._stepqueue, 0) > 0;
			//ret = this._ffi_lib.stepcompress_reset(this._stepqueue, 0);
			if (ret)
			{
				throw new Exception("Internal error in stepcompress");
			}
			uint* data = stackalloc uint[] { (uint)this._reset_cmd_id, (uint)this._oid, 0 };
			ret = Stepcompress.stepcompress_queue_msg(ref this._stepqueue, data, 3) > 0;
			//ret = this._ffi_lib.stepcompress_queue_msg(this._stepqueue, data, 3);
			if (ret)
			{
				throw new Exception("Internal error in stepcompress");
			}
			if (!did_trigger || this._mcu.is_fileoutput())
			{
				return;
			}
			var parameters = this._get_position_cmd.send_with_response(new object[] { this._oid }, response: "stepper_position", response_oid: this._oid);
			var mcu_pos_dist = (double)parameters["pos"] * this._step_dist;
			if (this._invert_dir)
			{
				mcu_pos_dist = -mcu_pos_dist;
			}
			Itersolve.itersolve_set_commanded_pos(ref this._stepper_kinematics, mcu_pos_dist - this._mcu_position_offset);
			//this._ffi_lib.itersolve_set_commanded_pos(this._stepper_kinematics, mcu_pos_dist - this._mcu_position_offset);
		}

		public void step_itersolve(move cmove)
		{
			var ret = this._itersolve_gen_steps(ref this._stepper_kinematics, ref cmove);
			if (ret)
			{
				throw new Exception("Internal error in stepcompress");
			}
		}
	}
}
