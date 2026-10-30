using System.Diagnostics;
using System.Net.NetworkInformation;
using SysPulse.Native;

namespace SysPulse.Monitoring;

/// <summary>Fixed-size ring of the most recent samples, oldest first when copied out.</summary>
internal sealed class History(int capacity)
{
    private readonly float[] _values = new float[capacity];
    private int _count, _next;

    public void Add(float? value)
    {
        _values[_next] = value ?? float.NaN;
        _next = (_next + 1) % _values.Length;
        _count = Math.Min(_count + 1, _values.Length);
    }

    public float[] ToArray()
    {
        var result = new float[_count];
        int start = (_next - _count + _values.Length) % _values.Length;
        for (int i = 0; i < _count; i++)
            result[i] = _values[(start + i) % _values.Length];
        return result;
    }
}

internal sealed record MonitorHistory(float[] Cpu, float[] Gpu, float[] Memory, float[] CpuTemp, float[] GpuTemp);

/// <summary>
/// Samples every sensor once a second on a background thread and raises
/// <see cref="Updated"/> there; subscribers marshal to the UI themselves.
/// The process scan is heavier and only runs while someone is looking at it.
/// </summary>
internal sealed class SystemMonitor : IDisposable
{
    public const int HistoryLength = 90;

    private readonly Thread _thread;
    private readonly AutoResetEvent _wake = new(false);
    private volatile bool _stop;
    private volatile bool _scanProcesses;
    private volatile bool _scanNow;

    private readonly History _cpuHistory = new(HistoryLength);
    private readonly History _gpuHistory = new(HistoryLength);
    private readonly History _memHistory = new(HistoryLength);
    private readonly History _cpuTempHistory = new(HistoryLength);
    private readonly History _gpuTempHistory = new(HistoryLength);

    private long _lastNetDown, _lastNetUp, _lastNetTicks;

