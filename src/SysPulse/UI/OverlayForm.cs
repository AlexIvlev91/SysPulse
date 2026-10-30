using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using SysPulse.Monitoring;
using SysPulse.Native;

namespace SysPulse.UI;

/// <summary>
/// Always-on-top white readout in the top-right corner, in the spirit of an
/// in-game FPS counter. It's click-through so it never blocks what's under it.
/// Hold Ctrl while pointing at it to get a close button and drag it elsewhere.
/// </summary>
internal sealed class OverlayForm : Form
{
    private const int EdgeMargin = 14;
    private const int DefaultTop = 44; // just under a maximized window's caption buttons

    private readonly System.Windows.Forms.Timer _hoverTimer = new() { Interval = 80 };
    private Snapshot? _snapshot;
    private bool _interactive;
    private bool _closeHover;
    private Rectangle _closeBounds;
    private Size _size = new(1, 1);

    public OverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Text = "SysPulse overlay";
        _hoverTimer.Tick += (_, _) => CheckInteractive();
    }

    /// <summary>The user closed the overlay with its × button.</summary>
    public event EventHandler? Dismissed;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Win32.WS_EX_LAYERED | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_TOOLWINDOW_STYLE |
                          Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _hoverTimer.Start();
        Render();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
            _hoverTimer.Start();
        else
            _hoverTimer.Stop();
    }

    public void UpdateSnapshot(Snapshot snapshot)
    {
        _snapshot = snapshot;
        if (Visible)
            Render();
    }

    // ---- content -----------------------------------------------------------
    /// <summary>Rows of [label, load, temperature, extra]; every column means the same thing on every row.</summary>
    private List<(string Label, string?[] Values)> Lines()
    {
        var s = _snapshot;
        if (s is null)
            return [("SYSPULSE", ["…", null, null])];
        var lines = new List<(string, string?[])>
        {
            ("CPU", [Pct(s.Cpu.Usage), Temp(s.Cpu.TempC), s.Cpu.ClockGhz is double ghz ? $"{ghz:0.0} GHz" : null]),
        };
        if (s.Gpu.Present)
        {
            lines.Add(s.Gpu.Sleeping
                ? ("GPU", ["off", null, null])
                : ("GPU", [Pct(s.Gpu.Usage), Temp(s.Gpu.TempC), s.Gpu.PowerW is double w ? $"{w:0} W" : null]));
        }
        lines.Add(("RAM", [s.Memory.Total > 0 ? $"{s.Memory.Used * 100.0 / s.Memory.Total:0}%" : "—", null, $"{s.Memory.Used / 1073741824.0:0.0} GB"]));
        if (s.Turbo.Supported)
            lines.Add(("TURBO", [s.Turbo.Enabled ? "ON" : "OFF", null, null]));
        return lines;
    }

    private static string? Temp(double? c) => c is double t ? $"{t:0}°C" : null;

    private static string Pct(double? v) => v is double d ? $"{d:0}%" : "—";

    // ---- rendering ---------------------------------------------------------
    private void Render()
    {
        if (!IsHandleCreated)
            return;
        using var bmp = RenderBitmap(_interactive, _closeHover, out _closeBounds);
        _size = bmp.Size;
        Win32.UpdateLayered(Handle, bmp, TargetLocation(bmp.Size));
    }

    /// <summary>Draws the overlay into a transparent bitmap (also used for --snapshot).</summary>
    public Bitmap RenderBitmap(bool interactive, bool closeHover, out Rectangle closeBounds)
    {
        var lines = Lines();
        using var font = new FontFamily("Segoe UI");
        float em = Theme.S(15f);
        int lineH = Theme.S(21);
        int pad = Theme.S(10);
        int colGap = Theme.S(12);

        // Fixed column widths from sample strings so values don't jitter as they change.
        float Measure(string s)
        {
            using var p = new GraphicsPath();
            p.AddString(s, font, (int)FontStyle.Bold, em, PointF.Empty, StringFormat.GenericTypographic);
            return p.GetBounds().Width;
        }
        float labelW = Measure("TURBO");
        float[] minW = [Measure("100%"), Measure("100°C"), Measure("00.0 GB")];
        var colW = new float[3];
        for (int c = 0; c < 3; c++)
        {
            // A column nobody uses (e.g. no CPU temperature and the GPU is off) takes no space.
            var used = lines.Select(l => l.Values[c]).OfType<string>().ToList();
            colW[c] = used.Count == 0 ? 0 : Math.Max(minW[c], used.Max(Measure));
        }

        float contentW = labelW + colW.Where(cw => cw > 0).Sum(cw => colGap + cw);
        int closeSize = Theme.S(18);
        const string hintText = "Drag to move · Ctrl+Shift+O hides";
        int w = (int)Math.Ceiling(contentW) + pad * 2 + (interactive ? closeSize + Theme.S(8) : 0);
        if (interactive)
            w = Math.Max(w, Theme.TextWidth(hintText, Theme.Small) + pad * 2 + Theme.S(4));
        int h = lines.Count * lineH + pad * 2 + (interactive ? Theme.S(18) : 0);

        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        g.Clear(Color.Transparent);

        closeBounds = Rectangle.Empty;
        if (interactive)
        {
            using var plate = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, w - 1, h - 1), Theme.S(8));
            using var back = new SolidBrush(Color.FromArgb(200, 17, 17, 16));
            using var edge = new Pen(Color.FromArgb(120, 255, 255, 255));
            g.FillPath(back, plate);
            g.DrawPath(edge, plate);
            closeBounds = new Rectangle(w - pad - closeSize + Theme.S(4), pad - Theme.S(4), closeSize, closeSize);
            if (closeHover)
            {
                using var hover = new SolidBrush(Theme.Critical);
                g.FillEllipse(hover, closeBounds);
            }
            using var x = new Pen(Color.White, Theme.S(1.6f));
            int inset = Theme.S(6);
            g.DrawLine(x, closeBounds.Left + inset, closeBounds.Top + inset, closeBounds.Right - inset, closeBounds.Bottom - inset);
            g.DrawLine(x, closeBounds.Right - inset, closeBounds.Top + inset, closeBounds.Left + inset, closeBounds.Bottom - inset);
            // GDI+ (not GDI) text: it writes alpha, which a layered window needs.
            using var hint = new SolidBrush(Color.FromArgb(200, 200, 200));
            g.DrawString(hintText, Theme.Small, hint, pad, h - pad - Theme.S(16));
        }

        // White text with a dark outline: readable on any background.
        using var outline = new Pen(Color.FromArgb(interactive ? 0 : 190, 0, 0, 0), Theme.S(2.6f)) { LineJoin = LineJoin.Round };
        using var white = new SolidBrush(Color.White);
        using var dim = new SolidBrush(Color.FromArgb(235, 255, 255, 255));
        float y = pad;
        foreach (var (label, values) in lines)
        {
            using var path = new GraphicsPath();
            path.AddString(label, font, (int)FontStyle.Bold, em, new PointF(pad, y), StringFormat.GenericTypographic);
            float x = pad + labelW;
            using var valuePath = new GraphicsPath();
            for (int c = 0; c < 3; c++)
            {
                if (colW[c] == 0)
                    continue;
                x += colGap;
                if (values[c] is string v)
                {
                    // Load is left-aligned under the labels' baseline grid; numbers right-align in their column.
                    float vx = c == 0 && v.Length > 0 && char.IsLetter(v[0]) ? x : x + colW[c] - Measure(v);
                    valuePath.AddString(v, font, (int)FontStyle.Bold, em, new PointF(vx, y), StringFormat.GenericTypographic);
                }
                x += colW[c];
            }
            g.DrawPath(outline, path);
            g.DrawPath(outline, valuePath);
            g.FillPath(dim, path);
            g.FillPath(white, valuePath);
            y += lineH;
        }
        return bmp;
    }

    // ---- placement ---------------------------------------------------------
    private Screen TargetScreen()
    {
        string? name = AppSettings.Get("overlay.screen");
        return Screen.AllScreens.FirstOrDefault(s => s.DeviceName == name) ?? Screen.PrimaryScreen ?? Screen.AllScreens[0];
    }

    /// <summary>Anchored to the top-right corner, so the overlay grows to the left.</summary>
    private Point TargetLocation(Size size)
    {
        var wa = TargetScreen().WorkingArea;
        int right = AppSettings.GetInt("overlay.right") ?? Theme.S(EdgeMargin);
        int top = AppSettings.GetInt("overlay.top") ?? Theme.S(DefaultTop);
        int x = Math.Clamp(wa.Right - right - size.Width, wa.Left, Math.Max(wa.Left, wa.Right - size.Width));
        int y = Math.Clamp(wa.Top + top, wa.Top, Math.Max(wa.Top, wa.Bottom - size.Height));
        return new Point(x, y);
    }

    private void SavePosition()
    {
        var screen = Screen.FromPoint(new Point(Left + _size.Width / 2, Top + _size.Height / 2));
        var wa = screen.WorkingArea;
        AppSettings.Set("overlay.screen", screen.DeviceName);
        AppSettings.Set("overlay.right", wa.Right - (Left + _size.Width));
        AppSettings.Set("overlay.top", Top - wa.Top);
    }

    public void ResetPosition()
    {
        AppSettings.Set("overlay.screen", (string?)null);
        AppSettings.Set("overlay.right", (int?)null);
        AppSettings.Set("overlay.top", (int?)null);
        Render();
    }

    // ---- Ctrl + hover: become clickable ------------------------------------
    private void CheckInteractive()
    {
        var bounds = new Rectangle(Location, _size);
        bool want = Win32.IsKeyDown(Win32.VK_CONTROL) && bounds.Contains(Cursor.Position);
        if (_interactive && !want && (MouseButtons & MouseButtons.Left) != 0)
            return; // keep it while dragging
        bool closeHover = want && _closeBounds.Contains(PointToClient(Cursor.Position));
        if (want == _interactive && closeHover == _closeHover)
            return;
        _interactive = want;
        _closeHover = closeHover;
        Win32.SetExStyle(Handle, Win32.WS_EX_TRANSPARENT, !want);
        Render();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!_interactive || e.Button != MouseButtons.Left)
            return;
        if (_closeBounds.Contains(e.Location))
        {
            _interactive = false;
            Win32.SetExStyle(Handle, Win32.WS_EX_TRANSPARENT, true);
            Dismissed?.Invoke(this, EventArgs.Empty);
            return;
        }
        Win32.ReleaseCapture();
        Win32.SendMessageW(Handle, Win32.WM_NCLBUTTONDOWN, (IntPtr)Win32.HTCAPTION, IntPtr.Zero);
        SavePosition(); // the move loop has finished by now
        Render();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _hoverTimer.Dispose();
        base.Dispose(disposing);
    }
}
