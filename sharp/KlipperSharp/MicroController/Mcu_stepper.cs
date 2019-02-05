using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp.MicroController
{
	public class Mcu_stepper
	{
		private Mcu _mcu;
		private int _oid;
		private int _step_pin;
		private int _invert_step;
		private int _dir_pin;
		private int _invert_dir;
		private double _mcu_position_offset;
		private double _step_dist;
		private double _min_stop_interval;
		private object _reset_cmd_id;
		private object _stepqueue;
		private object _stepper_kinematics;
		private object _itersolve_gen_steps;
		private object _get_position_cmd;

		public Mcu_stepper(Mcu mcu, object pin_parameters)
		{
			this._mcu = mcu;
			this._oid = this._mcu.create_oid();
			this._mcu.register_config_callback(this._build_config);
			this._step_pin = pin_parameters["pin"];
			this._invert_step = pin_parameters["invert"];
			this._dir_pin = 0;
			this._mcu_position_offset = 0.0;
			this._step_dist = 0.0;
			this._min_stop_interval = 0.0;
			this._reset_cmd_id = null;
			var _tup_1 = chelper.get_ffi();
			var ffi_main = _tup_1.Item1;
			this._ffi_lib = _tup_1.Item2;
			this._stepqueue = ffi_main.gc(this._ffi_lib.stepcompress_alloc(oid), this._ffi_lib.stepcompress_free);
			this._mcu.register_stepqueue(this._stepqueue);
			this._stepper_kinematics = null;
			this.set_ignore_move(false);
		}

		public Mcu get_mcu()
		{
			return this._mcu;
		}

		public void setup_dir_pin(object pin_parameters)
		{
			if (pin_parameters["chip"] != this._mcu)
			{
				throw new Exception("Stepper dir pin must be on same mcu as step pin");
			}
			this._dir_pin = pin_parameters["pin"];
			this._invert_dir = pin_parameters["invert"];
		}

		public void setup_min_stop_interval(double min_stop_interval)
		{
			this._min_stop_interval = min_stop_interval;
		}

		public void setup_step_distance(double step_dist)
		{
			this._step_dist = step_dist;
		}

		public void setup_itersolve(object alloc_func, params object[] parameters)
		{
			var _tup_1 = chelper.get_ffi();
			var ffi_main = _tup_1.Item1;
			var ffi_lib = _tup_1.Item2;
			var sk = ffi_main.gc(getattr(ffi_lib, alloc_func)(parameters), ffi_lib.free);
			this.set_stepper_kinematics(sk);
		}

		public void _build_config()
		{
			var max_error = this._mcu.get_max_stepper_error();
			var min_stop_interval = Math.Max(0.0, this._min_stop_interval - max_error);
			this._mcu.add_config_cmd(String.Format("config_stepper oid=%d step_pin=%s dir_pin=%s\" min_stop_interval=%d invert_step=%d\"", this._oid, this._step_pin, this._dir_pin, this._mcu.seconds_to_clock(min_stop_interval), this._invert_step));
			this._mcu.add_config_cmd(String.Format("reset_step_clock oid=%d clock=0", this._oid), is_init: true);
			var step_cmd_id = this._mcu.lookup_command_id("queue_step oid=%c interval=%u count=%hu add=%hi");
			var dir_cmd_id = this._mcu.lookup_command_id("set_next_step_dir oid=%c dir=%c");
			this._reset_cmd_id = this._mcu.lookup_command_id("reset_step_clock oid=%c clock=%u");
			this._get_position_cmd = this._mcu.lookup_command("stepper_get_position oid=%c");
			this._ffi_lib.stepcompress_fill(this._stepqueue, this._mcu.seconds_to_clock(max_error), this._invert_dir, step_cmd_id, dir_cmd_id);
		}

		public int get_oid()
		{
			return this._oid;
		}

		public double get_step_dist()
		{
			return this._step_dist;
		}

		public double calc_position_from_coord(object coord)
		{
			return this._ffi_lib.itersolve_calc_position_from_coord(this._stepper_kinematics, coord[0], coord[1], coord[2]);
		}

		public void set_position(object coord)
		{
			this.set_commanded_position(this.calc_position_from_coord(coord));
		}

		public double get_commanded_position()
		{
			return this._ffi_lib.itersolve_get_commanded_pos(this._stepper_kinematics);
		}

		public void set_commanded_position(double pos)
		{
			this._mcu_position_offset += this.get_commanded_position() - pos;
			this._ffi_lib.itersolve_set_commanded_pos(this._stepper_kinematics, pos);
		}

		public int get_mcu_position()
		{
			var mcu_pos_dist = this.get_commanded_position() + this._mcu_position_offset;
			var mcu_pos = mcu_pos_dist / this._step_dist;
			if (mcu_pos >= 0.0)
			{
				return Convert.ToInt32(mcu_pos + 0.5);
			}
			return Convert.ToInt32(mcu_pos - 0.5);
		}

		public object set_stepper_kinematics(object sk)
		{
			var old_sk = this._stepper_kinematics;
			this._stepper_kinematics = sk;
			if (sk != null)
			{
				this._ffi_lib.itersolve_set_stepcompress(sk, this._stepqueue, this._step_dist);
			}
			return old_sk;
		}

		public virtual object set_ignore_move(bool ignore_move)
		{
			var was_ignore = this._itersolve_gen_steps != this._ffi_lib.itersolve_gen_steps;
			if (ignore_move)
			{
				this._itersolve_gen_steps = args => 0;
			}
			else
			{
				this._itersolve_gen_steps = this._ffi_lib.itersolve_gen_steps;
			}
			return was_ignore;
		}

		public void note_homing_start(double homing_clock)
		{
			var ret = this._ffi_lib.stepcompress_set_homing(this._stepqueue, homing_clock);
			if (ret)
			{
				throw new Exception("Internal error in stepcompress");
			}
		}

		public void note_homing_end(bool did_trigger = false)
		{
			var ret = this._ffi_lib.stepcompress_set_homing(this._stepqueue, 0);
			if (ret)
			{
				throw new Exception("Internal error in stepcompress");
			}
			ret = this._ffi_lib.stepcompress_reset(this._stepqueue, 0);
			if (ret)
			{
				throw new Exception("Internal error in stepcompress");
			}
			var data = Tuple.Create(this._reset_cmd_id, this._oid, 0);
			ret = this._ffi_lib.stepcompress_queue_msg(this._stepqueue, data, 3);
			if (ret)
			{
				throw new Exception("Internal error in stepcompress");
			}
			if (!did_trigger || this._mcu.is_fileoutput())
			{
				return;
			}
			var parameters = this._get_position_cmd.send_with_response(new List<object> {
					 this._oid
				}, response: "stepper_position", response_oid: this._oid);
			var mcu_pos_dist = parameters["pos"] * this._step_dist;
			if (this._invert_dir != 0)
			{
				mcu_pos_dist = -mcu_pos_dist;
			}
			this._ffi_lib.itersolve_set_commanded_pos(this._stepper_kinematics, mcu_pos_dist - this._mcu_position_offset);
		}

		public void step_itersolve(object cmove)
		{
			var ret = this._itersolve_gen_steps(this._stepper_kinematics, cmove);
			if (ret)
			{
				throw new Exception("Internal error in stepcompress");
			}
		}
	}
}
