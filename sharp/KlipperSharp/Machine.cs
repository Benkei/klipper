using System;

namespace KlipperSharp
{
	public class Machine
	{
	}
}


//class Printer:
//    config_error = configfile.error
//    def __init__(self, input_fd, bglogger, start_args):
//        self.bglogger = bglogger
//        self.start_args = start_args
//        self.reactor = reactor.Reactor()
//        self.reactor.register_callback(self._connect)
//        self.state_message = message_startup
//        self.is_shutdown = False
//        self.run_result = None
//        self.event_handlers = {}
//        gc = gcode.GCodeParser(self, input_fd)
//        self.objects = collections.OrderedDict({'gcode': gc})
//    def get_start_args(self):
//        return self.start_args
//    def get_reactor(self):
//        return self.reactor
//    def get_state_message(self):
//        return self.state_message
//    def _set_state(self, msg):
//        if self.state_message in (message_ready, message_startup):
//            self.state_message = msg
//        if (msg != message_ready
//            and self.start_args.get('debuginput') is not None):
//            self.request_exit('error_exit')
//    def add_object(self, name, obj):
//        if obj in self.objects:
//            raise self.config_error(
//                "Printer object '%s' already created" % (name,))
//        self.objects[name] = obj
//    def lookup_object(self, name, default=configfile.sentinel):
//        if name in self.objects:
//            return self.objects[name]
//        if default is configfile.sentinel:
//            raise self.config_error("Unknown config object '%s'" % (name,))
//        return default
//    def lookup_objects(self, module=None):
//        if module is None:
//            return list(self.objects.items())
//        prefix = module + ' '
//        objs = [(n, self.objects[n])
//                for n in self.objects if n.startswith(prefix)]
//        if module in self.objects:
//            return [(module, self.objects[module])] + objs
//        return objs
//    def set_rollover_info(self, name, info, log=True):
//        if log:
//            logging.info(info)
//        if self.bglogger is not None:
//            self.bglogger.set_rollover_info(name, info)
//    def try_load_module(self, config, section):
//        if section in self.objects:
//            return self.objects[section]
//        module_parts = section.split()
//        module_name = module_parts[0]
//        py_name = os.path.join(os.path.dirname(__file__),
//                               'extras', module_name + '.py')
//        py_dirname = os.path.join(os.path.dirname(__file__),
//                                  'extras', module_name, '__init__.py')
//        if not os.path.exists(py_name) and not os.path.exists(py_dirname):
//            return None
//        mod = importlib.import_module('extras.' + module_name)
//        init_func = 'load_config'
//        if len(module_parts) > 1:
//            init_func = 'load_config_prefix'
//        init_func = getattr(mod, init_func, None)
//        if init_func is not None:
//            self.objects[section] = init_func(config.getsection(section))
//            return self.objects[section]
//    def _read_config(self):
//        self.objects['configfile'] = pconfig = configfile.PrinterConfig(self)
//        config = pconfig.read_main_config()
//        if self.bglogger is not None:
//            pconfig.log_config(config)
//        # Create printer components
//        for m in [pins, heater, mcu]:
//            m.add_printer_objects(config)
//        for section_config in config.get_prefix_sections(''):
//            self.try_load_module(config, section_config.get_name())
//        for m in [toolhead]:
//            m.add_printer_objects(config)
//        # Validate that there are no undefined parameters in the config file
//        pconfig.check_unused_options(config)
//    def _connect(self, eventtime):
//        try:
//            self._read_config()
//            for cb in self.event_handlers.get("klippy:connect", []):
//                if self.state_message is not message_startup:
//                    return
//                cb()
//        except (self.config_error, pins.error) as e:
//            logging.exception("Config error")
//            self._set_state("%s%s" % (str(e), message_restart))
//            return
//        except msgproto.error as e:
//            logging.exception("Protocol error")
//            self._set_state("%s%s" % (str(e), message_protocol_error))
//            return
//        except mcu.error as e:
//            logging.exception("MCU error during connect")
//            self._set_state("%s%s" % (str(e), message_mcu_connect_error))
//            return
//        except:
//            logging.exception("Unhandled exception during connect")
//            self._set_state("Internal error during connect.%s" % (
//                message_restart,))
//            return
//        try:
//            self._set_state(message_ready)
//            for cb in self.event_handlers.get("klippy:ready", []):
//                if self.state_message is not message_ready:
//                    return
//                cb()
//        except:
//            logging.exception("Unhandled exception during ready callback")
//            self.invoke_shutdown("Internal error during ready callback")
//    def run(self):
//        systime = time.time()
//        monotime = self.reactor.monotonic()
//        logging.info("Start printer at %s (%.1f %.1f)",
//                     time.asctime(time.localtime(systime)), systime, monotime)
//        # Enter main reactor loop
//        try:
//            self.reactor.run()
//        except:
//            logging.exception("Unhandled exception during run")
//            return "error_exit"
//        # Check restart flags
//        run_result = self.run_result
//        try:
//            if run_result == 'firmware_restart':
//                for n, m in self.lookup_objects(module='mcu'):
//                    m.microcontroller_restart()
//            self.send_event("klippy:disconnect")
//        except:
//            logging.exception("Unhandled exception during post run")
//        return run_result
//    def invoke_shutdown(self, msg):
//        if self.is_shutdown:
//            return
//        self.is_shutdown = True
//        self._set_state("%s%s" % (msg, message_shutdown))
//        for cb in self.event_handlers.get("klippy:shutdown", []):
//            try:
//                cb()
//            except:
//                logging.exception("Exception during shutdown handler")
//    def invoke_async_shutdown(self, msg):
//        self.reactor.register_async_callback(
//            (lambda e: self.invoke_shutdown(msg)))
//    def register_event_handler(self, event, callback):
//        self.event_handlers.setdefault(event, []).append(callback)
//    def send_event(self, event, *params):
//        return [cb(*params) for cb in self.event_handlers.get(event, [])]
//    def request_exit(self, result):
//        self.run_result = result
//        self.reactor.end()


