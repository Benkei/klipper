using System;
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

		public static double Get(this in Vector4d v, int index)
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
		public static double Get(this in Vector3d v, int index)
		{
			switch (index)
			{
				case 0: return v.X;
				case 1: return v.Y;
				case 2: return v.Z;
			}
			throw new ArgumentOutOfRangeException(nameof(index));
		}
		public static void Set(this ref Vector4d v, int index, double value)
		{
			switch (index)
			{
				case 0: v.X = value; break;
				case 1: v.Y = value; break;
				case 2: v.Z = value; break;
				case 3: v.W = value; break;
			}
			throw new ArgumentOutOfRangeException(nameof(index));
		}
		public static void Set(this ref Vector3d v, int index, double value)
		{
			switch (index)
			{
				case 0: v.X = value; break;
				case 1: v.Y = value; break;
				case 2: v.Z = value; break;
			}
			throw new ArgumentOutOfRangeException(nameof(index));
		}

		public static int IndexOf(this StringBuilder sb, char c)
		{
			int pos = 0;
			foreach (ReadOnlyMemory<char> chunk in sb.GetChunks())
			{
				var span = chunk.Span;
				for (int i = 0; i < span.Length; i++)
					if (span[i] == c)
						return pos + i;
				pos += span.Length;
			}
			return -1;
		}
		public static int LastIndexOf(this StringBuilder sb, char c)
		{
			int pos = -1, offset = 0;
			foreach (ReadOnlyMemory<char> chunk in sb.GetChunks())
			{
				var span = chunk.Span;
				for (int i = 0; i < span.Length; i++)
					if (span[i] == c)
						pos = offset + i;
				offset += span.Length;
			}
			return pos;
		}
	}
}
