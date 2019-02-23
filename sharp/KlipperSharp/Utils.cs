﻿using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace KlipperSharp
{
	public static class Utils
	{
		public static TReturn Get<TReturn>(this Dictionary<string, object> dict, string key, TReturn defaults = default(TReturn))
		{
			object value;
			dict.TryGetValue(key, out value);
			if (value == null)
			{
				return defaults;
			}
			value = Convert.ChangeType(value, typeof(TReturn), System.Globalization.CultureInfo.InvariantCulture);
			if (value is TReturn)
			{
				return (TReturn)value;
			}
			throw new ArgumentException($"Invalid cast '{value}' to {typeof(TReturn)}");
		}
		public static TReturn Get<TKey, TReturn>(this Dictionary<TKey, TReturn> dict, TKey key, TReturn defaults = default(TReturn))
		{
			TReturn value;
			dict.TryGetValue(key, out value);
			if (value == null)
			{
				return defaults;
			}
			return value;
		}

		// get programm start args
		public static string get(this string[] args, string key)
		{
			if (args == null)
			{
				return null;
			}

			var idx = Array.IndexOf(args, key);
			if (idx == -1 || idx + 1 >= args.Length)
			{
				return null;
			}

			return args[idx + 1];
		}

		public static double Get(this in Vector4 v, int index)
		{
			switch (index)
			{
				case 0: return v.X;
				case 1: return v.Y;
				case 2: return v.Z;
				case 3: return v.W;
			}
			throw new ArgumentOutOfRangeException(nameof(index));
		}
		public static double Get(this in Vector3 v, int index)
		{
			switch (index)
			{
				case 0: return v.X;
				case 1: return v.Y;
				case 2: return v.Z;
			}
			throw new ArgumentOutOfRangeException(nameof(index));
		}
		public static void Set(this ref Vector4 v, int index, double value)
		{
			switch (index)
			{
				case 0: v.X = (float)value; break;
				case 1: v.Y = (float)value; break;
				case 2: v.Z = (float)value; break;
				case 3: v.W = (float)value; break;
			}
			throw new ArgumentOutOfRangeException(nameof(index));
		}
		public static void Set(this ref Vector3 v, int index, double value)
		{
			switch (index)
			{
				case 0: v.X = (float)value; break;
				case 1: v.Y = (float)value; break;
				case 2: v.Z = (float)value; break;
			}
			throw new ArgumentOutOfRangeException(nameof(index));
		}
	}
}
