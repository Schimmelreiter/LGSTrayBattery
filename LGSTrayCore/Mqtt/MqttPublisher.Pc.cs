using LGSTrayPrimitives;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LGSTrayCore.Mqtt;

/// <summary>
/// The PC itself as a Home Assistant device: last boot, CPU load, battery (notebooks) and - if enabled -
/// buttons for shutdown, restart, hibernate, sleep, lock.
///
///   {topicPrefix}/{node}/pc/state   JSON: last_boot, cpu, battery, charging, plugged, remaining (retained)
///   {topicPrefix}/{node}/command    shutdown | restart | abort | hibernate | sleep | lock (not retained)
/// </summary>
public partial class MqttPublisher
{
    private static readonly (string Payload, string Key, string? Name, string? DeviceClass, string Icon)[] CommandButtons =
    [
        ("shutdown", "shutdown", "Shutdown", null, "mdi:power"),
        ("restart", "restart", null, "restart", "mdi:restart"),
        ("abort", "abort_shutdown", "Cancel shutdown", null, "mdi:cancel"),
        ("hibernate", "hibernate", "Hibernate", null, "mdi:power-sleep"),
        ("sleep", "sleep", "Sleep", null, "mdi:sleep"),
        ("lock", "lock", "Lock", null, "mdi:monitor-lock"),
    ];

    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();

    private CancellationTokenSource? _pcCts;
    private PcStatus? _pcStatus;
    private bool _pcBatteryAnnounced;
    private string? _pcBatteryIcon;

    private string PcStateTopic => $"{_baseTopic}/pc/state";
    private string CommandTopic => $"{_baseTopic}/command";
    private string PcObject => $"lgstray_{_node}";

    // ------------------------------------------------------------------ start / stop
    private async Task StartPcAsync()
    {
        if (!OperatingSystem.IsWindows() || _client == null)
        {
            return;
        }

        if (_settings.Commands)
        {
            _client.ApplicationMessageReceivedAsync += OnCommandAsync;
            await _client.SubscribeAsync(CommandTopic);
        }

        if (_settings.PcSensors)
        {
            _pcCts = new CancellationTokenSource();
            var token = _pcCts.Token;
            _ = Task.Run(() => PcLoopAsync(token), token);
        }
    }

    private void StopPc()
    {
        _pcCts?.Cancel();
        _pcStatus?.Dispose();
        _pcStatus = null;
    }

    // ------------------------------------------------------------------ discovery (called on every connect)
    private async Task AnnouncePcAsync()
    {
        _pcBatteryAnnounced = false;
        _pcBatteryIcon = null;

        string pcTopic(string domain, string key) => $"{_settings.DiscoveryPrefix}/{domain}/{PcObject}/{key}/config";

        if (_settings.PcSensors)
        {
            await Enqueue(pcTopic("sensor", "last_boot"), PcDiscovery("sensor", "last_boot", new()
            {
                ["name"] = "Last boot",
                ["device_class"] = "timestamp",
                ["value_template"] = "{{ value_json.last_boot }}",
            }));
            await Enqueue(pcTopic("sensor", "cpu"), PcDiscovery("sensor", "cpu", new()
            {
                ["name"] = "CPU load",
                ["unit_of_measurement"] = "%",
                ["state_class"] = "measurement",
                ["suggested_display_precision"] = 0,
                ["icon"] = "mdi:cpu-64-bit",
                ["value_template"] = "{{ value_json.cpu }}",
            }));
        }

        foreach (var b in CommandButtons)
        {
            if (!_settings.Commands)
            {
                // remove buttons announced earlier
                await Enqueue(pcTopic("button", b.Key), "");
                continue;
            }
            var config = new Dictionary<string, object?>
            {
                ["command_topic"] = CommandTopic,
                ["payload_press"] = b.Payload,
                ["icon"] = b.Icon,
            };
            if (b.Name != null) config["name"] = b.Name;
            if (b.DeviceClass != null) config["device_class"] = b.DeviceClass;
            await Enqueue(pcTopic("button", b.Key), PcDiscovery("button", b.Key, config, stateTopic: false));
        }
    }

    private string PcDiscovery(string domain, string key, Dictionary<string, object?> extra, bool stateTopic = true)
    {
        var config = new Dictionary<string, object?>
        {
            ["unique_id"] = $"{PcObject}_{key}",
            ["default_entity_id"] = $"{domain}.{PcObject}_{key}",
            ["device"] = PcDevice(),
        };
        if (stateTopic)
        {
            config["state_topic"] = PcStateTopic;
        }
        foreach (var (k, v) in extra)
        {
            config[k] = v;
        }
        return JsonConvert.SerializeObject(config);
    }

