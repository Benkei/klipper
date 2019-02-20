using System;

namespace KlipperSharp.Extra
{
	[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
	public sealed class ExtensionGeneratorAttribute : Attribute
	{
	}
}