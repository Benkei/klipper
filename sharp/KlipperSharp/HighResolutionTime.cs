using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace KlipperSharp
{
	public static class HighResolutionTime
	{
		private static readonly long timeInitialized = Stopwatch.GetTimestamp();
		private static readonly double invFreq = 1.0 / Stopwatch.Frequency;

		/// <summary>
		/// Get number of seconds since the application started
		/// </summary>
		public static double Now { get { return (Stopwatch.GetTimestamp() - timeInitialized) * invFreq; } }
	}
}
