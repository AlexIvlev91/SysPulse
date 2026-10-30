using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;
using SysPulse.Native;

namespace SysPulse.Monitoring;

/// <summary>
/// CPU load and effective clock from performance counters (no rights needed),
/// plus package temperature and power from LibreHardwareMonitor when this
/// process is elevated and the PawnIO driver is installed — Windows offers no
/// other way to read a modern CPU's temperature sensor.
/// </summary>
internal sealed class CpuMonitor : IDisposable
{
    private readonly PdhCounter? _utility;
    private readonly PdhCounter? _time;
    private readonly PdhCounter? _performance;
    private readonly string _name;
    private readonly double _baseGhz;

    private Computer? _computer;
    private IHardware? _cpu;
    private CpuTempStatus _tempStatus;
    private int _nullTempReads;

    public CpuMonitor(PdhQuery query)
    {
        // "% Processor Utility" is what Task Manager shows; it accounts for turbo and throttling.
        _utility = query.Add(@"\Processor Information(_Total)\% Processor Utility");
        _time = _utility is null ? query.Add(@"\Processor Information(_Total)\% Processor Time") : null;
        _performance = query.Add(@"\Processor Information(_Total)\% Processor Performance");

        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        _name = CleanName(key?.GetValue("ProcessorNameString") as string ?? "CPU");
        // "~MHz" in the registry is a boot-time measurement; the power API has the rated clock.
        _baseGhz = (PowerApi.BaseClockMhz() ?? (key?.GetValue("~MHz") is int mhz ? mhz : 0)) / 1000.0;

        _tempStatus = (Elevation.IsAdministrator, PawnIoInstalled()) switch
        {
            (false, false) => CpuTempStatus.NeedsAdminAndPawnIo,
            (false, true) => CpuTempStatus.NeedsAdmin,
            (true, false) => CpuTempStatus.NeedsPawnIo,
            _ => CpuTempStatus.Available,
        };
        if (_tempStatus == CpuTempStatus.Available)
            OpenSensors();
    }

    public string Name => _name;

    public string? SensorError { get; private set; }

    public static bool PawnIoInstalled()
    {
        using var service = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
        if (service is not null)
            return true;
        using var uninstall = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
        return uninstall is not null;
    }

    private void OpenSensors()
    {
        try
        {
            _computer = new Computer { IsCpuEnabled = true };
            _computer.Open();
            _cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
            if (_cpu is null)
            {
                _tempStatus = CpuTempStatus.Failed;
                SensorError = "No CPU sensors were found.";
            }
        }
        catch (Exception ex)
        {
            _tempStatus = CpuTempStatus.Failed;
            SensorError = ex.Message;
            _computer = null;
        }
    }

    public CpuReading Read()
    {
        double? usage = (_utility ?? _time)?.Value() is double u ? Math.Clamp(u, 0, 100) : null;
        double? clock = _performance?.Value() is double perf && _baseGhz > 0 ? _baseGhz * perf / 100.0 : null;

        double? temp = null, power = null;
        if (_cpu is not null)
        {
            try
            {
                _cpu.Update();
                temp = Sensor(SensorType.Temperature, "CPU Package", "Core Max", "Core (Tctl/Tdie)", "Core Average");
                power = Sensor(SensorType.Power, "CPU Package", "Package");
            }
            catch (Exception ex)
            {
                SensorError = ex.Message;
            }
            // A sensor that never produces a value means the driver refused us.
            if (temp is null && ++_nullTempReads == 5)
            {
                _tempStatus = CpuTempStatus.Failed;
                SensorError ??= "The PawnIO driver did not return CPU temperature data.";
            }
            else if (temp is not null)
            {
                _nullTempReads = 0;
                _tempStatus = CpuTempStatus.Available;
            }
        }

        return new CpuReading(_name, usage, clock, _baseGhz, temp, power, _tempStatus);
    }

    private double? Sensor(SensorType type, params string[] names)
    {
        foreach (var name in names)
        {
            foreach (var s in _cpu!.Sensors)
            {
                if (s.SensorType == type && s.Name == name && s.Value is float v && v > 0)
                    return v;
            }
        }
        return null;
    }

    private static string CleanName(string raw)
    {
        string s = raw.Replace("(R)", "").Replace("(TM)", "").Replace("(tm)", "").Replace(" CPU", "");
        int at = s.IndexOf(" @ ", StringComparison.Ordinal);
        if (at > 0)
            s = s[..at];
        s = s.Replace("Intel Core", "Core").Replace("13th Gen ", "").Replace("12th Gen ", "").Replace("14th Gen ", "");
        return string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public void Dispose()
    {
        try { _computer?.Close(); }
        catch (Exception) { }
    }
}
