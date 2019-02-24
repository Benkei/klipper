using KlipperSharp.MicroController;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KlipperSharp
{
	[Serializable]
	public class EndstopException : Exception
	{
		public EndstopException() { }
		public EndstopException(string message) : base(message) { }
		public EndstopException(string message, Exception inner) : base(message, inner) { }
		protected EndstopException(
		 System.Runtime.Serialization.SerializationInfo info,
		 System.Runtime.Serialization.StreamingContext context) : base(info, context) { }

		public static EndstopException EndstopMoveError(Vector4d pos, string msg = "Move out of range")
		{
			return new EndstopException($"{msg}: {pos.X:0.000} {pos.Y:0.000} {pos.Z:0.000} [{pos.W:0.000}]");
		}
	}

	public class Homing
	{
		public const double HOMING_STEP_DELAY = 0.00000025;
		public const double HOMING_START_DELAY = 0.001;
		public const double ENDSTOP_SAMPLE_TIME = 0.000015;
		public const int ENDSTOP_SAMPLE_COUNT = 4;

		private Machine printer;
		private ToolHead toolhead;
		private List<int> changed_axes;
		private bool verify_retract;

		public Homing(Machine printer)
		{
			this.printer = printer;
			this.toolhead = printer.lookup_object<ToolHead>("toolhead");
			this.changed_axes = new List<int>();
			this.verify_retract = true;
		}

		public void set_no_verify_retract()
		{
			this.verify_retract = false;
		}

		public void set_axes(List<int> axes)
		{
			this.changed_axes = axes;
		}

		public List<int> get_axes()
		{
			return this.changed_axes;
		}

		Vector4d _fill_coord(in (double?, double?, double?, double?) coord)
		{
			Vector4d result;
			// Fill in any None entries in 'coord' with current toolhead position
			var thcoord = this.toolhead.get_position();
			result.X = (coord.Item1 ?? thcoord.X);
			result.Y = (coord.Item2 ?? thcoord.Y);
			result.Z = (coord.Item3 ?? thcoord.Z);
			result.W = (coord.Item4 ?? thcoord.W);
			return result;
		}

		public void set_homed_position(in (double?, double?, double?, double?) pos)
		{
			this.toolhead.set_position(this._fill_coord(pos));
		}

		double _get_homing_speed(double speed, List<(Mcu_endstop endstop, string name)> endstops)
		{
			// Round the requested homing speed so that it is an even
			// number of ticks per step.
			Mcu_stepper mcu_stepper = endstops[0].endstop.get_steppers()[0];
			var adjusted_freq = mcu_stepper.get_mcu().get_adjusted_freq();
			var dist_ticks = adjusted_freq * mcu_stepper.get_step_dist();
			var ticks_per_step = Math.Ceiling(dist_ticks / speed);
			return dist_ticks / ticks_per_step;
		}

		public void homing_move(
			 Vector4d movepos,
			 List<(Mcu_endstop endstop, string name)> endstops,
			 double speed,
			 double dwell_t = 0.0,
			 bool probe_pos = false,
			 bool verify_movement = false)
		{
			// Notify endstops of upcoming home
			foreach (var item in endstops)
			{
				item.endstop.home_prepare();
			}
			if (dwell_t != 0)
			{
				this.toolhead.dwell(dwell_t, check_stall: false);
			}
			// Start endstop checking
			var print_time = this.toolhead.get_last_move_time();
			var start_mcu_pos = (from item in endstops
										let es = item.endstop
										from s in es.get_steppers()
										select (s, item.name, s.get_mcu_position())).ToList();
			foreach (var item in endstops)
			{
				var min_step_dist = (from s in item.endstop.get_steppers() select s.get_step_dist()).Min();
				item.endstop.home_start(print_time, ENDSTOP_SAMPLE_TIME, ENDSTOP_SAMPLE_COUNT, min_step_dist / speed);
			}
			this.toolhead.dwell(HOMING_START_DELAY, check_stall: false);
			// Issue move
			string error = null;
			try
			{
				this.toolhead.move(movepos, speed);
			}
			catch (EndstopException e)
			{
				error = $"Error during homing move: {e}";
			}
			var move_end_print_time = this.toolhead.get_last_move_time();
			this.toolhead.reset_print_time(print_time);
			foreach (var item in endstops)
			{
				try
				{
					item.endstop.home_wait(move_end_print_time);
				}
				catch (TimeoutEndstopException e)
				{
					if (error == null)
					{
						error = $"Failed to home {item.name}: {e}";
					}
				}
			}
			if (probe_pos)
			{
				var pos = this.toolhead.get_kinematics().calc_position();
				this.set_homed_position((pos.X, pos.Y, pos.Z, null));
			}
			else
			{
				this.toolhead.set_position(movepos);
			}
			foreach (var item in endstops)
			{
				try
				{
					item.endstop.home_finalize();
				}
				catch (EndstopException e)
				{
					if (error == null)
					{
						error = e.ToString();
					}
				}
			}
			if (error != null)
			{
				throw new EndstopException(error);
			}
			// Check if some movement occurred
			if (verify_movement)
			{
				foreach (var item in start_mcu_pos)
				{
					if (item.s.get_mcu_position() == item.Item3)
					{
						if (probe_pos)
						{
							throw new EndstopException("Probe triggered prior to movement");
						}
						throw new EndstopException($"Endstop {item.name} still triggered after retract");
					}
				}
			}
		}

		public void home_rails(List<PrinterRail> rails,
			(double?, double?, double?, double?) _forcepos,
			(double?, double?, double?, double?) _movepos,
			double? limit_speed = null)
		{
			// Alter kinematics class to think printer is at forcepos
			var homing_axes = new List<int>(3);
			if (_forcepos.Item1.HasValue) homing_axes.Add(0);
			if (_forcepos.Item2.HasValue) homing_axes.Add(1);
			if (_forcepos.Item3.HasValue) homing_axes.Add(2);
			var forcepos = this._fill_coord(_forcepos);
			var movepos = this._fill_coord(_movepos);
			this.toolhead.set_position(forcepos, homing_axes: homing_axes);
			// Determine homing speed
			var endstops = (from rail in rails
								 from es in rail.get_endstops()
								 select es).ToList();
			var hi = rails[0].get_homing_info();
			var max_velocity = this.toolhead.get_max_velocity().Item1;
			if (limit_speed != null && limit_speed < max_velocity)
			{
				max_velocity = (double)limit_speed;
			}
			var homing_speed = Math.Min(hi.speed, max_velocity);
			homing_speed = this._get_homing_speed(homing_speed, endstops);
			var second_homing_speed = Math.Min(hi.second_homing_speed, max_velocity);
			// Calculate a CPU delay when homing a large axis
			var axes_d = movepos - forcepos;
			var est_move_d = Math.Abs(axes_d.X) + Math.Abs(axes_d.Y) + Math.Abs(axes_d.Z);
			var est_steps = (from item in endstops
								  let es = item.endstop
								  let n = item.name
								  from s in es.get_steppers()
								  select (est_move_d / s.get_step_dist())).Sum();
			var dwell_t = est_steps * HOMING_STEP_DELAY;
			// Perform first home
			this.homing_move(movepos, endstops, homing_speed, dwell_t: dwell_t);
			// Perform second home
			if (hi.retract_dist != 0)
			{
				// Retract
				var move_d = axes_d.Length();
				var retract_r = Math.Min(1.0, hi.retract_dist / move_d);
				var retractpos = movepos - axes_d * retract_r;
				this.toolhead.move(retractpos, homing_speed);
				// Home again
				forcepos = retractpos - axes_d * retract_r;
				this.toolhead.set_position(forcepos);
				this.homing_move(movepos, endstops, second_homing_speed, verify_movement: this.verify_retract);
			}
			// Signal home operation complete
			var ret = this.printer.send_event("homing:homed_rails", this, rails);
			if (ret.Count != 0)
			{
				// Apply any homing offsets
				var adjustpos = this.toolhead.get_kinematics().calc_position();
				foreach (var axis in homing_axes)
				{
					movepos.Set(axis, adjustpos.Get(axis));
				}
				this.toolhead.set_position(movepos);
			}
		}

		public void home_axes(List<int> axes)
		{
			this.changed_axes = axes;
			try
			{
				this.toolhead.get_kinematics().home(this);
			}
			catch (EndstopException)
			{
				this.toolhead.motor_off();
				throw;
			}
		}
	}

}
