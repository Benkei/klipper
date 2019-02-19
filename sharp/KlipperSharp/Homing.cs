using KlipperSharp.MicroController;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KlipperSharp
{
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

		public List<double> _fill_coord(List<double> coord)
		{
			// Fill in any None entries in 'coord' with current toolhead position
			var thcoord = this.toolhead.get_position();
			for (int i = 0; i < coord.Count; i++)
			{
				//if (coord[i] != 0)
				{
					thcoord[i] = coord[i];
				}
			}
			return thcoord;
		}

		public void set_homed_position(List<double> pos)
		{
			this.toolhead.set_position(this._fill_coord(pos));
		}

		public double _get_homing_speed(double speed, List<(Mcu_endstop endstop, string name)> endstops)
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
			 List<double> movepos,
			 List<(Mcu_endstop endstop, string name)> endstops,
			 double speed,
			 double dwell_t = 0.0,
			 bool probe_pos = false,
			 bool verify_movement = false)
		{
			string name;
			Mcu_endstop mcu_endstop;
			// Notify endstops of upcoming home
			foreach (var _tup_1 in endstops)
			{
				mcu_endstop = _tup_1.Item1;
				name = _tup_1.Item2;
				mcu_endstop.home_prepare();
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
			foreach (var _tup_3 in endstops)
			{
				mcu_endstop = _tup_3.Item1;
				name = _tup_3.Item2;
				var min_step_dist = (from s in mcu_endstop.get_steppers() select s.get_step_dist()).Min();
				mcu_endstop.home_start(print_time, ENDSTOP_SAMPLE_TIME, ENDSTOP_SAMPLE_COUNT, min_step_dist / speed);
			}
			this.toolhead.dwell(HOMING_START_DELAY, check_stall: false);
			// Issue move
			string error = null;
			try
			{
				this.toolhead.move(movepos, speed);
			}
			catch (/*EndstopError*/ Exception e)
			{
				error = $"Error during homing move: {e}";
			}
			var move_end_print_time = this.toolhead.get_last_move_time();
			this.toolhead.reset_print_time(print_time);
			foreach (var _tup_4 in endstops)
			{
				mcu_endstop = _tup_4.Item1;
				name = _tup_4.Item2;
				try
				{
					mcu_endstop.home_wait(move_end_print_time);
				}
				catch (Exception e)
				{
					if (error == null)
					{
						error = $"Failed to home {name}: {e}";
					}
				}
			}
			if (probe_pos)
			{
				this.set_homed_position(new List<double>(this.toolhead.get_kinematics().calc_position()) { 0 });
			}
			else
			{
				this.toolhead.set_position(movepos);
			}
			foreach (var _tup_5 in endstops)
			{
				mcu_endstop = _tup_5.Item1;
				name = _tup_5.Item2;
				try
				{
					mcu_endstop.home_finalize();
				}
				catch (/*EndstopError*/ Exception e)
				{
					if (error == null)
					{
						error = e.ToString();
					}
				}
			}
			if (error != null)
			{
				throw /*EndstopError*/new Exception(error);
			}
			// Check if some movement occurred
			if (verify_movement)
			{
				foreach (var _tup_6 in start_mcu_pos)
				{
					var s = _tup_6.Item1;
					name = _tup_6.Item2;
					var pos = _tup_6.Item3;
					if (s.get_mcu_position() == pos)
					{
						if (probe_pos)
						{
							throw /*EndstopError*/new Exception("Probe triggered prior to movement");
						}
						throw /*EndstopError*/new Exception($"Endstop {name} still triggered after retract");
					}
				}
			}
		}

		public void home_rails(List<PrinterRail> rails, List<double> forcepos, List<double> movepos, double? limit_speed = null)
		{
			// Alter kinematics class to think printer is at forcepos
			var homing_axes = (from axis in Enumerable.Range(0, 3)
									 where forcepos[axis] != 0
									 select axis).ToList();
			forcepos = this._fill_coord(forcepos);
			movepos = this._fill_coord(movepos);
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
			var axes_d = (from item in movepos.Zip(forcepos, (mp, fp) => (mp, fp))
							  select (item.mp - item.fp)).ToList();
			var est_move_d = Math.Abs(axes_d[0]) + Math.Abs(axes_d[1]) + Math.Abs(axes_d[2]);
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
				var move_d = Math.Sqrt((from d in axes_d.GetRange(0, 3) select (d * d)).Sum());
				var retract_r = Math.Min(1.0, hi.retract_dist / move_d);
				var retractpos = (from item in movepos.Zip(axes_d, (mp, ad) => (mp, ad))
										select (item.mp - item.ad * retract_r)).ToList();
				this.toolhead.move(retractpos, homing_speed);
				// Home again
				forcepos = (from item in retractpos.Zip(axes_d, (rp, ad) => (rp, ad))
								select (item.rp - item.ad * retract_r)).ToList();
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
					movepos[axis] = adjustpos[axis];
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
			catch (/*EndstopError*/ Exception)
			{
				this.toolhead.motor_off();
				throw;
			}
		}
	}

}
