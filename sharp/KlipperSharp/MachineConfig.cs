using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

namespace KlipperSharp
{
	public class MachineConfig
	{
		Machine machine;
		XmlDocument config;

		public MachineConfig()
		{

		}


		XmlDocument _read_config_file(string filename)
		{
			try
			{
				var doc = new XmlDocument();
				using (var fs = File.OpenRead(filename))
				{
					doc.Load(fs);
				}
				return doc;
			}
			catch (Exception)
			{

				throw;
			}
		}


	}
}
