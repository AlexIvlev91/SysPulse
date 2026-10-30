using SysPulse.Native;

namespace SysPulse.Monitoring;

/// <summary>
/// NVIDIA GPU through NVML; any other GPU falls back to the "GPU Engine"
/// performance counters (load only — Windows exposes no vendor-neutral temperature API).
/// On hybrid laptops the NVIDIA GPU powers off when idle. Reading its sensors
/// would wake it, so we check the PnP power state first and back off while it's idle.
/// </summary>
internal sealed class GpuMonitor : IDisposable
{
    private const int IdleSecondsBeforeRest = 20;
    private const int RestSeconds = 20;

    private readonly string? _nvidiaId;
    private readonly bool _hybrid;
    private readonly PdhCounter? _engines;
    private readonly string _name;

    private bool _nvmlReady;
    private IntPtr _device;
    private int _idleSeconds;
    private int _restSeconds;
    private GpuReading? _last;

    public GpuMonitor(PdhQuery query)
    {
        var adapters = PnpDevice.DisplayAdapters().Where(PnpDevice.IsPciGpu).ToList();
        _nvidiaId = adapters.FirstOrDefault(PnpDevice.IsNvidia);
        _hybrid = _nvidiaId is not null && adapters.Count > 1;

        string? primary = _nvidiaId ?? adapters.FirstOrDefault();
        _name = CleanName(primary is null ? "GPU" : PnpDevice.Description(primary) ?? "GPU");

        if (_nvidiaId is null)
            _engines = query.Add(@"\GPU Engine(*)\Utilization Percentage");
    }

    public string Name => _name;

    public GpuReading Read()
    {
        if (_nvidiaId is null)
            return ReadFromCounters();

        bool? on = PnpDevice.IsPoweredOn(_nvidiaId);
        if (on == false)
        {
            Release();
            _restSeconds = 0;
            return _last = new GpuReading(_name, true, true, 0, null, null, null, null, null);
        }

        // Resting: stop polling for a while so an idle GPU is allowed to power down.
        if (_restSeconds > 0)
        {
            _restSeconds--;
            return _last ?? new GpuReading(_name, true, false, null, null, null, null, null, null);
        }

        if (!_nvmlReady)
        {
            _nvmlReady = Nvml.TryInit();
            _device = _nvmlReady ? Nvml.FirstDevice() : IntPtr.Zero;
            if (_device == IntPtr.Zero)
            {
                Release();
                return new GpuReading(_name, true, false, null, null, null, null, null, null);
            }
        }

        var load = Nvml.Load(_device);
        var vram = Nvml.Vram(_device);
        uint? mw = Nvml.Power(_device);
        var reading = new GpuReading(
            _name, true, false,
            load?.Gpu,
            Nvml.Temperature(_device),
            vram is { } v1 ? (long)v1.Used : null,
            vram is { } v2 ? (long)v2.Total : null,
            mw is uint w ? w / 1000.0 : null,
            Nvml.GraphicsClock(_device) is uint c ? (int)c : null);

        if (_hybrid)
        {
            _idleSeconds = load is { Gpu: 0 } ? _idleSeconds + 1 : 0;
            if (_idleSeconds >= IdleSecondsBeforeRest)
            {
                _idleSeconds = 0;
                _restSeconds = RestSeconds;
                Release();
            }
        }
        return _last = reading;
    }

    private GpuReading ReadFromCounters()
    {
        double? usage = null;
        if (_engines is not null)
        {
            // Instance: "pid_1234_luid_0x0_0x1234_phys_0_eng_3_engtype_3D". A GPU's load is its
            // busiest engine, summed over processes — the same number Task Manager shows.
            var busiest = _engines.Values()
                .GroupBy(v => StripPid(v.Instance))
                .Select(g => g.Sum(v => v.Value))
                .DefaultIfEmpty(0)
                .Max();
            usage = Math.Clamp(busiest, 0, 100);
        }
        return new GpuReading(_name, _name != "GPU", false, usage, null, null, null, null, null);
    }

    private static string StripPid(string instance)
    {
        int at = instance.IndexOf("_luid_", StringComparison.Ordinal);
        return at >= 0 ? instance[at..] : instance;
    }

    private static string CleanName(string raw) =>
        raw.Replace("NVIDIA ", "").Replace(" Generation", "").Replace("(R)", "").Replace("(TM)", "").Trim();

    private void Release()
    {
        if (_nvmlReady)
            Nvml.Shutdown();
        _nvmlReady = false;
        _device = IntPtr.Zero;
    }

    public void Dispose() => Release();
}
