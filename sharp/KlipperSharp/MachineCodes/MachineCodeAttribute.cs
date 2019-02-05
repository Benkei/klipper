using System;

namespace KlipperSharp.MachineCodes
{
	[System.AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
	sealed class MachineCodeAttribute : Attribute
	{
		public MachineCodeAttribute(string prefex, int code)
		{
			this.Prefex = prefex;
			this.Code = code;
		}

		public string Prefex { get; private set; }
		public int Code { get; private set; }
	}
}
