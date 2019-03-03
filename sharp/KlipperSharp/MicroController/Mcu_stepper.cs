using KlipperSharp.PulseGeneration;
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
		private ItersolveBase _stepper_kinematics;
		//private itersolve_gen_steps_callback _itersolve_gen_steps;
		private SerialCommand _get_position_cmd;
		private bool ignore_move;

		public delegate bool itersolve_gen_steps_callback(ref ItersolveBase sk, ref move m);

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
			this._stepqueue = new stepcompress((uint)this._oid);
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
				throw new PinsException("Stepper dir pin must be on same mcu as step pin");
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

		public void setup_itersolve(KinematicType type, params object[] parameters)
		{
			ItersolveBase sk;
			switch (type)
			{
				case KinematicType.cartesian: sk = ItersolveCartesian.cartesian_stepper_alloc((string)parameters[0]); break;
				case KinematicType.corexy: sk = ItersolveCoreXY.corexy_stepper_alloc((string)parameters[0]); break;
				case KinematicType.delta: sk = ItersolveDelta.delta_stepper_alloc((double)parameters[0], (double)parameters[1], (double)parameters[2]); break;
				case KinematicType.extruder: sk = ItersolveStepper.extruder_stepper_alloc(); break;
				case KinematicType.polar: sk = ItersolvePolar.polar_stepper_alloc((string)parameters[0]); break;
				case KinematicType.winch: sk = ItersolveWinch.winch_stepper_alloc((double)parameters[0], (double)parameters[1], (double)parameters[2]); break;
				default: throw new ArgumentOutOfRangeException();
			}
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
			this._stepqueue.fill((uint)this._mcu.seconds_to_clock(max_error), this._invert_dir, (uint)step_cmd_id, (uint)dir_cmd_id);
		}

		public int get_oid()
		{
			return this._oid;
		}

		public double get_step_dist()
		{
			return this._step_dist;
		}

		public double calc_position_from_coord(Vector3d coord)
		{
			return _stepper_kinematics.itersolve_calc_position_from_coord(coord.X, coord.Y, coord.Z);
			//return this._ffi_lib.itersolve_calc_position_from_coord(this._stepper_kinematics, coord[0], coord[1], coord[2]);
		}

		public void set_position(Vector3d coord)
		{
			this.set_commanded_position(this.calc_position_from_coord(coord));
		}

		public double get_commanded_position()
		{
			return _stepper_kinematics.itersolve_get_commanded_pos();
			//return this._ffi_lib.itersolve_get_commanded_pos(this._stepper_kinematics);
		}

		public void set_commanded_position(double pos)
		{
			this._mcu_position_offset += this.get_commanded_position() - pos;
			_stepper_kinematics.itersolve_set_commanded_pos(pos);
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

		public ItersolveBase set_stepper_kinematics(ItersolveBase sk)
		{
			var old_sk = this._stepper_kinematics;
			this._stepper_kinematics = sk;
			if (sk != null)
			{
				sk.itersolve_set_stepcompress(ref this._stepqueue, this._step_dist);
				//this._ffi_lib.itersolve_set_stepcompress(sk, this._stepqueue, this._step_dist);
			}
			return old_sk;
		}

		public object set_ignore_move(bool ignore_move)
		{
			this.ignore_move = ignore_move;
			//var was_ignore = this._itersolve_gen_steps != this._ffi_lib.itersolve_gen_steps;
			//var was_ignore = this._itersolve_gen_steps != Itersolve.itersolve_gen_steps;
			//if (ignore_move)
			//{
			//	this._itersolve_gen_steps = (ref KinematicBase sk, ref move m) => false;
			//}
			//else
			//{
			//	this._itersolve_gen_steps = Itersolve.itersolve_gen_steps;
			//}
			return ignore_move;
		}

		public void note_homing_start(ulong homing_clock)
		{
			var ret = _stepqueue.set_homing(homing_clock) > 0;
			//var ret = this._ffi_lib.stepcompress_set_homing(this._stepqueue, homing_clock);
			if (ret)
			{
				throw new McuException("Internal error in stepcompress");
			}
		}

		public unsafe void note_homing_end(bool did_trigger = false)
		{
			var ret = _stepqueue.set_homing(0) > 0;
			//var ret = this._ffi_lib.stepcompress_set_homing(this._stepqueue, 0);
			if (ret)
			{
				throw new McuException("Internal error in stepcompress");
			}
			ret = _stepqueue.reset(0) > 0;
			//ret = this._ffi_lib.stepcompress_reset(this._stepqueue, 0);
			if (ret)
			{
				throw new McuException("Internal error in stepcompress");
			}
			uint* data = stackalloc uint[] { (uint)this._reset_cmd_id, (uint)this._oid, 0 };
			ret = _stepqueue.queue_msg(new ReadOnlySpan<uint>(data, 3)) > 0;
			//ret = this._ffi_lib.stepcompress_queue_msg(this._stepqueue, data, 3);
			if (ret)
			{
				throw new McuException("Internal error in stepcompress");
			}
			if (!did_trigger || this._mcu.is_fileoutput())
			{
				return;
			}
			var parameters = this._get_position_cmd.send_with_response(new object[] { this._oid }, response: "stepper_position", response_oid: this._oid);
			var mcu_pos_dist = parameters.Get<int>("pos") * this._step_dist;
			if (this._invert_dir)
			{
				mcu_pos_dist = -mcu_pos_dist;
			}
			_stepper_kinematics.itersolve_set_commanded_pos(mcu_pos_dist - this._mcu_position_offset);
			//this._ffi_lib.itersolve_set_commanded_pos(this._stepper_kinematics, mcu_pos_dist - this._mcu_position_offset);
		}

		public void step_itersolve(move cmove)
		{
			if (ignore_move)
			{
				return;
			}
			var ret = _stepper_kinematics.itersolve_gen_steps(ref cmove);
			if (ret)
			{
				throw new McuException("Internal error in stepcompress");
			}
		}
	}
}
