using System.Diagnostics;
using SysPulse.Native;

namespace SysPulse.Monitoring;

internal enum AppKind
{
    /// <summary>Has a window on the taskbar — close it normally so it can ask to save.</summary>
    OpenApp,
    /// <summary>No window (tray apps, updaters, helpers) — safe to end; they return at next sign-in.</summary>
    Background,
}

/// <summary>All processes of one program (e.g. every chrome.exe), summed.</summary>
internal sealed class AppGroup
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public required string ExeName { get; init; }
    public required AppKind Kind { get; init; }
    public required List<int> Pids { get; init; }
    public required List<IntPtr> Windows { get; init; }
    public long Memory { get; init; }
    public double Cpu { get; init; }
}

/// <summary>
/// Finds programs worth closing to free memory or CPU. Windows components,
/// services and anything the desktop needs are never suggested.
/// </summary>
internal sealed class ProcessAdvisor
{
    private const long MinMemory = 40L * 1024 * 1024;
    private const double MinCpu = 0.5;

    // Shell and system pieces that live outside services/System32 checks or would break the desktop.
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "dwm", "csrss", "winlogon", "wininit", "services", "lsass", "smss", "svchost",
        "fontdrvhost", "sihost", "ctfmon", "taskhostw", "runtimebroker", "searchhost", "searchapp",
        "searchindexer", "startmenuexperiencehost", "shellexperiencehost", "shellhost", "textinputhost",
        "applicationframehost", "securityhealthsystray", "securityhealthservice", "msmpeng", "nissrv",
        "mpdefendercoreservice", "audiodg", "conhost", "dllhost", "lockapp", "msedgewebview2",
        "widgetservice", "smartscreen", "userinit", "wudfhost", "dashost", "spoolsv", "wmiprvse",
        "unsecapp", "lsaiso", "memory compression", "registry", "system", "secure system", "idle",
        "nvcontainer", "nvdisplay.container", "nvsphelper64", "igfxem", "igfxhk", "igfxcuiservice",
        "rtkauduservice64", "wlanext", "aggregatorhost", "dataexchangehost", "useroobebroker",
        "crossdeviceservice",
    };

    private static readonly string WindowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\";
    private static readonly int OwnPid = Environment.ProcessId;
    private static readonly int Cores = Environment.ProcessorCount;

    private readonly Dictionary<int, (long Created, long Cpu)> _lastCpu = [];
    private readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);
    private long _lastScanTicks;

    public List<AppGroup> Scan()
    {
        long now = Stopwatch.GetTimestamp();
        double elapsed100ns = _lastScanTicks == 0 ? 0 : (now - _lastScanTicks) * (10_000_000.0 / Stopwatch.Frequency);
        _lastScanTicks = now;

        var windows = Win32.TaskbarWindows();
        int ownSession = Process.GetCurrentProcess().SessionId;
        var seen = new HashSet<int>();
        var byPath = new Dictionary<string, (List<int> Pids, List<IntPtr> Windows, long Memory, double Cpu)>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                int pid = p.Id;
                if (pid <= 4 || pid == OwnPid)
                    continue;
                int session;
                try { session = p.SessionId; }
                catch (InvalidOperationException) { continue; }
                if (session != ownSession || Protected.Contains(p.ProcessName))
                    continue;

                IntPtr h = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (h == IntPtr.Zero)
                    continue; // protected or elevated process
                try
                {
                    string? path = Win32.ImagePath(h);
                    if (path is null)
                        continue;
                    windows.TryGetValue(pid, out var procWindows);
                    // Windows' own background pieces are never suggested; its apps with a window are (Task Manager, Paint…).
                    if (path.StartsWith(WindowsDir, StringComparison.OrdinalIgnoreCase) && procWindows is null)
                        continue;

                    seen.Add(pid);
                    long memory = Win32.PrivateWorkingSet(h);
                    double cpu = 0;
                    if (Win32.Times(h) is { } t)
                    {
                        if (elapsed100ns > 0 && _lastCpu.TryGetValue(pid, out var last) && last.Created == t.Created)
                            cpu = Math.Max(0, (t.CpuTime - last.Cpu) / elapsed100ns / Cores * 100);
                        _lastCpu[pid] = (t.Created, t.CpuTime);
                    }

                    if (!byPath.TryGetValue(path, out var g))
                        g = ([], [], 0, 0);
                    g.Pids.Add(pid);
                    if (procWindows is not null)
                        g.Windows.AddRange(procWindows);
                    byPath[path] = (g.Pids, g.Windows, g.Memory + memory, g.Cpu + cpu);
                }
                finally
                {
                    Win32.CloseHandle(h);
                }
            }
        }

        foreach (int pid in _lastCpu.Keys.Where(k => !seen.Contains(k)).ToList())
            _lastCpu.Remove(pid);

        var groups = new List<AppGroup>();
        foreach (var (path, g) in byPath)
        {
            if (g.Memory < MinMemory && g.Cpu < MinCpu)
                continue;
            groups.Add(new AppGroup
            {
                Path = path,
                Name = FriendlyName(path),
                ExeName = System.IO.Path.GetFileName(path),
                Kind = g.Windows.Count > 0 ? AppKind.OpenApp : AppKind.Background,
                Pids = g.Pids,
                Windows = g.Windows,
                Memory = g.Memory,
                Cpu = g.Cpu,
            });
        }
        return groups;
    }

    private string FriendlyName(string path)
    {
        if (_names.TryGetValue(path, out var name))
            return name;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            name = info.FileDescription?.Trim() is { Length: > 1 } d ? d : info.ProductName?.Trim() ?? "";
        }
        catch (Exception ex) when (ex is FileNotFoundException or UnauthorizedAccessException or IOException)
        {
        }
        if (string.IsNullOrWhiteSpace(name))
            name = System.IO.Path.GetFileNameWithoutExtension(path);
        return _names[path] = name;
    }

    /// <summary>Ask every window of an open app to close, as clicking its X would.</summary>
    public static void CloseWindows(AppGroup app)
    {
        foreach (var hwnd in app.Windows)
            Win32.PostMessageW(hwnd, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>End every process of the app. Returns how many could not be ended.</summary>
    public static int EndProcesses(AppGroup app)
    {
        int failed = 0;
        foreach (int pid in app.Pids)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                p.Kill();
            }
            catch (ArgumentException)
            {
                // already gone
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
            {
                failed++;
            }
        }
        return failed;
    }
}
