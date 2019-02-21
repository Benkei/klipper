using KlipperSharp.PulseGeneration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KlipperSharp
{
	public class Move
	{
		private ToolHead toolhead;
		public double[] start_pos;
		public move cmove;
		public bool is_kinematic_move;
		public List<double> axes_d;
		internal double accel;
		public double move_d;
		public double[] end_pos;
		internal double min_move_t;
		internal double max_start_v2;
		public double max_cruise_v2;
		public double delta_v2;
		public double max_smoothed_v2;
		public double smooth_delta_v2;
		private double accel_r;
		private double decel_r;
		private double cruise_r;
		internal double start_v;
		internal double cruise_v;
		internal double end_v;
		internal double accel_t;
		internal double cruise_t;
		internal double decel_t;
		internal double extrude_r;
		internal double extrude_max_corner_v;

		public Move(ToolHead toolhead, List<double> start_pos, List<double> end_pos, double speed)
		{
			this.toolhead = toolhead;
			this.start_pos = new[] { start_pos[0], start_pos[1], start_pos[2], start_pos[3] };
			this.end_pos = new[] { end_pos[0], end_pos[1], end_pos[2], end_pos[3] };
			this.accel = toolhead.max_accel;
			var velocity = Math.Min(speed, toolhead.max_velocity);
			this.cmove = toolhead.cmove;
			this.is_kinematic_move = true;
			this.axes_d = (from i in new[] { 0, 1, 2, 3 } select (end_pos[i] - start_pos[i])).ToList();
			this.move_d = Math.Sqrt((from d in axes_d.GetRange(0, 3) select (d * d)).Sum());
			if (move_d < 1E-09)
			{
				// Extrude only move
				this.end_pos = new[] { start_pos[0], start_pos[1], start_pos[2], end_pos[3] };
				axes_d[0] = 0.0;
				this.move_d = Math.Abs(axes_d[3]);
				this.accel = 99999999.9;
				velocity = speed;
				this.is_kinematic_move = false;
			}
			this.min_move_t = move_d / velocity;
			// Junction speeds are tracked in velocity squared.  The
			// delta_v2 is the maximum amount of this squared-velocity that
			// can change in this move.
			this.max_start_v2 = 0.0;
			this.max_cruise_v2 = Math.Pow(velocity, 2);
			this.delta_v2 = 2.0 * move_d * this.accel;
			this.max_smoothed_v2 = 0.0;
			this.smooth_delta_v2 = 2.0 * move_d * toolhead.max_accel_to_decel;
		}

		public void limit_speed(double speed, double accel)
		{
			var speed2 = Math.Pow(speed, 2);
			if (speed2 < this.max_cruise_v2)
			{
				this.max_cruise_v2 = speed2;
				this.min_move_t = this.move_d / speed;
			}
			this.accel = Math.Min(this.accel, accel);
			this.delta_v2 = 2.0 * this.move_d * this.accel;
			this.smooth_delta_v2 = Math.Min(this.smooth_delta_v2, this.delta_v2);
		}

		public void calc_junction(Move prev_move)
		{
			if (!this.is_kinematic_move || !prev_move.is_kinematic_move)
			{
				return;
			}
			// Allow extruder to calculate its maximum junction
			var extruder_v2 = this.toolhead.extruder.calc_junction(prev_move, this);
			// Find max velocity using approximated centripetal velocity as
			// described at:
			// https://onehossshay.wordpress.com/2011/09/24/improving_grbl_cornering_algorithm/
			var axes_d = this.axes_d;
			var prev_axes_d = prev_move.axes_d;
			var junction_cos_theta = -(axes_d[0] * prev_axes_d[0] + axes_d[1] * prev_axes_d[1] + axes_d[2] * prev_axes_d[2]) / (this.move_d * prev_move.move_d);
			if (junction_cos_theta > 0.999999)
			{
				return;
			}
			junction_cos_theta = Math.Max(junction_cos_theta, -0.999999);
			var sin_theta_d2 = Math.Sqrt(0.5 * (1.0 - junction_cos_theta));
			var R = this.toolhead.junction_deviation * sin_theta_d2 / (1.0 - sin_theta_d2);
			var tan_theta_d2 = sin_theta_d2 / Math.Sqrt(0.5 * (1.0 + junction_cos_theta));
			var move_centripetal_v2 = 0.5 * this.move_d * tan_theta_d2 * this.accel;
			var prev_move_centripetal_v2 = 0.5 * prev_move.move_d * tan_theta_d2 * prev_move.accel;
			//this.max_start_v2 = Math.Min(R * this.accel, R * prev_move.accel,
			//	move_centripetal_v2, prev_move_centripetal_v2,
			//	extruder_v2, this.max_cruise_v2,
			//	prev_move.max_cruise_v2, prev_move.max_start_v2 + prev_move.delta_v2
			//	);
			this.max_start_v2 = Math.Min(R * this.accel, R * prev_move.accel);
			this.max_start_v2 = Math.Min(this.max_start_v2, move_centripetal_v2);
			this.max_start_v2 = Math.Min(this.max_start_v2, prev_move_centripetal_v2);
			this.max_start_v2 = Math.Min(this.max_start_v2, extruder_v2);
			this.max_start_v2 = Math.Min(this.max_start_v2, this.max_cruise_v2);
			this.max_start_v2 = Math.Min(this.max_start_v2, prev_move.max_cruise_v2);
			this.max_start_v2 = Math.Min(this.max_start_v2, prev_move.max_start_v2 + prev_move.delta_v2);
			this.max_smoothed_v2 = Math.Min(this.max_start_v2, prev_move.max_smoothed_v2 + prev_move.smooth_delta_v2);
		}

		public void set_junction(double start_v2, double cruise_v2, double end_v2)
		{
			// Determine accel, cruise, and decel portions of the move distance
			var inv_delta_v2 = 1.0 / this.delta_v2;
			this.accel_r = (cruise_v2 - start_v2) * inv_delta_v2;
			this.decel_r = (cruise_v2 - end_v2) * inv_delta_v2;
			this.cruise_r = 1.0 - accel_r - decel_r;
			// Determine move velocities
			this.start_v = Math.Sqrt(start_v2);
			this.cruise_v = Math.Sqrt(cruise_v2);
			this.end_v = Math.Sqrt(end_v2);
			// Determine time spent in each portion of move (time is the
			// distance divided by average velocity)
			this.accel_t = accel_r * this.move_d / ((start_v + cruise_v) * 0.5);
			this.cruise_t = cruise_r * this.move_d / cruise_v;
			this.decel_t = decel_r * this.move_d / ((end_v + cruise_v) * 0.5);
		}

		public void move()
		{
			// Generate step times for the move
			var next_move_time = this.toolhead.get_next_move_time();
			if (this.is_kinematic_move)
			{
				this.toolhead.move_fill(ref this.cmove, next_move_time,
					this.accel_t, this.cruise_t, this.decel_t,
					this.start_pos[0], this.start_pos[1], this.start_pos[2],
					this.axes_d[0], this.axes_d[1], this.axes_d[2],
					this.start_v, this.cruise_v, this.accel);
				this.toolhead.kin.move(next_move_time, this);
			}
			if (this.axes_d[3] != 0)
			{
				this.toolhead.extruder.move(next_move_time, this);
			}
			this.toolhead.update_move_time(this.accel_t + this.cruise_t + this.decel_t);
		}
	}

}
