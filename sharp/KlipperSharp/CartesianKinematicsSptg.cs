using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp
{
	// Cartesian kinematics stepper pulse time generation
	public class CartesianKinematicsSptg
	{

		static double cart_stepper_x_calc_position(ref stepper_kinematics sk, ref move m, double move_time)
		{
			return Itersolve.move_get_coord(ref m, move_time).x;
		}

		static double cart_stepper_y_calc_position(ref stepper_kinematics sk, ref move m, double move_time)
		{
			return Itersolve.move_get_coord(ref m, move_time).y;
		}

		static double cart_stepper_z_calc_position(ref stepper_kinematics sk, ref move m, double move_time)
		{
			return Itersolve.move_get_coord(ref m, move_time).z;
		}

		public static stepper_kinematics cartesian_stepper_alloc(char axis)
		{
			stepper_kinematics sk = new stepper_kinematics();
			if (axis == 'x')
				sk.calc_position = cart_stepper_x_calc_position;
			else if (axis == 'y')
				sk.calc_position = cart_stepper_y_calc_position;
			else if (axis == 'z')
				sk.calc_position = cart_stepper_z_calc_position;
			return sk;
		}

	}
}
