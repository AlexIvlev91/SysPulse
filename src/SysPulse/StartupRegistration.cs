using System.Diagnostics;
using System.Security;
using Microsoft.Win32;

namespace SysPulse;

/// <summary>
/// "Start with Windows". A normal instance registers in the user's Run key.
/// An elevated instance registers a logon task with highest privileges instead,
/// so CPU temperature keeps working after a reboot without a UAC prompt.
/// </summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SysPulse";
    private const string TaskName = "SysPulse";

    private static string Command => $"\"{Environment.ProcessPath}\" --startup";

    public static bool IsEnabled => RunValue() is not null || TaskExists();

    public static bool IsElevatedTask => TaskExists();

    /// <summary>Returns null on success, or an error message.</summary>
    public static string? Enable()
    {
        try
        {
            if (Elevation.IsAdministrator)
            {
                string? err = CreateTask();
                if (err is not null)
                    return err;
                RemoveRunValue();
            }
            else
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                key.SetValue(ValueName, Command);
            }
            AppSettings.Set("startup", true);
            return null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            return ex.Message;
        }
    }

    /// <summary>Returns null on success, or an error message.</summary>
    public static string? Disable()
    {
        try
        {
            RemoveRunValue();
            if (TaskExists())
            {
                int? code = Elevation.IsAdministrator
                    ? Schtasks($"/delete /tn \"{TaskName}\" /f")
                    : Elevation.RunElevated("schtasks.exe", $"/delete /tn \"{TaskName}\" /f");
                if (code != 0)
                    return "The startup task could not be removed (administrator approval is needed).";
            }
            AppSettings.Set("startup", false);
            return null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            return ex.Message;
        }
    }

    /// <summary>The exe moved (rebuilt or republished somewhere else): point the Run entry at this copy.</summary>
    public static void RepairPath()
    {
        try
        {
            if (RunValue() is string v && !string.Equals(v, Command, StringComparison.OrdinalIgnoreCase))
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                key.SetValue(ValueName, Command);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
        }
    }

    private static string? RunValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) as string;
    }

    private static void RemoveRunValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static bool TaskExists() => Schtasks($"/query /tn \"{TaskName}\"") == 0;

    private static string? CreateTask()
    {
        string user = $"{Environment.UserDomainName}\\{Environment.UserName}";
        // Defaults of schtasks /create would stop the app on battery and after 72 hours — the XML avoids both.
        string xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Starts SysPulse at sign-in with CPU temperature access.</Description></RegistrationInfo>
              <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{SecurityElement.Escape(user)}</UserId><Delay>PT5S</Delay></LogonTrigger></Triggers>
              <Principals><Principal id="Author"><UserId>{SecurityElement.Escape(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author"><Exec><Command>{SecurityElement.Escape(Environment.ProcessPath)}</Command><Arguments>--startup</Arguments></Exec></Actions>
            </Task>
            """;
        string file = Path.Combine(Path.GetTempPath(), "SysPulse-task.xml");
        File.WriteAllText(file, xml, System.Text.Encoding.Unicode);
        try
        {
            return Schtasks($"/create /tn \"{TaskName}\" /xml \"{file}\" /f") == 0 ? null : "Task Scheduler refused the startup task.";
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static int Schtasks(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (p is null)
                return -1;
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit(10_000);
            return p.HasExited ? p.ExitCode : -1;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return -1;
        }
    }
}
