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
                using (var settings = new UI.OverlaySettingsForm(app.Overlay) { Location = new Point(-20000, -20000) })
                {
                    settings.Show();
                    using var bmp = new Bitmap(settings.Width, settings.Height);
                    settings.DrawToBitmap(bmp, new Rectangle(Point.Empty, settings.Size));
                    bmp.Save(Path.Combine(dir, "overlay-settings.png"), ImageFormat.Png);
                    settings.Close();
                }
                using (var levels = new ContextMenuStrip { Renderer = new UI.DarkMenuRenderer(), ShowImageMargin = true, Font = UI.Theme.Body })
                {
                    UI.Controls.TurboCard.AddLevelItems(levels.Items, app.Last?.Snapshot.Turbo.Current, _ => { });
                    levels.Show(new Point(-20000, -20000));
                    using var bmp = new Bitmap(levels.Width, levels.Height);
                    levels.DrawToBitmap(bmp, new Rectangle(Point.Empty, levels.Size));
                    bmp.Save(Path.Combine(dir, "turbo-levels.png"), ImageFormat.Png);
                    levels.Close();
                }
                var o = app.Overlay.Options;
                SaveOverlay(app, Path.Combine(dir, "overlay.png"), o, interactive: false);
                SaveOverlay(app, Path.Combine(dir, "overlay-ctrl.png"), o, interactive: true);
                SaveOverlay(app, Path.Combine(dir, "overlay-oneline.png"), o with { Layout = UI.OverlayLayout.OneLine }, interactive: false);
                SaveOverlay(app, Path.Combine(dir, "overlay-small-dark-60.png"),
                    o with { Size = UI.OverlaySize.Small, Background = true, Opacity = 60 }, interactive: false);
                SaveOverlay(app, Path.Combine(dir, "overlay-large.png"), o with { Size = UI.OverlaySize.Large }, interactive: false);
                SaveOverlay(app, Path.Combine(dir, "overlay-yellow.png"), o with { TextColor = UI.Theme.Hex("#FFE15A") }, interactive: false);
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
    private static void SaveOverlay(SysPulseApp app, string file, UI.OverlayOptions options, bool interactive)
    {
        using var overlay = app.Overlay.RenderBitmap(options, interactive, closeHover: false, out _);
        int pad = 24;
        using var canvas = new Bitmap(overlay.Width * 2 + pad * 3, overlay.Height + pad * 2);
        using var g = Graphics.FromImage(canvas);
        var half = new Rectangle(0, 0, canvas.Width / 2, canvas.Height);
        using (var light = new LinearGradientBrush(half, Color.White, Color.FromArgb(210, 220, 235), 0f))
            g.FillRectangle(light, half);
        using (var dark = new LinearGradientBrush(half with { X = half.Width }, Color.FromArgb(30, 40, 60), Color.FromArgb(10, 10, 12), 0f))
            g.FillRectangle(dark, half with { X = half.Width });
        // The window fades the whole bitmap by the opacity setting; do the same here.
        using var fade = new ImageAttributes();
        fade.SetColorMatrix(new ColorMatrix { Matrix33 = interactive ? 1f : options.Opacity / 100f });
        foreach (int x in new[] { pad, half.Width + pad / 2 })
            g.DrawImage(overlay, new Rectangle(x, pad, overlay.Width, overlay.Height), 0, 0, overlay.Width, overlay.Height, GraphicsUnit.Pixel, fade);
        canvas.Save(file, ImageFormat.Png);
    }
}
