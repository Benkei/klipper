using KlipperSharp;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharpTest
{
	class MathUtilTest
	{
		[SetUp]
		public void Setup()
		{
		}

		[Test]
		public void Max()
		{
			long a = 3, b = 2, c = 1;
			var res = MathUtil.Max(a, b, c);

			Assert.AreEqual(3, res);
		}

		[Test]
		public void Min()
		{
			long a = -3, b = 4523, c = 1;
			var res = MathUtil.Min(a, b, c);

			Assert.AreEqual(-3, res);
		}
	}
}
