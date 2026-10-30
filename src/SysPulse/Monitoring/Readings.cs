namespace SysPulse.Monitoring;

internal sealed record CpuReading(
    string Name,
    double? Usage,
    double? ClockGhz,
    double BaseGhz,
    double? TempC,
    double? PowerW,
    CpuTempStatus TempStatus);

internal enum CpuTempStatus
{
    Available,
    NeedsAdmin,
    NeedsPawnIo,
    NeedsAdminAndPawnIo,
    Failed,
}

internal sealed record GpuReading(
    string Name,
    bool Present,
    bool Sleeping,
    double? Usage,
    double? TempC,
    long? VramUsed,
    long? VramTotal,
    double? PowerW,
    int? ClockMhz);

internal sealed record MemoryReading(long Total, long Used, int LoadPercent, long CommitUsed, long CommitLimit);

internal sealed record DiskReading(string Drive, long Free, long Total, double? ActivePercent, double? ReadBps, double? WriteBps);

internal sealed record NetworkReading(double DownBps, double UpBps);

internal enum PowerMode { BestEfficiency, Balanced, BestPerformance }

internal sealed record PowerReading(bool OnBattery, int? BatteryPercent, bool Charging, string? PlanName, PowerMode? Mode);

internal sealed record TurboReading(bool Supported, uint Ac, uint Dc, bool OnBattery)
{
    /// <summary>Boost mode index that applies right now (AC when plugged in, DC on battery).</summary>
    public uint Current => OnBattery ? Dc : Ac;

    public bool Enabled => Supported && Current != 0;

    public static string ModeName(uint mode) => mode switch
    {
        0 => "Disabled",
        1 => "Enabled",
        2 => "Aggressive",
        3 => "Efficient enabled",
        4 => "Efficient aggressive",
        5 => "Aggressive at guaranteed",
        6 => "Efficient aggressive at guaranteed",
        _ => $"Mode {mode}",
    };
}

internal sealed record Snapshot(
    DateTime Time,
    CpuReading Cpu,
    GpuReading Gpu,
    MemoryReading Memory,
    DiskReading Disk,
    NetworkReading Network,
    PowerReading Power,
    TurboReading Turbo,
    TimeSpan Uptime);
