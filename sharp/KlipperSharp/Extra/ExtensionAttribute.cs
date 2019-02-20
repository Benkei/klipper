using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp.Extra
{
	[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
	public sealed class ExtensionAttribute : Attribute
	{
		public ExtensionAttribute(string configSection)
		{
			ConfigSection = configSection;
		}

		public string ConfigSection { get; }
	}
}
