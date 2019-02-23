using NLog;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace KlipperSharp
{
	public static class MathUtil
	{
		private static readonly Logger logging = LogManager.GetCurrentClassLogger();

		public static double ToRadians(double angle)
		{
			return (Math.PI / 180) * angle;
		}

		public static T Min<T>(in T arg0, in T arg1, in T arg2, in T arg3) where T : IComparable<T>
		{
			T res = arg0.CompareTo(arg1) < 0 ? arg0 : arg1;
			res = res.CompareTo(arg2) < 0 ? res : arg2;
			res = res.CompareTo(arg3) < 0 ? res : arg3;
			return res;
		}
		public static T Min<T>(in T arg0, in T arg1, in T arg2) where T : IComparable<T>
		{
			T res = arg0.CompareTo(arg1) < 0 ? arg0 : arg1;
			res = res.CompareTo(arg2) < 0 ? res : arg2;
			return res;
		}
		public static T Max<T>(in T arg0, in T arg1, in T arg2) where T : IComparable<T>
		{
			T res = arg0.CompareTo(arg1) > 0 ? arg0 : arg1;
			res = res.CompareTo(arg2) > 0 ? res : arg2;
			return res;
		}

		// Helper code that implements coordinate descent
		/*
		public static object coordinate_descent(Dictionary<string, object> adj_params, Dictionary<string, object> parameters,
														Func<Dictionary<string, object>, double> error_func)
		{
			// Define potential changes
			//parameters = parameters.ToDictionary();
			var dp = adj_params.ToDictionary(param_name => param_name, param_name => 1.0);
			// Calculate the error
			var best_err = error_func(parameters);
			logging.Info("Coordinate descent initial error: %s", best_err);
			var threshold = 0.00001;
			var rounds = 0;
			while (dp.values().Sum() > threshold && rounds < 10000)
			{
				rounds += 1;
				foreach (var param_name in adj_params.Keys)
				{
					var orig = parameters[param_name];
					parameters[param_name] = orig + dp[param_name];
					var err = error_func(parameters);
					if (err < best_err)
					{
						// There was some improvement
						best_err = err;
						dp[param_name] *= 1.1;
						continue;
					}
					parameters[param_name] = orig - dp[param_name];
					err = error_func(parameters);
					if (err < best_err)
					{
						// There was some improvement
						best_err = err;
						dp[param_name] *= 1.1;
						continue;
					}
					parameters[param_name] = orig;
					dp[param_name] *= 0.9;
				}
			}
			logging.Info("Coordinate descent best_err: %s  rounds: %d", best_err, rounds);
			return parameters;
		}
		*/

		// Helper to run the coordinate descent function in a background
		// process so that it does not block the main thread.
		/*
		public static object background_coordinate_descent(Machine printer, Dictionary<string, object> adj_params, Dictionary<string, object> parameters, object error_func)
		{
			var _tup_1 = multiprocessing.Pipe();
			var parent_conn = _tup_1.Item1;
			var child_conn = _tup_1.Item2;
			Func<object> wrapper = () =>
			{
				var res = coordinate_descent(adj_params, parameters, error_func);
				child_conn.send(res);
				child_conn.close();
			};
			// Start a process to perform the calculation
			var calc_proc = multiprocessing.Process(target: wrapper);
			calc_proc.daemon = true;
			calc_proc.start();
			// Wait for the process to finish
			var reactor = printer.get_reactor();
			var gcode = printer.lookup_object("gcode");
			var eventtime = reactor.monotonic();
			while (calc_proc.is_alive())
			{
				if (eventtime > last_report_time + 5.0)
				{
					var last_report_time = eventtime;
					gcode.respond_info("Working on calibration...");
				}
				eventtime = reactor.pause(eventtime + 0.1);
			}
			// Return results
			var res = parent_conn.recv();
			calc_proc.join();
			parent_conn.close();
			return res;
		}
		*/

		//#####################################################################
		// Trilateration
		//#####################################################################
		// Trilateration finds the intersection of three spheres. See the
		// wikipedia article for the details of the algorithm.
		public static Vector3 trilateration(Vector3 sphere_coord1, Vector3 sphere_coord2, Vector3 sphere_coord3, double radius1, double radius2, double radius3)
		{
			//var _tup_1 = sphere_coords;
			//var sphere_coord1 = _tup_1.Item1;
			//var sphere_coord2 = _tup_1.Item2;
			//var sphere_coord3 = _tup_1.Item3;
			var s21 = matrix_sub(sphere_coord2, sphere_coord1);
			var s31 = matrix_sub(sphere_coord3, sphere_coord1);
			var d = Math.Sqrt(matrix_magsq(s21));
			var ex = matrix_mul(s21, 1.0 / d);
			var i = matrix_dot(ex, s31);
			var vect_ey = matrix_sub(s31, matrix_mul(ex, i));
			var ey = matrix_mul(vect_ey, 1.0 / Math.Sqrt(matrix_magsq(vect_ey)));
			var ez = matrix_cross(ex, ey);
			var j = matrix_dot(ey, s31);
			var x = (radius1 - radius2 + Math.Pow(d, 2)) / (2.0 * d);
			var y = (radius1 - radius3 - Math.Pow(x, 2) + Math.Pow(x - i, 2) + Math.Pow(j, 2)) / (2.0 * j);
			var z = -Math.Sqrt(radius1 - Math.Pow(x, 2) - Math.Pow(y, 2));
			var ex_x = matrix_mul(ex, x);
			var ey_y = matrix_mul(ey, y);
			var ez_z = matrix_mul(ez, z);
			return matrix_add(sphere_coord1, matrix_add(ex_x, matrix_add(ey_y, ez_z)));
		}

		//#####################################################################
		// Matrix helper functions for 3x1 matrices
		//#####################################################################
		public static Vector3 matrix_cross(Vector3 m1, Vector3 m2)
		{
			return new Vector3(
				m1.Y * m2.Z - m1.Z * m2.Y,
				m1.Z * m2.X - m1.X * m2.Z,
				m1.X * m2.Y - m1.Y * m2.X
		  );
		}

		public static double matrix_dot(Vector3 m1, Vector3 m2)
		{
			return m1.X * m2.X + m1.Y * m2.Y + m1.Z * m2.Z;
		}

		public static double matrix_magsq(Vector3 m1)
		{
			return Math.Pow(m1.X, 2) + Math.Pow(m1.Y, 2) + Math.Pow(m1.Z, 2);
		}

		public static Vector3 matrix_add(Vector3 m1, Vector3 m2)
		{
			return new Vector3(
				m1.X + m2.X,
				m1.Y + m2.Y,
				m1.Z + m2.Z
		  );
		}

		public static Vector3 matrix_sub(Vector3 m1, Vector3 m2)
		{
			return m1 - m2;
		}

		public static Vector3 matrix_mul(Vector3 m1, double s)
		{
			return m1 * (float)s;
		}

	}
}
