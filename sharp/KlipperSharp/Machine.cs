using KlipperSharp.MachineCodes;
using KlipperSharp.MicroController;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KlipperSharp
{
	public class Machine
	{
		public const string message_ready = "Printer is ready";
		public const string message_startup =
@"Printer is not ready
The klippy host software is attempting to connect.Please
retry in a few moments.
";
		public const string message_restart =
@"Once the underlying issue is corrected, use the ""RESTART""
command to reload the config and restart the host software.
Printer is halted
";
		public const string message_protocol_error =
@"This type of error is frequently caused by running an older
version of the firmware on the micro-controller (fix by
recompiling and flashing the firmware).
Once the underlying issue is corrected, use the ""RESTART""
command to reload the config and restart the host software.
Protocol error connecting to printer
";
		public const string message_mcu_connect_error =
@"Once the underlying issue is corrected, use the
""FIRMWARE_RESTART"" command to reset the firmware, reload the
config, and restart the host software.
Error configuring printer
";
		public const string message_shutdown =
@"Once the underlying issue is corrected, use the
""FIRMWARE_RESTART"" command to reset the firmware, reload the
config, and restart the host software.
Printer is shutdown
";
		private static readonly Logger logging = LogManager.GetCurrentClassLogger();

		private QueueListener bglogger;
		private Dictionary<string, object> start_args;
		private SelectReactor reactor;
		private string state_message;
		private bool is_shutdown;
		private string run_result;
		private Dictionary<string, List<Delegate>> event_handlers = new Dictionary<string, List<Delegate>>();
		private Dictionary<string, object> objects;


		//public object config_error = configfile.error;

		public Machine(Stream input_fd, QueueListener bglogger, Dictionary<string, object> start_args)
		{
			this.bglogger = bglogger;
			this.start_args = start_args;
			this.reactor = new SelectReactor();
			this.reactor.register_callback(this._connect);
			this.state_message = message_startup;
			this.is_shutdown = false;
			this.run_result = null;

			var gc = new GCodeParser(this, input_fd);
			this.objects = new Dictionary<string, object> { { "gcode", gc } };
		}

		public Dictionary<string, object> get_start_args()
		{
			return this.start_args;
		}

		public SelectReactor get_reactor()
		{
			return this.reactor;
		}

		public string get_state_message()
		{
			return this.state_message;
		}

		void _set_state(string msg)
		{
			if (this.state_message == message_ready || this.state_message == message_startup)
			{
				this.state_message = msg;
			}
			if (msg != message_ready && this.start_args.ContainsKey("debuginput"))
			{
				this.request_exit("error_exit");
			}
		}

		public void add_object(string name, object obj)
		{
			if (objects.ContainsValue(obj))
			{
				throw new Exception(string.Format("Printer object '{0}' already created", name));
			}
			objects[name] = obj;
		}

		public T lookup_object<T>(string name) where T : class
		{
			if (this.objects.ContainsKey(name))
			{
				return this.objects[name] as T;
			}
			throw new Exception(string.Format("Unknown config object '{0}'", name));
		}

		public T lookup_object<T>(string name, T @default = null) where T : class
		{
			if (this.objects.ContainsKey(name))
			{
				return this.objects[name] as T;
			}
			return @default;
		}

		public List<(string name, T modul)> lookup_objects<T>(string module = null) where T : class
		{
			if (module == null)
			{
				return (from n in objects
						  where n.Value is T
						  select (n.Key, n.Value as T)).ToList();
			}
			var prefix = module + " ";
			var objs = (from n in objects
							where n.Value is T && n.Key.StartsWith(prefix)
							select (n.Key, n.Value as T)).ToList();
			if (objects.ContainsKey(module))
			{
				objs.Insert(0, (module, objects[module] as T));
			}
			return objs;
		}

		public void set_rollover_info(string name, string info, bool log = true)
		{
			if (log)
			{
				logging.Info(info);
			}
			if (bglogger != null)
			{
				bglogger.set_rollover_info(name, info);
			}
		}

		public object try_load_module(ConfigWrapper config, string section)
		{
			if (objects.ContainsKey(section))
			{
				return objects[section];
			}
			var module_parts = section.Split();
			var module_name = module_parts[0];
			//var py_name = os.path.join(os.path.dirname(@__file__), "extras", module_name + ".py");
			//var py_dirname = os.path.join(os.path.dirname(@__file__), "extras", module_name, "__init__.py");
			//if (!os.path.exists(py_name) && !os.path.exists(py_dirname))
			//{
			//	return null;
			//}
			//var mod = importlib.import_module("extras." + module_name);
			//var init_func = "load_config";
			//if (module_parts.Length > 1)
			//{
			//	init_func = "load_config_prefix";
			//}
			//init_func = getattr(mod, init_func, null);
			//if (init_func != null)
			//{
			//	this.objects[section] = init_func(config.getsection(section));
			//	return this.objects[section];
			//}
			return null;
		}

		void _read_config()
		{
			MachineConfig pconfig;
			objects["configfile"] = pconfig = new MachineConfig(this);
			var config = pconfig.read_main_config();
			if (bglogger != null)
			{
				pconfig.log_config(config);
			}
			// Create printer components
			add_printer_objects_pins(config);
			add_printer_objects_heater(config);
			add_printer_objects_mcu(config);
			//foreach (var m in new List<object> { pins, heater, mcu })
			//{
			//	m.add_printer_objects(config);
			//}
			foreach (var section_config in config.get_prefix_sections(""))
			{
				this.try_load_module(config, section_config.get_name());
			}
			add_printer_objects_toolhead(config);
			//foreach (var m in new List<object> { toolhead })
			//{
			//	m.add_printer_objects(config);
			//}
			// Validate that there are no undefined parameters in the config file
			//pconfig.check_unused_options(config);
		}

		private void add_printer_objects_pins(ConfigWrapper config)
		{
			config.get_printer().add_object("pins", new PrinterPins());
		}
		private void add_printer_objects_heater(ConfigWrapper config)
		{
			config.get_printer().add_object("heater", new PrinterHeaters(config));
		}
		public void add_printer_objects_mcu(ConfigWrapper config)
		{
			var printer = config.get_printer();
			var reactor = printer.get_reactor();
			var mainsync = new ClockSync(reactor);
			printer.add_object("mcu", new Mcu(config.getsection("mcu"), mainsync));
			foreach (var s in config.get_prefix_sections("mcu "))
			{
				printer.add_object(s.get_name(), new Mcu(s, new SecondarySync(reactor, mainsync)));
			}
		}
		// Main code to track events (and their timing) on the printer toolhead
		public void add_printer_objects_toolhead(ConfigWrapper config)
		{
			config.get_printer().add_object("toolhead", new ToolHead(config));
			PrinterExtruder.add_printer_objects(config);
		}


		double _connect(double eventtime)
		{
			try
			{
				this._read_config();
				foreach (var cb in this.event_handlers.Get("klippy:connect"))
				{
					if (this.state_message != message_startup)
					{
						return SelectReactor.NEVER;
					}
					cb.DynamicInvoke();
				}
			}
			catch (Exception ex)
			{
				logging.Error("Config error");
				this._set_state(String.Format("{0}{1}", ex, message_restart));
				return SelectReactor.NEVER;
			}
			//catch (Exception ex)
			//{
			//	logging.Error("Protocol error");
			//	this._set_state(String.Format("%s%s", str(e), message_protocol_error));
			//	return;
			//}
			//catch (Exception ex)
			//{
			//	logging.Error("MCU error during connect");
			//	this._set_state(String.Format("%s%s", str(e), message_mcu_connect_error));
			//	return;
			//}
			//catch (Exception ex)
			//{
			//	logging.Error("Unhandled exception during connect");
			//	this._set_state(String.Format("Internal error during connect.%s", message_restart));
			//	return;
			//}
			try
			{
				this._set_state(message_ready);
				foreach (var cb in this.event_handlers.Get("klippy:ready"))
				{
					if (this.state_message != message_ready)
					{
						return SelectReactor.NEVER;
					}
					cb.DynamicInvoke();
				}
			}
			catch
			{
				logging.Error("Unhandled exception during ready callback");
				this.invoke_shutdown("Internal error during ready callback");
			}
			return SelectReactor.NEVER;
		}

		public string run()
		{
			var systime = DateTime.UtcNow;
			var monotime = reactor.monotonic();
			logging.Info("Start printer at {0} ({1})", systime, monotime);
			// Enter main reactor loop
			try
			{
				this.reactor.run();
			}
			catch
			{
				logging.Error("Unhandled exception during run");
				return "error_exit";
			}
			var run_result = this.run_result;
			try
			{
				if (run_result == "firmware_restart")
				{
					foreach (var item in this.lookup_objects<Mcu>("mcu"))
					{
						item.modul.microcontroller_restart();
					}
				}
				this.send_event("klippy:disconnect");
			}
			catch
			{
				logging.Error("Unhandled exception during post run");
			}
			return run_result;
		}

		public void invoke_shutdown(string msg)
		{
			if (is_shutdown)
			{
				return;
			}
			is_shutdown = true;
			_set_state(string.Format("%s%s", msg, message_shutdown));
			foreach (var cb in event_handlers.Get("klippy:shutdown"))
			{
				try
				{
					cb.DynamicInvoke();
				}
				catch
				{
					logging.Error("Exception during shutdown handler");
				}
			}
		}

		public void invoke_async_shutdown(string msg)
		{
			reactor.register_async_callback((e) => { invoke_shutdown(msg); return 0; });
		}

		public void register_event_handler(string @event, Action callback)
		{
			register_event_handler(@event, (Delegate)callback);
		}
		public void register_event_handler(string @event, Delegate callback)
		{
			List<Delegate> callbacks;
			if (!event_handlers.TryGetValue(@event, out callbacks))
			{
				callbacks = new List<Delegate>(1);
				event_handlers.Add(@event, callbacks);
			}
			callbacks.Add(callback);
		}

		public List<object> send_event(string @event, params object[] parameters)
		{
			var list = event_handlers.Get(@event);
			return (from cb in list select cb.DynamicInvoke(parameters)).ToList();
		}

		public void request_exit(string result)
		{
			run_result = result;
			reactor.end();
		}

	}
}
