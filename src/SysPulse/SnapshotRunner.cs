using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace SysPulse;

/// <summary>
/// <c>SysPulse --snapshot &lt;dir&gt;</c>: runs the real sensors for a few seconds
/// with the window off-screen, then saves PNGs of the main window and the
/// overlay (plain and Ctrl-hover) and exits. Used to check the UI without
/// touching the desktop.
/// </summary>
internal static class SnapshotRunner
{
    public static int Run(string dir)
    {
        Directory.CreateDirectory(dir);
        var app = new SysPulseApp(startInTray: false, headless: true);
        var timer = new System.Windows.Forms.Timer { Interval = 7000 };
        int exitCode = 0;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                if (app.Main is { } main)
                {
                    using var bmp = new Bitmap(main.Width, main.Height);
                    main.DrawToBitmap(bmp, new Rectangle(Point.Empty, main.Size));
                    bmp.Save(Path.Combine(dir, "main.png"), ImageFormat.Png);
                }
                SaveOverlay(app, Path.Combine(dir, "overlay.png"), interactive: false);
                SaveOverlay(app, Path.Combine(dir, "overlay-ctrl.png"), interactive: true);
                if (app.Last is { } last)
                    File.WriteAllText(Path.Combine(dir, "snapshot.txt"), last.Snapshot.ToString());
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(dir, "error.txt"), ex.ToString());
                exitCode = 1;
            }
            app.ExitApp();
        };
        timer.Start();
        Application.Run(app);
        return exitCode;
    }

    /// <summary>The overlay over a light and a dark backdrop, to check readability on both.</summary>
    private static void SaveOverlay(SysPulseApp app, string file, bool interactive)
    {
        using var overlay = app.Overlay.RenderBitmap(interactive, closeHover: false, out _);
        int pad = 24;
        using var canvas = new Bitmap(overlay.Width * 2 + pad * 3, overlay.Height + pad * 2);
        using var g = Graphics.FromImage(canvas);
        var half = new Rectangle(0, 0, canvas.Width / 2, canvas.Height);
        using (var light = new LinearGradientBrush(half, Color.White, Color.FromArgb(210, 220, 235), 0f))
            g.FillRectangle(light, half);
        using (var dark = new LinearGradientBrush(half with { X = half.Width }, Color.FromArgb(30, 40, 60), Color.FromArgb(10, 10, 12), 0f))
            g.FillRectangle(dark, half with { X = half.Width });
        g.DrawImage(overlay, pad, pad);
        g.DrawImage(overlay, half.Width + pad / 2, pad);
        canvas.Save(file, ImageFormat.Png);
    }
}
