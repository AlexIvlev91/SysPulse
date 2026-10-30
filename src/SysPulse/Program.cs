using SysPulse.UI;

namespace SysPulse;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Theme.InitDpi();

        int snapshot = Array.IndexOf(args, "--snapshot");
        if (snapshot >= 0)
            return SnapshotRunner.Run(snapshot + 1 < args.Length ? args[snapshot + 1] : ".");

        // "SysPulse --turbo on|off": flip turbo from a shortcut or script without opening the app.
        int turbo = Array.IndexOf(args, "--turbo");
        if (turbo >= 0 && turbo + 1 < args.Length && args[turbo + 1] is "on" or "off")
            return Monitoring.TurboControl.Set(args[turbo + 1] == "on") is null ? 0 : 1;

        if (!SingleInstance.TryAcquire(args.Contains("--restarted") ? 10_000 : 0))
        {
            SingleInstance.SignalExisting();
            return 0;
        }

        // A monitor that lives in the tray must not vanish: report unexpected errors and keep running.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportError(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportError(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => e.SetObserved();

        Application.Run(new SysPulseApp(startInTray: args.Contains("--startup")));
        return 0;
    }

    private static void ReportError(Exception? ex)
    {
        if (ex is null)
            return;
        var answer = MessageBox.Show(
            $"Something went wrong:\n\n{ex.Message}\n\nSysPulse will keep running. Copy the technical details to the clipboard?",
            "SysPulse", MessageBoxButtons.YesNo, MessageBoxIcon.Error, MessageBoxDefaultButton.Button2);
        if (answer == DialogResult.Yes)
        {
            try { Clipboard.SetText(ex.ToString()); }
            catch (System.Runtime.InteropServices.ExternalException) { }
        }
    }
}
