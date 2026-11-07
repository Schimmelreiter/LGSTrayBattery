# LGS Tray Battery

A rewrite/combination of my two programs [LGSTrayBattery](https://github.com/andyvorld/LGSTrayBattery) and [LGSTrayBattery_GHUB](https://github.com/andyvorld/LGSTrayBattery_GHUB), which should allow for interaction via both the native HID and Logitech GaminG Hub websockets.

> **Fork notice:** This is a fork of [andyvorld/LGSTrayBattery](https://github.com/andyvorld/LGSTrayBattery) by Schimmelreiter.
> It adds an MQTT publisher with Home Assistant discovery and fixes a startup crash. Many thanks to
> **andyvorld** for the original program - all credit for LGSTrayBattery goes to him.
> Releases of this fork: [Schimmelreiter/LGSTrayBattery/releases](https://github.com/Schimmelreiter/LGSTrayBattery/releases).
> Please report problems with the additions of this fork here, not upstream.

## Changes in this fork
### 3.0.6-schimmelreiter
- **Storage locations:** settings and logs are no longer kept in the program folder (see
  [Storage locations](#storage-locations-fork)). The selected tray devices are stored in the registry, so
  they are no longer lost after an update or when LGSTray is started via a differently spelled path.
- If no device is selected yet, the first device with a battery is selected automatically.
- **MQTT, this PC as a device:** last boot, CPU load (average over `pcInterval`), and on notebooks battery,
  charging, power plug and remaining battery time.
- **MQTT commands (optional, `commands = true`):** buttons in Home Assistant for shutdown, restart, cancel
  shutdown, hibernate, sleep and lock.

### 3.0.5-schimmelreiter
- MQTT: entities use the translated device class names of Home Assistant (e.g. "Battery", "Charging")
  instead of the device name; battery level without decimals.
- MQTT: while a device is charging, the battery entity gets a charging icon (`mdi:battery-charging-NN`), like
  the Home Assistant companion app. Together with `state_color: true` in a card, no template is needed.
- Native HID: a forced battery update was never reported (inverted condition) - fixed.
- `lgstray.log`: the HID helper's crashes, restarts and giving up, and devices that do not answer battery
  queries for more than 30 minutes.

### 3.0.4-schimmelreiter
- **MQTT / Home Assistant:** optional `[MQTT]` section in `appsettings.toml` (see below).
- **Startup crash fixed:** if a part of the program failed during startup, cleaning up the HTTP server
  threw a `NullReferenceException` in `HttpServer.Dispose` that hid the real error. Now the real error is
  written to `crashlog_<time>.log` and shown in a message box.
- A failing HTTP server (port in use, invalid address) no longer stops the program; the reason is logged.
- Errors that were silently ignored before are written to `lgstray.log` next to the program.
- A second instance shows a message instead of crashing.

## How to install

[![GitHub Release](https://img.shields.io/github/v/release/andyvorld/LGSTrayBattery?sort=semver)](https://github.com/andyvorld/LGSTrayBattery/releases/latest)


Please, visit [the latest release page](https://github.com/andyvorld/LGSTrayBattery/releases/latest) and download the release zip files from assets. Builds with the `-standalone` suffix are pre-bundled with .Net 8 and does not require any further dependencies, the non-standalone version requires .Net 8 to be pre-installed (https://dotnet.microsoft.com/en-us/download/dotnet/8.0).

## Changes from V2
*When migrating from earlier versions, device ids may have changed.*
- Moved to .Net 8
- Realtime reactive icons and rich tooltips
    - Light/Dark theme is now reactive in realtime
- Rewritten to use hidapi directly for hotplug support
    - Wired/Wireless devices like the G403 should behave like a single device
- Multi-device mode
- Numerical Icons
- HID.NET manager has been deprecated
- Migrated to using a `.toml` for appsettings

## Features
### Tray Indicator
![image](https://user-images.githubusercontent.com/24492062/138280300-6966b6a4-ff6d-46e6-9698-d2c8d612eb11.png)

Battery percentage and voltage (if supported) in a tray tooltip with notification icon.

Right-click for more options.

### Multiple Icons
![image](Assets/multi_icon.png)

Depending on the number of devices selected in the context menu, multiple devices can be seen simultatniously

### Numerical Icons
![image](Assets/numerical_icon.png)

Display the current battery percentage as a number.

*In numerical display mode, charging status will not be displayed*

### Reactive Icons
![image](https://user-images.githubusercontent.com/24492062/138284660-95949372-c59a-4569-9545-0cfe0506d1fb.png)

*Icon changes to match devices type (Current supported: mouse, keyboard and headsets)*

![image](https://user-images.githubusercontent.com/24492062/138285048-ad229703-5c4e-430e-b107-c50eb341e46b.png)

*Icon changes to match light/dark system theme*

![image](Assets/charging_icon.png)

*Icon changes to reflect current charging status*

### Http/Web "server" api
By default the running of the http server is enabled. The IP address and port used for bindings are under `addr` and `port` respectively with the defaults being `localhost` and `12321`.

`addr` accepts either a hostname (`DESKTOP-1234`) or an IP address (`127.0.0.1`) to bind to, if you are not sure use `localhost` or if you have admin permission `0.0.0.0` to bind to all interfaces.

IPv6 can also be disabled with `useIpv6` in the event that dual stack networking is causing issues.

If any issues arise from running the server, it can be turned off by the `enable` value in `appsettings.toml` under `[HTTPServer]`.

![image](Assets/server_index.png)

Visit `http://{addr}:{port}/` on your browser to view the list of devices available.

![image](https://user-images.githubusercontent.com/24492062/138281030-f40ba805-69bf-48ac-a126-6f58f9ca7828.png)

With the `deviceID`, a HTTP/GET request to `{addr}:{port}/device/{deviceID}`, will result in an xml document of the name and battery status of the device. Devices that do not support `battery_voltage` will report 0.00.

Device ids starting with `dev` originates from tapping into Logitech GHUB's own drivers, while random numbers are from the natively implement HID++ code. Thus, there are some fields that different between the two,

|                 | GHUB* | Native  |
|-----------------|-------|---------|
| device_id       | ✔️   | ✔️     |
| device_name     | ✔️   | ✔️     |
| device_type     | ✔️   | ✔️     |
| battery_percent | ✔️   | ✔️     |
| battery_voltage | ❌   | ✔️**   |
| mileage***      | ✔️   | ❌     |
| charging        | ✔️   | ✔️     |

\* - Requires Logitech G Hub Installed

\** - Depends on the device

\*** - Logitech G Hub's metric of estimated life left on the battery

### MQTT / Home Assistant (fork)
With `enabled = true` in the `[MQTT]` section, the battery state of every device is published to an MQTT
broker. Home Assistant creates the devices and entities automatically (MQTT discovery).

```
[MQTT]
enabled = true
host = "homeassistant.local"
port = 1883
useTls = false
username = "lgstray"
password = "..."
# Name of this PC in Home Assistant (empty = computer name)
name = ""
topicPrefix = "lgstray"
discoveryPrefix = "homeassistant"
```

Per device: battery level (%), charging (on/off), voltage (if the device reports it) and the time of the
last update. While charging, the battery entity shows a charging icon. Per PC: a connectivity sensor, which turns off via the MQTT last will when the PC or the
program stops. The battery values are retained and have no availability topic, so Home Assistant keeps
the last known level while the PC is off.

Topics: `{topicPrefix}/{name}/status` (`online`/`offline`) and `{topicPrefix}/{name}/{deviceId}/state`
(JSON: `battery`, `charging`, `status`, `voltage`, `last_update`).

Use a separate broker user for each PC. The password is stored in plain text in `appsettings.toml`.

#### This PC (`pcSensors`, default on)
The PC appears as device "LGSTray {name}" with: connectivity (last will), last boot (timestamp), CPU load in %
(average over `pcInterval` seconds, same counter as the Task Manager) and - only if Windows reports a system
battery - battery level (with charging icon), charging, power plug and remaining battery time (minutes, empty
while on AC power). Topic: `{topicPrefix}/{name}/pc/state`.

```
pcSensors = true
pcInterval = 30
```

#### Commands (`commands`, default off)
With `commands = true`, Home Assistant gets buttons for shutdown, restart, cancel shutdown, hibernate, sleep
and lock. Shutdown and restart show the Windows warning `shutdownDelay` seconds in advance and can be
cancelled with the button or `shutdown /a`. Commands arrive on `{topicPrefix}/{name}/command`
(`shutdown`, `restart`, `abort`, `hibernate`, `sleep`, `lock`); retained messages are ignored.

```
commands = true
shutdownDelay = 60
```

Note: with the default settings of the Mosquitto add-on every broker user may publish to every topic. If you
enable commands, consider an ACL that allows only Home Assistant to publish to `lgstray/+/command`.

## Storage locations (fork)
| What | Where |
|------|-------|
| Settings (`appsettings.toml`) | `%LOCALAPPDATA%\LGSTray\appsettings.toml` |
| Log (`lgstray.log`) and crash logs (`crashlog_*.log`) | `%LOCALAPPDATA%\LGSTray\` |
| Selected tray devices, numeric icon | `HKEY_CURRENT_USER\Software\LGSTray` (`SelectedDevices`, `NumericDisplay`) |
| Autostart | `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run` (unchanged) |

The program folder only contains the template `appsettings.default.toml`. On the first start of 3.0.6, an
existing `appsettings.toml` in the program folder (versions up to 3.0.5) is copied once to
`%LOCALAPPDATA%\LGSTray`, otherwise the template. After that, edit only the file in `%LOCALAPPDATA%\LGSTray`
(the old file in the program folder can be deleted). Updating is simple: extract the new zip over the program
folder - it no longer contains an `appsettings.toml`. The previous tray selection (.NET `user.config`) is taken over once as well.

## HID++ Device Sources
As of v3.0.0, there are 2 sources in which the program will pull battery status,

- Logitech G Hub via Websockets
- Native HID, hidapi via PInvoke (Called "Native" in settings)

These sources can be individually disabled/enabled before runtime via `appsettings.toml`, in the their respective sections,

```
[GHub]
enabled = true

[Native]
enabled = true
```

*GHub is Logitech G Hub, Native is hidapi*

## appsettings.toml
Refer to https://toml.io/en/ for a guide on toml syntax.

*Note the `"` around strings*

In the event of an invalid settings, at launch the app will prompt you for a reset to the default settings.


### `[Native]` settings
- `retryTime` - The time in seconds to re-try a device on a failed device state query; usually the device is asleep.
- `pollPeriod` - The time in seconds to request an update from the device, keep this as high as possible as it may interfere with the default power saving sleep modes of devices.
- `disabledDevices` - If all else fails, and the addition of a new device has caused the app to be unusable. Adding a part of the device name into this list will prevent the device from being polled. E.g. The following will disable the G403 and G502,
```
disabledDevices = [
    "G403",
    "G502"
]
```

## Known Issues
### Common
- Native HID and GHUB do not provide similar percentages, this is due to how native and GHUB calculates percentages from the device's voltages. Native uses an average curve of a 3.7V lipo battery, while GHUB will use a lookup table specific to the device.

### Native HID (hidapi)
- Certain wired devices like the G403 when in wired mode does not report the number of HID devices connected and will respond to all request. Causing battery polls to occur 6x per request.
- Device and protocol discovery changed from the previous method, some devices like the G533 headsets might not be detected, try the GHUB based manager.

### GHUB
- Future GHUB version may change IPC protocol/endpoints (currently websocket)

## Working with
- G403 Wireless
- MX Anywhere 2

### Community Tested
*HID Backend has changed, would need restesting of devices, please raise a PR to add to this list*


## How to Build project
TBA

## Acknowledgements
This project began as a task with me messing around with my mouse for battery tracking.

- [Solaar](https://github.com/pwr-Solaar/Solaar), for the source code to base the HID++ paramters and reverse engineering of the protocol.
- [XB1ControllerBatteryIndicator](https://github.com/NiyaShy/XB1ControllerBatteryIndicator), for the idea and base of the icons
- [The Noun Project](https://thenounproject.com/), for base icons
    - Mouse, By projecthayat, ID, In the Technology & computer hardware Collection
    - Keyboard, By HideMaru, ID, In the Electronic BL.2 Collection
    - Headphones, By Peter Lakenbrink, DE, In the School and Online Learning Glyph Collection
