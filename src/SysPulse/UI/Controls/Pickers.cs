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
