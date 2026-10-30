using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using SysPulse.Monitoring;
using SysPulse.Native;

namespace SysPulse.UI;

/// <summary>
/// Always-on-top white readout, in the spirit of an in-game FPS counter.
/// It's click-through so it never blocks what's under it. Hold Ctrl while
/// pointing at it to get a close button and drag it elsewhere. Layout, size,
/// opacity and position come from <see cref="OverlayOptions"/>.
/// </summary>
internal sealed class OverlayForm : Form
{
    private readonly System.Windows.Forms.Timer _hoverTimer = new() { Interval = 80 };
    private readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = 500 };
    private Snapshot? _snapshot;
    private OverlayOptions _options = OverlayOptions.Load();
    private bool _interactive;
    private bool _closeHover;
    private Rectangle _closeBounds;
    private Size _size = new(1, 1);
    private Point _location;

    public OverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Text = "SysPulse overlay";
        _hoverTimer.Tick += (_, _) => CheckInteractive();
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            _options.Save();
        };
    }

    /// <summary>The user closed the overlay with its × button.</summary>
    public event EventHandler? Dismissed;

    /// <summary>Raised after the position changed by dragging or nudging.</summary>
    public event EventHandler? OptionsChanged;

    public OverlayOptions Options
    {
        get => _options;
        set
        {
            _options = value;
            SaveSoon();
            Render();
        }
    }

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
        Render();
    }

    private void SaveSoon()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    // ---- content -----------------------------------------------------------
    /// <param name="Sample">Widest value the cell normally holds, so digits changing don't make the overlay jitter.</param>
    private sealed record Cell(string Text, string Sample);

    private sealed record Group(string Label, Cell[] Cells);

    private List<Group> Groups()
    {
        var s = _snapshot;
        if (s is null)
            return [new("SYSPULSE", [new("…", "…")])];

        var cpu = new List<Cell> { Pct(s.Cpu.Usage) };
        if (s.Cpu.TempC is double ct) cpu.Add(new($"{ct:0}°C", "88°C"));
        if (s.Cpu.ClockGhz is double ghz) cpu.Add(new($"{ghz:0.0} GHz", "8.8 GHz"));
        var groups = new List<Group> { new("CPU", [.. cpu]) };

        if (s.Gpu.Present)
        {
            var gpu = new List<Cell>();
            if (s.Gpu.Sleeping)
                gpu.Add(new("off", "88%"));
            else
            {
                gpu.Add(Pct(s.Gpu.Usage));
                if (s.Gpu.TempC is double gt) gpu.Add(new($"{gt:0}°C", "88°C"));
                if (s.Gpu.PowerW is double w) gpu.Add(new($"{w:0} W", "88 W"));
            }
            groups.Add(new("GPU", [.. gpu]));
        }

        double used = s.Memory.Used / 1073741824.0;
        groups.Add(new("RAM", [
            new(s.Memory.Total > 0 ? $"{s.Memory.Used * 100.0 / s.Memory.Total:0}%" : "—", "88%"),
            new($"{used:0.0} GB", "88.8 GB")]));
        if (s.Turbo.Supported)
            groups.Add(new("TURBO", [new(s.Turbo.Enabled ? "ON" : "OFF", "OFF")]));
        return groups;
    }

    private static Cell Pct(double? v) => new(v is double d ? $"{d:0}%" : "—", "88%");

    // ---- rendering ---------------------------------------------------------
    private void Render()
    {
        if (!IsHandleCreated || !Visible)
            return;
        using var bmp = RenderBitmap(_options, _interactive, _closeHover, out _closeBounds);
        _size = bmp.Size;
        _location = TargetLocation(_options, bmp.Size);
        // Full opacity while Ctrl-hovering, so it's clear what you're grabbing.
        byte alpha = _interactive ? (byte)255 : (byte)Math.Round(_options.Opacity * 2.55);
        Win32.UpdateLayered(Handle, bmp, _location, alpha);
    }

    /// <summary>Draws the overlay into a transparent bitmap (also used for --snapshot and the settings preview).</summary>
    public Bitmap RenderBitmap(OverlayOptions options, bool interactive, bool closeHover, out Rectangle closeBounds)
    {
        var groups = Groups();
        using var font = new FontFamily("Segoe UI");
        float em = Theme.S(options.Size switch { OverlaySize.Small => 12f, OverlaySize.Large => 19f, _ => 15f });
        float pad = em * 0.45f;
        float labelGap = em * 0.42f;  // label → first value
        float cellGap = em * 0.36f;   // value → value
        float groupGap = em * 0.95f;  // between groups on one line
        bool oneLine = options.Layout == OverlayLayout.OneLine;

        float Measure(string s)
        {
            using var p = new GraphicsPath();
            p.AddString(s, font, (int)FontStyle.Bold, em, PointF.Empty, StringFormat.GenericTypographic);
            return s.Trim().Length == 0 ? 0 : p.GetBounds().Right;
        }
        float CellW(Cell c) => Math.Max(Measure(c.Text), Measure(c.Sample));

        // Lay rows out by the ink of capitals and digits, not the font's line box,
        // so the padding is even and rows sit close together.
        RectangleF ink;
        using (var probe = new GraphicsPath())
        {
            probe.AddString("CPU 88°C", font, (int)FontStyle.Bold, em, PointF.Empty, StringFormat.GenericTypographic);
            ink = probe.GetBounds();
        }
        float lineH = ink.Height + em * 0.4f;

        // Stacked: labels share one column and the first values right-align in the next,
        // the rest of each row flows after it. One line: groups follow each other.
        float labelColW = oneLine ? 0 : groups.Max(g => Measure(g.Label));
        float firstColW = oneLine ? 0 : groups.Max(g => CellW(g.Cells[0]));
        float RowW(Group g) => labelColW + labelGap + firstColW + g.Cells.Skip(1).Sum(c => cellGap + CellW(c));
        float GroupW(Group g) => Measure(g.Label) + labelGap + CellW(g.Cells[0]) + g.Cells.Skip(1).Sum(c => cellGap + CellW(c));

        float contentW = oneLine
            ? groups.Sum(GroupW) + groupGap * (groups.Count - 1)
            : groups.Max(RowW);
        float contentH = oneLine ? ink.Height : groups.Count * lineH - (lineH - ink.Height);

        int closeSize = Theme.S(18);
        const string hintText = "Drag to move · Ctrl+Shift+O hides";
        int w = (int)Math.Ceiling(contentW + pad * 2 + (interactive ? closeSize + Theme.S(6) : 0));
        if (interactive)
            w = Math.Max(w, Theme.TextWidth(hintText, Theme.Small) + (int)(pad * 2) + Theme.S(4));
        int h = (int)Math.Ceiling(contentH + pad * 2 + (interactive ? Theme.S(20) : 0));

        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        g.Clear(Color.Transparent);

        closeBounds = Rectangle.Empty;
        if (interactive || options.Background)
        {
            using var plate = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, w - 1, h - 1), Math.Min(Theme.S(8), pad + 2));
            using var back = new SolidBrush(Color.FromArgb(interactive ? 210 : 150, 17, 17, 16));
            g.FillPath(back, plate);
            if (interactive)
            {
                using var edge = new Pen(Color.FromArgb(120, 255, 255, 255));
                g.DrawPath(edge, plate);
            }
        }
        if (interactive)
        {
            closeBounds = new Rectangle(w - Theme.S(4) - closeSize, Theme.S(4), closeSize, closeSize);
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
            g.DrawString(hintText, Theme.Small, hint, pad - Theme.S(2), h - pad / 2 - Theme.S(17));
        }

        using var labels = new GraphicsPath();
        using var values = new GraphicsPath();
        void Add(GraphicsPath path, string text, float x, float y) =>
            path.AddString(text, font, (int)FontStyle.Bold, em, new PointF(x, y), StringFormat.GenericTypographic);

        float gx = pad, gy = pad - ink.Top;
        foreach (var group in groups)
        {
            float x = gx;
            Add(labels, group.Label, x, gy);
            x += (oneLine ? Measure(group.Label) : labelColW) + labelGap;
            for (int i = 0; i < group.Cells.Length; i++)
            {
                var cell = group.Cells[i];
                if (i > 0)
                    x += cellGap;
                float cw = i == 0 && !oneLine ? firstColW : CellW(cell);
                // Numbers right-align in their slot; words (ON, OFF, off) start at the slot's left.
                bool word = cell.Text.Length > 0 && char.IsLetter(cell.Text[0]);
                Add(values, cell.Text, word ? x : x + cw - Measure(cell.Text), gy);
                x += cw;
            }
            if (oneLine)
                gx = x + groupGap;
            else
                gy += lineH;
        }

        // White text with a dark outline: readable on any background (a backplate makes it lighter).
        int outlineAlpha = interactive ? 0 : options.Background ? 110 : 190;
        using var outline = new Pen(Color.FromArgb(outlineAlpha, 0, 0, 0), Math.Max(1.5f, em * 0.17f)) { LineJoin = LineJoin.Round };
        using var dim = new SolidBrush(Color.FromArgb(225, 255, 255, 255));
        using var white = new SolidBrush(Color.White);
        if (outlineAlpha > 0)
        {
            g.DrawPath(outline, labels);
            g.DrawPath(outline, values);
        }
        g.FillPath(dim, labels);
        g.FillPath(white, values);
        return bmp;
    }

    // ---- placement ---------------------------------------------------------
    private static Screen ScreenFor(OverlayOptions o) =>
        System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == o.Screen)
        ?? System.Windows.Forms.Screen.PrimaryScreen ?? System.Windows.Forms.Screen.AllScreens[0];

    private static Point TargetLocation(OverlayOptions o, Size size)
    {
        var wa = ScreenFor(o).WorkingArea;
        int col = (int)o.Anchor % 3, row = (int)o.Anchor / 3;
        int x = col switch
        {
            0 => wa.Left + o.OffsetX,
            1 => wa.Left + (wa.Width - size.Width) / 2 + o.OffsetX,
            _ => wa.Right - size.Width - o.OffsetX,
        };
        int y = row switch
        {
            0 => wa.Top + o.OffsetY,
            1 => wa.Top + (wa.Height - size.Height) / 2 + o.OffsetY,
            _ => wa.Bottom - size.Height - o.OffsetY,
        };
        return new Point(
            Math.Clamp(x, wa.Left, Math.Max(wa.Left, wa.Right - size.Width)),
            Math.Clamp(y, wa.Top, Math.Max(wa.Top, wa.Bottom - size.Height)));
    }

    /// <summary>Options that put the overlay at <paramref name="location"/>, measured from <paramref name="anchor"/>.</summary>
    private OverlayOptions PlacedAt(Point location, OverlayAnchor anchor)
    {
        var screen = System.Windows.Forms.Screen.FromPoint(new Point(location.X + _size.Width / 2, location.Y + _size.Height / 2));
        var wa = screen.WorkingArea;
        int col = (int)anchor % 3, row = (int)anchor / 3;
        int ox = col switch
        {
            0 => location.X - wa.Left,
            1 => location.X - (wa.Left + (wa.Width - _size.Width) / 2),
            _ => wa.Right - _size.Width - location.X,
        };
        int oy = row switch
        {
            0 => location.Y - wa.Top,
            1 => location.Y - (wa.Top + (wa.Height - _size.Height) / 2),
            _ => wa.Bottom - _size.Height - location.Y,
        };
        return _options with { Anchor = anchor, OffsetX = ox, OffsetY = oy, Screen = screen.DeviceName };
    }

    /// <summary>Moves the overlay by a few pixels, keeping its anchor.</summary>
    public void Nudge(int dx, int dy)
    {
        var wa = ScreenFor(_options).WorkingArea;
        var target = TargetLocation(_options, _size);
        var moved = new Point(
            Math.Clamp(target.X + dx, wa.Left, Math.Max(wa.Left, wa.Right - _size.Width)),
            Math.Clamp(target.Y + dy, wa.Top, Math.Max(wa.Top, wa.Bottom - _size.Height)));
        Options = PlacedAt(moved, _options.Anchor);
        OptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Sticks the overlay to a corner/edge at the default distance.</summary>
    public void SetAnchor(OverlayAnchor anchor) =>
        Options = _options with
        {
            Anchor = anchor,
            OffsetX = OverlayOptions.DefaultOffsetX(anchor),
            OffsetY = OverlayOptions.DefaultOffsetY(anchor),
        };

    public void ResetPosition()
    {
        var d = OverlayOptions.Default;
        Options = _options with { Anchor = d.Anchor, OffsetX = d.OffsetX, OffsetY = d.OffsetY, Screen = null };
        OptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>After a drag: stick to whichever third of the screen it was dropped in.</summary>
    private void SnapAfterDrag()
    {
        var location = Location;
        var wa = System.Windows.Forms.Screen.FromPoint(location).WorkingArea;
        var center = new Point(location.X + _size.Width / 2, location.Y + _size.Height / 2);
        int col = Math.Clamp((center.X - wa.Left) * 3 / Math.Max(1, wa.Width), 0, 2);
        int row = Math.Clamp((center.Y - wa.Top) * 3 / Math.Max(1, wa.Height), 0, 2);
        Options = PlacedAt(location, (OverlayAnchor)(row * 3 + col));
        OptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- Ctrl + hover: become clickable ------------------------------------
    private void CheckInteractive()
    {
        var bounds = new Rectangle(_location, _size);
        bool want = Win32.IsKeyDown(Win32.VK_CONTROL) && bounds.Contains(Cursor.Position);
        if (_interactive && !want && (MouseButtons & MouseButtons.Left) != 0)
            return; // keep it while dragging
        bool closeHover = want && _closeBounds.Contains(new Point(Cursor.Position.X - _location.X, Cursor.Position.Y - _location.Y));
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
        SnapAfterDrag(); // the move loop has finished by now
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_saveTimer.Enabled)
                _options.Save();
            _hoverTimer.Dispose();
            _saveTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
