using System.Drawing.Drawing2D;
using SysPulse.Monitoring;

namespace SysPulse.UI.Controls;

/// <summary>
/// Owner-drawn list of apps that could be closed, with a memory share bar and
/// a Close / End button per row. Scrolls with the wheel or by dragging the thumb.
/// </summary>
internal sealed class ProcessList : Control
{
    private List<AppGroup> _items = [];
    private readonly Dictionary<string, Bitmap?> _icons = new(StringComparer.OrdinalIgnoreCase);
    private int _scroll;
    private int _hoverRow = -1;
    private bool _hoverButton;
    private int? _dragOffset;
    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);

    public ProcessList()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Theme.Surface;
    }

    public event Action<AppGroup>? ActionClicked;

    public string EmptyText { get; set; } = "Looking for apps…";

    private static int RowH => Theme.S(46);
    private static int HeaderH => Theme.S(30);
    private int ContentH => _items.Count * RowH;
    private int ViewH => Height - HeaderH;
    private int MaxScroll => Math.Max(0, ContentH - ViewH);

    public void SetItems(List<AppGroup> items)
    {
        // Rows whose close is in flight stay marked until their app disappears or changes kind.
        _pending.RemoveWhere(key => !items.Any(i => Key(i) == key));
        _items = items;
        _scroll = Math.Min(_scroll, MaxScroll);
        Invalidate();
    }

    public void MarkPending(AppGroup app)
    {
        _pending.Add(Key(app));
        Invalidate();
    }

    private static string Key(AppGroup a) => a.Path + "|" + a.Kind;

    // ---- geometry ----------------------------------------------------------
    private (int Name, int Type, int Cpu, int Memory, int Action) Columns()
    {
        int pad = Theme.S(16);
        int action = Width - pad - Theme.S(76);
        int memory = action - Theme.S(150);
        int cpu = memory - Theme.S(76);
        int type = cpu - Theme.S(118);
        return (pad, type, cpu, memory, action);
    }

    private Rectangle RowBounds(int i) => new(0, HeaderH + i * RowH - _scroll, Width - Theme.S(10), RowH);

    private Rectangle ButtonBounds(int i)
    {
        var row = RowBounds(i);
        var c = Columns();
        return new Rectangle(c.Action, row.Y + (RowH - Theme.S(28)) / 2, Theme.S(76), Theme.S(28));
    }

    private Rectangle? Thumb()
    {
        if (MaxScroll == 0)
            return null;
        int track = ViewH - Theme.S(8);
        int h = Math.Max(Theme.S(28), track * ViewH / ContentH);
        int y = HeaderH + Theme.S(4) + (track - h) * _scroll / MaxScroll;
        return new Rectangle(Width - Theme.S(8), y, Theme.S(5), h);
    }

    // ---- input -------------------------------------------------------------
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ScrollTo(_scroll - e.Delta / 120 * RowH);
    }

    private void ScrollTo(int value)
    {
        int v = Math.Clamp(value, 0, MaxScroll);
        if (v != _scroll)
        {
            _scroll = v;
            Invalidate();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (Thumb() is Rectangle t && e.X >= Width - Theme.S(12))
        {
            _dragOffset = t.Contains(e.Location) ? e.Y - t.Y : t.Height / 2;
            DragThumb(e.Y);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragOffset = null;
    }

    private void DragThumb(int mouseY)
    {
        if (_dragOffset is not int off || Thumb() is not Rectangle t)
            return;
        int track = ViewH - Theme.S(8) - t.Height;
        if (track <= 0)
            return;
        ScrollTo((mouseY - off - HeaderH - Theme.S(4)) * MaxScroll / track);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragOffset is not null)
        {
            DragThumb(e.Y);
            return;
        }
        int row = e.Y < HeaderH ? -1 : (e.Y - HeaderH + _scroll) / RowH;
        if (row >= _items.Count)
            row = -1;
        bool onButton = row >= 0 && ButtonBounds(row).Contains(e.Location) && !_pending.Contains(Key(_items[row]));
        Cursor = onButton ? Cursors.Hand : Cursors.Default;
        if (row != _hoverRow || onButton != _hoverButton)
        {
            _hoverRow = row;
            _hoverButton = onButton;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverRow = -1;
        _hoverButton = false;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left || _hoverRow < 0 || _hoverRow >= _items.Count)
            return;
        var app = _items[_hoverRow];
        if (ButtonBounds(_hoverRow).Contains(e.Location) && !_pending.Contains(Key(app)))
            ActionClicked?.Invoke(app);
    }

    // ---- painting ----------------------------------------------------------
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Surface);
        var c = Columns();

        // Header
        Theme.DrawText(g, "APP", Theme.Label, Theme.TextSubtle, new Rectangle(c.Name, 0, c.Type - c.Name, HeaderH));
        Theme.DrawText(g, "TYPE", Theme.Label, Theme.TextSubtle, new Rectangle(c.Type, 0, c.Cpu - c.Type, HeaderH));
        Theme.DrawText(g, "CPU", Theme.Label, Theme.TextSubtle, new Rectangle(c.Cpu, 0, c.Memory - c.Cpu - Theme.S(16), HeaderH), Theme.RightLine);
        Theme.DrawText(g, "MEMORY", Theme.Label, Theme.TextSubtle, new Rectangle(c.Memory, 0, c.Action - c.Memory - Theme.S(16), HeaderH), Theme.RightLine);
        using (var line = new Pen(Theme.Border))
            g.DrawLine(line, 0, HeaderH - 1, Width, HeaderH - 1);

        if (_items.Count == 0)
        {
            Theme.DrawText(g, EmptyText, Theme.Body, Theme.TextSubtle, new Rectangle(0, HeaderH, Width, Math.Min(ViewH, Theme.S(80))),
                Theme.LeftLine | TextFormatFlags.HorizontalCenter);
            return;
        }

        long maxMemory = Math.Max(1, _items.Max(i => i.Memory));
        g.SetClip(new Rectangle(0, HeaderH, Width, ViewH));
        int first = Math.Max(0, _scroll / RowH);
        int last = Math.Min(_items.Count - 1, (_scroll + ViewH) / RowH);
        for (int i = first; i <= last; i++)
            DrawRow(g, i, c, maxMemory);
        g.ResetClip();

        if (Thumb() is Rectangle t)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Theme.RoundedRect(t, Theme.S(2.5f));
            using var brush = new SolidBrush(_dragOffset is null ? Theme.BorderStrong : Theme.TextSubtle);
            g.FillPath(brush, path);
            g.SmoothingMode = SmoothingMode.None;
        }
    }

    private void DrawRow(Graphics g, int i, (int Name, int Type, int Cpu, int Memory, int Action) c, long maxMemory)
    {
        var app = _items[i];
        var row = RowBounds(i);
        bool pending = _pending.Contains(Key(app));
        if (i == _hoverRow)
        {
            using var hover = new SolidBrush(Theme.SurfaceAlt);
            g.FillRectangle(hover, row);
        }
        if (i > 0)
        {
            using var sep = new Pen(Theme.Grid);
            g.DrawLine(sep, c.Name, row.Y, row.Right, row.Y);
        }

        // Icon + name + exe/process count
        int iconSize = Theme.S(20);
        if (Icon(app.Path) is Bitmap icon)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(icon, new Rectangle(c.Name, row.Y + (RowH - iconSize) / 2, iconSize, iconSize));
        }
        int textX = c.Name + iconSize + Theme.S(12);
        int nameW = c.Type - textX - Theme.S(12);
        Theme.DrawText(g, app.Name, Theme.BodyBold, Theme.Text, new Rectangle(textX, row.Y + Theme.S(5), nameW, Theme.S(20)));
        string sub = app.Pids.Count == 1 ? app.ExeName : $"{app.ExeName} · {app.Pids.Count} processes";
        Theme.DrawText(g, sub, Theme.Small, Theme.TextSubtle, new Rectangle(textX, row.Y + Theme.S(23), nameW, Theme.S(17)));

        // Type
        bool open = app.Kind == AppKind.OpenApp;
        Theme.DrawGlyph(g, open ? Glyph.Window : Glyph.Background, 8.5f, Theme.TextSubtle,
            new Rectangle(c.Type, row.Y, Theme.S(16), RowH));
        Theme.DrawText(g, open ? "Open app" : "Background", Theme.Small, Theme.TextMuted,
            new Rectangle(c.Type + Theme.S(22), row.Y, c.Cpu - c.Type - Theme.S(22), RowH));

        // CPU
        string cpu = app.Cpu >= 10 ? $"{app.Cpu:0}%" : app.Cpu >= 0.1 ? $"{app.Cpu:0.0}%" : "0%";
        Theme.DrawText(g, cpu, Theme.Body, app.Cpu >= 5 ? Theme.Text : Theme.TextMuted,
            new Rectangle(c.Cpu, row.Y, c.Memory - c.Cpu - Theme.S(16), RowH), Theme.RightLine);

        // Memory value + share bar (single hue, magnitude)
        int memRight = c.Action - Theme.S(16);
        Theme.DrawText(g, Ui.Bytes(app.Memory), Theme.Body, Theme.Text,
            new Rectangle(c.Memory, row.Y + Theme.S(4), memRight - c.Memory, Theme.S(22)), Theme.RightLine);
        int barW = memRight - c.Memory - Theme.S(20);
        var track = new Rectangle(c.Memory + Theme.S(20), row.Y + Theme.S(30), barW, Theme.S(4));
        using (var trackBrush = new SolidBrush(Theme.Grid))
            g.FillRectangle(trackBrush, track);
        int fillW = (int)Math.Max(Theme.S(2), barW * (double)app.Memory / maxMemory);
        using (var fill = new SolidBrush(Theme.MemorySeries))
            g.FillRectangle(fill, new Rectangle(track.Right - fillW, track.Y, fillW, track.Height));

        // Action button
        var b = ButtonBounds(i);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Theme.RoundedRect(new RectangleF(b.X + 0.5f, b.Y + 0.5f, b.Width - 1, b.Height - 1), Theme.S(6)))
        {
            Color fill = pending ? Theme.Surface : i == _hoverRow && _hoverButton ? (open ? Theme.Hover : Theme.Critical) : Theme.SurfaceAlt;
            using var brush = new SolidBrush(fill);
            using var pen = new Pen(i == _hoverRow && _hoverButton && !open ? Theme.Critical : Theme.BorderStrong);
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }
        g.SmoothingMode = SmoothingMode.None;
        string label = pending ? (open ? "Closing…" : "Ending…") : open ? "Close" : "End";
        Theme.DrawText(g, label, Theme.SmallBold, pending ? Theme.TextSubtle : Theme.Text, b, Theme.LeftLine | TextFormatFlags.HorizontalCenter);
    }

    private Bitmap? Icon(string path)
    {
        if (_icons.TryGetValue(path, out var bmp))
            return bmp;
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            bmp = icon?.ToBitmap();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            bmp = null;
        }
        return _icons[path] = bmp;
    }
}
