using LGSTrayPrimitives;
using LGSTrayPrimitives.MessageStructs;
using MessagePipe;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using Newtonsoft.Json;
using System.Reflection;
using System.Text.RegularExpressions;

namespace LGSTrayCore.Mqtt;

/// <summary>
/// Publishes the battery state of all devices to an MQTT broker, with Home Assistant MQTT discovery.
///
/// Topics (node = configured name or computer name):
///   {topicPrefix}/{node}/status          online / offline (last will), retained
///   {topicPrefix}/{node}/{device}/state  JSON with battery, charging, voltage, last_update, retained
///
/// The battery entities have no availability topic on purpose: when the PC is off, Home Assistant keeps
/// showing the last known battery level. Whether the PC is reachable is a separate connectivity sensor.
/// </summary>
public partial class MqttPublisher : IHostedService, IDisposable
{
    private sealed class DeviceState
    {
        public string Name = "";
        public DeviceType Type;
        public bool HasBattery = true;
        public bool Announced;
        public bool VoltageAnnounced;
        public string? BatteryIcon;
        public UpdateMessage? Last;
    }

    [GeneratedRegex("[^a-zA-Z0-9_-]")]
    private static partial Regex InvalidChars();

    private static readonly string _version =
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

    private readonly MqttSettings _settings;
    private readonly ISubscriber<IPCMessage> _subscriber;
    private readonly Dictionary<string, DeviceState> _devices = [];
    private readonly object _lock = new();

    private readonly string _node;
    private readonly string _displayName;
    private readonly string _baseTopic;
    private readonly string _statusTopic;

    private IManagedMqttClient? _client;
    private IDisposable? _subscription;
    private bool _wasConnected;

    public MqttPublisher(IOptions<AppSettings> appSettings, ISubscriber<IPCMessage> subscriber)
    {
        _settings = appSettings.Value.MQTT;
        _subscriber = subscriber;

        _displayName = string.IsNullOrWhiteSpace(_settings.Name) ? Environment.MachineName : _settings.Name.Trim();
        _node = Sanitize(_displayName).ToLowerInvariant();
        _baseTopic = $"{_settings.TopicPrefix.TrimEnd('/')}/{_node}";
        _statusTopic = $"{_baseTopic}/status";
    }

