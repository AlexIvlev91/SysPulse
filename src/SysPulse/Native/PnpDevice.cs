using System.Runtime.InteropServices;
using System.Text;

namespace SysPulse.Native;

/// <summary>
/// Display adapters from the PnP manager: their names and device power state
/// (D0 = on, D3 = off). None of this touches the GPU itself, so on Optimus
/// laptops it lets us skip NVML polls while the NVIDIA GPU sleeps instead of
/// waking it up and burning battery.
/// </summary>
internal static class PnpDevice
{
    private const string DisplayClass = "{4d36e968-e325-11ce-bfc1-08002be10318}";
    private const uint CM_GETIDLIST_FILTER_PRESENT = 0x00000100;
    private const uint CM_GETIDLIST_FILTER_CLASS = 0x00000200;
    private const int CR_SUCCESS = 0;
    private const int PowerDeviceD0 = 1;

    private static DEVPROPKEY PowerDataKey = new() { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 32 };
    private static DEVPROPKEY DeviceDescKey = new() { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 2 };

    /// <summary>Device instance IDs of present display adapters, e.g. "PCI\VEN_10DE&amp;DEV_27BB...".</summary>
    public static List<string> DisplayAdapters()
    {
        uint flags = CM_GETIDLIST_FILTER_CLASS | CM_GETIDLIST_FILTER_PRESENT;
        if (CM_Get_Device_ID_List_SizeW(out uint len, DisplayClass, flags) != CR_SUCCESS || len == 0)
            return [];
        var buffer = new char[len];
        if (CM_Get_Device_ID_ListW(DisplayClass, buffer, len, flags) != CR_SUCCESS)
            return [];
        return [.. new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries)];
    }

    public static bool IsPciGpu(string instanceId) => instanceId.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase);

    public static bool IsNvidia(string instanceId) => instanceId.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase);

    public static string? Description(string instanceId)
    {
        if (CM_Locate_DevNodeW(out uint devInst, instanceId, 0) != CR_SUCCESS)
            return null;
        var data = new byte[512];
        uint size = (uint)data.Length;
        if (CM_Get_DevNode_PropertyW(devInst, ref DeviceDescKey, out _, data, ref size, 0) != CR_SUCCESS || size < 2)
            return null;
        return Encoding.Unicode.GetString(data, 0, (int)size).TrimEnd('\0');
    }

    /// <summary>True when the device is in D0, false when powered down, null when unknown.</summary>
    public static bool? IsPoweredOn(string instanceId)
    {
        if (CM_Locate_DevNodeW(out uint devInst, instanceId, 0) != CR_SUCCESS)
            return null;
        var data = new byte[64];
        uint size = (uint)data.Length;
        if (CM_Get_DevNode_PropertyW(devInst, ref PowerDataKey, out _, data, ref size, 0) != CR_SUCCESS || size < 8)
            return null;
        // CM_POWER_DATA: ULONG PD_Size; DEVICE_POWER_STATE PD_MostRecentPowerState; ...
        return BitConverter.ToInt32(data, 4) == PowerDeviceD0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_ID_List_SizeW(out uint length, string filter, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_ID_ListW(string filter, [Out] char[] buffer, uint bufferLength, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_DevNode_PropertyW(uint devInst, ref DEVPROPKEY key, out uint type, [Out] byte[] buffer, ref uint bufferSize, uint flags);
}