    public SystemMonitor()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "SysPulse monitor" };
        _thread.SetApartmentState(ApartmentState.STA); // icon/version lookups use the shell
        _thread.Start();
    }

    /// <summary>Raised on the monitor thread after each sample.</summary>
    public event Action<Snapshot, MonitorHistory>? Updated;

    /// <summary>Raised on the monitor thread with fresh close-to-free-up suggestions.</summary>
    public event Action<List<AppGroup>>? ProcessesUpdated;

    public string? CpuSensorError { get; private set; }

    public bool ScanProcesses
    {
        get => _scanProcesses;
        set
        {
            if (value && !_scanProcesses)
                _scanNow = true;
            _scanProcesses = value;
            if (value)
                _wake.Set();
        }
    }

    /// <summary>Sample right away (after the user changed something), optionally rescanning processes too.</summary>
    public void RefreshNow(bool processes = false)
    {
        if (processes)
            _scanNow = true;
        _wake.Set();
    }

    private void Run()
    {
        using var query = new PdhQuery();
        using var cpu = new CpuMonitor(query);
        using var gpu = new GpuMonitor(query);
        var diskIdle = query.Add(@"\PhysicalDisk(_Total)\% Idle Time");
        var diskRead = query.Add(@"\PhysicalDisk(_Total)\Disk Read Bytes/sec");
        var diskWrite = query.Add(@"\PhysicalDisk(_Total)\Disk Write Bytes/sec");
        var advisor = new ProcessAdvisor();
        string systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";

        query.Collect();
        ReadNetwork(); // prime the byte counters
        int tick = 0;
        _wake.WaitOne(1000);

        while (!_stop)
        {
            try
            {
                query.Collect();
                var power = SystemInformation.PowerStatus;
                bool onBattery = power.PowerLineStatus == PowerLineStatus.Offline;

                var cpuReading = cpu.Read();
                CpuSensorError = cpu.SensorError;
                var gpuReading = gpu.Read();
                var memory = ReadMemory();
                var snapshot = new Snapshot(
                    DateTime.Now,
                    cpuReading,
                    gpuReading,
                    memory,
                    ReadDisk(systemDrive, diskIdle, diskRead, diskWrite),
                    ReadNetwork(),
                    ReadPower(power, onBattery),
                    TurboControl.Read(onBattery),
                    TimeSpan.FromMilliseconds(Environment.TickCount64));

                _cpuHistory.Add((float?)cpuReading.Usage);
                _gpuHistory.Add(gpuReading.Sleeping ? 0 : (float?)gpuReading.Usage);
                _memHistory.Add(memory.Total > 0 ? memory.Used * 100f / memory.Total : null);
                _cpuTempHistory.Add((float?)cpuReading.TempC);
                _gpuTempHistory.Add((float?)gpuReading.TempC);

                Updated?.Invoke(snapshot, new MonitorHistory(
                    _cpuHistory.ToArray(), _gpuHistory.ToArray(), _memHistory.ToArray(),
                    _cpuTempHistory.ToArray(), _gpuTempHistory.ToArray()));

                if (_scanProcesses && (_scanNow || tick % 2 == 0))
                {
                    _scanNow = false;
                    ProcessesUpdated?.Invoke(advisor.Scan());
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Debug.WriteLine(ex); // a bad sample must never stop monitoring
            }
            tick++;
            _wake.WaitOne(1000);
        }
    }

    private static MemoryReading ReadMemory()
    {
        if (Win32.MemoryStatus() is not { } m)
            return new MemoryReading(0, 0, 0, 0, 0);
        return new MemoryReading(
            (long)m.ullTotalPhys,
            (long)(m.ullTotalPhys - m.ullAvailPhys),
            (int)m.dwMemoryLoad,
            (long)(m.ullTotalPageFile - m.ullAvailPageFile),
            (long)m.ullTotalPageFile);
    }

    private static DiskReading ReadDisk(string drive, PdhCounter? idle, PdhCounter? read, PdhCounter? write)
    {
        long free = 0, total = 0;
        try
        {
            var info = new DriveInfo(drive);
            free = info.AvailableFreeSpace;
            total = info.TotalSize;
        }
        catch (IOException)
        {
        }
        double? active = idle?.Value() is double i ? Math.Clamp(100 - i, 0, 100) : null;
        return new DiskReading(drive.TrimEnd('\\'), free, total, active, read?.Value(), write?.Value());
    }

    private NetworkReading ReadNetwork()
    {
        long down = 0, up = 0;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;
                var stats = nic.GetIPStatistics();
                down += stats.BytesReceived;
                up += stats.BytesSent;
            }
        }
        catch (NetworkInformationException)
        {
        }

        long now = Stopwatch.GetTimestamp();
        double seconds = _lastNetTicks == 0 ? 0 : (now - _lastNetTicks) / (double)Stopwatch.Frequency;
        var reading = seconds > 0
            ? new NetworkReading(Math.Max(0, (down - _lastNetDown) / seconds), Math.Max(0, (up - _lastNetUp) / seconds))
            : new NetworkReading(0, 0);
        (_lastNetDown, _lastNetUp, _lastNetTicks) = (down, up, now);
        return reading;
    }

    private static PowerReading ReadPower(PowerStatus status, bool onBattery)
    {
        bool hasBattery = (status.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) == 0 &&
                          status.BatteryChargeStatus != BatteryChargeStatus.Unknown;
        int? percent = hasBattery ? (int)Math.Round(status.BatteryLifePercent * 100) : null;
        bool charging = (status.BatteryChargeStatus & BatteryChargeStatus.Charging) != 0;
        string? plan = PowerApi.ActiveScheme() is Guid g ? PowerApi.SchemeName(g) : null;
        return new PowerReading(onBattery, percent, charging, plan, TurboControl.ReadPowerMode());
    }

    public void Dispose()
    {
        _stop = true;
        _wake.Set();
        _thread.Join(3000);
        _wake.Dispose();
    }
}
