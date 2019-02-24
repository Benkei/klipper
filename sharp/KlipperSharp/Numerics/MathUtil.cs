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
		public static Vector3d trilateration(Vector3d sphere_coord1, Vector3d sphere_coord2, Vector3d sphere_coord3, double radius1, double radius2, double radius3)
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
		public static Vector3d matrix_cross(Vector3d m1, Vector3d m2)
		{
			return new Vector3d(
				m1.Y * m2.Z - m1.Z * m2.Y,
				m1.Z * m2.X - m1.X * m2.Z,
				m1.X * m2.Y - m1.Y * m2.X
		  );
		}

		public static double matrix_dot(Vector3d m1, Vector3d m2)
		{
			return m1.X * m2.X + m1.Y * m2.Y + m1.Z * m2.Z;
		}

		public static double matrix_magsq(Vector3d m1)
		{
			return Math.Pow(m1.X, 2) + Math.Pow(m1.Y, 2) + Math.Pow(m1.Z, 2);
		}

		public static Vector3d matrix_add(Vector3d m1, Vector3d m2)
		{
			return new Vector3d(
				m1.X + m2.X,
				m1.Y + m2.Y,
				m1.Z + m2.Z
		  );
		}

		public static Vector3d matrix_sub(Vector3d m1, Vector3d m2)
		{
			return m1 - m2;
		}

		public static Vector3d matrix_mul(Vector3d m1, double s)
		{
			return m1 * s;
		}









		/// <summary>
		/// The value for which all absolute numbers smaller than are considered equal to zero.
		/// </summary>
		public const float ZeroTolerance = 1e-6f; // Value a 8x higher than 1.19209290E-07F

		/// <summary>
		/// A value specifying the approximation of π which is 180 degrees.
		/// </summary>
		public const float Pi = (float)Math.PI;

		/// <summary>
		/// A value specifying the approximation of 2π which is 360 degrees.
		/// </summary>
		public const float TwoPi = (float)(2 * Math.PI);

		/// <summary>
		/// A value specifying the approximation of π/2 which is 90 degrees.
		/// </summary>
		public const float PiOverTwo = (float)(Math.PI / 2);

		/// <summary>
		/// A value specifying the approximation of π/4 which is 45 degrees.
		/// </summary>
		public const float PiOverFour = (float)(Math.PI / 4);

		/// <summary>
		/// Checks if a and b are almost equals, taking into account the magnitude of floating point numbers (unlike <see cref="WithinEpsilon"/> method). See Remarks.
		/// See remarks.
		/// </summary>
		/// <param name="a">The left value to compare.</param>
		/// <param name="b">The right value to compare.</param>
		/// <returns><c>true</c> if a almost equal to b, <c>false</c> otherwise</returns>
		/// <remarks>
		/// The code is using the technique described by Bruce Dawson in 
		/// <a href="http://randomascii.wordpress.com/2012/02/25/comparing-floating-point-numbers-2012-edition/">Comparing Floating point numbers 2012 edition</a>. 
		/// </remarks>
		public unsafe static bool NearEqual(float a, float b)
		{
			// Check if the numbers are really close -- needed
			// when comparing numbers near zero.
			if (IsZero(a - b))
				return true;

			// Original from Bruce Dawson: http://randomascii.wordpress.com/2012/02/25/comparing-floating-point-numbers-2012-edition/
			int aInt = *(int*)&a;
			int bInt = *(int*)&b;

			// Different signs means they do not match.
			if ((aInt < 0) != (bInt < 0))
				return false;

			// Find the difference in ULPs.
			int ulp = Math.Abs(aInt - bInt);

			// Choose of maxUlp = 4
			// according to http://code.google.com/p/googletest/source/browse/trunk/include/gtest/internal/gtest-internal.h
			const int maxUlp = 4;
			return (ulp <= maxUlp);
		}

		public unsafe static bool NearEqual(double a, double b)
		{
			// Check if the numbers are really close -- needed
			// when comparing numbers near zero.
			if (IsZero(a - b))
				return true;

			// Original from Bruce Dawson: http://randomascii.wordpress.com/2012/02/25/comparing-floating-point-numbers-2012-edition/
			long aInt = *(long*)&a;
			long bInt = *(long*)&b;

			// Different signs means they do not match.
			if ((aInt < 0) != (bInt < 0))
				return false;

			// Find the difference in ULPs.
			long ulp = Math.Abs(aInt - bInt);

			// Choose of maxUlp = 4
			// according to http://code.google.com/p/googletest/source/browse/trunk/include/gtest/internal/gtest-internal.h
			const int maxUlp = 4;
			return (ulp <= maxUlp);
		}

		/// <summary>
		/// Determines whether the specified value is close to zero (0.0f).
		/// </summary>
		/// <param name="a">The floating value.</param>
		/// <returns><c>true</c> if the specified value is close to zero (0.0f); otherwise, <c>false</c>.</returns>
		public static bool IsZero(float a)
		{
			return Math.Abs(a) < ZeroTolerance;
		}

		public static bool IsZero(double a)
		{
			return Math.Abs(a) < ZeroTolerance;
		}

		/// <summary>
		/// Determines whether the specified value is close to one (1.0f).
		/// </summary>
		/// <param name="a">The floating value.</param>
		/// <returns><c>true</c> if the specified value is close to one (1.0f); otherwise, <c>false</c>.</returns>
		public static bool IsOne(float a)
		{
			return IsZero(a - 1.0f);
		}

		public static bool IsOne(double a)
		{
			return IsZero(a - 1.0);
		}

		/// <summary>
		/// Checks if a - b are almost equals within a float epsilon.
		/// </summary>
		/// <param name="a">The left value to compare.</param>
		/// <param name="b">The right value to compare.</param>
		/// <param name="epsilon">Epsilon value</param>
		/// <returns><c>true</c> if a almost equal to b within a float epsilon, <c>false</c> otherwise</returns>
		public static bool WithinEpsilon(float a, float b, float epsilon)
		{
			float num = a - b;
			return ((-epsilon <= num) && (num <= epsilon));
		}

		public static bool WithinEpsilon(double a, double b, double epsilon)
		{
			double num = a - b;
			return ((-epsilon <= num) && (num <= epsilon));
		}

		/// <summary>
		/// Converts revolutions to degrees.
		/// </summary>
		/// <param name="revolution">The value to convert.</param>
		/// <returns>The converted value.</returns>
		public static float RevolutionsToDegrees(float revolution)
		{
			return revolution * 360.0f;
		}

		/// <summary>
		/// Converts revolutions to radians.
		/// </summary>
		/// <param name="revolution">The value to convert.</param>
		/// <returns>The converted value.</returns>
		public static float RevolutionsToRadians(float revolution)
		{
			return revolution * TwoPi;
		}

		/// <summary>
		/// Converts revolutions to gradians.
		/// </summary>
		/// <param name="revolution">The value to convert.</param>
		/// <returns>The converted value.</returns>
		public static float RevolutionsToGradians(float revolution)
		{
			return revolution * 400.0f;
		}

		/// <summary>
		/// Converts degrees to revolutions.
		/// </summary>
		/// <param name="degree">The value to convert.</param>
		/// <returns>The converted value.</returns>
		public static float DegreesToRevolutions(float degree)
		{
			return degree / 360.0f;
		}

		/// <summary>
		/// Converts degrees to radians.
		/// </summary>
		/// <param name="degree">The value to convert.</param>
		/// <returns>The converted value.</returns>
		public static float DegreesToRadians(float degree)
		{
			return degree * (Pi / 180.0f);
		}

		/// <summary>
		/// Converts radians to revolutions.
		/// </summary>
		/// <param name="radian">The value to convert.</param>
		/// <returns>The converted value.</returns>
		public static float RadiansToRevolutions(float radian)
		{
			return radian / TwoPi;
		}

		/// <summary>
		/// Converts radians to gradians.
		/// </summary>
		/// <param name="radian">The value to convert.</param>
		/// <returns>The converted value.</returns>
		public static float RadiansToGradians(float radian)
		{
			return radian * (200.0f / Pi);
		}

		/// <summary>
		/// Converts gradians to revolutions.
		/// </summary>
		/// <param name="gradian">The value to convert.</param>
		/// <returns>The converted value.</returns>
		public static float GradiansToRevolutions(float gradian)
		{
			return gradian / 400.0f;
		}

		/// <summary>
		/// Converts gradians to degrees.
		/// </summary>
		/// <param name="gradian">The value to convert.</param>
		/// <returns>The converted value.</returns>
		public static float GradiansToDegrees(float gradian)
		{
			return gradian * (9.0f / 10.0f);
		}

		/// <summary>
		/// Converts gradians to radians.
		/// </summary>
		/// <param name="gradian">The value to convert.</param>
		/// <returns>The converted value.</returns>
		public static float GradiansToRadians(float gradian)
		{
			return gradian * (Pi / 200.0f);
		}

		/// <summary>
		/// Converts radians to degrees.
		/// </summary>
		/// <param name="radian">The value to convert.</param>
		/// <returns>The converted value.</returns>
		public static float RadiansToDegrees(float radian)
		{
			return radian * (180.0f / Pi);
		}

		/// <summary>
		/// Clamps the specified value.
		/// </summary>
		/// <param name="value">The value.</param>
		/// <param name="min">The min.</param>
		/// <param name="max">The max.</param>
		/// <returns>The result of clamping a value between min and max</returns>
		public static float Clamp(float value, float min, float max)
		{
			return value < min ? min : value > max ? max : value;
		}

		/// <summary>
		/// Clamps the specified value.
		/// </summary>
		/// <param name="value">The value.</param>
		/// <param name="min">The min.</param>
		/// <param name="max">The max.</param>
		/// <returns>The result of clamping a value between min and max</returns>
		public static int Clamp(int value, int min, int max)
		{
			return value < min ? min : value > max ? max : value;
		}

		/// <summary>
		/// Interpolates between two values using a linear function by a given amount.
		/// </summary>
		/// <remarks>
		/// See http://www.encyclopediaofmath.org/index.php/Linear_interpolation and
		/// http://fgiesen.wordpress.com/2012/08/15/linear-interpolation-past-present-and-future/
		/// </remarks>
		/// <param name="from">Value to interpolate from.</param>
		/// <param name="to">Value to interpolate to.</param>
		/// <param name="amount">Interpolation amount.</param>
		/// <returns>The result of linear interpolation of values based on the amount.</returns>
		public static double Lerp(double from, double to, double amount)
		{
			return (1 - amount) * from + amount * to;
		}

		/// <summary>
		/// Interpolates between two values using a linear function by a given amount.
		/// </summary>
		/// <remarks>
		/// See http://www.encyclopediaofmath.org/index.php/Linear_interpolation and
		/// http://fgiesen.wordpress.com/2012/08/15/linear-interpolation-past-present-and-future/
		/// </remarks>
		/// <param name="from">Value to interpolate from.</param>
		/// <param name="to">Value to interpolate to.</param>
		/// <param name="amount">Interpolation amount.</param>
		/// <returns>The result of linear interpolation of values based on the amount.</returns>
		public static float Lerp(float from, float to, float amount)
		{
			return (1 - amount) * from + amount * to;
		}

		/// <summary>
		/// Interpolates between two values using a linear function by a given amount.
		/// </summary>
		/// <remarks>
		/// See http://www.encyclopediaofmath.org/index.php/Linear_interpolation and
		/// http://fgiesen.wordpress.com/2012/08/15/linear-interpolation-past-present-and-future/
		/// </remarks>
		/// <param name="from">Value to interpolate from.</param>
		/// <param name="to">Value to interpolate to.</param>
		/// <param name="amount">Interpolation amount.</param>
		/// <returns>The result of linear interpolation of values based on the amount.</returns>
		public static byte Lerp(byte from, byte to, float amount)
		{
			return (byte)Lerp((float)from, (float)to, amount);
		}

		/// <summary>
		/// Performs smooth (cubic Hermite) interpolation between 0 and 1.
		/// </summary>
		/// <remarks>
		/// See https://en.wikipedia.org/wiki/Smoothstep
		/// </remarks>
		/// <param name="amount">Value between 0 and 1 indicating interpolation amount.</param>
		public static float SmoothStep(float amount)
		{
			return (amount <= 0) ? 0
				 : (amount >= 1) ? 1
				 : amount * amount * (3 - (2 * amount));
		}

		public static double SmoothStep(double amount)
		{
			return (amount <= 0) ? 0
				 : (amount >= 1) ? 1
				 : amount * amount * (3 - (2 * amount));
		}

		/// <summary>
		/// Performs a smooth(er) interpolation between 0 and 1 with 1st and 2nd order derivatives of zero at endpoints.
		/// </summary>
		/// <remarks>
		/// See https://en.wikipedia.org/wiki/Smoothstep
		/// </remarks>
		/// <param name="amount">Value between 0 and 1 indicating interpolation amount.</param>
		public static float SmootherStep(float amount)
		{
			return (amount <= 0) ? 0
				 : (amount >= 1) ? 1
				 : amount * amount * amount * (amount * ((amount * 6) - 15) + 10);
		}

		/// <summary>
		/// Calculates the modulo of the specified value.
		/// </summary>
		/// <param name="value">The value.</param>
		/// <param name="modulo">The modulo.</param>
		/// <returns>The result of the modulo applied to value</returns>
		public static float Mod(float value, float modulo)
		{
			if (modulo == 0.0f)
			{
				return value;
			}

			return value % modulo;
		}

		/// <summary>
		/// Calculates the modulo 2*PI of the specified value.
		/// </summary>
		/// <param name="value">The value.</param>
		/// <returns>The result of the modulo applied to value</returns>
		public static float Mod2PI(float value)
		{
			return Mod(value, TwoPi);
		}

		/// <summary>
		/// Wraps the specified value into a range [min, max]
		/// </summary>
		/// <param name="value">The value to wrap.</param>
		/// <param name="min">The min.</param>
		/// <param name="max">The max.</param>
		/// <returns>Result of the wrapping.</returns>
		/// <exception cref="ArgumentException">Is thrown when <paramref name="min"/> is greater than <paramref name="max"/>.</exception>
		public static int Wrap(int value, int min, int max)
		{
			if (min > max)
				throw new ArgumentException(string.Format("min {0} should be less than or equal to max {1}", min, max), "min");

			// Code from http://stackoverflow.com/a/707426/1356325
			int range_size = max - min + 1;

			if (value < min)
				value += range_size * ((min - value) / range_size + 1);

			return min + (value - min) % range_size;
		}

		/// <summary>
		/// Wraps the specified value into a range [min, max[
		/// </summary>
		/// <param name="value">The value.</param>
		/// <param name="min">The min.</param>
		/// <param name="max">The max.</param>
		/// <returns>Result of the wrapping.</returns>
		/// <exception cref="ArgumentException">Is thrown when <paramref name="min"/> is greater than <paramref name="max"/>.</exception>
		public static float Wrap(float value, float min, float max)
		{
			if (NearEqual(min, max)) return min;

			double mind = min;
			double maxd = max;
			double valued = value;

			if (mind > maxd)
				throw new ArgumentException(string.Format("min {0} should be less than or equal to max {1}", min, max), "min");

			var range_size = maxd - mind;
			return (float)(mind + (valued - mind) - (range_size * Math.Floor((valued - mind) / range_size)));
		}

		/// <summary>
		/// Gauss function.
		/// http://en.wikipedia.org/wiki/Gaussian_function#Two-dimensional_Gaussian_function
		/// </summary>
		/// <param name="amplitude">Curve amplitude.</param>
		/// <param name="x">Position X.</param>
		/// <param name="y">Position Y</param>
		/// <param name="centerX">Center X.</param>
		/// <param name="centerY">Center Y.</param>
		/// <param name="sigmaX">Curve sigma X.</param>
		/// <param name="sigmaY">Curve sigma Y.</param>
		/// <returns>The result of Gaussian function.</returns>
		public static float Gauss(float amplitude, float x, float y, float centerX, float centerY, float sigmaX, float sigmaY)
		{
			return (float)Gauss((double)amplitude, x, y, centerX, centerY, sigmaX, sigmaY);
		}

		/// <summary>
		/// Gauss function.
		/// http://en.wikipedia.org/wiki/Gaussian_function#Two-dimensional_Gaussian_function
		/// </summary>
		/// <param name="amplitude">Curve amplitude.</param>
		/// <param name="x">Position X.</param>
		/// <param name="y">Position Y</param>
		/// <param name="centerX">Center X.</param>
		/// <param name="centerY">Center Y.</param>
		/// <param name="sigmaX">Curve sigma X.</param>
		/// <param name="sigmaY">Curve sigma Y.</param>
		/// <returns>The result of Gaussian function.</returns>
		public static double Gauss(double amplitude, double x, double y, double centerX, double centerY, double sigmaX, double sigmaY)
		{
			var cx = x - centerX;
			var cy = y - centerY;

			var componentX = (cx * cx) / (2 * sigmaX * sigmaX);
			var componentY = (cy * cy) / (2 * sigmaY * sigmaY);

			return amplitude * Math.Exp(-(componentX + componentY));
		}








	}
}
