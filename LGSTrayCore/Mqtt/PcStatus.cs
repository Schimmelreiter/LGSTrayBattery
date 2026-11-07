using LGSTrayPrimitives;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LGSTrayCore.Mqtt;

/// <summary>Reads the state of this PC: last boot, CPU load, battery (Windows power status).</summary>
[SupportedOSPlatform("windows")]
public sealed class PcStatus : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint Low;
        public uint High;
        public readonly ulong Value => ((ulong)High << 32) | Low;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    public sealed record Battery(int Percent, bool Charging, bool PluggedIn, int? RemainingMinutes);

    private PerformanceCounter? _cpu;
    private ulong _lastIdle, _lastTotal;

    public PcStatus()
    {
        // "% Processor Utility" is the value the Task Manager shows; older systems only have "% Processor Time".
        foreach (var (category, counter) in new[] { ("Processor Information", "% Processor Utility"), ("Processor", "% Processor Time") })
        {
            try
            {
                _cpu = new PerformanceCounter(category, counter, "_Total", true);
                _cpu.NextValue(); // the first value is always 0
                break;
            }
            catch (Exception ex)
            {
                _cpu?.Dispose();
                _cpu = null;
                AppLog.Write($"CPU counter {category}\\{counter} not available: {ex.Message}");
            }
        }
        if (_cpu == null)
        {
            CpuFromSystemTimes(); // prime the fallback
        }
    }

    /// <summary>Boot time, rounded to the minute (GetTickCount64 includes sleep and hibernation).</summary>
    public static DateTimeOffset LastBoot()
    {
        var boot = DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
        return new DateTimeOffset(boot.Year, boot.Month, boot.Day, boot.Hour, boot.Minute, 0, TimeSpan.Zero);
    }

    /// <summary>Average CPU load in % since the previous call.</summary>
    public double? CpuLoad()
    {
        try
        {
            if (_cpu != null)
            {
                return Math.Clamp(_cpu.NextValue(), 0, 100);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("CPU counter failed, using GetSystemTimes", ex);
            _cpu?.Dispose();
            _cpu = null;
            CpuFromSystemTimes();
            return null;
        }
        return CpuFromSystemTimes();
    }

    private double? CpuFromSystemTimes()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return null;
        }
        ulong total = kernel.Value + user.Value; // kernel time includes idle time
        ulong dTotal = total - _lastTotal, dIdle = idle.Value - _lastIdle;
        bool first = _lastTotal == 0;
        _lastTotal = total;
        _lastIdle = idle.Value;
        if (first || dTotal == 0)
        {
            return null;
        }
        return Math.Clamp(100.0 * (dTotal - dIdle) / dTotal, 0, 100);
    }

    /// <summary>Battery of this PC, null if there is no system battery (desktop PC).</summary>
    public static Battery? ReadBattery()
    {
        if (!GetSystemPowerStatus(out var s))
        {
            return null;
        }
        // BatteryFlag 128 = no system battery, 255 = unknown; percent 255 = unknown
        if (s.BatteryFlag == 128 || s.BatteryFlag == 255 || s.BatteryLifePercent > 100)
        {
            return null;
        }
        return new Battery(
            s.BatteryLifePercent,
            (s.BatteryFlag & 8) != 0,
            s.ACLineStatus == 1,
            s.BatteryLifeTime >= 0 ? (int)Math.Round(s.BatteryLifeTime / 60.0) : null
        );
    }

    public void Dispose()
    {
        _cpu?.Dispose();
    }
}
