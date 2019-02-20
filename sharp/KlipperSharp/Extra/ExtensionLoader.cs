using System;
using System.Collections.Generic;
using System.Reflection;

namespace KlipperSharp.Extra
{
	public class ExtensionLoader
	{
		static Dictionary<string, ExtensionInfo> extensions = new Dictionary<string, ExtensionInfo>();

		struct ExtensionInfo
		{
			public ExtensionAttribute Attri;
			public Func<ConfigWrapper, object> Generator;
		}

		public static Func<ConfigWrapper, object> GetGenerator(string configSection)
		{
			ExtensionInfo info;
			extensions.TryGetValue(configSection, out info);
			return info.Generator;
		}

		static ExtensionLoader()
		{
			foreach (var item in GetTypesWithHelpAttribute<ExtensionAttribute>(Assembly.GetExecutingAssembly()))
			{
				var methods = item.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
				foreach (var method in methods)
				{
					var extGenAttri = method.GetCustomAttribute<ExtensionGeneratorAttribute>();
					if (extGenAttri == null)
					{
						continue;
					}
					var extAttri = item.GetCustomAttribute<ExtensionAttribute>();

					var callback = (Func<ConfigWrapper, object>)method.CreateDelegate(typeof(Func<ConfigWrapper, object>));

					extensions.Add(extAttri.ConfigSection,
						new ExtensionInfo()
						{
							Attri = extAttri,
							Generator = callback
						}
					);
				}
			}
		}

		static IEnumerable<Type> GetTypesWithHelpAttribute<T>(Assembly assembly) where T : Attribute
		{
			foreach (Type type in assembly.GetTypes())
			{
				if (type.GetCustomAttributes(typeof(T), true).Length > 0)
				{
					yield return type;
				}
			}
		}
	}

}
