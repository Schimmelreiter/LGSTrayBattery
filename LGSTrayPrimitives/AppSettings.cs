namespace LGSTrayPrimitives;

public class AppSettings
{
    public UISettings UI { get; set; } = null!;

    public HttpServerSettings HTTPServer { get; set; } = null!;

    public IDeviceManagerSettings GHub { get; set; } = null!;

    public NativeDeviceManagerSettings Native { get; set; } = null!;

    public MqttSettings MQTT { get; set; } = new();
}

public class UISettings
{
    public bool EnableRichToolTips { get; set; }
}

public class HttpServerSettings
{
    public bool Enabled { get; set; }
    public int Port { get; set; }

    private string _addr = null!;
    public string Addr
    {
        get => _addr;
        set => _addr = (value == "0.0.0.0") ? "+" : value;
    }

    public bool UseIpv6 { get; set; }

    public string UrlPrefix => $"http://{Addr}:{Port}";
}

public class IDeviceManagerSettings
{
    public bool Enabled { get; set; }
}

public class NativeDeviceManagerSettings : IDeviceManagerSettings
{
    public int RetryTime { get; set; } = 10;
    public int PollPeriod { get; set; } = 600;

    public IEnumerable<string> DisabledDevices { get; set; } = [];
}

public class MqttSettings
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 1883;
    public bool UseTls { get; set; }
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    /// <summary>Name of this PC in Home Assistant, also used in the topics. Default: computer name.</summary>
    public string Name { get; set; } = "";

    public string TopicPrefix { get; set; } = "lgstray";
    public string DiscoveryPrefix { get; set; } = "homeassistant";

    /// <summary>Report this PC: last boot, CPU load and (notebooks) battery.</summary>
    public bool PcSensors { get; set; } = true;

    /// <summary>Seconds between two PC reports; the CPU load is the average over this time.</summary>
    public int PcInterval { get; set; } = 30;

    /// <summary>Accept commands (shutdown, restart, hibernate, sleep, lock) from Home Assistant.</summary>
    public bool Commands { get; set; }

    /// <summary>Seconds of warning before shutdown/restart (can be cancelled with "shutdown /a").</summary>
    public int ShutdownDelay { get; set; } = 60;
}
