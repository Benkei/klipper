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
	}
}
