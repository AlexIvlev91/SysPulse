using System.Runtime.InteropServices;
using System.Text;

namespace SysPulse.Native;

internal static class Win32
{
    // ---- memory ------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    public static MEMORYSTATUSEX? MemoryStatus()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref m) ? m : null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    // ---- processes ---------------------------------------------------------
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_MEMORY_COUNTERS_EX2
    {
        public uint cb;
        public uint PageFaultCount;
        public UIntPtr PeakWorkingSetSize;
        public UIntPtr WorkingSetSize;
        public UIntPtr QuotaPeakPagedPoolUsage;
        public UIntPtr QuotaPagedPoolUsage;
        public UIntPtr QuotaPeakNonPagedPoolUsage;
        public UIntPtr QuotaNonPagedPoolUsage;
        public UIntPtr PagefileUsage;
        public UIntPtr PeakPagefileUsage;
        public UIntPtr PrivateUsage;
        public UIntPtr PrivateWorkingSetSize;
        public ulong SharedCommitUsage;
    }

    private static readonly uint MemCountersEx2Size = (uint)Marshal.SizeOf<PROCESS_MEMORY_COUNTERS_EX2>();
    private static readonly uint MemCountersExSize = MemCountersEx2Size - (uint)(IntPtr.Size + 8);
    private static bool _ex2Supported = true;

    /// <summary>
    /// Private working set — the "Memory" column of Task Manager. Falls back to
    /// the full working set on Windows builds without PROCESS_MEMORY_COUNTERS_EX2.
    /// </summary>
    public static long PrivateWorkingSet(IntPtr process)
    {
        var c = new PROCESS_MEMORY_COUNTERS_EX2 { cb = _ex2Supported ? MemCountersEx2Size : MemCountersExSize };
        if (!GetProcessMemoryInfo(process, ref c, c.cb))
        {
            if (!_ex2Supported)
                return 0;
            _ex2Supported = false;
            c.cb = MemCountersExSize;
            if (!GetProcessMemoryInfo(process, ref c, c.cb))
                return 0;
        }
        return (long)(_ex2Supported ? c.PrivateWorkingSetSize : c.WorkingSetSize);
    }

    public static string? ImagePath(IntPtr process)
    {
        var sb = new StringBuilder(1024);
        int size = sb.Capacity;
        return QueryFullProcessImageNameW(process, 0, sb, ref size) ? sb.ToString(0, size) : null;
    }

    /// <summary>Kernel + user CPU time in 100 ns units, and the creation time (to detect PID reuse).</summary>
    public static (long CpuTime, long Created)? Times(IntPtr process)
    {
        if (!GetProcessTimes(process, out long created, out _, out long kernel, out long user))
            return null;
        return (kernel + user, created);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll")]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder exeName, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool GetProcessMemoryInfo(IntPtr process, ref PROCESS_MEMORY_COUNTERS_EX2 counters, uint size);

    // ---- windows -----------------------------------------------------------
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const uint GW_OWNER = 4;
    private const int DWMWA_CLOAKED = 14;
    public const uint WM_CLOSE = 0x0010;

    /// <summary>Visible, unowned, titled, uncloaked top-level windows — what the taskbar shows — by process ID.</summary>
    public static Dictionary<int, List<IntPtr>> TaskbarWindows()
    {
        var result = new Dictionary<int, List<IntPtr>>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || GetWindow(hwnd, GW_OWNER) != IntPtr.Zero || GetWindowTextLengthW(hwnd) == 0)
                return true;
            if ((GetWindowLongW(hwnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0)
                return true;
            if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (!result.TryGetValue((int)pid, out var list))
                result[(int)pid] = list = [];
            list.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowLongW(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Dark title bar on Windows 10 20H1+ / 11.</summary>
    public static void UseDarkTitleBar(IntPtr hwnd)
    {
        int on = 1;
        if (DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
    }

    // ---- input -------------------------------------------------------------
    public const int VK_CONTROL = 0x11;

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int key);

    public static bool IsKeyDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_NOREPEAT = 0x4000;
    public const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    public const int WM_NCLBUTTONDOWN = 0x00A1;
    public const int HTCAPTION = 2;

    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessageW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    // ---- layered windows ---------------------------------------------------
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_TOOLWINDOW_STYLE = WS_EX_TOOLWINDOW;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_TOPMOST = 0x00000008;

    public static void SetExStyle(IntPtr hwnd, int flag, bool on)
    {
        int style = GetWindowLongW(hwnd, GWL_EXSTYLE);
        int updated = on ? style | flag : style & ~flag;
        if (updated != style)
            SetWindowLongW(hwnd, GWL_EXSTYLE, updated);
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowLongW(IntPtr hwnd, int index, int value);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int Cx, Cy; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    /// <summary>Puts a 32-bit ARGB bitmap on a layered window with per-pixel alpha, faded by <paramref name="opacity"/>.</summary>
    public static void UpdateLayered(IntPtr hwnd, Bitmap bitmap, Point location, byte opacity = 255)
    {
        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr hBitmap = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
            old = SelectObject(memDc, hBitmap);
            var size = new SIZE { Cx = bitmap.Width, Cy = bitmap.Height };
            var src = new POINT();
            var dst = new POINT { X = location.X, Y = location.Y };
            var blend = new BLENDFUNCTION { BlendOp = 0 /* AC_SRC_OVER */, SourceConstantAlpha = opacity, AlphaFormat = 1 /* AC_SRC_ALPHA */ };
            UpdateLayeredWindow(hwnd, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, 2 /* ULW_ALPHA */);
        }
        finally
        {
            if (hBitmap != IntPtr.Zero)
            {
                SelectObject(memDc, old);
                DeleteObject(hBitmap);
            }
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr icon);
}