    // ------------------------------------------------------------------ sensors
    private async Task PcLoopAsync(CancellationToken token)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                _pcStatus = new PcStatus();
            }
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, _settings.PcInterval)));
            do
            {
                try
                {
                    await PublishPcAsync();
                }
                catch (Exception ex)
                {
                    AppLog.Write("PC state could not be published", ex);
                }
            }
            while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task PublishPcAsync()
    {
        if (_client == null || !_client.IsConnected || !OperatingSystem.IsWindows() || _pcStatus == null)
        {
            // CPU load is still sampled, so the next value is the average since the last one
            if (OperatingSystem.IsWindows()) _pcStatus?.CpuLoad();
            return;
        }

        double? cpu = _pcStatus.CpuLoad();
        var battery = PcStatus.ReadBattery();

        if (battery != null)
        {
            string? icon = battery.Charging ? ChargingIcon(battery.Percent) : null;
            if (!_pcBatteryAnnounced || icon != _pcBatteryIcon)
            {
                await AnnouncePcBatteryAsync(icon, announceAll: !_pcBatteryAnnounced);
                _pcBatteryAnnounced = true;
                _pcBatteryIcon = icon;
            }
        }

        var state = JsonConvert.SerializeObject(new Dictionary<string, object?>
        {
            ["last_boot"] = PcStatus.LastBoot().ToString("yyyy-MM-ddTHH:mm:ssK"),
            ["cpu"] = cpu.HasValue ? Math.Round(cpu.Value, 1) : null,
            ["battery"] = battery?.Percent,
            ["charging"] = battery == null ? null : battery.Charging ? "ON" : "OFF",
            ["plugged"] = battery == null ? null : battery.PluggedIn ? "ON" : "OFF",
            ["remaining"] = battery?.RemainingMinutes,
        });
        await Enqueue(PcStateTopic, state);
    }

    private async Task AnnouncePcBatteryAsync(string? icon, bool announceAll)
    {
        string pcTopic(string domain, string key) => $"{_settings.DiscoveryPrefix}/{domain}/{PcObject}/{key}/config";

        var battery = new Dictionary<string, object?>
        {
            ["device_class"] = "battery",
            ["unit_of_measurement"] = "%",
            ["state_class"] = "measurement",
            ["suggested_display_precision"] = 0,
            ["value_template"] = "{{ value_json.battery }}",
        };
        if (icon != null) battery["icon"] = icon;
        await Enqueue(pcTopic("sensor", "battery"), PcDiscovery("sensor", "battery", battery));

        if (!announceAll)
        {
            return;
        }
        await Enqueue(pcTopic("binary_sensor", "charging"), PcDiscovery("binary_sensor", "charging", new()
        {
            ["device_class"] = "battery_charging",
            ["value_template"] = "{{ value_json.charging }}",
            ["payload_on"] = "ON",
            ["payload_off"] = "OFF",
        }));
        await Enqueue(pcTopic("binary_sensor", "plugged"), PcDiscovery("binary_sensor", "plugged", new()
        {
            ["device_class"] = "plug",
            ["value_template"] = "{{ value_json.plugged }}",
            ["payload_on"] = "ON",
            ["payload_off"] = "OFF",
        }));
        await Enqueue(pcTopic("sensor", "remaining"), PcDiscovery("sensor", "remaining", new()
        {
            ["name"] = "Battery time remaining",
            ["device_class"] = "duration",
            ["unit_of_measurement"] = "min",
            ["icon"] = "mdi:timer-sand",
            ["value_template"] = "{{ value_json.remaining }}",
        }));
    }

    // ------------------------------------------------------------------ commands
    private Task OnCommandAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var msg = e.ApplicationMessage;
        if (msg.Topic != CommandTopic)
        {
            return Task.CompletedTask;
        }
        if (msg.Retain)
        {
            // a retained command would be executed again on every start
            AppLog.Write($"MQTT command ignored (retained): {msg.ConvertPayloadToString()}");
            return Task.CompletedTask;
        }

        string command = (msg.ConvertPayloadToString() ?? "").Trim().ToLowerInvariant();
        AppLog.Write($"MQTT command received: {command}");
        try
        {
            int delay = Math.Max(0, _settings.ShutdownDelay);
            switch (command)
            {
                case "shutdown":
                    RunShutdown($"/s /t {delay} /c \"Home Assistant: shutdown in {delay} s (cancel: shutdown /a)\"");
                    break;
                case "restart":
                    RunShutdown($"/r /t {delay} /c \"Home Assistant: restart in {delay} s (cancel: shutdown /a)\"");
                    break;
                case "abort":
                    RunShutdown("/a");
                    break;
                case "hibernate":
                    RunShutdown("/h");
                    break;
                case "sleep":
                    if (OperatingSystem.IsWindows()) SetSuspendState(false, false, false);
                    break;
                case "lock":
                    if (OperatingSystem.IsWindows()) LockWorkStation();
                    break;
                default:
                    AppLog.Write($"MQTT command unknown: {command}");
                    break;
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"MQTT command {command} failed", ex);
        }
        return Task.CompletedTask;
    }

    private static void RunShutdown(string arguments)
    {
        using var p = Process.Start(new ProcessStartInfo("shutdown.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        });
    }

    private static string ChargingIcon(int percent)
    {
        int level = percent / 10 * 10;
        return level switch
        {
            >= 100 => "mdi:battery-charging-100",
            < 10 => "mdi:battery-charging-outline",
            _ => $"mdi:battery-charging-{level}",
        };
    }
}
