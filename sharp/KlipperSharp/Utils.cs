using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp
{
	static class Utils
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
	}
}
