using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace SysPulse;

internal static class Elevation
{
    public static bool IsAdministrator { get; } = Check();

    private static bool Check()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Start a new elevated instance (UAC prompt). The caller must exit right
    /// after, so the new instance can take over the single-instance lock.
    /// Returns false when the user declined the prompt.
    /// </summary>
    public static bool TryRestartElevated(bool showWindow)
    {
        string? exe = Environment.ProcessPath;
        if (exe is null)
            return false;
        string args = showWindow ? "--restarted" : "--restarted --startup";
        try
        {
            // "dotnet SysPulse.dll" during development: relaunch the dll through the host.
            var psi = exe.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase)
                ? new ProcessStartInfo(exe, $"\"{Path.Combine(AppContext.BaseDirectory, "SysPulse.dll")}\" {args}")
                : new ProcessStartInfo(exe, args);
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            Process.Start(psi);
            return true;
        }
        catch (Win32Exception)
        {
            return false; // UAC cancelled
        }
    }

    /// <summary>Run a command elevated and wait for it (UAC prompt). Returns its exit code, or null if declined.</summary>
    public static int? RunElevated(string file, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file, args)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (p is null)
                return null;
            p.WaitForExit(30_000);
            return p.HasExited ? p.ExitCode : null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }
}
