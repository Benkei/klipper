using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp.Kinematics
{
	public static class KinematicFactory
	{
		public static KinematicBase load_kinematics(KinematicType type, ToolHead toolhead, ConfigWrapper config)
		{
			switch (type)
			{
				case KinematicType.none: break;
				case KinematicType.cartesian: return new KinematicCartesian(toolhead, config);
				case KinematicType.corexy: break;
				case KinematicType.delta: break;
				case KinematicType.extruder: break;
				case KinematicType.polar: break;
				case KinematicType.winch: break;
			}
			throw new NotImplementedException();
		}
	}
}
