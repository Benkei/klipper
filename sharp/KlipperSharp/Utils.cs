using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp
{
	static class Utils
	{
		public static TReturn Get<T, TReturn>(this Dictionary<T, TReturn> dict, T key, TReturn defaults = default(TReturn))
		{
			TReturn value;
			if (!dict.TryGetValue(key, out value))
				value = defaults;
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