    private static string Sanitize(string text) => InvalidChars().Replace(text, "_");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.Host))
        {
            AppLog.Write("MQTT is enabled but no host is configured - MQTT disabled");
            return;
        }

        try
        {
            var options = new MqttClientOptionsBuilder()
                .WithTcpServer(_settings.Host, _settings.Port)
                .WithClientId($"lgstray-{_node}")
                .WithWillTopic(_statusTopic)
                .WithWillPayload("offline")
                .WithWillRetain(true)
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(30));
            if (!string.IsNullOrEmpty(_settings.Username))
            {
                options = options.WithCredentials(_settings.Username, _settings.Password);
            }
            if (_settings.UseTls)
            {
                options = options.WithTlsOptions(o => o.UseTls());
            }

            var managedOptions = new ManagedMqttClientOptionsBuilder()
                .WithAutoReconnectDelay(TimeSpan.FromSeconds(15))
                .WithClientOptions(options.Build())
                .Build();

            _client = new MqttFactory().CreateManagedMqttClient();
            _client.ConnectedAsync += OnConnectedAsync;
            _client.DisconnectedAsync += OnDisconnectedAsync;
            _client.ConnectingFailedAsync += OnConnectingFailedAsync;

            _subscription = _subscriber.Subscribe(OnMessage);

            await _client.StartAsync(managedOptions);
        }
        catch (Exception ex)
        {
            AppLog.Write($"MQTT could not be started ({_settings.Host}:{_settings.Port})", ex);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        _subscription = null;

        if (_client == null)
        {
            return;
        }

        try
        {
            if (_client.IsConnected)
            {
                // Clean shutdown: the broker does not send the last will, so report offline ourselves.
                await _client.InternalClient.PublishAsync(Message(_statusTopic, "offline"), cancellationToken);
            }
            await _client.StopAsync();
        }
        catch (Exception ex)
        {
            AppLog.Write("MQTT stop failed", ex);
        }
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        _client?.Dispose();
        GC.SuppressFinalize(this);
    }

    // ------------------------------------------------------------------ connection events
    private async Task OnConnectedAsync(MqttClientConnectedEventArgs args)
    {
        if (!_wasConnected)
        {
            AppLog.Write($"MQTT connected to {_settings.Host}:{_settings.Port} as {_baseTopic}");
        }
        _wasConnected = true;
        _failureLogged = false;

        await Enqueue(_statusTopic, "online");
        await Enqueue(
            $"{_settings.DiscoveryPrefix}/binary_sensor/lgstray_{_node}/online/config",
            JsonConvert.SerializeObject(new Dictionary<string, object?>
            {
                // no "name": Home Assistant uses the translated name of the device class
                ["unique_id"] = $"lgstray_{_node}_online",
                ["default_entity_id"] = $"binary_sensor.lgstray_{_node}_online",
                ["state_topic"] = _statusTopic,
                ["payload_on"] = "online",
                ["payload_off"] = "offline",
                ["device_class"] = "connectivity",
                ["entity_category"] = "diagnostic",
                ["device"] = PcDevice(),
            })
        );

        List<string> ids;
        lock (_lock)
        {
            ids = [.. _devices.Keys];
            foreach (var d in _devices.Values)
            {
                d.Announced = false;
                d.VoltageAnnounced = false;
            }
        }
        foreach (var id in ids)
        {
            await PublishDevice(id);
        }
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        if (_wasConnected)
        {
            AppLog.Write($"MQTT disconnected: {args.Reason} {args.Exception?.Message}");
        }
        _wasConnected = false;
        return Task.CompletedTask;
    }

    private Task OnConnectingFailedAsync(ConnectingFailedEventArgs args)
    {
        // Logged once per outage, not on every retry.
        if (_wasConnected || !_failureLogged)
        {
            AppLog.Write($"MQTT connection to {_settings.Host}:{_settings.Port} failed: {args.Exception?.Message}");
            _failureLogged = true;
        }
        _wasConnected = false;
        return Task.CompletedTask;
    }

    private bool _failureLogged;

    // ------------------------------------------------------------------ device messages
    private void OnMessage(IPCMessage message)
    {
        string id = message.deviceId;
        lock (_lock)
        {
            if (!_devices.TryGetValue(id, out var d))
            {
                d = _devices[id] = new DeviceState { Name = id };
            }

            if (message is InitMessage init)
            {
                bool changed = d.Name != init.deviceName || d.Type != init.deviceType || d.HasBattery != init.hasBattery;
                d.Name = init.deviceName;
                d.Type = init.deviceType;
                d.HasBattery = init.hasBattery;
                if (changed)
                {
                    d.Announced = false;
                    d.VoltageAnnounced = false;
                }
            }
            else if (message is UpdateMessage update)
            {
                d.Last = update;
            }
        }

        _ = PublishDevice(id);
    }

    private async Task PublishDevice(string id)
    {
        if (_client == null || !_client.IsStarted)
        {
            return;
        }

        try
        {
            DeviceState d;
            UpdateMessage? last;
            bool announce, announceVoltage, announceBattery;
            string? icon;
            lock (_lock)
            {
                d = _devices[id];
                last = d.Last;
                if (!d.HasBattery || (d.Name == id && last == null))
                {
                    return;
                }
                announce = !d.Announced;
                announceVoltage = !d.VoltageAnnounced && last != null && last.batteryMVolt > 0;
                icon = ChargingIcon(last);
                announceBattery = announce || icon != d.BatteryIcon;
                d.Announced = true;
                d.VoltageAnnounced |= announceVoltage;
                d.BatteryIcon = icon;
            }

            string obj = $"lgstray_{_node}_{Sanitize(id)}";
            string stateTopic = $"{_baseTopic}/{Sanitize(id)}/state";

            if (announceBattery)
            {
                // While charging, the icon is sent with the discovery (like the companion app does);
                // otherwise Home Assistant shows its normal battery icon for the level.
                var battery = new Dictionary<string, object?>
                {
                    ["device_class"] = "battery",
                    ["unit_of_measurement"] = "%",
                    ["state_class"] = "measurement",
                    ["suggested_display_precision"] = 0,
                    ["value_template"] = "{{ value_json.battery }}",
                };
                if (icon != null)
                {
                    battery["icon"] = icon;
                }
                await Enqueue($"{_settings.DiscoveryPrefix}/sensor/{obj}/battery/config", Discovery(d, id, obj, stateTopic, "sensor", "battery", battery));
            }
            if (announce)
            {
                await Enqueue($"{_settings.DiscoveryPrefix}/binary_sensor/{obj}/charging/config", Discovery(d, id, obj, stateTopic, "binary_sensor", "charging",
                    new()
                    {
                        ["device_class"] = "battery_charging",
                        ["value_template"] = "{{ value_json.charging }}",
                        ["payload_on"] = "ON",
                        ["payload_off"] = "OFF",
                    }));
                await Enqueue($"{_settings.DiscoveryPrefix}/sensor/{obj}/last_update/config", Discovery(d, id, obj, stateTopic, "sensor", "last_update",
                    new()
                    {
                        ["name"] = "Last update",
                        ["device_class"] = "timestamp",
                        ["entity_category"] = "diagnostic",
                        ["value_template"] = "{{ value_json.last_update }}",
                    }));
            }
            if (announceVoltage)
            {
                await Enqueue($"{_settings.DiscoveryPrefix}/sensor/{obj}/voltage/config", Discovery(d, id, obj, stateTopic, "sensor", "voltage",
                    new()
                    {
                        ["device_class"] = "voltage",
                        ["unit_of_measurement"] = "V",
                        ["state_class"] = "measurement",
                        ["suggested_display_precision"] = 2,
                        ["entity_category"] = "diagnostic",
                        ["value_template"] = "{{ value_json.voltage }}",
                    }));
            }

            if (last != null && last.batteryPercentage >= 0)
            {
                var state = new Dictionary<string, object?>
                {
                    ["battery"] = (int)Math.Round(last.batteryPercentage),
                    ["charging"] = last.powerSupplyStatus == PowerSupplyStatus.POWER_SUPPLY_STATUS_CHARGING ? "ON" : "OFF",
                    ["status"] = last.powerSupplyStatus.ToString().Replace("POWER_SUPPLY_STATUS_", "").ToLowerInvariant(),
                    ["voltage"] = last.batteryMVolt > 0 ? Math.Round(last.batteryMVolt / 1000.0, 3) : null,
                    ["last_update"] = last.updateTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssK"),
                };
                await Enqueue(stateTopic, JsonConvert.SerializeObject(state));
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"MQTT publish for device {id} failed", ex);
        }
    }

    // ------------------------------------------------------------------ helpers
    private Dictionary<string, object?> PcDevice() => new()
    {
        ["identifiers"] = new[] { $"lgstray_{_node}" },
        ["name"] = $"LGSTray {_displayName}",
        ["manufacturer"] = "LGSTray",
        ["model"] = "Logitech battery monitor",
        ["sw_version"] = _version,
    };

    private string Discovery(DeviceState d, string id, string obj, string stateTopic, string domain, string key, Dictionary<string, object?> extra)
    {
        var config = new Dictionary<string, object?>
        {
            // no "name" (unless given in extra): Home Assistant uses the translated name of the device
            // class ("name": null would mean "this entity is the device" and show the device name)
            ["unique_id"] = $"{obj}_{key}",
            ["default_entity_id"] = $"{domain}.{obj}_{key}",
            ["state_topic"] = stateTopic,
            ["device"] = new Dictionary<string, object?>
            {
                ["identifiers"] = new[] { obj },
                ["name"] = d.Name,
                ["manufacturer"] = "Logitech",
                ["model"] = d.Type.ToString(),
                ["serial_number"] = id,
                ["via_device"] = $"lgstray_{_node}",
            },
        };
        foreach (var (k, v) in extra)
        {
            config[k] = v;
        }
        return JsonConvert.SerializeObject(config);
    }

    /// <summary>mdi:battery-charging-NN while charging, otherwise null (default battery icon).</summary>
    private static string? ChargingIcon(UpdateMessage? last)
    {
        if (last == null || last.batteryPercentage < 0
            || last.powerSupplyStatus != PowerSupplyStatus.POWER_SUPPLY_STATUS_CHARGING)
        {
            return null;
        }
        int level = (int)Math.Round(last.batteryPercentage) / 10 * 10;
        return level switch
        {
            >= 100 => "mdi:battery-charging-100",
            < 10 => "mdi:battery-charging-outline",
            _ => $"mdi:battery-charging-{level}",
        };
    }

    private static MqttApplicationMessage Message(string topic, string payload) =>
        new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithRetainFlag(true)
            .Build();

    private Task Enqueue(string topic, string payload) =>
        _client!.EnqueueAsync(Message(topic, payload));
}
