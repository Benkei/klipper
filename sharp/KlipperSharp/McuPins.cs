using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace KlipperSharp
{

	[Serializable]
	public class PinsException : Exception
	{
		public PinsException() { }
		public PinsException(string message) : base(message) { }
		public PinsException(string message, Exception inner) : base(message, inner) { }
		protected PinsException(
		 System.Runtime.Serialization.SerializationInfo info,
		 System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
	}

	/// <summary>
	/// Pin name to pin number definitions
	/// Hardware pin names
	/// </summary>
	public class McuPins
	{
		public static Dictionary<string, Dictionary<string, int>> Pins;


		static Dictionary<string, int> Port_pins(int port_count, int bit_count = 8)
		{
			var pins = new Dictionary<string, int>();
			for (int port = 0; port < port_count; port++)
			{
				char portchr = (char)(65 + port);
				if (portchr == 'I')
					continue;
				for (int portbit = 0; portbit < bit_count; portbit++)
				{
					pins[$"P{(char)portchr}{portbit}"] = port * bit_count + portbit;
				}
			}
			return pins;
		}

		static Dictionary<string, int> Named_pins(string format, int port_count, int bit_count = 32)
		{
			var pins = new Dictionary<string, int>();
			for (int port = 0; port < port_count; port++)
			{
				for (int portbit = 0; portbit < bit_count; portbit++)
				{
					pins[string.Format(format, port, portbit)] = port * bit_count + portbit;
				}
			}
			return pins;
		}

		static Dictionary<string, int> Lpc_pins()
		{
			var pins = new Dictionary<string, int>();
			for (int port = 0; port < 5; port++)
			{
				for (int pin = 0; pin < 32; pin++)
				{
					pins[$"P{port}.{pin}"] = port * 32 + pin;
				}
			}
			return pins;
		}

		static Dictionary<string, int> Beaglebone_pins()
		{
			var pins = Named_pins("gpio{0}_{1}", 4);
			for (int port = 0; port < 8; port++)
			{
				pins[$"AIN{port}"] = port + 4 * 32;
			}
			return pins;
		}

		/// <summary>
		/// Arduino mappings
		/// </summary>
		public static class Arduino
		{
			public static string[] Arduino_standard = {
				"PD0", "PD1", "PD2", "PD3", "PD4", "PD5", "PD6", "PD7", "PB0", "PB1",
				"PB2", "PB3", "PB4", "PB5", "PC0", "PC1", "PC2", "PC3", "PC4", "PC5"
			};
			public static string[] Arduino_analog_standard = {
				"PC0", "PC1", "PC2", "PC3", "PC4", "PC5", "PE0", "PE1"
			};
			public static string[] Arduino_mega = {
				"PE0", "PE1", "PE4", "PE5", "PG5", "PE3", "PH3", "PH4", "PH5", "PH6",
				"PB4", "PB5", "PB6", "PB7", "PJ1", "PJ0", "PH1", "PH0", "PD3", "PD2",
				"PD1", "PD0", "PA0", "PA1", "PA2", "PA3", "PA4", "PA5", "PA6", "PA7",
				"PC7", "PC6", "PC5", "PC4", "PC3", "PC2", "PC1", "PC0", "PD7", "PG2",
				"PG1", "PG0", "PL7", "PL6", "PL5", "PL4", "PL3", "PL2", "PL1", "PL0",
				"PB3", "PB2", "PB1", "PB0", "PF0", "PF1", "PF2", "PF3", "PF4", "PF5",
				"PF6", "PF7", "PK0", "PK1", "PK2", "PK3", "PK4", "PK5", "PK6", "PK7"
			};
			public static string[] Arduino_analog_mega = {
				"PF0", "PF1", "PF2", "PF3", "PF4", "PF5",
				"PF6", "PF7", "PK0", "PK1", "PK2", "PK3", "PK4", "PK5", "PK6", "PK7"
			};
			public static string[] Sanguino = {
				"PB0", "PB1", "PB2", "PB3", "PB4", "PB5", "PB6", "PB7", "PD0", "PD1",
				"PD2", "PD3", "PD4", "PD5", "PD6", "PD7", "PC0", "PC1", "PC2", "PC3",
				"PC4", "PC5", "PC6", "PC7", "PA0", "PA1", "PA2", "PA3", "PA4", "PA5",
				"PA6", "PA7"
			};
			public static string[] Sanguino_analog = {
				"PA0", "PA1", "PA2", "PA3", "PA4", "PA5", "PA6", "PA7"
			};
			public static string[] Arduino_Due = {
				"PA8", "PA9", "PB25", "PC28", "PA29", "PC25", "PC24", "PC23", "PC22", "PC21",
				"PA28", "PD7", "PD8", "PB27", "PD4", "PD5", "PA13", "PA12", "PA11", "PA10",
				"PB12", "PB13", "PB26", "PA14", "PA15", "PD0", "PD1", "PD2", "PD3", "PD6",
				"PD9", "PA7", "PD10", "PC1", "PC2", "PC3", "PC4", "PC5", "PC6", "PC7",
				"PC8", "PC9", "PA19", "PA20", "PC19", "PC18", "PC17", "PC16", "PC15", "PC14",
				"PC13", "PC12", "PB21", "PB14", "PA16", "PA24", "PA23", "PA22", "PA6", "PA4",
				"PA3", "PA2", "PB17", "PB18", "PB19", "PB20", "PB15", "PB16", "PA1", "PA0",
				"PA17", "PA18", "PC30", "PA21", "PA25", "PA26", "PA27", "PA28", "PB23"
			};
			public static string[] Arduino_Due_analog = {
				"PA16", "PA24", "PA23", "PA22", "PA6", "PA4", "PA3", "PA2", "PB17", "PB18",
				"PB19", "PB20"
			};

			public static Dictionary<string, (string[] digitals, string[] analogs)>
				Arduino_from_mcu = new Dictionary<string, (string[], string[])>()
			{
				{"atmega168" , (Arduino_standard, Arduino_analog_standard) },
				{"atmega328" , (Arduino_standard, Arduino_analog_standard) },
				{"atmega328p", (Arduino_standard, Arduino_analog_standard) },
				{"atmega644p", (Sanguino, Sanguino_analog) },
				{"atmega1280", (Arduino_mega, Arduino_analog_mega) },
				{"atmega2560", (Arduino_mega, Arduino_analog_mega) },
				{"sam3x8e"   , (Arduino_Due, Arduino_Due_analog) },
			};

			public static void Update_map_arduino(Dictionary<string, int> pins, string mcu)
			{
				if (!Arduino_from_mcu.ContainsKey(mcu))
					throw new ArgumentOutOfRangeException(nameof(mcu), $"Arduino aliases not supported on mcu '{mcu}'");

				var data = Arduino_from_mcu[mcu];
				for (int i = 0; i < data.digitals.Length; i++)
				{
					pins[$"ar{i}"] = pins[data.digitals[i]];
				}
				for (int i = 0; i < data.analogs.Length; i++)
				{
					pins[$"analog{i}"] = pins[data.analogs[i]];
				}
			}
		}
		/// <summary>
		/// Beaglebone mappings
		/// </summary>
		public static class Beaglebone
		{
			public static Dictionary<string, string> beagleboneblack_mappings = new Dictionary<string, string>()
			{
				{"P8_3" , "gpio1_6" }, {"P8_4" , "gpio1_7" }, {"P8_5" , "gpio1_2" },
				{"P8_6" , "gpio1_3" }, {"P8_7" , "gpio2_2" }, {"P8_8" , "gpio2_3" },
				{"P8_9" , "gpio2_5" }, {"P8_10", "gpio2_4" }, {"P8_11", "gpio1_13"},
				{"P8_12", "gpio1_12"}, {"P8_13", "gpio0_23"}, {"P8_14", "gpio0_26"},
				{"P8_15", "gpio1_15"}, {"P8_16", "gpio1_14"}, {"P8_17", "gpio0_27"},
				{"P8_18", "gpio2_1" }, {"P8_19", "gpio0_22"}, {"P8_20", "gpio1_31"},
				{"P8_21", "gpio1_30"}, {"P8_22", "gpio1_5" }, {"P8_23", "gpio1_4" },
				{"P8_24", "gpio1_1" }, {"P8_25", "gpio1_0" }, {"P8_26", "gpio1_29"},
				{"P8_27", "gpio2_22"}, {"P8_28", "gpio2_24"}, {"P8_29", "gpio2_23"},
				{"P8_30", "gpio2_25"}, {"P8_31", "gpio0_10"}, {"P8_32", "gpio0_11"},
				{"P8_33", "gpio0_9" }, {"P8_34", "gpio2_17"}, {"P8_35", "gpio0_8" },
				{"P8_36", "gpio2_16"}, {"P8_37", "gpio2_14"}, {"P8_38", "gpio2_15"},
				{"P8_39", "gpio2_12"}, {"P8_40", "gpio2_13"}, {"P8_41", "gpio2_10"},
				{"P8_42", "gpio2_11"}, {"P8_43", "gpio2_8" }, {"P8_44", "gpio2_9" },
				{"P8_45", "gpio2_6" }, {"P8_46", "gpio2_7" }, {"P9_11", "gpio0_30"},
				{"P9_12", "gpio1_28"}, {"P9_13", "gpio0_31"}, {"P9_14", "gpio1_18"},
				{"P9_15", "gpio1_16"}, {"P9_16", "gpio1_19"}, {"P9_17", "gpio0_5" },
				{"P9_18", "gpio0_4" }, {"P9_19", "gpio0_13"}, {"P9_20", "gpio0_12"},
				{"P9_21", "gpio0_3" }, {"P9_22", "gpio0_2" }, {"P9_23", "gpio1_17"},
				{"P9_24", "gpio0_15"}, {"P9_25", "gpio3_21"}, {"P9_26", "gpio0_14"},
				{"P9_27", "gpio3_19"}, {"P9_28", "gpio3_17"}, {"P9_29", "gpio3_15"},
				{"P9_30", "gpio3_16"}, {"P9_31", "gpio3_14"}, {"P9_41", "gpio0_20"},
				{"P9_42", "gpio3_20"}, {"P9_43", "gpio0_7" }, {"P9_44", "gpio3_18"},
				{"P9_33", "AIN4"    }, {"P9_35", "AIN6"    }, {"P9_36", "AIN5"    }, {"P9_37", "AIN2" },
				{"P9_38", "AIN3"    }, {"P9_39", "AIN0"    }, {"P9_40", "AIN1"    },
			};

			public static void update_map_beaglebone(Dictionary<string, int> pins, string mcu)
			{
				if (mcu != "pru")
					throw new ArgumentOutOfRangeException(nameof(mcu), $"Beaglebone aliases not supported on mcu '{mcu}'");
				foreach (var item in beagleboneblack_mappings)
				{
					pins[item.Key] = pins[item.Value];
				}
			}
		}


		static McuPins()
		{
			var linux = new Dictionary<string, int>();
			for (int i = 0; i < 8; i++)
			{
				linux[$"analog{i}"] = i;
			}

			Pins = new Dictionary<string, Dictionary<string, int>>() {
				{"atmega168"  , Port_pins(5)      },
				{"atmega328"  , Port_pins(5)      }, {"atmega328p" , Port_pins(5) },
				{"atmega644p" , Port_pins(4)      }, {"atmega1284p", Port_pins(4) },
				{"at90usb1286", Port_pins(6)      }, {"at90usb646" , Port_pins(6) },
				{"atmega32u4" , Port_pins(6)      },
				{"atmega1280" , Port_pins(12)     }, {"atmega2560" , Port_pins(12)    },
				{"sam3x8e"    , Port_pins(4, 32)  }, {"sam3x8c"    , Port_pins(2, 32) },
				{"sam4s8c"    , Port_pins(3, 32)  }, {"sam4e8e"    , Port_pins(5, 32) },
				{"samd21g"    , Port_pins(2, 32)  },
				{"stm32f103"  , Port_pins(5, 16)  },
				{"lpc176x"    , Lpc_pins()        },
				{"pru"        , Beaglebone_pins() },
				{"linux"      , linux }, // XXX
			};
		}
	}

	/// <summary>
	/// Command translation
	/// </summary>
	public class PinResolver
	{
		private readonly string mcu_Type;
		private readonly bool validate_Aliases;
		private readonly Dictionary<string, int> pins;
		private readonly Dictionary<int, string> active_pins;

		static readonly Regex re_pin = new Regex(@"(?<prefix>[ _]pin=)(?<name>[^ ]*)",
			RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture);

		public PinResolver(string mcu_type, bool validate_aliases = true)
		{
			if (string.IsNullOrEmpty(mcu_type))
				throw new ArgumentException("mcu_type is empty", nameof(mcu_type));

			mcu_Type = mcu_type;
			validate_Aliases = validate_aliases;
			pins = McuPins.Pins[mcu_type];
			active_pins = new Dictionary<int, string>();
		}
		public void Update_aliases(string mapping_name)
		{
			if (mapping_name == "arduino")
				McuPins.Arduino.Update_map_arduino(pins, mcu_Type);
			else if (mapping_name == "beaglebone")
				McuPins.Beaglebone.update_map_beaglebone(pins, mcu_Type);
			else
				throw new ArgumentException($"Unknown pin alias mapping '{mapping_name}'", nameof(mapping_name));
		}
		public string Update_command(string cmd)
		{
			var m = re_pin.Match(cmd);
			if (!m.Success)
				return cmd;
			var name = m.Groups["name"].Value;
			if (!pins.ContainsKey(name))
				throw new ArgumentException($"Unable to translate pin name: {cmd}");
			var pin_id = pins[name];
			if (active_pins.ContainsKey(pin_id) && name != active_pins[pin_id] && validate_Aliases)
				throw new ArgumentException($"pin {name} is an alias for {active_pins[pin_id]}");

			return re_pin.Replace(cmd, m.Groups["prefix"].Value + pin_id);
		}
	}


	public interface IPinSetup
	{
		T setup_pin<T>(string pin_type, PinParams pin_params) where T : class;
	}
	public class PinParams
	{
		public IPinSetup chip;
		public string chip_name;
		public string pin;
		public string share_type;
		public bool invert;
		public bool pullup;
		internal StepperEnablePin classPin;
	}

	/// <summary>
	/// Pin to chip mapping
	/// </summary>
	public class PrinterPins
	{
		private Dictionary<string, IPinSetup> chips = new Dictionary<string, IPinSetup>();
		private Dictionary<string, PinParams> active_pins = new Dictionary<string, PinParams>();

		static string[] canInvertType = { "stepper", "endstop", "digital_out", "pwm" };
		static string[] can_pullupType = { "endstop" };


		public PinParams lookup_pin(string pin_desc, bool can_invert = false, bool can_pullup = false, string share_type = null)
		{
			PinParams pin_params;
			string pin;
			string chip_name;
			var desc = pin_desc.Trim();
			bool pullup = false;
			bool invert = false;
			if (can_pullup && desc.StartsWith("^"))
			{
				pullup = true;
				desc = desc.Substring(1).Trim();
			}
			if (can_invert && desc.StartsWith("!"))
			{
				invert = true;
				desc = desc.Substring(1).Trim();
			}
			if (!desc.Contains(":"))
			{
				chip_name = "mcu";
				pin = desc;
			}
			else
			{
				var _tup_1 = (from s in desc.Split(":", 1) select s.Trim()).ToList();
				chip_name = _tup_1[0];
				pin = _tup_1[1];
			}
			if (!chips.ContainsKey(chip_name))
			{
				throw new Exception($"Unknown pin chip name '{chip_name}'");
			}
			if ((from c in "^!: " where pin.Contains(c) select c).Count() > 0)
			{
				var format = "";
				if (can_pullup)
				{
					format += "[^] ";
				}
				if (can_invert)
				{
					format += "[!] ";
				}
				throw new Exception($"Invalid pin description '{pin_desc}'\n\"Format is: {format}[chip_name:] pin_name\"");
			}
			var share_name = $"{chip_name}:{pin}";
			if (this.active_pins.ContainsKey(share_name))
			{
				pin_params = this.active_pins[share_name];
				if (share_type == null || share_type != pin_params.share_type)
				{
					throw new Exception($"pin {pin} used multiple times in config");
				}
				if (invert != pin_params.invert || pullup != pin_params.pullup)
				{
					throw new Exception($"Shared pin {pin} must have same polarity");
				}
				return pin_params;
			}
			pin_params = new PinParams()
			{
				chip = this.chips[chip_name],
				chip_name = chip_name,
				pin = pin,
				share_type = share_type,
				invert = invert,
				pullup = pullup
			};
			this.active_pins[share_name] = pin_params;
			return pin_params;
		}

		public T setup_pin<T>(string pin_type, string pin_desc) where T : class
		{
			var can_invert = canInvertType.Contains(pin_type);
			var can_pullup = can_pullupType.Contains(pin_type);
			var pin_params = lookup_pin(pin_desc, can_invert, can_pullup);
			var result = pin_params.chip.setup_pin<T>(pin_type, pin_params);
			return result;
		}

		public void reset_pin_sharing(PinParams pin_params)
		{
			var share_name = $"{pin_params.chip_name}:{pin_params.pin}";
			active_pins.Remove(share_name);
		}

		public void register_chip(string chip_name, IPinSetup chip)
		{
			chip_name = chip_name.Trim();
			if (chips.ContainsKey(chip_name))
			{
				throw new Exception($"Duplicate chip name '{chip_name}'");
			}
			chips[chip_name] = chip;
		}
	}

}
