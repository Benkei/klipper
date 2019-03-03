// Code for handling the kinematics of corexy robots
//
// Copyright (C) 2017-2018  Kevin O'Connor <kevin@koconnor.net>
//
// This file may be distributed under the terms of the GNU GPLv3 license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KlipperSharp.Kinematics
{
	public class KinematicCoreXY : KinematicBase
	{
		private PrinterRail[] rails;
		private double max_z_velocity;
		private double max_z_accel;
		private bool need_motor_enable;
		private Vector2d[] limits;

		public KinematicCoreXY(ToolHead toolhead, ConfigWrapper config)
		{
			// Setup axis rails
			this.rails = new PrinterRail[] {
				new PrinterRail(config.getsection("stepper_x")),
				new PrinterRail(config.getsection("stepper_y")),
				PrinterRail.LookupMultiRail(config.getsection("stepper_z"))
			};
			this.rails[0].add_to_endstop(this.rails[1].get_endstops()[0].endstop);
			this.rails[1].add_to_endstop(this.rails[0].get_endstops()[0].endstop);
			this.rails[0].setup_itersolve(KinematicType.corexy, "+");
			this.rails[1].setup_itersolve(KinematicType.corexy, "-");
			this.rails[2].setup_itersolve(KinematicType.cartesian, "z");
			// Setup boundary checks
			var _tup_1 = toolhead.get_max_velocity();
			var max_velocity = _tup_1.Item1;
			var max_accel = _tup_1.Item2;
			this.max_z_velocity = config.getfloat("max_z_velocity", max_velocity, above: 0.0, maxval: max_velocity);
			this.max_z_accel = config.getfloat("max_z_accel", max_accel, above: 0.0, maxval: max_accel);
			this.need_motor_enable = true;
			this.limits = new Vector2d[] { new Vector2d(1, -1), new Vector2d(1, -1), new Vector2d(1, -1) };
			// Setup stepper max halt velocity
			var max_halt_velocity = toolhead.get_max_axis_halt();
			var max_xy_halt_velocity = max_halt_velocity * Math.Sqrt(2.0);
			this.rails[0].set_max_jerk(max_xy_halt_velocity, max_accel);
			this.rails[1].set_max_jerk(max_xy_halt_velocity, max_accel);
			this.rails[2].set_max_jerk(Math.Min(max_halt_velocity, this.max_z_velocity), this.max_z_accel);
		}

		public override List<PrinterStepper> get_steppers(string flags = "")
		{
			if (flags == "Z")
			{
				return this.rails[2].get_steppers();
			}
			return (from rail in this.rails
					  from s in rail.get_steppers()
					  select s).ToList();
		}

		public override Vector3d calc_position()
		{
			Vector3d pos;
			pos.X = rails[0].get_commanded_position();
			pos.Y = rails[1].get_commanded_position();
			pos.Z = rails[2].get_commanded_position();
			pos.X = 0.5f * (pos.X + pos.Y);
			pos.Y = 0.5f * (pos.X - pos.Y);
			return pos;
		}

		public override void set_position(Vector3d newpos, List<int> homing_axes)
		{
			for (int i = 0; i < rails.Length; i++)
			{
				var rail = rails[i];
				rail.set_position(newpos);
				if (homing_axes.Contains(i))
				{
					this.limits[i] = rail.get_range();
				}
			}
		}

		public override void home(Homing homing_state)
		{
			// Each axis is homed independently and in order
			foreach (var axis in homing_state.get_axes())
			{
				var rail = this.rails[axis];
				// Determine movement
				var _tup_1 = rail.get_range();
				var position_min = _tup_1.X;
				var position_max = _tup_1.Y;
				var hi = rail.get_homing_info();
				var homepos = new List<double?> { null, null, null, null };
				homepos[axis] = hi.position_endstop;
				var forcepos = homepos.ToList();
				if ((bool)hi.positive_dir)
				{
					forcepos[axis] -= 1.5 * (hi.position_endstop - position_min);
				}
				else
				{
					forcepos[axis] += 1.5 * (position_max - hi.position_endstop);
				}
				// Perform homing
				double? limit_speed = null;
				if (axis == 2)
				{
					limit_speed = this.max_z_velocity;
				}
				homing_state.home_rails(new List<PrinterRail> { rail },
					(forcepos[0], forcepos[1], forcepos[2], forcepos[3]),
					(homepos[0], homepos[1], homepos[2], homepos[3]),
					limit_speed);
			}
		}

		public override void motor_off(double print_time)
		{
			this.limits[0] = new Vector2d(0, 0);
			this.limits[1] = new Vector2d(0, 0);
			this.limits[2] = new Vector2d(0, 0);
			foreach (var rail in this.rails)
			{
				rail.motor_enable(print_time, false);
			}
			this.need_motor_enable = true;
		}

		void _check_motor_enable(double print_time, Move move)
		{
			if (move.axes_d.X != 0 || move.axes_d.Y != 0)
			{
				this.rails[0].motor_enable(print_time, true);
				this.rails[1].motor_enable(print_time, true);
			}
			if (move.axes_d.Y != 0)
			{
				this.rails[2].motor_enable(print_time, true);
			}
			var need_motor_enable = false;
			foreach (var rail in this.rails)
			{
				need_motor_enable |= !rail.is_motor_enabled();
			}
			this.need_motor_enable = need_motor_enable;
		}

		void _check_endstops(Move move)
		{
			var end_pos = move.end_pos;
			for (int i = 0; i < 3; i++)
			{
				if (move.axes_d.Get(i) != 0 && (end_pos.Get(i) < this.limits[i].X || end_pos.Get(i) > this.limits[i].Y))
				{
					if (this.limits[i].X > this.limits[i].Y)
					{
						throw EndstopException.EndstopMoveError(end_pos, "Must home axis first");
					}
					throw EndstopException.EndstopMoveError(end_pos);
				}
			}
		}

		public override void check_move(Move move)
		{
			var limits = this.limits;
			var xpos = move.end_pos.X;
			var ypos = move.end_pos.Y;
			if (xpos < limits[0].X || xpos > limits[0].Y || ypos < limits[1].X || ypos > limits[1].Y)
			{
				this._check_endstops(move);
			}
			if (move.axes_d.Z == 0)
			{
				// Normal XY move - use defaults
				return;
			}
			// Move with Z - update velocity and accel for slower Z axis
			this._check_endstops(move);
			var z_ratio = move.move_d / Math.Abs(move.axes_d.Z);
			move.limit_speed(this.max_z_velocity * z_ratio, this.max_z_accel * z_ratio);
		}

		public override void move(double print_time, Move move)
		{
			if (this.need_motor_enable)
			{
				this._check_motor_enable(print_time, move);
			}
			var axes_d = move.axes_d;
			var cmove = move.cmove;
			var rail_x = rails[0];
			var rail_y = rails[1];
			var rail_z = rails[2];
			if (axes_d.X != 0 || axes_d.Y != 0)
			{
				rail_x.step_itersolve(cmove);
				rail_y.step_itersolve(cmove);
			}
			if (axes_d.Z != 0)
			{
				rail_z.step_itersolve(cmove);
			}
		}
	}
}
