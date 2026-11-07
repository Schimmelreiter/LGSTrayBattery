Fork of [andyvorld/LGSTrayBattery](https://github.com/andyvorld/LGSTrayBattery) - thanks to andyvorld for the original program.

- MQTT publisher with Home Assistant discovery (`[MQTT]` in `appsettings.toml`, disabled by default)
- Startup crash (`NullReferenceException` in `HttpServer.Dispose`) fixed; the real startup error is now logged and shown
- Failing HTTP server no longer stops the program; errors go to `lgstray.log`
- Second instance shows a message instead of crashing

**Updating:** keep your own `appsettings.toml` - the zip contains the default one. Add the `[MQTT]` section from the README to your file.

`-standalone` includes .NET 8; the other zip needs the .NET 8 Desktop Runtime.
