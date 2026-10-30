using System.Runtime.InteropServices;

namespace SysPulse.Native;

/// <summary>
/// Thin wrapper over the Performance Data Helper API. Counter paths are given
/// in English (PdhAddEnglishCounter) so they work on any Windows display language.
/// Rate counters need two collections before they return a value.
/// </summary>
internal sealed class PdhQuery : IDisposable
{
    private IntPtr _query;

    public PdhQuery()
    {
        if (PdhOpenQueryW(null, IntPtr.Zero, out _query) != 0)
            _query = IntPtr.Zero;
    }

    /// <summary>Adds a counter, or returns null when this PC doesn't have it.</summary>
    public PdhCounter? Add(string englishPath)
    {
        if (_query == IntPtr.Zero)
            return null;
        return PdhAddEnglishCounterW(_query, englishPath, IntPtr.Zero, out var counter) == 0
            ? new PdhCounter(counter)
            : null;
    }

    public void Collect()
    {
        if (_query != IntPtr.Zero)
            PdhCollectQueryData(_query);
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero)
        {
            PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(IntPtr query, string fullCounterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);
}

internal sealed class PdhCounter(IntPtr handle)
{
    private const uint PDH_FMT_DOUBLE = 0x00000200;
    private const uint PDH_FMT_NOCAP100 = 0x00008000;
    private const uint PDH_MORE_DATA = 0x800007D2;

    /// <summary>The current value, or null until the counter has enough samples.</summary>
    public double? Value()
    {
        if (PdhGetFormattedCounterValue(handle, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, IntPtr.Zero, out var v) != 0 || !IsValid(v.CStatus))
            return null;
        return v.DoubleValue;
    }

    /// <summary>Every instance of a wildcard counter, e.g. "\GPU Engine(*)\Utilization Percentage".</summary>
    public List<(string Instance, double Value)> Values()
    {
        var result = new List<(string, double)>();
        uint size = 0;
        uint status = PdhGetFormattedCounterArrayW(handle, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out uint count, IntPtr.Zero);
        if (status != PDH_MORE_DATA || size == 0)
            return result;

        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArrayW(handle, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out count, buffer) != 0)
                return result;
            int itemSize = Marshal.SizeOf<PDH_FMT_COUNTERVALUE_ITEM>();
            for (int i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE_ITEM>(buffer + i * itemSize);
                if (IsValid(item.Value.CStatus))
                    result.Add((Marshal.PtrToStringUni(item.Name) ?? "", item.Value.DoubleValue));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return result;
    }

    private static bool IsValid(uint cstatus) => cstatus is 0 or 1; // PDH_CSTATUS_VALID_DATA / NEW_DATA

    [StructLayout(LayoutKind.Explicit)]
    private struct PDH_FMT_COUNTERVALUE
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double DoubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_FMT_COUNTERVALUE_ITEM
    {
        public IntPtr Name;
        public PDH_FMT_COUNTERVALUE Value;
    }

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, IntPtr type, out PDH_FMT_COUNTERVALUE value);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr itemBuffer);
}
