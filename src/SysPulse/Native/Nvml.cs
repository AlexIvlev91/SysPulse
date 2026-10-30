using System.Runtime.InteropServices;
using System.Text;

namespace SysPulse.Native;

/// <summary>
/// NVIDIA Management Library (nvml.dll ships with the NVIDIA driver in System32).
/// Gives temperature, load, VRAM, power and clocks without administrator rights.
/// </summary>
internal static class Nvml
{
    public const int Success = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct Utilization
    {
        public uint Gpu;
        public uint Memory;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Memory
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }

    public static bool TryInit()
    {
        try
        {
            return nvmlInit_v2() == Success;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return false; // no NVIDIA driver
        }
    }

    public static void Shutdown()
    {
        try { nvmlShutdown(); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    public static IntPtr FirstDevice()
    {
        if (nvmlDeviceGetCount_v2(out uint count) != Success || count == 0)
            return IntPtr.Zero;
        return nvmlDeviceGetHandleByIndex_v2(0, out IntPtr device) == Success ? device : IntPtr.Zero;
    }

    public static string? Name(IntPtr device)
    {
        var buffer = new byte[96];
        if (nvmlDeviceGetName(device, buffer, (uint)buffer.Length) != Success)
            return null;
        int len = Array.IndexOf(buffer, (byte)0);
        return Encoding.UTF8.GetString(buffer, 0, len < 0 ? buffer.Length : len);
    }

    public static uint? Temperature(IntPtr device) =>
        nvmlDeviceGetTemperature(device, 0 /* NVML_TEMPERATURE_GPU */, out uint t) == Success ? t : null;

    public static Utilization? Load(IntPtr device) =>
        nvmlDeviceGetUtilizationRates(device, out var u) == Success ? u : null;

    public static Memory? Vram(IntPtr device) =>
        nvmlDeviceGetMemoryInfo(device, out var m) == Success ? m : null;

    /// <summary>Board power in milliwatts.</summary>
    public static uint? Power(IntPtr device) =>
        nvmlDeviceGetPowerUsage(device, out uint mw) == Success ? mw : null;

    /// <summary>Graphics clock in MHz.</summary>
    public static uint? GraphicsClock(IntPtr device) =>
        nvmlDeviceGetClockInfo(device, 0 /* NVML_CLOCK_GRAPHICS */, out uint mhz) == Success ? mhz : null;

    [DllImport("nvml.dll")] private static extern int nvmlInit_v2();
    [DllImport("nvml.dll")] private static extern int nvmlShutdown();
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetCount_v2(out uint count);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetName(IntPtr device, byte[] name, uint length);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetTemperature(IntPtr device, int sensorType, out uint temp);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out Utilization utilization);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out Memory memory);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint milliwatts);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetClockInfo(IntPtr device, int clockType, out uint mhz);
}
