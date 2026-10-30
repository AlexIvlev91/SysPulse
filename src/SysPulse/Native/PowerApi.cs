using System.Runtime.InteropServices;

namespace SysPulse.Native;

/// <summary>
/// Power scheme settings — the same store <c>powercfg /setacvalueindex</c> writes.
/// Changing settings of the active scheme does not need administrator rights.
/// </summary>
internal static class PowerApi
{
    public static readonly Guid SubProcessor = new("54533251-82be-4824-96c1-47b60b740d00");
    public static readonly Guid PerfBoostMode = new("be337238-0d82-4146-a960-4f3749d470c7");

    // Windows 11 Settings > Power mode ("overlay" schemes on top of the plan).
    public static readonly Guid OverlayBestEfficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    public static readonly Guid OverlayBalanced = Guid.Empty;
    public static readonly Guid OverlayBestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");

    public static Guid? ActiveScheme()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out IntPtr ptr) != 0)
            return null;
        try
        {
            return Marshal.PtrToStructure<Guid>(ptr);
        }
        finally
        {
            LocalFree(ptr);
        }
    }

    public static string? SchemeName(Guid scheme)
    {
        uint size = 0;
        if (PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size) != 0 || size == 0)
            return null;
        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            return PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size) == 0
                ? Marshal.PtrToStringUni(buffer)
                : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static (uint Ac, uint Dc)? ReadValue(Guid scheme, Guid subgroup, Guid setting)
    {
        if (PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out uint ac) != 0)
            return null;
        if (PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out uint dc) != 0)
            return null;
        return (ac, dc);
    }

    /// <summary>Writes AC and DC values and re-applies the scheme so the change takes effect now.</summary>
    public static uint WriteValue(Guid scheme, Guid subgroup, Guid setting, uint ac, uint dc)
    {
        uint err = PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, ac);
        if (err == 0)
            err = PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, dc);
        if (err == 0)
            err = PowerSetActiveScheme(IntPtr.Zero, ref scheme);
        return err;
    }

    public static Guid? EffectiveOverlay()
    {
        try
        {
            return PowerGetEffectiveOverlayScheme(out Guid g) == 0 ? g : null;
        }
        catch (EntryPointNotFoundException)
        {
            return null; // Windows 10 before 1709
        }
    }

    public static bool SetOverlay(Guid overlay)
    {
        try
        {
            return PowerSetActiveOverlayScheme(overlay) == 0;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>The CPU's rated (base) clock in MHz — what Task Manager calls "Base speed".</summary>
    public static int? BaseClockMhz()
    {
        int count = Environment.ProcessorCount;
        var info = new PROCESSOR_POWER_INFORMATION[count];
        int size = Marshal.SizeOf<PROCESSOR_POWER_INFORMATION>() * count;
        if (CallNtPowerInformation(11 /* ProcessorInformation */, IntPtr.Zero, 0, info, size) != 0)
            return null;
        int max = info.Max(i => (int)i.MaxMhz);
        return max > 0 ? max : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESSOR_POWER_INFORMATION
    {
        public uint Number;
        public uint MaxMhz;
        public uint CurrentMhz;
        public uint MhzLimit;
        public uint MaxIdleState;
        public uint CurrentIdleState;
    }

    [DllImport("powrprof.dll")]
    private static extern uint CallNtPowerInformation(int level, IntPtr inputBuffer, int inputLength, [Out] PROCESSOR_POWER_INFORMATION[] outputBuffer, int outputLength);

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadFriendlyName(IntPtr rootPowerKey, ref Guid schemeGuid, IntPtr subGroupGuid, IntPtr powerSettingGuid, IntPtr buffer, ref uint bufferSize);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid powerSettingGuid, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid powerSettingGuid, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid powerSettingGuid, uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid powerSettingGuid, uint value);

    // Undocumented but stable since Windows 10 1709; it's what Settings > Power mode calls.
    [DllImport("powrprof.dll")]
    private static extern uint PowerGetEffectiveOverlayScheme(out Guid effectiveOverlayGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveOverlayScheme(Guid overlaySchemeGuid);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr mem);
}
