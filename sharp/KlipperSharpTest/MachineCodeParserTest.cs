using KlipperSharp.MachineCodes;
using NUnit.Framework;
using System.IO;

namespace KlipperSharpTest
{
	public class MachineCodeParserTest
	{
		[SetUp]
		public void Setup()
		{
		}

		[Test]
		public void ParserTest()
		{
			var parser = new MachineCodeParser();
			var mc = new MachineCode();

			parser.Process("G0 F4800 E-1.0000", mc);

			Assert.AreEqual(mc.Linenumber, 0);

			Assert.AreEqual(mc.Command.Code, "G");
			Assert.AreEqual(mc.Command.Value, 0);

			Assert.AreEqual(mc.Parameters.Count, 2);

			Assert.AreEqual(mc.Parameters[0].Code, "F");
			Assert.AreEqual(mc.Parameters[0].Value, 4800);
			Assert.AreEqual(mc.Parameters[1].Code, "E");
			Assert.AreEqual(mc.Parameters[1].Value, -1.0);

			parser.Process("G0 F4800 E+1.00", mc);

			Assert.AreEqual(mc.Parameters[1].Code, "E");
			Assert.AreEqual(mc.Parameters[1].Value, 1.0);

			parser.Process("K1 X107.256 Y119.925 E 5.6946", mc);

			Assert.AreEqual(mc.Command.Code, "K");
			Assert.AreEqual(mc.Command.Value, 1);

			Assert.AreEqual(mc.Parameters.Count, 3);

			Assert.AreEqual(mc.Parameters[0].Code, "X");
			Assert.AreEqual(mc.Parameters[0].Value, 107.256);
			Assert.AreEqual(mc.Parameters[1].Code, "Y");
			Assert.AreEqual(mc.Parameters[1].Value, 119.925);
			Assert.AreEqual(mc.Parameters[2].Code, "E");
			Assert.AreEqual(mc.Parameters[2].Value, 5.6946);

			parser.Process("N2999   M55   F480 f ; E7.6855", mc);

			Assert.AreEqual(mc.Linenumber, 2999);

			Assert.AreEqual(mc.Command.Code, "M");
			Assert.AreEqual(mc.Command.Value, 55);

			Assert.AreEqual(mc.Parameters.Count, 1);

			Assert.AreEqual(mc.Parameters[0].Code, "F");
			Assert.AreEqual(mc.Parameters[0].Value, 480);

			parser.Process(";  G0 X109.762 Y122.431 F7800", mc);

			Assert.AreEqual(mc.Linenumber, 0);

			Assert.AreEqual(mc.Command.Code, null);
			Assert.AreEqual(mc.Command.Value, 0);

			Assert.AreEqual(mc.Parameters.Count, 0);

			parser.Process("G0 X109.762 X109.762 X109.762 X109.762 X109.762 Y122.431 F7800", mc);

			Assert.AreEqual(mc.Parameters.Count, 7);
		}
		
	}
}