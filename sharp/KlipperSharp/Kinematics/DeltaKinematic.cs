// Code for handling the kinematics of linear delta robots
//
// Copyright (C) 2016-2018  Kevin O'Connor <kevin@koconnor.net>
//
// This file may be distributed under the terms of the GNU GPLv3 license.

using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KlipperSharp.Kinematics
{
	public class DeltaKinematic : BaseKinematic
	{
		private static readonly Logger logging = LogManager.GetCurrentClassLogger();

		private PrinterRail[] rails;
		private double max_velocity;
		private double max_accel;
		private double max_z_velocity;
		private double slow_ratio;
		private double radius;
		private double printable_radius;
		private Vector3d arm_lengths;
		private Vector3d arm2;
		private Vector3d abs_endstops;
		private Vector3d angles;
		private (double cos, double sin)[] towers;
		private bool need_motor_enable;
		private double limit_xy2;
		private Vector3d home_position;
		private double max_z;
		private double min_z;
		private double limit_z;
		private double slow_xy2;
		private double very_slow_xy2;
		private double max_xy2;
		private bool need_home;

		public DeltaKinematic(ToolHead toolhead, ConfigWrapper config)
		{
			// Setup tower rails
			var stepper_config_a = config.getsection("stepper_a");
			var stepper_config_b = config.getsection("stepper_b");
			var stepper_config_c = config.getsection("stepper_c");
			var rail_a = new PrinterRail(stepper_config_a, need_position_minmax: false);
			var a_endstop = rail_a.get_homing_info().position_endstop;
			var rail_b = new PrinterRail(stepper_config_b, need_position_minmax: false, default_position_endstop: a_endstop);
			var rail_c = new PrinterRail(stepper_config_c, need_position_minmax: false, default_position_endstop: a_endstop);
			this.rails = new PrinterRail[]
			{
				rail_a,
				rail_b,
				rail_c
			};
			// Setup stepper max halt velocity
			var _tup_1 = toolhead.get_max_velocity();
			this.max_velocity = _tup_1.Item1;
			this.max_accel = _tup_1.Item2;
			this.max_z_velocity = config.getfloat("max_z_velocity", this.max_velocity, above: 0.0, maxval: this.max_velocity);
			this.slow_ratio = config.getfloat("delta_slow_ratio", above: 1.0);
			var max_halt_velocity = toolhead.get_max_axis_halt() * slow_ratio;
			var max_halt_accel = this.max_accel * slow_ratio;
			foreach (var rail in this.rails)
			{
				rail.set_max_jerk(max_halt_velocity, max_halt_accel);
			}
			// Read radius and arm lengths
			this.radius = config.getfloat("delta_radius", above: 0.0);
			this.printable_radius = config.getfloat("delta_printable_radius", above: 0.0);
			var arm_length_a = stepper_config_a.getfloat("arm_length", above: radius);

			arm_lengths.X = arm_length_a;
			arm_lengths.Y = stepper_config_b.getfloat("arm_length", arm_length_a, above: radius);
			arm_lengths.Z = stepper_config_c.getfloat("arm_length", arm_length_a, above: radius);
			arm2.X = Math.Pow(arm_lengths.X, 2);
			arm2.Y = Math.Pow(arm_lengths.Y, 2);
			arm2.Z = Math.Pow(arm_lengths.Z, 2);
			abs_endstops.X = (rails[0].get_homing_info().position_endstop + Math.Sqrt(arm2.X - Math.Pow(radius, 2)));
			abs_endstops.Y = (rails[1].get_homing_info().position_endstop + Math.Sqrt(arm2.Y - Math.Pow(radius, 2)));
			abs_endstops.Z = (rails[2].get_homing_info().position_endstop + Math.Sqrt(arm2.Z - Math.Pow(radius, 2)));
			// Determine tower locations in cartesian space
			angles.X = stepper_config_a.getfloat("angle", 210.0);
			angles.Y = stepper_config_b.getfloat("angle", 330.0);
			angles.Z = stepper_config_c.getfloat("angle", 90.0);
			this.towers = new (double, double)[]
			{
				(Math.Cos(MathUtil.ToRadians(angles.X)) * radius, Math.Sin(MathUtil.ToRadians(angles.X)) * radius),
				(Math.Cos(MathUtil.ToRadians(angles.Y)) * radius, Math.Sin(MathUtil.ToRadians(angles.Y)) * radius),
				(Math.Cos(MathUtil.ToRadians(angles.Z)) * radius, Math.Sin(MathUtil.ToRadians(angles.Z)) * radius)
			};

			rails[0].setup_itersolve(KinematicType.delta, arm2.X, towers[0].cos, towers[0].sin);
			rails[1].setup_itersolve(KinematicType.delta, arm2.Y, towers[1].cos, towers[1].sin);
			rails[2].setup_itersolve(KinematicType.delta, arm2.Z, towers[2].cos, towers[2].sin);

			// Setup boundary checks
			this.need_motor_enable = true;
			this.limit_xy2 = -1.0;
			this.home_position = this._actuator_to_cartesian(this.abs_endstops);
			this.max_z = (from rail in this.rails select rail.get_homing_info().position_endstop).Min();
			this.min_z = config.getfloat("minimum_z_position", 0, maxval: this.max_z);

			limit_z = double.MaxValue;
			var min = abs_endstops - arm_lengths;
			limit_z = MathUtil.Min(min.X, min.Y, min.Z, limit_z);

			logging.Info(String.Format("Delta max build height %.2fmm (radius tapered above %.2fmm)", this.max_z, this.limit_z));
			// Find the point where an XY move could result in excessive
			// tower movement
			var half_min_step_dist = (from r in this.rails select r.get_steppers()[0].get_step_dist()).Min() * 0.5;
			var min_arm_length = MathUtil.Min(arm_lengths.X, arm_lengths.Y, arm_lengths.Z);
			this.slow_xy2 = Math.Pow(printable_radius * 0.6, 2);
			this.very_slow_xy2 = Math.Pow(printable_radius * 0.8, 2);
			this.max_xy2 = Math.Pow(
				MathUtil.Min(printable_radius, min_arm_length - printable_radius,
				ratio_to_dist(4.0 * slow_ratio, half_min_step_dist, min_arm_length) - printable_radius),
				2);

			logging.Info(String.Format("Delta max build radius %.2fmm (moves slowed past %.2fmm and %.2fmm)",
				Math.Sqrt(this.max_xy2), Math.Sqrt(this.slow_xy2), Math.Sqrt(this.very_slow_xy2)));

			this.set_position(Vector3d.Zero, null);
		}

		private static double ratio_to_dist(double ratio, double half_min_step_dist, double min_arm_length)
		{
			return ratio * Math.Sqrt(Math.Pow(min_arm_length, 2) / (Math.Pow(ratio, 2) + 1.0) - Math.Pow(half_min_step_dist, 2)) + half_min_step_dist;
		}

		public override List<PrinterStepper> get_steppers(string flags = "")
		{
			return (from rail in this.rails
					  from s in rail.get_steppers()
					  select s).ToList();
		}

		Vector3d _actuator_to_cartesian(Vector3d spos)
		{
			Vector3d sphere_coord_x = new Vector3d(this.towers[0].cos, this.towers[0].sin, spos.X);
			Vector3d sphere_coord_y = new Vector3d(this.towers[1].cos, this.towers[1].sin, spos.X);
			Vector3d sphere_coord_z = new Vector3d(this.towers[2].cos, this.towers[2].sin, spos.X);
			return MathUtil.trilateration(sphere_coord_x, sphere_coord_y, sphere_coord_z, arm2.X, arm2.Y, arm2.Z);
		}

		public override Vector3d calc_position()
		{
			Vector3d spos;
			spos.X = rails[0].get_commanded_position();
			spos.Y = rails[1].get_commanded_position();
			spos.Z = rails[2].get_commanded_position();
			return this._actuator_to_cartesian(spos);
		}

		public override void set_position(Vector3d newpos, List<int> homing_axes)
		{
			foreach (var rail in this.rails)
			{
				rail.set_position(newpos);
			}
			this.limit_xy2 = -1.0;
			if (homing_axes.Contains(0) && homing_axes.Contains(1) && homing_axes.Contains(2))
			{
				this.need_home = false;
			}
		}

		public override void home(Homing homing_state)
		{
			// All axes are homed simultaneously
			homing_state.set_axes(new List<int> { 0, 1, 2 });
			var forcepos = home_position;
			forcepos.Z = (-1.5 * Math.Sqrt(MathUtil.Max(arm2.X, arm2.Y, arm2.Z) - this.max_xy2));

			homing_state.home_rails(new List<PrinterRail>(rails),
				(forcepos.X, forcepos.Y, forcepos.Z, null),
				(home_position.X, home_position.Y, home_position.Z, null),
				max_z_velocity);
		}

		public override void motor_off(double print_time)
		{
			this.limit_xy2 = -1.0;
			foreach (var rail in this.rails)
			{
				rail.motor_enable(print_time, false);
			}
			this.need_motor_enable = true;
		}

		void _check_motor_enable(double print_time)
		{
			foreach (var rail in this.rails)
			{
				rail.motor_enable(print_time, true);
			}
			this.need_motor_enable = false;
		}

		public override void check_move(Move move)
		{
			var end_pos = move.end_pos;
			var end_xy2 = Math.Pow(end_pos.X, 2) + Math.Pow(end_pos.Y, 2);
			if (end_xy2 <= this.limit_xy2 && move.axes_d.Z == 0)
			{
				// Normal XY move
				return;
			}
			if (this.need_home)
			{
				throw EndstopException.EndstopMoveError(end_pos, "Must home first");
			}
			var end_z = end_pos.Z;
			var limit_xy2 = this.max_xy2;
			if (end_z > this.limit_z)
			{
				limit_xy2 = Math.Min(limit_xy2, Math.Pow(this.max_z - end_z, 2));
			}
			if (end_xy2 > limit_xy2 || end_z > this.max_z || end_z < this.min_z)
			{
				// Move out of range - verify not a homing move
				var ePos = new Vector3d(end_pos.X, end_pos.Y, end_pos.Z);
				if (ePos != this.home_position
					|| end_z < this.min_z || end_z > this.home_position.Z)
				{
					throw EndstopException.EndstopMoveError(end_pos);
				}
				limit_xy2 = -1.0;
			}
			if (move.axes_d.Z != 0)
			{
				move.limit_speed(this.max_z_velocity, move.accel);
				limit_xy2 = -1.0;
			}
			// Limit the speed/accel of this move if is is at the extreme
			// end of the build envelope
			var extreme_xy2 = Math.Max(end_xy2, Math.Pow(move.start_pos.X, 2) + Math.Pow(move.start_pos.Y, 2));
			if (extreme_xy2 > this.slow_xy2)
			{
				var r = 0.5;
				if (extreme_xy2 > this.very_slow_xy2)
				{
					r = 0.25;
				}
				var max_velocity = this.max_velocity;
				if (move.axes_d.Z != 0)
				{
					max_velocity = this.max_z_velocity;
				}
				move.limit_speed(max_velocity * r, this.max_accel * r);
				limit_xy2 = -1.0;
			}
			this.limit_xy2 = Math.Min(limit_xy2, this.slow_xy2);
		}

		public override void move(double print_time, Move move)
		{
			if (this.need_motor_enable)
			{
				this._check_motor_enable(print_time);
			}
			foreach (var rail in this.rails)
			{
				rail.step_itersolve(move.cmove);
			}
		}

		// Helper function for DELTA_CALIBRATE script
		public DeltaCalibrateInfo get_calibrate_params()
		{
			DeltaCalibrateInfo result;
			result.Radius = radius;
			result.X = new DeltaCalibrateParam()
			{
				Angle = angles.X,
				Arm = arm_lengths.X,
				Endstop = rails[0].get_homing_info().position_endstop,
				Stepdist = rails[0].get_steppers()[0].get_step_dist(),
			};
			result.Y = new DeltaCalibrateParam()
			{
				Angle = angles.Y,
				Arm = arm_lengths.Y,
				Endstop = rails[1].get_homing_info().position_endstop,
				Stepdist = rails[1].get_steppers()[0].get_step_dist(),
			};
			result.Z = new DeltaCalibrateParam()
			{
				Angle = angles.Z,
				Arm = arm_lengths.Z,
				Endstop = rails[2].get_homing_info().position_endstop,
				Stepdist = rails[2].get_steppers()[0].get_step_dist(),
			};
			return result;
		}
	}

	public struct DeltaCalibrateInfo
	{
		public double Radius;
		public DeltaCalibrateParam X, Y, Z;
	}
	public struct DeltaCalibrateParam
	{
		public double Angle, Arm, Endstop, Stepdist;
	}
}
