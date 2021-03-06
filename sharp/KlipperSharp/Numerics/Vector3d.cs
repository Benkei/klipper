﻿// Copyright (c) 2010-2014 SharpDX - Alexandre Mutel
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
// -----------------------------------------------------------------------------
// Original code from SlimMath project. http://code.google.com/p/slimmath/
// Greetings to SlimDX Group. Original code published with the following license:
// -----------------------------------------------------------------------------
/*
* Copyright (c) 2007-2011 SlimDX Group
* 
* Permission is hereby granted, free of charge, to any person obtaining a copy
* of this software and associated documentation files (the "Software"), to deal
* in the Software without restriction, including without limitation the rights
* to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
* copies of the Software, and to permit persons to whom the Software is
* furnished to do so, subject to the following conditions:
* 
* The above copyright notice and this permission notice shall be included in
* all copies or substantial portions of the Software.
* 
* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
* IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
* FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
* AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
* LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
* OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
* THE SOFTWARE.
*/

using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace KlipperSharp
{
	/// <summary>
	/// Represents a three dimensional mathematical vector.
	/// </summary>
	[StructLayout(LayoutKind.Sequential, Pack = 8)]
	public unsafe struct Vector3d : IEquatable<Vector3d>, IFormattable
	{
		/// <summary>
		/// The size of the <see cref="Vector3d"/> type, in bytes.
		/// </summary>
		public static readonly int SizeInBytes = sizeof(Vector3d);

		/// <summary>
		/// A <see cref="Vector3d"/> with all of its components set to zero.
		/// </summary>
		public static readonly Vector3d Zero = new Vector3d();

		/// <summary>
		/// The X unit <see cref="Vector3d"/> (1, 0, 0).
		/// </summary>
		public static readonly Vector3d UnitX = new Vector3d(1.0f, 0.0f, 0.0f);

		/// <summary>
		/// The Y unit <see cref="Vector3d"/> (0, 1, 0).
		/// </summary>
		public static readonly Vector3d UnitY = new Vector3d(0.0f, 1.0f, 0.0f);

		/// <summary>
		/// The Z unit <see cref="Vector3d"/> (0, 0, 1).
		/// </summary>
		public static readonly Vector3d UnitZ = new Vector3d(0.0f, 0.0f, 1.0f);

		/// <summary>
		/// A <see cref="Vector3d"/> with all of its components set to one.
		/// </summary>
		public static readonly Vector3d One = new Vector3d(1.0f, 1.0f, 1.0f);

		/// <summary>
		/// A unit <see cref="Vector3d"/> designating up (0, 1, 0).
		/// </summary>
		public static readonly Vector3d Up = new Vector3d(0.0f, 1.0f, 0.0f);

		/// <summary>
		/// A unit <see cref="Vector3d"/> designating down (0, -1, 0).
		/// </summary>
		public static readonly Vector3d Down = new Vector3d(0.0f, -1.0f, 0.0f);

		/// <summary>
		/// A unit <see cref="Vector3d"/> designating left (-1, 0, 0).
		/// </summary>
		public static readonly Vector3d Left = new Vector3d(-1.0f, 0.0f, 0.0f);

		/// <summary>
		/// A unit <see cref="Vector3d"/> designating right (1, 0, 0).
		/// </summary>
		public static readonly Vector3d Right = new Vector3d(1.0f, 0.0f, 0.0f);

		/// <summary>
		/// A unit <see cref="Vector3d"/> designating forward in a right-handed coordinate system (0, 0, -1).
		/// </summary>
		public static readonly Vector3d ForwardRH = new Vector3d(0.0f, 0.0f, -1.0f);

		/// <summary>
		/// A unit <see cref="Vector3d"/> designating forward in a left-handed coordinate system (0, 0, 1).
		/// </summary>
		public static readonly Vector3d ForwardLH = new Vector3d(0.0f, 0.0f, 1.0f);

		/// <summary>
		/// A unit <see cref="Vector3d"/> designating backward in a right-handed coordinate system (0, 0, 1).
		/// </summary>
		public static readonly Vector3d BackwardRH = new Vector3d(0.0f, 0.0f, 1.0f);

		/// <summary>
		/// A unit <see cref="Vector3d"/> designating backward in a left-handed coordinate system (0, 0, -1).
		/// </summary>
		public static readonly Vector3d BackwardLH = new Vector3d(0.0f, 0.0f, -1.0f);

		/// <summary>
		/// The X component of the vector.
		/// </summary>
		public double X;

		/// <summary>
		/// The Y component of the vector.
		/// </summary>
		public double Y;

		/// <summary>
		/// The Z component of the vector.
		/// </summary>
		public double Z;

		/// <summary>
		/// Initializes a new instance of the <see cref="Vector3d"/> struct.
		/// </summary>
		/// <param name="value">The value that will be assigned to all components.</param>
		public Vector3d(double value)
		{
			X = value;
			Y = value;
			Z = value;
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="Vector3d"/> struct.
		/// </summary>
		/// <param name="x">Initial value for the X component of the vector.</param>
		/// <param name="y">Initial value for the Y component of the vector.</param>
		/// <param name="z">Initial value for the Z component of the vector.</param>
		public Vector3d(double x, double y, double z)
		{
			X = x;
			Y = y;
			Z = z;
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="Vector3d"/> struct.
		/// </summary>
		/// <param name="value">A vector containing the values with which to initialize the X and Y components.</param>
		/// <param name="z">Initial value for the Z component of the vector.</param>
		public Vector3d(Vector2d value, double z)
		{
			X = value.X;
			Y = value.Y;
			Z = z;
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="Vector3d"/> struct.
		/// </summary>
		/// <param name="values">The values to assign to the X, Y, and Z components of the vector. This must be an array with three elements.</param>
		/// <exception cref="ArgumentNullException">Thrown when <paramref name="values"/> is <c>null</c>.</exception>
		/// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="values"/> contains more or less than three elements.</exception>
		public Vector3d(double[] values)
		{
			if (values == null)
				throw new ArgumentNullException("values");
			if (values.Length != 3)
				throw new ArgumentOutOfRangeException("values", "There must be three and only three input values for Vector3.");

			X = values[0];
			Y = values[1];
			Z = values[2];
		}

		/// <summary>
		/// Gets a value indicting whether this instance is normalized.
		/// </summary>
		public bool IsNormalized
		{
			get { return MathUtil.IsOne((X * X) + (Y * Y) + (Z * Z)); }
		}

		/// <summary>
		/// Gets a value indicting whether this vector is zero
		/// </summary>
		public bool IsZero
		{
			get { return X == 0 && Y == 0 && Z == 0; }
		}

		/// <summary>
		/// Gets or sets the component at the specified index.
		/// </summary>
		/// <value>The value of the X, Y, or Z component, depending on the index.</value>
		/// <param name="index">The index of the component to access. Use 0 for the X component, 1 for the Y component, and 2 for the Z component.</param>
		/// <returns>The value of the component at the specified index.</returns>
		/// <exception cref="System.ArgumentOutOfRangeException">Thrown when the <paramref name="index"/> is out of the range [0, 2].</exception>
		public double this[int index]
		{
			get
			{
				switch (index)
				{
					case 0: return X;
					case 1: return Y;
					case 2: return Z;
				}

				throw new ArgumentOutOfRangeException("index", "Indices for Vector3 run from 0 to 2, inclusive.");
			}

			set
			{
				switch (index)
				{
					case 0: X = value; break;
					case 1: Y = value; break;
					case 2: Z = value; break;
					default: throw new ArgumentOutOfRangeException("index", "Indices for Vector3 run from 0 to 2, inclusive.");
				}
			}
		}

		/// <summary>
		/// Calculates the length of the vector.
		/// </summary>
		/// <returns>The length of the vector.</returns>
		/// <remarks>
		/// <see cref="Vector3d.LengthSquared"/> may be preferred when only the relative length is needed
		/// and speed is of the essence.
		/// </remarks>
		public double Length()
		{
			return Math.Sqrt((X * X) + (Y * Y) + (Z * Z));
		}

		/// <summary>
		/// Calculates the squared length of the vector.
		/// </summary>
		/// <returns>The squared length of the vector.</returns>
		/// <remarks>
		/// This method may be preferred to <see cref="Vector3d.Length"/> when only a relative length is needed
		/// and speed is of the essence.
		/// </remarks>
		public double LengthSquared()
		{
			return (X * X) + (Y * Y) + (Z * Z);
		}

		/// <summary>
		/// Converts the vector into a unit vector.
		/// </summary>
		public void Normalize()
		{
			double length = Length();
			if (!MathUtil.IsZero(length))
			{
				double inv = 1.0f / length;
				X *= inv;
				Y *= inv;
				Z *= inv;
			}
		}

		/// <summary>
		/// Creates an array containing the elements of the vector.
		/// </summary>
		/// <returns>A three-element array containing the components of the vector.</returns>
		public double[] ToArray()
		{
			return new double[] { X, Y, Z };
		}

		/// <summary>
		/// Adds two vectors.
		/// </summary>
		/// <param name="left">The first vector to add.</param>
		/// <param name="right">The second vector to add.</param>
		/// <param name="result">When the method completes, contains the sum of the two vectors.</param>
		public static void Add(in Vector3d left, in Vector3d right, out Vector3d result)
		{
			result = new Vector3d(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
		}

		/// <summary>
		/// Adds two vectors.
		/// </summary>
		/// <param name="left">The first vector to add.</param>
		/// <param name="right">The second vector to add.</param>
		/// <returns>The sum of the two vectors.</returns>
		public static Vector3d Add(Vector3d left, Vector3d right)
		{
			return new Vector3d(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
		}

		/// <summary>
		/// Perform a component-wise addition
		/// </summary>
		/// <param name="left">The input vector</param>
		/// <param name="right">The scalar value to be added to elements</param>
		/// <param name="result">The vector with added scalar for each element.</param>
		public static void Add(in Vector3d left, in double right, out Vector3d result)
		{
			result = new Vector3d(left.X + right, left.Y + right, left.Z + right);
		}

		/// <summary>
		/// Perform a component-wise addition
		/// </summary>
		/// <param name="left">The input vector</param>
		/// <param name="right">The scalar value to be added to elements</param>
		/// <returns>The vector with added scalar for each element.</returns>
		public static Vector3d Add(Vector3d left, double right)
		{
			return new Vector3d(left.X + right, left.Y + right, left.Z + right);
		}

		/// <summary>
		/// Subtracts two vectors.
		/// </summary>
		/// <param name="left">The first vector to subtract.</param>
		/// <param name="right">The second vector to subtract.</param>
		/// <param name="result">When the method completes, contains the difference of the two vectors.</param>
		public static void Subtract(in Vector3d left, in Vector3d right, out Vector3d result)
		{
			result = new Vector3d(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
		}

		/// <summary>
		/// Subtracts two vectors.
		/// </summary>
		/// <param name="left">The first vector to subtract.</param>
		/// <param name="right">The second vector to subtract.</param>
		/// <returns>The difference of the two vectors.</returns>
		public static Vector3d Subtract(Vector3d left, Vector3d right)
		{
			return new Vector3d(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
		}

		/// <summary>
		/// Perform a component-wise subtraction
		/// </summary>
		/// <param name="left">The input vector</param>
		/// <param name="right">The scalar value to be subtraced from elements</param>
		/// <param name="result">The vector with subtracted scalar for each element.</param>
		public static void Subtract(in Vector3d left, in double right, out Vector3d result)
		{
			result = new Vector3d(left.X - right, left.Y - right, left.Z - right);
		}

		/// <summary>
		/// Perform a component-wise subtraction
		/// </summary>
		/// <param name="left">The input vector</param>
		/// <param name="right">The scalar value to be subtraced from elements</param>
		/// <returns>The vector with subtracted scalar for each element.</returns>
		public static Vector3d Subtract(Vector3d left, double right)
		{
			return new Vector3d(left.X - right, left.Y - right, left.Z - right);
		}

		/// <summary>
		/// Perform a component-wise subtraction
		/// </summary>
		/// <param name="left">The scalar value to be subtraced from elements</param>
		/// <param name="right">The input vector.</param>
		/// <param name="result">The vector with subtracted scalar for each element.</param>
		public static void Subtract(in double left, in Vector3d right, out Vector3d result)
		{
			result = new Vector3d(left - right.X, left - right.Y, left - right.Z);
		}

		/// <summary>
		/// Perform a component-wise subtraction
		/// </summary>
		/// <param name="left">The scalar value to be subtraced from elements</param>
		/// <param name="right">The input vector.</param>
		/// <returns>The vector with subtracted scalar for each element.</returns>
		public static Vector3d Subtract(double left, Vector3d right)
		{
			return new Vector3d(left - right.X, left - right.Y, left - right.Z);
		}

		/// <summary>
		/// Scales a vector by the given value.
		/// </summary>
		/// <param name="value">The vector to scale.</param>
		/// <param name="scale">The amount by which to scale the vector.</param>
		/// <param name="result">When the method completes, contains the scaled vector.</param>
		public static void Multiply(in Vector3d value, double scale, out Vector3d result)
		{
			result = new Vector3d(value.X * scale, value.Y * scale, value.Z * scale);
		}

		/// <summary>
		/// Scales a vector by the given value.
		/// </summary>
		/// <param name="value">The vector to scale.</param>
		/// <param name="scale">The amount by which to scale the vector.</param>
		/// <returns>The scaled vector.</returns>
		public static Vector3d Multiply(Vector3d value, double scale)
		{
			return new Vector3d(value.X * scale, value.Y * scale, value.Z * scale);
		}

		/// <summary>
		/// Multiply a vector with another by performing component-wise multiplication.
		/// </summary>
		/// <param name="left">The first vector to multiply.</param>
		/// <param name="right">The second vector to multiply.</param>
		/// <param name="result">When the method completes, contains the multiplied vector.</param>
		public static void Multiply(in Vector3d left, in Vector3d right, out Vector3d result)
		{
			result = new Vector3d(left.X * right.X, left.Y * right.Y, left.Z * right.Z);
		}

		/// <summary>
		/// Multiply a vector with another by performing component-wise multiplication.
		/// </summary>
		/// <param name="left">The first vector to Multiply.</param>
		/// <param name="right">The second vector to multiply.</param>
		/// <returns>The multiplied vector.</returns>
		public static Vector3d Multiply(Vector3d left, Vector3d right)
		{
			return new Vector3d(left.X * right.X, left.Y * right.Y, left.Z * right.Z);
		}

		/// <summary>
		/// Scales a vector by the given value.
		/// </summary>
		/// <param name="value">The vector to scale.</param>
		/// <param name="scale">The amount by which to scale the vector.</param>
		/// <param name="result">When the method completes, contains the scaled vector.</param>
		public static void Divide(in Vector3d value, double scale, out Vector3d result)
		{
			result = new Vector3d(value.X / scale, value.Y / scale, value.Z / scale);
		}

		/// <summary>
		/// Scales a vector by the given value.
		/// </summary>
		/// <param name="value">The vector to scale.</param>
		/// <param name="scale">The amount by which to scale the vector.</param>
		/// <returns>The scaled vector.</returns>
		public static Vector3d Divide(Vector3d value, double scale)
		{
			return new Vector3d(value.X / scale, value.Y / scale, value.Z / scale);
		}

		/// <summary>
		/// Scales a vector by the given value.
		/// </summary>
		/// <param name="scale">The amount by which to scale the vector.</param>
		/// <param name="value">The vector to scale.</param>
		/// <param name="result">When the method completes, contains the scaled vector.</param>
		public static void Divide(double scale, in Vector3d value, out Vector3d result)
		{
			result = new Vector3d(scale / value.X, scale / value.Y, scale / value.Z);
		}

		/// <summary>
		/// Scales a vector by the given value.
		/// </summary>
		/// <param name="value">The vector to scale.</param>
		/// <param name="scale">The amount by which to scale the vector.</param>
		/// <returns>The scaled vector.</returns>
		public static Vector3d Divide(double scale, Vector3d value)
		{
			return new Vector3d(scale / value.X, scale / value.Y, scale / value.Z);
		}

		/// <summary>
		/// Reverses the direction of a given vector.
		/// </summary>
		/// <param name="value">The vector to negate.</param>
		/// <param name="result">When the method completes, contains a vector facing in the opposite direction.</param>
		public static void Negate(in Vector3d value, out Vector3d result)
		{
			result = new Vector3d(-value.X, -value.Y, -value.Z);
		}

		/// <summary>
		/// Reverses the direction of a given vector.
		/// </summary>
		/// <param name="value">The vector to negate.</param>
		/// <returns>A vector facing in the opposite direction.</returns>
		public static Vector3d Negate(Vector3d value)
		{
			return new Vector3d(-value.X, -value.Y, -value.Z);
		}

		/// <summary>
		/// Returns per component absolute value of a vector
		/// </summary>
		/// <param name="value">Input vector</param>
		/// <param name="result">When the method completes, contains a vector with each component being the absolute value of the input component</param>
		public static void Abs(in Vector3d value, out Vector3d result)
		{
			result = new Vector3d(value.X > 0.0f ? value.X : -value.X,
				 value.Y > 0.0f ? value.Y : -value.Y,
				 value.Z > 0.0f ? value.Z : -value.Z);
		}

		/// <summary>
		/// Returns per component absolute value of a vector
		/// </summary>
		/// <param name="value">Input vector</param>
		/// <returns>A vector with each component being the absolute value of the input component</returns>
		public static Vector3d Abs(Vector3d value)
		{
			return new Vector3d(
				 value.X > 0.0f ? value.X : -value.X,
				 value.Y > 0.0f ? value.Y : -value.Y,
				 value.Z > 0.0f ? value.Z : -value.Z);
		}

		/// <summary>
		/// Returns a <see cref="Vector3d"/> containing the 3D Cartesian coordinates of a point specified in Barycentric coordinates relative to a 3D triangle.
		/// </summary>
		/// <param name="value1">A <see cref="Vector3d"/> containing the 3D Cartesian coordinates of vertex 1 of the triangle.</param>
		/// <param name="value2">A <see cref="Vector3d"/> containing the 3D Cartesian coordinates of vertex 2 of the triangle.</param>
		/// <param name="value3">A <see cref="Vector3d"/> containing the 3D Cartesian coordinates of vertex 3 of the triangle.</param>
		/// <param name="amount1">Barycentric coordinate b2, which expresses the weighting factor toward vertex 2 (specified in <paramref name="value2"/>).</param>
		/// <param name="amount2">Barycentric coordinate b3, which expresses the weighting factor toward vertex 3 (specified in <paramref name="value3"/>).</param>
		/// <param name="result">When the method completes, contains the 3D Cartesian coordinates of the specified point.</param>
		public static void Barycentric(in Vector3d value1, in Vector3d value2, in Vector3d value3, double amount1, double amount2, out Vector3d result)
		{
			result = new Vector3d((value1.X + (amount1 * (value2.X - value1.X))) + (amount2 * (value3.X - value1.X)),
				 (value1.Y + (amount1 * (value2.Y - value1.Y))) + (amount2 * (value3.Y - value1.Y)),
				 (value1.Z + (amount1 * (value2.Z - value1.Z))) + (amount2 * (value3.Z - value1.Z)));
		}

		/// <summary>
		/// Returns a <see cref="Vector3d"/> containing the 3D Cartesian coordinates of a point specified in Barycentric coordinates relative to a 3D triangle.
		/// </summary>
		/// <param name="value1">A <see cref="Vector3d"/> containing the 3D Cartesian coordinates of vertex 1 of the triangle.</param>
		/// <param name="value2">A <see cref="Vector3d"/> containing the 3D Cartesian coordinates of vertex 2 of the triangle.</param>
		/// <param name="value3">A <see cref="Vector3d"/> containing the 3D Cartesian coordinates of vertex 3 of the triangle.</param>
		/// <param name="amount1">Barycentric coordinate b2, which expresses the weighting factor toward vertex 2 (specified in <paramref name="value2"/>).</param>
		/// <param name="amount2">Barycentric coordinate b3, which expresses the weighting factor toward vertex 3 (specified in <paramref name="value3"/>).</param>
		/// <returns>A new <see cref="Vector3d"/> containing the 3D Cartesian coordinates of the specified point.</returns>
		public static Vector3d Barycentric(Vector3d value1, Vector3d value2, Vector3d value3, double amount1, double amount2)
		{
			Vector3d result;
			Barycentric(in value1, in value2, in value3, amount1, amount2, out result);
			return result;
		}

		/// <summary>
		/// Restricts a value to be within a specified range.
		/// </summary>
		/// <param name="value">The value to clamp.</param>
		/// <param name="min">The minimum value.</param>
		/// <param name="max">The maximum value.</param>
		/// <param name="result">When the method completes, contains the clamped value.</param>
		public static void Clamp(in Vector3d value, in Vector3d min, in Vector3d max, out Vector3d result)
		{
			double x = value.X;
			x = (x > max.X) ? max.X : x;
			x = (x < min.X) ? min.X : x;

			double y = value.Y;
			y = (y > max.Y) ? max.Y : y;
			y = (y < min.Y) ? min.Y : y;

			double z = value.Z;
			z = (z > max.Z) ? max.Z : z;
			z = (z < min.Z) ? min.Z : z;

			result = new Vector3d(x, y, z);
		}

		/// <summary>
		/// Restricts a value to be within a specified range.
		/// </summary>
		/// <param name="value">The value to clamp.</param>
		/// <param name="min">The minimum value.</param>
		/// <param name="max">The maximum value.</param>
		/// <returns>The clamped value.</returns>
		public static Vector3d Clamp(Vector3d value, Vector3d min, Vector3d max)
		{
			Vector3d result;
			Clamp(in value, in min, in max, out result);
			return result;
		}

		/// <summary>
		/// Calculates the cross product of two vectors.
		/// </summary>
		/// <param name="left">First source vector.</param>
		/// <param name="right">Second source vector.</param>
		/// <param name="result">When the method completes, contains he cross product of the two vectors.</param>
		public static void Cross(in Vector3d left, in Vector3d right, out Vector3d result)
		{
			result = new Vector3d(
				 (left.Y * right.Z) - (left.Z * right.Y),
				 (left.Z * right.X) - (left.X * right.Z),
				 (left.X * right.Y) - (left.Y * right.X));
		}

		/// <summary>
		/// Calculates the cross product of two vectors.
		/// </summary>
		/// <param name="left">First source vector.</param>
		/// <param name="right">Second source vector.</param>
		/// <returns>The cross product of the two vectors.</returns>
		public static Vector3d Cross(Vector3d left, Vector3d right)
		{
			Vector3d result;
			Cross(in left, in right, out result);
			return result;
		}

		/// <summary>
		/// Calculates the distance between two vectors.
		/// </summary>
		/// <param name="value1">The first vector.</param>
		/// <param name="value2">The second vector.</param>
		/// <param name="result">When the method completes, contains the distance between the two vectors.</param>
		/// <remarks>
		/// <see cref="Vector3d.DistanceSquared(in Vector3d, in Vector3d, out double)"/> may be preferred when only the relative distance is needed
		/// and speed is of the essence.
		/// </remarks>
		public static void Distance(in Vector3d value1, in Vector3d value2, out double result)
		{
			double x = value1.X - value2.X;
			double y = value1.Y - value2.Y;
			double z = value1.Z - value2.Z;

			result = (double)Math.Sqrt((x * x) + (y * y) + (z * z));
		}

		/// <summary>
		/// Calculates the distance between two vectors.
		/// </summary>
		/// <param name="value1">The first vector.</param>
		/// <param name="value2">The second vector.</param>
		/// <returns>The distance between the two vectors.</returns>
		/// <remarks>
		/// <see cref="Vector3d.DistanceSquared(Vector3d, Vector3d)"/> may be preferred when only the relative distance is needed
		/// and speed is of the essence.
		/// </remarks>
		public static double Distance(Vector3d value1, Vector3d value2)
		{
			double x = value1.X - value2.X;
			double y = value1.Y - value2.Y;
			double z = value1.Z - value2.Z;

			return (double)Math.Sqrt((x * x) + (y * y) + (z * z));
		}

		/// <summary>
		/// Calculates the squared distance between two vectors.
		/// </summary>
		/// <param name="value1">The first vector.</param>
		/// <param name="value2">The second vector.</param>
		/// <param name="result">When the method completes, contains the squared distance between the two vectors.</param>
		/// <remarks>Distance squared is the value before taking the square root. 
		/// Distance squared can often be used in place of distance if relative comparisons are being made. 
		/// For example, consider three points A, B, and C. To determine whether B or C is further from A, 
		/// compare the distance between A and B to the distance between A and C. Calculating the two distances 
		/// involves two square roots, which are computationally expensive. However, using distance squared 
		/// provides the same information and avoids calculating two square roots.
		/// </remarks>
		public static void DistanceSquared(in Vector3d value1, in Vector3d value2, out double result)
		{
			double x = value1.X - value2.X;
			double y = value1.Y - value2.Y;
			double z = value1.Z - value2.Z;

			result = (x * x) + (y * y) + (z * z);
		}

		/// <summary>
		/// Calculates the squared distance between two vectors.
		/// </summary>
		/// <param name="value1">The first vector.</param>
		/// <param name="value2">The second vector.</param>
		/// <returns>The squared distance between the two vectors.</returns>
		/// <remarks>Distance squared is the value before taking the square root. 
		/// Distance squared can often be used in place of distance if relative comparisons are being made. 
		/// For example, consider three points A, B, and C. To determine whether B or C is further from A, 
		/// compare the distance between A and B to the distance between A and C. Calculating the two distances 
		/// involves two square roots, which are computationally expensive. However, using distance squared 
		/// provides the same information and avoids calculating two square roots.
		/// </remarks>
		public static double DistanceSquared(Vector3d value1, Vector3d value2)
		{
			double x = value1.X - value2.X;
			double y = value1.Y - value2.Y;
			double z = value1.Z - value2.Z;

			return (x * x) + (y * y) + (z * z);
		}

		/// <summary>
		/// Tests whether one 3D vector is near another 3D vector.
		/// </summary>
		/// <param name="left">The left vector.</param>
		/// <param name="right">The right vector.</param>
		/// <param name="epsilon">The epsilon.</param>
		/// <returns><c>true</c> if left and right are near another 3D, <c>false</c> otherwise</returns>
		public static bool NearEqual(Vector3d left, Vector3d right, Vector3d epsilon)
		{
			return NearEqual(in left, in right, in epsilon);
		}

		/// <summary>
		/// Tests whether one 3D vector is near another 3D vector.
		/// </summary>
		/// <param name="left">The left vector.</param>
		/// <param name="right">The right vector.</param>
		/// <param name="epsilon">The epsilon.</param>
		/// <returns><c>true</c> if left and right are near another 3D, <c>false</c> otherwise</returns>
		public static bool NearEqual(in Vector3d left, in Vector3d right, in Vector3d epsilon)
		{
			return MathUtil.WithinEpsilon(left.X, right.X, epsilon.X) &&
					  MathUtil.WithinEpsilon(left.Y, right.Y, epsilon.Y) &&
					  MathUtil.WithinEpsilon(left.Z, right.Z, epsilon.Z);
		}

		/// <summary>
		/// Calculates the dot product of two vectors.
		/// </summary>
		/// <param name="left">First source vector.</param>
		/// <param name="right">Second source vector.</param>
		/// <param name="result">When the method completes, contains the dot product of the two vectors.</param>
		public static void Dot(in Vector3d left, in Vector3d right, out double result)
		{
			result = (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
		}

		/// <summary>
		/// Calculates the dot product of two vectors.
		/// </summary>
		/// <param name="left">First source vector.</param>
		/// <param name="right">Second source vector.</param>
		/// <returns>The dot product of the two vectors.</returns>
		public static double Dot(Vector3d left, Vector3d right)
		{
			return (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
		}

		/// <summary>
		/// Converts the vector into a unit vector.
		/// </summary>
		/// <param name="value">The vector to normalize.</param>
		/// <param name="result">When the method completes, contains the normalized vector.</param>
		public static void Normalize(in Vector3d value, out Vector3d result)
		{
			result = value;
			result.Normalize();
		}

		/// <summary>
		/// Converts the vector into a unit vector.
		/// </summary>
		/// <param name="value">The vector to normalize.</param>
		/// <returns>The normalized vector.</returns>
		public static Vector3d Normalize(Vector3d value)
		{
			value.Normalize();
			return value;
		}

		/// <summary>
		/// Performs a linear interpolation between two vectors.
		/// </summary>
		/// <param name="start">Start vector.</param>
		/// <param name="end">End vector.</param>
		/// <param name="amount">Value between 0 and 1 indicating the weight of <paramref name="end"/>.</param>
		/// <param name="result">When the method completes, contains the linear interpolation of the two vectors.</param>
		/// <remarks>
		/// Passing <paramref name="amount"/> a value of 0 will cause <paramref name="start"/> to be returned; a value of 1 will cause <paramref name="end"/> to be returned. 
		/// </remarks>
		public static void Lerp(in Vector3d start, in Vector3d end, double amount, out Vector3d result)
		{
			result.X = MathUtil.Lerp(start.X, end.X, amount);
			result.Y = MathUtil.Lerp(start.Y, end.Y, amount);
			result.Z = MathUtil.Lerp(start.Z, end.Z, amount);
		}

		/// <summary>
		/// Performs a linear interpolation between two vectors.
		/// </summary>
		/// <param name="start">Start vector.</param>
		/// <param name="end">End vector.</param>
		/// <param name="amount">Value between 0 and 1 indicating the weight of <paramref name="end"/>.</param>
		/// <returns>The linear interpolation of the two vectors.</returns>
		/// <remarks>
		/// Passing <paramref name="amount"/> a value of 0 will cause <paramref name="start"/> to be returned; a value of 1 will cause <paramref name="end"/> to be returned. 
		/// </remarks>
		public static Vector3d Lerp(Vector3d start, Vector3d end, double amount)
		{
			Vector3d result;
			Lerp(in start, in end, amount, out result);
			return result;
		}

		/// <summary>
		/// Performs a cubic interpolation between two vectors.
		/// </summary>
		/// <param name="start">Start vector.</param>
		/// <param name="end">End vector.</param>
		/// <param name="amount">Value between 0 and 1 indicating the weight of <paramref name="end"/>.</param>
		/// <param name="result">When the method completes, contains the cubic interpolation of the two vectors.</param>
		public static void SmoothStep(in Vector3d start, in Vector3d end, double amount, out Vector3d result)
		{
			amount = MathUtil.SmoothStep(amount);
			Lerp(in start, in end, amount, out result);
		}

		/// <summary>
		/// Performs a cubic interpolation between two vectors.
		/// </summary>
		/// <param name="start">Start vector.</param>
		/// <param name="end">End vector.</param>
		/// <param name="amount">Value between 0 and 1 indicating the weight of <paramref name="end"/>.</param>
		/// <returns>The cubic interpolation of the two vectors.</returns>
		public static Vector3d SmoothStep(Vector3d start, Vector3d end, double amount)
		{
			Vector3d result;
			SmoothStep(in start, in end, amount, out result);
			return result;
		}

		/// <summary>
		/// Performs a Hermite spline interpolation.
		/// </summary>
		/// <param name="value1">First source position vector.</param>
		/// <param name="tangent1">First source tangent vector.</param>
		/// <param name="value2">Second source position vector.</param>
		/// <param name="tangent2">Second source tangent vector.</param>
		/// <param name="amount">Weighting factor.</param>
		/// <param name="result">When the method completes, contains the result of the Hermite spline interpolation.</param>
		public static void Hermite(in Vector3d value1, in Vector3d tangent1, in Vector3d value2, in Vector3d tangent2, double amount, out Vector3d result)
		{
			double squared = amount * amount;
			double cubed = amount * squared;
			double part1 = ((2.0f * cubed) - (3.0f * squared)) + 1.0f;
			double part2 = (-2.0f * cubed) + (3.0f * squared);
			double part3 = (cubed - (2.0f * squared)) + amount;
			double part4 = cubed - squared;

			result.X = (((value1.X * part1) + (value2.X * part2)) + (tangent1.X * part3)) + (tangent2.X * part4);
			result.Y = (((value1.Y * part1) + (value2.Y * part2)) + (tangent1.Y * part3)) + (tangent2.Y * part4);
			result.Z = (((value1.Z * part1) + (value2.Z * part2)) + (tangent1.Z * part3)) + (tangent2.Z * part4);
		}

		/// <summary>
		/// Performs a Hermite spline interpolation.
		/// </summary>
		/// <param name="value1">First source position vector.</param>
		/// <param name="tangent1">First source tangent vector.</param>
		/// <param name="value2">Second source position vector.</param>
		/// <param name="tangent2">Second source tangent vector.</param>
		/// <param name="amount">Weighting factor.</param>
		/// <returns>The result of the Hermite spline interpolation.</returns>
		public static Vector3d Hermite(Vector3d value1, Vector3d tangent1, Vector3d value2, Vector3d tangent2, double amount)
		{
			Vector3d result;
			Hermite(in value1, in tangent1, in value2, in tangent2, amount, out result);
			return result;
		}

		/// <summary>
		/// Performs a Catmull-Rom interpolation using the specified positions.
		/// </summary>
		/// <param name="value1">The first position in the interpolation.</param>
		/// <param name="value2">The second position in the interpolation.</param>
		/// <param name="value3">The third position in the interpolation.</param>
		/// <param name="value4">The fourth position in the interpolation.</param>
		/// <param name="amount">Weighting factor.</param>
		/// <param name="result">When the method completes, contains the result of the Catmull-Rom interpolation.</param>
		public static void CatmullRom(in Vector3d value1, in Vector3d value2, in Vector3d value3, in Vector3d value4, double amount, out Vector3d result)
		{
			double squared = amount * amount;
			double cubed = amount * squared;

			result.X = 0.5f * ((((2.0f * value2.X) + ((-value1.X + value3.X) * amount)) +
			(((((2.0f * value1.X) - (5.0f * value2.X)) + (4.0f * value3.X)) - value4.X) * squared)) +
			((((-value1.X + (3.0f * value2.X)) - (3.0f * value3.X)) + value4.X) * cubed));

			result.Y = 0.5f * ((((2.0f * value2.Y) + ((-value1.Y + value3.Y) * amount)) +
				 (((((2.0f * value1.Y) - (5.0f * value2.Y)) + (4.0f * value3.Y)) - value4.Y) * squared)) +
				 ((((-value1.Y + (3.0f * value2.Y)) - (3.0f * value3.Y)) + value4.Y) * cubed));

			result.Z = 0.5f * ((((2.0f * value2.Z) + ((-value1.Z + value3.Z) * amount)) +
				 (((((2.0f * value1.Z) - (5.0f * value2.Z)) + (4.0f * value3.Z)) - value4.Z) * squared)) +
				 ((((-value1.Z + (3.0f * value2.Z)) - (3.0f * value3.Z)) + value4.Z) * cubed));
		}

		/// <summary>
		/// Performs a Catmull-Rom interpolation using the specified positions.
		/// </summary>
		/// <param name="value1">The first position in the interpolation.</param>
		/// <param name="value2">The second position in the interpolation.</param>
		/// <param name="value3">The third position in the interpolation.</param>
		/// <param name="value4">The fourth position in the interpolation.</param>
		/// <param name="amount">Weighting factor.</param>
		/// <returns>A vector that is the result of the Catmull-Rom interpolation.</returns>
		public static Vector3d CatmullRom(Vector3d value1, Vector3d value2, Vector3d value3, Vector3d value4, double amount)
		{
			Vector3d result;
			CatmullRom(in value1, in value2, in value3, in value4, amount, out result);
			return result;
		}

		/// <summary>
		/// Returns a vector containing the largest components of the specified vectors.
		/// </summary>
		/// <param name="left">The first source vector.</param>
		/// <param name="right">The second source vector.</param>
		/// <param name="result">When the method completes, contains an new vector composed of the largest components of the source vectors.</param>
		public static void Max(in Vector3d left, in Vector3d right, out Vector3d result)
		{
			result.X = (left.X > right.X) ? left.X : right.X;
			result.Y = (left.Y > right.Y) ? left.Y : right.Y;
			result.Z = (left.Z > right.Z) ? left.Z : right.Z;
		}

		/// <summary>
		/// Returns a vector containing the largest components of the specified vectors.
		/// </summary>
		/// <param name="left">The first source vector.</param>
		/// <param name="right">The second source vector.</param>
		/// <returns>A vector containing the largest components of the source vectors.</returns>
		public static Vector3d Max(Vector3d left, Vector3d right)
		{
			Vector3d result;
			Max(in left, in right, out result);
			return result;
		}

		/// <summary>
		/// Returns a vector containing the smallest components of the specified vectors.
		/// </summary>
		/// <param name="left">The first source vector.</param>
		/// <param name="right">The second source vector.</param>
		/// <param name="result">When the method completes, contains an new vector composed of the smallest components of the source vectors.</param>
		public static void Min(in Vector3d left, in Vector3d right, out Vector3d result)
		{
			result.X = (left.X < right.X) ? left.X : right.X;
			result.Y = (left.Y < right.Y) ? left.Y : right.Y;
			result.Z = (left.Z < right.Z) ? left.Z : right.Z;
		}

		/// <summary>
		/// Returns a vector containing the smallest components of the specified vectors.
		/// </summary>
		/// <param name="left">The first source vector.</param>
		/// <param name="right">The second source vector.</param>
		/// <returns>A vector containing the smallest components of the source vectors.</returns>
		public static Vector3d Min(Vector3d left, Vector3d right)
		{
			Vector3d result;
			Min(in left, in right, out result);
			return result;
		}
		/*
		/// <summary>
		/// Projects a 3D vector from object space into screen space. 
		/// </summary>
		/// <param name="vector">The vector to project.</param>
		/// <param name="x">The X position of the viewport.</param>
		/// <param name="y">The Y position of the viewport.</param>
		/// <param name="width">The width of the viewport.</param>
		/// <param name="height">The height of the viewport.</param>
		/// <param name="minZ">The minimum depth of the viewport.</param>
		/// <param name="maxZ">The maximum depth of the viewport.</param>
		/// <param name="worldViewProjection">The combined world-view-projection matrix.</param>
		/// <param name="result">When the method completes, contains the vector in screen space.</param>
		public static void Project(in Vector3d vector, double x, double y, double width, double height, double minZ, double maxZ, in Matrix worldViewProjection, out Vector3d result)
		{
			Vector3d v = new Vector3d();
			TransformCoordinate(in vector, in worldViewProjection, out v);

			result = new Vector3d(((1.0f + v.X) * 0.5f * width) + x, ((1.0f - v.Y) * 0.5f * height) + y, (v.Z * (maxZ - minZ)) + minZ);
		}

		/// <summary>
		/// Projects a 3D vector from object space into screen space. 
		/// </summary>
		/// <param name="vector">The vector to project.</param>
		/// <param name="x">The X position of the viewport.</param>
		/// <param name="y">The Y position of the viewport.</param>
		/// <param name="width">The width of the viewport.</param>
		/// <param name="height">The height of the viewport.</param>
		/// <param name="minZ">The minimum depth of the viewport.</param>
		/// <param name="maxZ">The maximum depth of the viewport.</param>
		/// <param name="worldViewProjection">The combined world-view-projection matrix.</param>
		/// <returns>The vector in screen space.</returns>
		public static Vector3d Project(Vector3d vector, double x, double y, double width, double height, double minZ, double maxZ, Matrix worldViewProjection)
		{
			Vector3d result;
			Project(in vector, x, y, width, height, minZ, maxZ, in worldViewProjection, out result);
			return result;
		}

		/// <summary>
		/// Projects a 3D vector from screen space into object space. 
		/// </summary>
		/// <param name="vector">The vector to project.</param>
		/// <param name="x">The X position of the viewport.</param>
		/// <param name="y">The Y position of the viewport.</param>
		/// <param name="width">The width of the viewport.</param>
		/// <param name="height">The height of the viewport.</param>
		/// <param name="minZ">The minimum depth of the viewport.</param>
		/// <param name="maxZ">The maximum depth of the viewport.</param>
		/// <param name="worldViewProjection">The combined world-view-projection matrix.</param>
		/// <param name="result">When the method completes, contains the vector in object space.</param>
		public static void Unproject(in Vector3d vector, double x, double y, double width, double height, double minZ, double maxZ, in Matrix worldViewProjection, out Vector3d result)
		{
			Vector3d v = new Vector3d();
			Matrix matrix = new Matrix();
			Matrix.Invert(in worldViewProjection, out matrix);

			v.X = (((vector.X - x) / width) * 2.0f) - 1.0f;
			v.Y = -((((vector.Y - y) / height) * 2.0f) - 1.0f);
			v.Z = (vector.Z - minZ) / (maxZ - minZ);

			TransformCoordinate(in v, in matrix, out result);
		}
		
		/// <summary>
		/// Projects a 3D vector from screen space into object space. 
		/// </summary>
		/// <param name="vector">The vector to project.</param>
		/// <param name="x">The X position of the viewport.</param>
		/// <param name="y">The Y position of the viewport.</param>
		/// <param name="width">The width of the viewport.</param>
		/// <param name="height">The height of the viewport.</param>
		/// <param name="minZ">The minimum depth of the viewport.</param>
		/// <param name="maxZ">The maximum depth of the viewport.</param>
		/// <param name="worldViewProjection">The combined world-view-projection matrix.</param>
		/// <returns>The vector in object space.</returns>
		public static Vector3d Unproject(Vector3d vector, double x, double y, double width, double height, double minZ, double maxZ, Matrix worldViewProjection)
		{
			Vector3d result;
			Unproject(in vector, x, y, width, height, minZ, maxZ, in worldViewProjection, out result);
			return result;
		}
		*/
		/// <summary>
		/// Returns the reflection of a vector off a surface that has the specified normal. 
		/// </summary>
		/// <param name="vector">The source vector.</param>
		/// <param name="normal">Normal of the surface.</param>
		/// <param name="result">When the method completes, contains the reflected vector.</param>
		/// <remarks>Reflect only gives the direction of a reflection off a surface, it does not determine 
		/// whether the original vector was close enough to the surface to hit it.</remarks>
		public static void Reflect(in Vector3d vector, in Vector3d normal, out Vector3d result)
		{
			double dot = (vector.X * normal.X) + (vector.Y * normal.Y) + (vector.Z * normal.Z);

			result.X = vector.X - ((2.0f * dot) * normal.X);
			result.Y = vector.Y - ((2.0f * dot) * normal.Y);
			result.Z = vector.Z - ((2.0f * dot) * normal.Z);
		}

		/// <summary>
		/// Returns the reflection of a vector off a surface that has the specified normal. 
		/// </summary>
		/// <param name="vector">The source vector.</param>
		/// <param name="normal">Normal of the surface.</param>
		/// <returns>The reflected vector.</returns>
		/// <remarks>Reflect only gives the direction of a reflection off a surface, it does not determine 
		/// whether the original vector was close enough to the surface to hit it.</remarks>
		public static Vector3d Reflect(Vector3d vector, Vector3d normal)
		{
			Vector3d result;
			Reflect(in vector, in normal, out result);
			return result;
		}

		/// <summary>
		/// Orthogonalizes a list of vectors.
		/// </summary>
		/// <param name="destination">The list of orthogonalized vectors.</param>
		/// <param name="source">The list of vectors to orthogonalize.</param>
		/// <remarks>
		/// <para>Orthogonalization is the process of making all vectors orthogonal to each other. This
		/// means that any given vector in the list will be orthogonal to any other given vector in the
		/// list.</para>
		/// <para>Because this method uses the modified Gram-Schmidt process, the resulting vectors
		/// tend to be numerically unstable. The numeric stability decreases according to the vectors
		/// position in the list so that the first vector is the most stable and the last vector is the
		/// least stable.</para>
		/// </remarks>
		/// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> or <paramref name="destination"/> is <c>null</c>.</exception>
		/// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="destination"/> is shorter in length than <paramref name="source"/>.</exception>
		public static void Orthogonalize(Vector3d[] destination, params Vector3d[] source)
		{
			//Uses the modified Gram-Schmidt process.
			//q1 = m1
			//q2 = m2 - ((q1 ⋅ m2) / (q1 ⋅ q1)) * q1
			//q3 = m3 - ((q1 ⋅ m3) / (q1 ⋅ q1)) * q1 - ((q2 ⋅ m3) / (q2 ⋅ q2)) * q2
			//q4 = m4 - ((q1 ⋅ m4) / (q1 ⋅ q1)) * q1 - ((q2 ⋅ m4) / (q2 ⋅ q2)) * q2 - ((q3 ⋅ m4) / (q3 ⋅ q3)) * q3
			//q5 = ...

			if (source == null)
				throw new ArgumentNullException("source");
			if (destination == null)
				throw new ArgumentNullException("destination");
			if (destination.Length < source.Length)
				throw new ArgumentOutOfRangeException("destination", "The destination array must be of same length or larger length than the source array.");

			for (int i = 0; i < source.Length; ++i)
			{
				Vector3d newvector = source[i];

				for (int r = 0; r < i; ++r)
				{
					newvector -= (Vector3d.Dot(destination[r], newvector) / Vector3d.Dot(destination[r], destination[r])) * destination[r];
				}

				destination[i] = newvector;
			}
		}

		/// <summary>
		/// Orthonormalizes a list of vectors.
		/// </summary>
		/// <param name="destination">The list of orthonormalized vectors.</param>
		/// <param name="source">The list of vectors to orthonormalize.</param>
		/// <remarks>
		/// <para>Orthonormalization is the process of making all vectors orthogonal to each
		/// other and making all vectors of unit length. This means that any given vector will
		/// be orthogonal to any other given vector in the list.</para>
		/// <para>Because this method uses the modified Gram-Schmidt process, the resulting vectors
		/// tend to be numerically unstable. The numeric stability decreases according to the vectors
		/// position in the list so that the first vector is the most stable and the last vector is the
		/// least stable.</para>
		/// </remarks>
		/// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> or <paramref name="destination"/> is <c>null</c>.</exception>
		/// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="destination"/> is shorter in length than <paramref name="source"/>.</exception>
		public static void Orthonormalize(Vector3d[] destination, params Vector3d[] source)
		{
			//Uses the modified Gram-Schmidt process.
			//Because we are making unit vectors, we can optimize the math for orthogonalization
			//and simplify the projection operation to remove the division.
			//q1 = m1 / |m1|
			//q2 = (m2 - (q1 ⋅ m2) * q1) / |m2 - (q1 ⋅ m2) * q1|
			//q3 = (m3 - (q1 ⋅ m3) * q1 - (q2 ⋅ m3) * q2) / |m3 - (q1 ⋅ m3) * q1 - (q2 ⋅ m3) * q2|
			//q4 = (m4 - (q1 ⋅ m4) * q1 - (q2 ⋅ m4) * q2 - (q3 ⋅ m4) * q3) / |m4 - (q1 ⋅ m4) * q1 - (q2 ⋅ m4) * q2 - (q3 ⋅ m4) * q3|
			//q5 = ...

			if (source == null)
				throw new ArgumentNullException("source");
			if (destination == null)
				throw new ArgumentNullException("destination");
			if (destination.Length < source.Length)
				throw new ArgumentOutOfRangeException("destination", "The destination array must be of same length or larger length than the source array.");

			for (int i = 0; i < source.Length; ++i)
			{
				Vector3d newvector = source[i];

				for (int r = 0; r < i; ++r)
				{
					newvector -= Vector3d.Dot(destination[r], newvector) * destination[r];
				}

				newvector.Normalize();
				destination[i] = newvector;
			}
		}
		/*
		/// <summary>
		/// Transforms a 3D vector by the given <see cref="Quaternion"/> rotation.
		/// </summary>
		/// <param name="vector">The vector to rotate.</param>
		/// <param name="rotation">The <see cref="Quaternion"/> rotation to apply.</param>
		/// <param name="result">When the method completes, contains the transformed <see cref="Vector4d"/>.</param>
		public static void Transform(in Vector3d vector, in Quaternion rotation, out Vector3d result)
		{
			double x = rotation.X + rotation.X;
			double y = rotation.Y + rotation.Y;
			double z = rotation.Z + rotation.Z;
			double wx = rotation.W * x;
			double wy = rotation.W * y;
			double wz = rotation.W * z;
			double xx = rotation.X * x;
			double xy = rotation.X * y;
			double xz = rotation.X * z;
			double yy = rotation.Y * y;
			double yz = rotation.Y * z;
			double zz = rotation.Z * z;

			result = new Vector3d(
				 ((vector.X * ((1.0f - yy) - zz)) + (vector.Y * (xy - wz))) + (vector.Z * (xz + wy)),
				 ((vector.X * (xy + wz)) + (vector.Y * ((1.0f - xx) - zz))) + (vector.Z * (yz - wx)),
				 ((vector.X * (xz - wy)) + (vector.Y * (yz + wx))) + (vector.Z * ((1.0f - xx) - yy)));
		}

		/// <summary>
		/// Transforms a 3D vector by the given <see cref="Quaternion"/> rotation.
		/// </summary>
		/// <param name="vector">The vector to rotate.</param>
		/// <param name="rotation">The <see cref="Quaternion"/> rotation to apply.</param>
		/// <returns>The transformed <see cref="Vector4d"/>.</returns>
		public static Vector3d Transform(Vector3d vector, Quaternion rotation)
		{
			Vector3d result;
			Transform(in vector, in rotation, out result);
			return result;
		}

		/// <summary>
		/// Transforms an array of vectors by the given <see cref="Quaternion"/> rotation.
		/// </summary>
		/// <param name="source">The array of vectors to transform.</param>
		/// <param name="rotation">The <see cref="Quaternion"/> rotation to apply.</param>
		/// <param name="destination">The array for which the transformed vectors are stored.
		/// This array may be the same array as <paramref name="source"/>.</param>
		/// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> or <paramref name="destination"/> is <c>null</c>.</exception>
		/// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="destination"/> is shorter in length than <paramref name="source"/>.</exception>
		public static void Transform(Vector3d[] source, in Quaternion rotation, Vector3d[] destination)
		{
			if (source == null)
				throw new ArgumentNullException("source");
			if (destination == null)
				throw new ArgumentNullException("destination");
			if (destination.Length < source.Length)
				throw new ArgumentOutOfRangeException("destination", "The destination array must be of same length or larger length than the source array.");

			double x = rotation.X + rotation.X;
			double y = rotation.Y + rotation.Y;
			double z = rotation.Z + rotation.Z;
			double wx = rotation.W * x;
			double wy = rotation.W * y;
			double wz = rotation.W * z;
			double xx = rotation.X * x;
			double xy = rotation.X * y;
			double xz = rotation.X * z;
			double yy = rotation.Y * y;
			double yz = rotation.Y * z;
			double zz = rotation.Z * z;

			double num1 = ((1.0f - yy) - zz);
			double num2 = (xy - wz);
			double num3 = (xz + wy);
			double num4 = (xy + wz);
			double num5 = ((1.0f - xx) - zz);
			double num6 = (yz - wx);
			double num7 = (xz - wy);
			double num8 = (yz + wx);
			double num9 = ((1.0f - xx) - yy);

			for (int i = 0; i < source.Length; ++i)
			{
				destination[i] = new Vector3d(
					 ((source[i].X * num1) + (source[i].Y * num2)) + (source[i].Z * num3),
					 ((source[i].X * num4) + (source[i].Y * num5)) + (source[i].Z * num6),
					 ((source[i].X * num7) + (source[i].Y * num8)) + (source[i].Z * num9));
			}
		}


		/// <summary>
		/// Transforms a 3D vector by the given <see cref="Matrix3x3"/>.
		/// </summary>
		/// <param name="vector">The source vector.</param>
		/// <param name="transform">The transformation <see cref="Matrix3x3"/>.</param>
		/// <param name="result">When the method completes, contains the transformed <see cref="Vector3d"/>.</param>
		public static void Transform(in Vector3d vector, in Matrix3x3 transform, out Vector3d result)
		{
			result = new Vector3d((vector.X * transform.M11) + (vector.Y * transform.M21) + (vector.Z * transform.M31),
											(vector.X * transform.M12) + (vector.Y * transform.M22) + (vector.Z * transform.M32),
											(vector.X * transform.M13) + (vector.Y * transform.M23) + (vector.Z * transform.M33)
									  );
		}

		/// <summary>
		/// Transforms a 3D vector by the given <see cref="Matrix3x3"/>.
		/// </summary>
		/// <param name="vector">The source vector.</param>
		/// <param name="transform">The transformation <see cref="Matrix3x3"/>.</param>
		/// <returns>The transformed <see cref="Vector3d"/>.</returns>
		public static Vector3d Transform(Vector3d vector, Matrix3x3 transform)
		{
			Vector3d result;
			Transform(in vector, in transform, out result);
			return result;
		}

		/// <summary>
		/// Transforms a 3D vector by the given <see cref="Matrix"/>.
		/// </summary>
		/// <param name="vector">The source vector.</param>
		/// <param name="transform">The transformation <see cref="Matrix"/>.</param>
		/// <param name="result">When the method completes, contains the transformed <see cref="Vector3d"/>.</param>
		public static void Transform(in Vector3d vector, in Matrix transform, out Vector3d result)
		{
			Vector4d intermediate;
			Transform(in vector, in transform, out intermediate);
			result = (Vector3d)intermediate;
		}

		/// <summary>
		/// Transforms a 3D vector by the given <see cref="Matrix"/>.
		/// </summary>
		/// <param name="vector">The source vector.</param>
		/// <param name="transform">The transformation <see cref="Matrix"/>.</param>
		/// <param name="result">When the method completes, contains the transformed <see cref="Vector4d"/>.</param>
		public static void Transform(in Vector3d vector, in Matrix transform, out Vector4d result)
		{
			result = new Vector4d(
				 (vector.X * transform.M11) + (vector.Y * transform.M21) + (vector.Z * transform.M31) + transform.M41,
				 (vector.X * transform.M12) + (vector.Y * transform.M22) + (vector.Z * transform.M32) + transform.M42,
				 (vector.X * transform.M13) + (vector.Y * transform.M23) + (vector.Z * transform.M33) + transform.M43,
				 (vector.X * transform.M14) + (vector.Y * transform.M24) + (vector.Z * transform.M34) + transform.M44);
		}

		/// <summary>
		/// Transforms a 3D vector by the given <see cref="Matrix"/>.
		/// </summary>
		/// <param name="vector">The source vector.</param>
		/// <param name="transform">The transformation <see cref="Matrix"/>.</param>
		/// <returns>The transformed <see cref="Vector4d"/>.</returns>
		public static Vector4d Transform(Vector3d vector, Matrix transform)
		{
			Vector4d result;
			Transform(in vector, in transform, out result);
			return result;
		}

		/// <summary>
		/// Transforms an array of 3D vectors by the given <see cref="Matrix"/>.
		/// </summary>
		/// <param name="source">The array of vectors to transform.</param>
		/// <param name="transform">The transformation <see cref="Matrix"/>.</param>
		/// <param name="destination">The array for which the transformed vectors are stored.</param>
		/// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> or <paramref name="destination"/> is <c>null</c>.</exception>
		/// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="destination"/> is shorter in length than <paramref name="source"/>.</exception>
		public static void Transform(Vector3d[] source, in Matrix transform, Vector4d[] destination)
		{
			if (source == null)
				throw new ArgumentNullException("source");
			if (destination == null)
				throw new ArgumentNullException("destination");
			if (destination.Length < source.Length)
				throw new ArgumentOutOfRangeException("destination", "The destination array must be of same length or larger length than the source array.");

			for (int i = 0; i < source.Length; ++i)
			{
				Transform(in source[i], in transform, out destination[i]);
			}
		}

		/// <summary>
		/// Performs a coordinate transformation using the given <see cref="Matrix"/>.
		/// </summary>
		/// <param name="coordinate">The coordinate vector to transform.</param>
		/// <param name="transform">The transformation <see cref="Matrix"/>.</param>
		/// <param name="result">When the method completes, contains the transformed coordinates.</param>
		/// <remarks>
		/// A coordinate transform performs the transformation with the assumption that the w component
		/// is one. The four dimensional vector obtained from the transformation operation has each
		/// component in the vector divided by the w component. This forces the w component to be one and
		/// therefore makes the vector homogeneous. The homogeneous vector is often preferred when working
		/// with coordinates as the w component can safely be ignored.
		/// </remarks>
		public static void TransformCoordinate(in Vector3d coordinate, in Matrix transform, out Vector3d result)
		{
			Vector4d vector = new Vector4d();
			vector.X = (coordinate.X * transform.M11) + (coordinate.Y * transform.M21) + (coordinate.Z * transform.M31) + transform.M41;
			vector.Y = (coordinate.X * transform.M12) + (coordinate.Y * transform.M22) + (coordinate.Z * transform.M32) + transform.M42;
			vector.Z = (coordinate.X * transform.M13) + (coordinate.Y * transform.M23) + (coordinate.Z * transform.M33) + transform.M43;
			vector.W = 1f / ((coordinate.X * transform.M14) + (coordinate.Y * transform.M24) + (coordinate.Z * transform.M34) + transform.M44);

			result = new Vector3d(vector.X * vector.W, vector.Y * vector.W, vector.Z * vector.W);
		}

		/// <summary>
		/// Performs a coordinate transformation using the given <see cref="Matrix"/>.
		/// </summary>
		/// <param name="coordinate">The coordinate vector to transform.</param>
		/// <param name="transform">The transformation <see cref="Matrix"/>.</param>
		/// <returns>The transformed coordinates.</returns>
		/// <remarks>
		/// A coordinate transform performs the transformation with the assumption that the w component
		/// is one. The four dimensional vector obtained from the transformation operation has each
		/// component in the vector divided by the w component. This forces the w component to be one and
		/// therefore makes the vector homogeneous. The homogeneous vector is often preferred when working
		/// with coordinates as the w component can safely be ignored.
		/// </remarks>
		public static Vector3d TransformCoordinate(Vector3d coordinate, Matrix transform)
		{
			Vector3d result;
			TransformCoordinate(in coordinate, in transform, out result);
			return result;
		}

		/// <summary>
		/// Performs a coordinate transformation on an array of vectors using the given <see cref="Matrix"/>.
		/// </summary>
		/// <param name="source">The array of coordinate vectors to transform.</param>
		/// <param name="transform">The transformation <see cref="Matrix"/>.</param>
		/// <param name="destination">The array for which the transformed vectors are stored.
		/// This array may be the same array as <paramref name="source"/>.</param>
		/// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> or <paramref name="destination"/> is <c>null</c>.</exception>
		/// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="destination"/> is shorter in length than <paramref name="source"/>.</exception>
		/// <remarks>
		/// A coordinate transform performs the transformation with the assumption that the w component
		/// is one. The four dimensional vector obtained from the transformation operation has each
		/// component in the vector divided by the w component. This forces the w component to be one and
		/// therefore makes the vector homogeneous. The homogeneous vector is often preferred when working
		/// with coordinates as the w component can safely be ignored.
		/// </remarks>
		public static void TransformCoordinate(Vector3d[] source, in Matrix transform, Vector3d[] destination)
		{
			if (source == null)
				throw new ArgumentNullException("source");
			if (destination == null)
				throw new ArgumentNullException("destination");
			if (destination.Length < source.Length)
				throw new ArgumentOutOfRangeException("destination", "The destination array must be of same length or larger length than the source array.");

			for (int i = 0; i < source.Length; ++i)
			{
				TransformCoordinate(in source[i], in transform, out destination[i]);
			}
		}

		/// <summary>
		/// Performs a normal transformation using the given <see cref="Matrix"/>.
		/// </summary>
		/// <param name="normal">The normal vector to transform.</param>
		/// <param name="transform">The transformation <see cref="Matrix"/>.</param>
		/// <param name="result">When the method completes, contains the transformed normal.</param>
		/// <remarks>
		/// A normal transform performs the transformation with the assumption that the w component
		/// is zero. This causes the fourth row and fourth column of the matrix to be unused. The
		/// end result is a vector that is not translated, but all other transformation properties
		/// apply. This is often preferred for normal vectors as normals purely represent direction
		/// rather than location because normal vectors should not be translated.
		/// </remarks>
		public static void TransformNormal(in Vector3d normal, in Matrix transform, out Vector3d result)
		{
			result = new Vector3d(
				 (normal.X * transform.M11) + (normal.Y * transform.M21) + (normal.Z * transform.M31),
				 (normal.X * transform.M12) + (normal.Y * transform.M22) + (normal.Z * transform.M32),
				 (normal.X * transform.M13) + (normal.Y * transform.M23) + (normal.Z * transform.M33));
		}

		/// <summary>
		/// Performs a normal transformation using the given <see cref="Matrix"/>.
		/// </summary>
		/// <param name="normal">The normal vector to transform.</param>
		/// <param name="transform">The transformation <see cref="Matrix"/>.</param>
		/// <returns>The transformed normal.</returns>
		/// <remarks>
		/// A normal transform performs the transformation with the assumption that the w component
		/// is zero. This causes the fourth row and fourth column of the matrix to be unused. The
		/// end result is a vector that is not translated, but all other transformation properties
		/// apply. This is often preferred for normal vectors as normals purely represent direction
		/// rather than location because normal vectors should not be translated.
		/// </remarks>
		public static Vector3d TransformNormal(Vector3d normal, Matrix transform)
		{
			Vector3d result;
			TransformNormal(in normal, in transform, out result);
			return result;
		}

		/// <summary>
		/// Performs a normal transformation on an array of vectors using the given <see cref="Matrix"/>.
		/// </summary>
		/// <param name="source">The array of normal vectors to transform.</param>
		/// <param name="transform">The transformation <see cref="Matrix"/>.</param>
		/// <param name="destination">The array for which the transformed vectors are stored.
		/// This array may be the same array as <paramref name="source"/>.</param>
		/// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> or <paramref name="destination"/> is <c>null</c>.</exception>
		/// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="destination"/> is shorter in length than <paramref name="source"/>.</exception>
		/// <remarks>
		/// A normal transform performs the transformation with the assumption that the w component
		/// is zero. This causes the fourth row and fourth column of the matrix to be unused. The
		/// end result is a vector that is not translated, but all other transformation properties
		/// apply. This is often preferred for normal vectors as normals purely represent direction
		/// rather than location because normal vectors should not be translated.
		/// </remarks>
		public static void TransformNormal(Vector3d[] source, in Matrix transform, Vector3d[] destination)
		{
			if (source == null)
				throw new ArgumentNullException("source");
			if (destination == null)
				throw new ArgumentNullException("destination");
			if (destination.Length < source.Length)
				throw new ArgumentOutOfRangeException("destination", "The destination array must be of same length or larger length than the source array.");

			for (int i = 0; i < source.Length; ++i)
			{
				TransformNormal(in source[i], in transform, out destination[i]);
			}
		}
		*/
		/// <summary>
		/// Adds two vectors.
		/// </summary>
		/// <param name="left">The first vector to add.</param>
		/// <param name="right">The second vector to add.</param>
		/// <returns>The sum of the two vectors.</returns>
		public static Vector3d operator +(Vector3d left, Vector3d right)
		{
			return new Vector3d(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
		}

		/// <summary>
		/// Multiplies a vector with another by performing component-wise multiplication equivalent to <see cref="Multiply(in Vector3d,in Vector3d,out Vector3d)"/>.
		/// </summary>
		/// <param name="left">The first vector to multiply.</param>
		/// <param name="right">The second vector to multiply.</param>
		/// <returns>The multiplication of the two vectors.</returns>
		public static Vector3d operator *(Vector3d left, Vector3d right)
		{
			return new Vector3d(left.X * right.X, left.Y * right.Y, left.Z * right.Z);
		}

		/// <summary>
		/// Assert a vector (return it unchanged).
		/// </summary>
		/// <param name="value">The vector to assert (unchanged).</param>
		/// <returns>The asserted (unchanged) vector.</returns>
		public static Vector3d operator +(Vector3d value)
		{
			return value;
		}

		/// <summary>
		/// Subtracts two vectors.
		/// </summary>
		/// <param name="left">The first vector to subtract.</param>
		/// <param name="right">The second vector to subtract.</param>
		/// <returns>The difference of the two vectors.</returns>
		public static Vector3d operator -(Vector3d left, Vector3d right)
		{
			return new Vector3d(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
		}

		/// <summary>
		/// Reverses the direction of a given vector.
		/// </summary>
		/// <param name="value">The vector to negate.</param>
		/// <returns>A vector facing in the opposite direction.</returns>
		public static Vector3d operator -(Vector3d value)
		{
			return new Vector3d(-value.X, -value.Y, -value.Z);
		}

		/// <summary>
		/// Scales a vector by the given value.
		/// </summary>
		/// <param name="value">The vector to scale.</param>
		/// <param name="scale">The amount by which to scale the vector.</param>
		/// <returns>The scaled vector.</returns>
		public static Vector3d operator *(double scale, Vector3d value)
		{
			return new Vector3d(value.X * scale, value.Y * scale, value.Z * scale);
		}

		/// <summary>
		/// Scales a vector by the given value.
		/// </summary>
		/// <param name="value">The vector to scale.</param>
		/// <param name="scale">The amount by which to scale the vector.</param>
		/// <returns>The scaled vector.</returns>
		public static Vector3d operator *(Vector3d value, double scale)
		{
			return new Vector3d(value.X * scale, value.Y * scale, value.Z * scale);
		}

		/// <summary>
		/// Scales a vector by the given value.
		/// </summary>
		/// <param name="value">The vector to scale.</param>
		/// <param name="scale">The amount by which to scale the vector.</param>
		/// <returns>The scaled vector.</returns>
		public static Vector3d operator /(Vector3d value, double scale)
		{
			return new Vector3d(value.X / scale, value.Y / scale, value.Z / scale);
		}

		/// <summary>
		/// Scales a vector by the given value.
		/// </summary>
		/// <param name="scale">The amount by which to scale the vector.</param>
		/// <param name="value">The vector to scale.</param>  
		/// <returns>The scaled vector.</returns>
		public static Vector3d operator /(double scale, Vector3d value)
		{
			return new Vector3d(scale / value.X, scale / value.Y, scale / value.Z);
		}

		/// <summary>
		/// Scales a vector by the given value.
		/// </summary>
		/// <param name="value">The vector to scale.</param>
		/// <param name="scale">The amount by which to scale the vector.</param>
		/// <returns>The scaled vector.</returns>
		public static Vector3d operator /(Vector3d value, Vector3d scale)
		{
			return new Vector3d(value.X / scale.X, value.Y / scale.Y, value.Z / scale.Z);
		}

		/// <summary>
		/// Perform a component-wise addition
		/// </summary>
		/// <param name="value">The input vector.</param>
		/// <param name="scalar">The scalar value to be added on elements</param>
		/// <returns>The vector with added scalar for each element.</returns>
		public static Vector3d operator +(Vector3d value, double scalar)
		{
			return new Vector3d(value.X + scalar, value.Y + scalar, value.Z + scalar);
		}

		/// <summary>
		/// Perform a component-wise addition
		/// </summary>
		/// <param name="value">The input vector.</param>
		/// <param name="scalar">The scalar value to be added on elements</param>
		/// <returns>The vector with added scalar for each element.</returns>
		public static Vector3d operator +(double scalar, Vector3d value)
		{
			return new Vector3d(scalar + value.X, scalar + value.Y, scalar + value.Z);
		}

		/// <summary>
		/// Perform a component-wise subtraction
		/// </summary>
		/// <param name="value">The input vector.</param>
		/// <param name="scalar">The scalar value to be subtraced from elements</param>
		/// <returns>The vector with added scalar from each element.</returns>
		public static Vector3d operator -(Vector3d value, double scalar)
		{
			return new Vector3d(value.X - scalar, value.Y - scalar, value.Z - scalar);
		}

		/// <summary>
		/// Perform a component-wise subtraction
		/// </summary>
		/// <param name="value">The input vector.</param>
		/// <param name="scalar">The scalar value to be subtraced from elements</param>
		/// <returns>The vector with subtraced scalar from each element.</returns>
		public static Vector3d operator -(double scalar, Vector3d value)
		{
			return new Vector3d(scalar - value.X, scalar - value.Y, scalar - value.Z);
		}

		/// <summary>
		/// Tests for equality between two objects.
		/// </summary>
		/// <param name="left">The first value to compare.</param>
		/// <param name="right">The second value to compare.</param>
		/// <returns><c>true</c> if <paramref name="left"/> has the same value as <paramref name="right"/>; otherwise, <c>false</c>.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool operator ==(Vector3d left, Vector3d right)
		{
			return left.Equals(in right);
		}

		/// <summary>
		/// Tests for inequality between two objects.
		/// </summary>
		/// <param name="left">The first value to compare.</param>
		/// <param name="right">The second value to compare.</param>
		/// <returns><c>true</c> if <paramref name="left"/> has a different value than <paramref name="right"/>; otherwise, <c>false</c>.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool operator !=(Vector3d left, Vector3d right)
		{
			return !left.Equals(in right);
		}

		/// <summary>
		/// Performs an explicit conversion from <see cref="Vector3d"/> to <see cref="Vector2d"/>.
		/// </summary>
		/// <param name="value">The value.</param>
		/// <returns>The result of the conversion.</returns>
		public static explicit operator Vector2d(Vector3d value)
		{
			return new Vector2d(value.X, value.Y);
		}

		/// <summary>
		/// Performs an explicit conversion from <see cref="Vector3d"/> to <see cref="Vector4d"/>.
		/// </summary>
		/// <param name="value">The value.</param>
		/// <returns>The result of the conversion.</returns>
		public static explicit operator Vector4d(Vector3d value)
		{
			return new Vector4d(value, 0.0f);
		}

		/// <summary>
		/// Returns a <see cref="System.String"/> that represents this instance.
		/// </summary>
		/// <returns>
		/// A <see cref="System.String"/> that represents this instance.
		/// </returns>
		public override string ToString()
		{
			return string.Format(CultureInfo.CurrentCulture, "X:{0} Y:{1} Z:{2}", X, Y, Z);
		}

		/// <summary>
		/// Returns a <see cref="System.String"/> that represents this instance.
		/// </summary>
		/// <param name="format">The format.</param>
		/// <returns>
		/// A <see cref="System.String"/> that represents this instance.
		/// </returns>
		public string ToString(string format)
		{
			if (format == null)
				return ToString();

			return string.Format(CultureInfo.CurrentCulture, "X:{0} Y:{1} Z:{2}", X.ToString(format, CultureInfo.CurrentCulture),
				 Y.ToString(format, CultureInfo.CurrentCulture), Z.ToString(format, CultureInfo.CurrentCulture));
		}

		/// <summary>
		/// Returns a <see cref="System.String"/> that represents this instance.
		/// </summary>
		/// <param name="formatProvider">The format provider.</param>
		/// <returns>
		/// A <see cref="System.String"/> that represents this instance.
		/// </returns>
		public string ToString(IFormatProvider formatProvider)
		{
			return string.Format(formatProvider, "X:{0} Y:{1} Z:{2}", X, Y, Z);
		}

		/// <summary>
		/// Returns a <see cref="System.String"/> that represents this instance.
		/// </summary>
		/// <param name="format">The format.</param>
		/// <param name="formatProvider">The format provider.</param>
		/// <returns>
		/// A <see cref="System.String"/> that represents this instance.
		/// </returns>
		public string ToString(string format, IFormatProvider formatProvider)
		{
			if (format == null)
				return ToString(formatProvider);

			return string.Format(formatProvider, "X:{0} Y:{1} Z:{2}", X.ToString(format, formatProvider),
				 Y.ToString(format, formatProvider), Z.ToString(format, formatProvider));
		}

		/// <summary>
		/// Returns a hash code for this instance.
		/// </summary>
		/// <returns>
		/// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table. 
		/// </returns>
		public override int GetHashCode()
		{
			unchecked
			{
				var hashCode = X.GetHashCode();
				hashCode = (hashCode * 397) ^ Y.GetHashCode();
				hashCode = (hashCode * 397) ^ Z.GetHashCode();
				return hashCode;
			}
		}

		/// <summary>
		/// Determines whether the specified <see cref="Vector3d"/> is equal to this instance.
		/// </summary>
		/// <param name="other">The <see cref="Vector3d"/> to compare with this instance.</param>
		/// <returns>
		/// 	<c>true</c> if the specified <see cref="Vector3d"/> is equal to this instance; otherwise, <c>false</c>.
		/// </returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool Equals(in Vector3d other)
		{
			return MathUtil.NearEqual(other.X, X) && MathUtil.NearEqual(other.Y, Y) && MathUtil.NearEqual(other.Z, Z);
		}

		/// <summary>
		/// Determines whether the specified <see cref="Vector3d"/> is equal to this instance.
		/// </summary>
		/// <param name="other">The <see cref="Vector3d"/> to compare with this instance.</param>
		/// <returns>
		/// 	<c>true</c> if the specified <see cref="Vector3d"/> is equal to this instance; otherwise, <c>false</c>.
		/// </returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool Equals(Vector3d other)
		{
			return Equals(in other);
		}

		/// <summary>
		/// Determines whether the specified <see cref="System.Object"/> is equal to this instance.
		/// </summary>
		/// <param name="value">The <see cref="System.Object"/> to compare with this instance.</param>
		/// <returns>
		/// 	<c>true</c> if the specified <see cref="System.Object"/> is equal to this instance; otherwise, <c>false</c>.
		/// </returns>
		public override bool Equals(object value)
		{
			if (!(value is Vector3d))
				return false;

			var strongValue = (Vector3d)value;
			return Equals(in strongValue);
		}
	}
}
