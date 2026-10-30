using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace SysPulse.UI.Controls;

/// <summary>Horizontal slider: drag, click the track, or use the arrow keys.</summary>
internal sealed class Slider : Control
{
    private int _value;
    private bool _dragging, _hover;

    public Slider(int min, int max, int step)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
        Minimum = min;
        Maximum = max;
        Step = step;
        _value = max;
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        TabStop = true;
        Height = Theme.S(28);
    }

    public int Minimum { get; }
    public int Maximum { get; }
    public int Step { get; }

    public event EventHandler? ValueChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set
        {
            int v = Math.Clamp(value, Minimum, Maximum);
            if (v == _value)
                return;
            _value = v;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private int Knob => Theme.S(16);
    private int TrackLeft => Knob / 2;
    private int TrackRight => Width - Knob / 2;

    private void SetFromX(int x)
    {
        double t = (x - TrackLeft) / (double)Math.Max(1, TrackRight - TrackLeft);
        int raw = Minimum + (int)Math.Round(t * (Maximum - Minimum));
        Value = (int)Math.Round(raw / (double)Step) * Step;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
            return;
        Focus();
        _dragging = true;
        Capture = true;
        SetFromX(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
            SetFromX(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
        Capture = false;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Left or Keys.Down: Value -= Step; break;
            case Keys.Right or Keys.Up: Value += Step; break;
            case Keys.Home: Value = Minimum; break;
            case Keys.End: Value = Maximum; break;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.EffectiveBack(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cy = Height / 2f, th = Theme.S(4f);
        float t = (_value - Minimum) / (float)Math.Max(1, Maximum - Minimum);
        float kx = TrackLeft + t * (TrackRight - TrackLeft);

        using (var track = Theme.RoundedRect(new RectangleF(TrackLeft, cy - th / 2, TrackRight - TrackLeft, th), th / 2))
        using (var back = new SolidBrush(Theme.BorderStrong))
            g.FillPath(back, track);
        using (var fill = Theme.RoundedRect(new RectangleF(TrackLeft, cy - th / 2, Math.Max(th, kx - TrackLeft), th), th / 2))
        using (var accent = new SolidBrush(_hover || _dragging ? Theme.AccentHover : Theme.Accent))
            g.FillPath(accent, fill);

        float k = Knob;
        if (Focused && ShowFocusCues)
        {
            using var ring = new Pen(Theme.AccentHover, Theme.S(1.5f));
            g.DrawEllipse(ring, kx - k / 2 - Theme.S(3), cy - k / 2 - Theme.S(3), k + Theme.S(6), k + Theme.S(6));
        }
        using var knob = new SolidBrush(Color.White);
        g.FillEllipse(knob, kx - k / 2, cy - k / 2, k, k);
    }
}

/// <summary>A 3×3 grid standing for the screen: pick the corner or edge the overlay sticks to.</summary>
internal sealed class AnchorPicker : Control
{
    private OverlayAnchor _selected = OverlayAnchor.TopRight;
    private int _hover = -1;

    public AnchorPicker()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Size = Theme.S(120, 76);
    }

    /// <summary>Raised when the user picks a position (not when Selected is set in code).</summary>
    public event EventHandler? SelectionCommitted;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public OverlayAnchor Selected
    {
        get => _selected;
        set { if (_selected != value) { _selected = value; Invalidate(); } }
    }

    private Rectangle Cell(int i)
    {
        int pad = Theme.S(6);
        int cw = (Width - pad * 2) / 3, ch = (Height - pad * 2) / 3;
        return new Rectangle(pad + i % 3 * cw, pad + i / 3 * ch, cw, ch);
    }

    private int HitTest(Point p)
    {
        for (int i = 0; i < 9; i++)
            if (Cell(i).Contains(p))
                return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int h = HitTest(e.Location);
        if (h != _hover) { _hover = h; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = -1; Invalidate(); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        int h = HitTest(e.Location);
        if (h >= 0)
        {
            _selected = (OverlayAnchor)h;
            Invalidate();
            SelectionCommitted?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.EffectiveBack(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var screen = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.S(6)))
        using (var back = new SolidBrush(Theme.Window))
        using (var pen = new Pen(Theme.BorderStrong))
        {
            g.FillPath(back, screen);
            g.DrawPath(pen, screen);
        }
        for (int i = 0; i < 9; i++)
        {
            var c = Cell(i);
            bool selected = i == (int)_selected;
            // A small bar placed where the overlay would sit inside this cell.
            int bw = Theme.S(18), bh = Theme.S(8);
            int bx = (i % 3) switch { 0 => c.Left + Theme.S(3), 1 => c.Left + (c.Width - bw) / 2, _ => c.Right - bw - Theme.S(3) };
            int by = (i / 3) switch { 0 => c.Top + Theme.S(3), 1 => c.Top + (c.Height - bh) / 2, _ => c.Bottom - bh - Theme.S(3) };
            using var path = Theme.RoundedRect(new RectangleF(bx, by, bw, bh), Theme.S(2));
            using var brush = new SolidBrush(selected ? Theme.Accent : i == _hover ? Theme.TextSubtle : Theme.BorderStrong);
            g.FillPath(brush, path);
        }
    }
}

/// <summary>A row of color circles; a custom color that isn't a preset shows as an extra circle at the end.</summary>
internal sealed class ColorSwatches : Control
{
    private readonly (string Name, Color Color)[] _presets;
    private Color _selected = Color.White;
    private int _hover = -1;
    private readonly ToolTip _tip = new();
    private int _tipIndex = -1;

    public ColorSwatches(params (string Name, Color Color)[] presets)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        _presets = presets;
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Size = new Size(Theme.S(30) * (presets.Length + 1), Theme.S(30));
    }

    /// <summary>Raised when the user clicks a color (not when Selected is set in code).</summary>
    public event EventHandler? SelectionCommitted;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Selected
    {
        get => _selected;
        set { _selected = value; Invalidate(); }
    }

    private bool IsPreset(Color c) => _presets.Any(p => p.Color.ToArgb() == c.ToArgb());

    private int Count => _presets.Length + (IsPreset(_selected) ? 0 : 1);

    private Color ColorAt(int i) => i < _presets.Length ? _presets[i].Color : _selected;

    private Rectangle Swatch(int i)
    {
        int cell = Theme.S(30), d = Theme.S(22);
        return new Rectangle(i * cell + (cell - d) / 2, (Height - d) / 2, d, d);
    }

    private int HitTest(Point p)
    {
        for (int i = 0; i < Count; i++)
            if (Rectangle.Inflate(Swatch(i), Theme.S(3), Theme.S(3)).Contains(p))
                return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int h = HitTest(e.Location);
        if (h != _hover) { _hover = h; Invalidate(); }
        if (h != _tipIndex)
        {
            _tipIndex = h;
            _tip.SetToolTip(this, h < 0 ? "" : h < _presets.Length ? _presets[h].Name : $"Custom {OverlayOptions.ToHex(_selected)}");
        }
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = -1; Invalidate(); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        int h = HitTest(e.Location);
        if (h >= 0)
        {
            _selected = ColorAt(h);
            Invalidate();
            SelectionCommitted?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.EffectiveBack(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        for (int i = 0; i < Count; i++)
        {
            var r = Swatch(i);
            var c = ColorAt(i);
            if (c.ToArgb() == _selected.ToArgb())
            {
                using var ring = new Pen(Theme.Text, Theme.S(2f));
                g.DrawEllipse(ring, RectangleF.Inflate(r, Theme.S(3.5f), Theme.S(3.5f)));
            }
            else if (i == _hover)
            {
                using var ring = new Pen(Theme.TextSubtle, Theme.S(1.5f));
                g.DrawEllipse(ring, RectangleF.Inflate(r, Theme.S(3f), Theme.S(3f)));
            }
            using var fill = new SolidBrush(c);
            g.FillEllipse(fill, r);
            using var edge = new Pen(Color.FromArgb(70, 0, 0, 0));
            g.DrawEllipse(edge, r);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _tip.Dispose();
        base.Dispose(disposing);
    }
}
