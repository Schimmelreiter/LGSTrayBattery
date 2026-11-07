Fork of [andyvorld/LGSTrayBattery](https://github.com/andyvorld/LGSTrayBattery) - thanks to andyvorld for the original program.

**3.0.5**
- MQTT: entity names from Home Assistant's device classes (translated, e.g. "Akku", "Wird geladen") instead of the device name; battery level without decimals
- MQTT: charging icon on the battery entity while charging - no template needed in cards (`state_color: true` gives the colour)
- Native HID: forced battery update was never reported (inverted condition) - fixed
- `lgstray.log`: HID helper crashes/restarts/giving up, devices not answering battery queries for more than 30 min

**3.0.4**
- MQTT publisher with Home Assistant discovery (`[MQTT]` in `appsettings.toml`, disabled by default)
- Startup crash (`NullReferenceException` in `HttpServer.Dispose`) fixed; the real startup error is now logged and shown
- Failing HTTP server no longer stops the program; errors go to `lgstray.log`
- Second instance shows a message instead of crashing

**Updating:** keep your own `appsettings.toml` - the zip contains the default one.

`-standalone` includes .NET 8; the other zip needs the .NET 8 Desktop Runtime.
