// toolhead.py
//# Code for coordinating events on the printer toolhead
//#
//# Copyright (C) 2016-2018  Kevin O'Connor <kevin@koconnor.net>
//#
//# This file may be distributed under the terms of the GNU GPLv3 license.

using KlipperSharp.PulseGeneration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KlipperSharp
{
	//# Common suffixes: _d is distance (in mm), _v is velocity (in
	//# mm/second), _v2 is velocity squared (mm^2/s^2), _t is time (in
	//# seconds), _r is ratio (scalar between 0.0 and 1.0)

	//# Class to track each move request
	public class Move
	{
		private ToolHead toolhead;
		public Vector4d start_pos;
		public move cmove;
		public bool is_kinematic_move;
		public Vector4d axes_d;
		internal double accel;
		public double move_d;
		public Vector4d end_pos;
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

		public Move(ToolHead toolhead, Vector4d start_pos, Vector4d end_pos, double speed)
		{
			this.toolhead = toolhead;
			this.start_pos = start_pos;
			this.end_pos = end_pos;
			this.accel = toolhead.max_accel;
			var velocity = Math.Min(speed, toolhead.max_velocity);
			this.cmove = toolhead.cmove;
			this.is_kinematic_move = true;
			this.axes_d = end_pos - start_pos;
			this.move_d = ((Vector3d)axes_d).Length(); //Math.Sqrt((from d in axes_d.GetRange(0, 3) select (d * d)).Sum());
			if (move_d < 0.000000001)
			{
				// Extrude only move
				this.end_pos = start_pos;
				axes_d.X = 0.0f;
				this.move_d = Math.Abs(axes_d.W);
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

		public unsafe void calc_junction(Move prev_move)
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
			var junction_cos_theta = -(axes_d.X * prev_axes_d.X + axes_d.Y * prev_axes_d.Y + axes_d.Z * prev_axes_d.Z) / (this.move_d * prev_move.move_d);
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
			double* ptr = stackalloc double[8] {
				R * this.accel,
				R * prev_move.accel,
				move_centripetal_v2,
				prev_move_centripetal_v2,
				extruder_v2,
				max_cruise_v2,
				prev_move.max_cruise_v2,
				prev_move.max_start_v2 + prev_move.delta_v2
			};
			max_start_v2 = MathUtil.Min(new ReadOnlySpan<double>(ptr, 8));
			max_smoothed_v2 = Math.Min(max_start_v2, prev_move.max_smoothed_v2 + prev_move.smooth_delta_v2);
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
				this.toolhead.cmove.fill(next_move_time,
					this.accel_t, this.cruise_t, this.decel_t,
					this.start_pos.X, this.start_pos.Y, this.start_pos.Z,
					this.axes_d.X, this.axes_d.Y, this.axes_d.Z,
					this.start_v, this.cruise_v, this.accel);
				this.toolhead.kin.move(next_move_time, this);
			}
			if (this.axes_d.W != 0)
			{
				this.toolhead.extruder.move(next_move_time, this);
			}
			this.toolhead.update_move_time(this.accel_t + this.cruise_t + this.decel_t);
		}
	}

}
