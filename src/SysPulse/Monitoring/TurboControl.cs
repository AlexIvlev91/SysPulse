using SysPulse.Native;

namespace SysPulse.Monitoring;

/// <summary>
/// CPU turbo boost through the power plan's "Processor performance boost mode"
/// (PERFBOOSTMODE) — the API behind
/// <c>powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 0</c>.
/// Turning turbo off remembers the previous mode so turning it on restores it.
/// </summary>
internal static class TurboControl
{
    private const uint DefaultOnMode = 2; // Aggressive — the Windows default for Balanced

    public static TurboReading Read(bool onBattery)
    {
        if (PowerApi.ActiveScheme() is not Guid scheme ||
            PowerApi.ReadValue(scheme, PowerApi.SubProcessor, PowerApi.PerfBoostMode) is not { } values)
        {
            return new TurboReading(false, 0, 0, onBattery);
        }
        return new TurboReading(true, values.Ac, values.Dc, onBattery);
    }

    /// <summary>Returns null on success, or an error message.</summary>
    public static string? Set(bool enabled)
    {
        if (PowerApi.ActiveScheme() is not Guid scheme)
            return "Couldn't read the active power plan.";
        var current = PowerApi.ReadValue(scheme, PowerApi.SubProcessor, PowerApi.PerfBoostMode);

        uint ac, dc;
        if (enabled)
        {
            ac = AppSettings.GetInt("turbo.ac") is int a and > 0 ? (uint)a : DefaultOnMode;
            dc = AppSettings.GetInt("turbo.dc") is int d and > 0 ? (uint)d : DefaultOnMode;
        }
        else
        {
            if (current is { } cur)
            {
                if (cur.Ac > 0) AppSettings.Set("turbo.ac", (int)cur.Ac);
                if (cur.Dc > 0) AppSettings.Set("turbo.dc", (int)cur.Dc);
            }
            ac = dc = 0;
        }
        return Write(scheme, ac, dc);
    }

    /// <summary>
    /// Applies one boost mode plugged in and on battery; 0 turns turbo off. The mode
    /// is also what turning turbo back on restores. Returns null on success, or an error message.
    /// </summary>
    public static string? SetLevel(uint mode)
    {
        if (mode == 0)
            return Set(false);
        if (PowerApi.ActiveScheme() is not Guid scheme)
            return "Couldn't read the active power plan.";
        AppSettings.SetMany(("turbo.ac", mode.ToString()), ("turbo.dc", mode.ToString()));
        return Write(scheme, mode, mode);
    }

    private static string? Write(Guid scheme, uint ac, uint dc)
    {
        uint err = PowerApi.WriteValue(scheme, PowerApi.SubProcessor, PowerApi.PerfBoostMode, ac, dc);
        return err == 0 ? null : $"Windows refused the change (error {err}).";
    }

    public static PowerMode? ReadPowerMode()
    {
        if (PowerApi.EffectiveOverlay() is not Guid g)
            return null;
        if (g == PowerApi.OverlayBestPerformance)
            return PowerMode.BestPerformance;
        if (g == PowerApi.OverlayBestEfficiency)
            return PowerMode.BestEfficiency;
        return PowerMode.Balanced;
    }

    public static bool SetPowerMode(PowerMode mode) => PowerApi.SetOverlay(mode switch
    {
        PowerMode.BestEfficiency => PowerApi.OverlayBestEfficiency,
        PowerMode.BestPerformance => PowerApi.OverlayBestPerformance,
        _ => PowerApi.OverlayBalanced,
    });
}
