using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace SysPulse.UI.Controls;

internal enum ButtonKind
{
    /// <summary>Filled accent — the one main action of a card.</summary>
    Primary,
    /// <summary>Raised surface with a border — ordinary actions.</summary>
    Secondary,
    /// <summary>No chrome until hovered — toolbar/icon actions.</summary>
    Ghost,
    /// <summary>Filled red — destructive actions.</summary>
    Danger,
}

/// <summary>Flat rounded button with an optional icon glyph.</summary>
internal sealed class ModernButton : Button
{
    private bool _hover, _pressed;
    private ButtonKind _kind;
    private string? _glyph;

    public ModernButton(string text = "", ButtonKind kind = ButtonKind.Secondary, string? glyph = null)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Font = kind is ButtonKind.Primary or ButtonKind.Danger ? Theme.BodyBold : Theme.Body;
        Text = text;
        _kind = kind;
        _glyph = glyph;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = Theme.S(0, 0, 8, 0);
        UseVisualStyleBackColor = false;
        BackColor = Color.Transparent;
        ForeColor = Theme.Text;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ButtonKind Kind
    {
        get => _kind;
        set
        {
            _kind = value;
            Font = value is ButtonKind.Primary or ButtonKind.Danger ? Theme.BodyBold : Theme.Body;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Glyph
    {
        get => _glyph;
        set { _glyph = value; PerformLayout(); Invalidate(); }
    }

    /// <summary>Fixed height in 96-DPI pixels.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int BaseHeight { get; set; } = 32;

    public override Size GetPreferredSize(Size proposedSize)
    {
        int h = Theme.S(BaseHeight);
        int w = Theme.S(14) * 2;
        if (!string.IsNullOrEmpty(Text))
            w += Theme.TextWidth(Text, Font);
        if (_glyph is not null)
            w += Theme.S(16) + (string.IsNullOrEmpty(Text) ? 0 : Theme.S(8));
        if (string.IsNullOrEmpty(Text))
            w = h;
        return new Size(w, h);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Parent?.PerformLayout();
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.EffectiveBack(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var (fill, border, fore) = Colors();
        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Theme.RoundedRect(rect, Theme.S(6)))
        {
            if (fill != Color.Transparent)
            {
                using var brush = new SolidBrush(fill);
                g.FillPath(brush, path);
            }
            if (border != Color.Transparent)
            {
                using var pen = new Pen(border);
                g.DrawPath(pen, path);
            }
        }

        if (Focused && ShowFocusCues)
        {
            using var pen = new Pen(Theme.AccentHover, Theme.S(2)) { Alignment = PenAlignment.Inset };
            using var path = Theme.RoundedRect(new RectangleF(1, 1, Width - 3, Height - 3), Theme.S(6));
            g.DrawPath(pen, path);
        }

        int glyphW = _glyph is null ? 0 : Theme.S(16);
        int gap = _glyph is not null && !string.IsNullOrEmpty(Text) ? Theme.S(8) : 0;
        int textW = string.IsNullOrEmpty(Text) ? 0 : Theme.TextWidth(Text, Font);
        int x = (Width - (glyphW + gap + textW)) / 2;

        if (_glyph is not null)
        {
            Theme.DrawGlyph(g, _glyph, 10.5f, fore, new Rectangle(x, 0, glyphW, Height));
            x += glyphW + gap;
        }
        if (textW > 0)
            Theme.DrawText(g, Text, Font, fore, new Rectangle(x, 0, textW + 2, Height));
    }

    private (Color Fill, Color Border, Color Fore) Colors()
    {
        if (!Enabled)
            return _kind == ButtonKind.Ghost
                ? (Color.Transparent, Color.Transparent, Theme.TextSubtle)
                : (Theme.SurfaceAlt, Theme.Border, Theme.TextSubtle);
        return _kind switch
        {
            ButtonKind.Primary => (_pressed ? Theme.AccentPressed : _hover ? Theme.AccentHover : Theme.Accent, Color.Transparent, Color.White),
            ButtonKind.Danger => (_pressed || _hover ? Theme.CriticalHover : Theme.Critical, Color.Transparent, Color.White),
            ButtonKind.Ghost => (_pressed ? Theme.BorderStrong : _hover ? Theme.Hover : Color.Transparent, Color.Transparent, Theme.Text),
            _ => (_pressed ? Theme.BorderStrong : _hover ? Theme.Hover : Theme.SurfaceAlt, Theme.BorderStrong, Theme.Text),
        };
    }
}

/// <summary>On/off switch with a label to its left.</summary>
internal sealed class ToggleSwitch : Control
{
    private bool _checked, _hover;

    public ToggleSwitch(string text)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
        Text = text;
        Font = Theme.Body;
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
        Margin = Theme.S(16, 0, 0, 0);
        Size = GetPreferredSize(Size.Empty);
        TabStop = true;
    }

    public event EventHandler? CheckedChanged;

    /// <summary>Raised when the user flips the switch; set Cancel to keep the old state.</summary>
    public event EventHandler<CancelEventArgs>? Toggling;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
                return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public override Size GetPreferredSize(Size proposedSize) =>
        new(Theme.TextWidth(Text, Font) + Theme.S(10 + 36), Theme.S(32));

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        var args = new CancelEventArgs();
        Toggling?.Invoke(this, args);
        if (!args.Cancel)
            Checked = !Checked;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Space or Keys.Enter)
            OnClick(EventArgs.Empty);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.EffectiveBack(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int trackW = Theme.S(36), trackH = Theme.S(18);
        int textW = Theme.TextWidth(Text, Font);
        Theme.DrawText(g, Text, Font, Enabled ? Theme.Text : Theme.TextSubtle, new Rectangle(0, 0, textW + 2, Height));

        var track = new RectangleF(Width - trackW - 1, (Height - trackH) / 2f, trackW, trackH);
        using (var path = Theme.RoundedRect(track, trackH / 2f))
        {
            Color fill = _checked ? (_hover ? Theme.AccentHover : Theme.Accent) : (_hover ? Theme.Hover : Theme.SurfaceAlt);
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);
            if (!_checked)
            {
                using var pen = new Pen(Theme.TextSubtle);
                g.DrawPath(pen, path);
            }
            if (Focused && ShowFocusCues)
            {
                using var focus = new Pen(Theme.AccentHover, Theme.S(1.5f));
                using var ring = Theme.RoundedRect(RectangleF.Inflate(track, Theme.S(2.5f), Theme.S(2.5f)), trackH / 2f + Theme.S(2.5f));
                g.DrawPath(focus, ring);
            }
        }

        float knob = trackH - Theme.S(6);
        float kx = _checked ? track.Right - knob - Theme.S(3) : track.X + Theme.S(3);
        using var knobBrush = new SolidBrush(_checked ? Color.White : Theme.TextMuted);
        g.FillEllipse(knobBrush, kx, track.Y + Theme.S(3), knob, knob);
    }
}

/// <summary>A row of mutually exclusive options (e.g. power mode, sort order).</summary>
internal sealed class Segmented : Control
{
    private readonly string[] _items;
    private int _selected = -1, _hover = -1;

    public Segmented(params string[] items)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        _items = items;
        Font = Theme.Small;
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
    }

    /// <summary>Raised when the user picks an option (not when SelectedIndex is set in code).</summary>
    public event EventHandler? SelectionCommitted;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selected;
        set { if (_selected != value) { _selected = value; Invalidate(); } }
    }

    public override Size GetPreferredSize(Size proposedSize) =>
        new(_items.Sum(i => Theme.TextWidth(i, Font) + Theme.S(20)) + Theme.S(4), Theme.S(28));

    private Rectangle Segment(int i)
    {
        int inner = Width - Theme.S(4);
        int x0 = Theme.S(2) + inner * i / _items.Length;
        int x1 = Theme.S(2) + inner * (i + 1) / _items.Length;
        return new Rectangle(x0, Theme.S(2), x1 - x0, Height - Theme.S(4));
    }

    private int HitTest(Point p)
    {
        for (int i = 0; i < _items.Length; i++)
            if (Segment(i).Contains(p))
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
        if (h >= 0 && h != _selected && Enabled)
        {
            _selected = h;
            Invalidate();
            SelectionCommitted?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.EffectiveBack(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var outer = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.S(6)))
        using (var back = new SolidBrush(Theme.Window))
        using (var pen = new Pen(Theme.Border))
        {
            g.FillPath(back, outer);
            g.DrawPath(pen, outer);
        }
        for (int i = 0; i < _items.Length; i++)
        {
            var r = Segment(i);
            if (i == _selected || i == _hover)
            {
                using var path = Theme.RoundedRect(r, Theme.S(4));
                using var brush = new SolidBrush(i == _selected ? Theme.BorderStrong : Theme.SurfaceAlt);
                g.FillPath(brush, path);
            }
            Color fore = !Enabled ? Theme.TextSubtle : i == _selected ? Theme.Text : Theme.TextMuted;
            Theme.DrawText(g, _items[i], i == _selected ? Theme.SmallBold : Font, fore, r,
                Theme.LeftLine | TextFormatFlags.HorizontalCenter);
        }
    }
}

/// <summary>Rounded surface panel with a hairline border.</summary>
internal class Card : Panel
{
    public Card()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
        Padding = Theme.S(16, 14, 16, 14);
        Margin = Theme.S(0, 0, 12, 12);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Window);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.S(10));
        using var brush = new SolidBrush(BackColor);
        using var pen = new Pen(Theme.Border);
        g.FillPath(brush, path);
        g.DrawPath(pen, path);
    }
}

internal static class Ui
{
    private static readonly ToolTip SharedTip = new() { InitialDelay = 400, ReshowDelay = 100 };

    public static T Tip<T>(this T control, string text) where T : Control
    {
        SharedTip.SetToolTip(control, text);
        return control;
    }

    public static Color EffectiveBack(Control control)
    {
        for (Control? c = control.Parent; c is not null; c = c.Parent)
        {
            if (c.BackColor.A == 255)
                return c.BackColor;
        }
        return Theme.Window;
    }

    public static string Bytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        int u = 0;
        while (bytes >= 1024 && u < units.Length - 1)
        {
            bytes /= 1024;
            u++;
        }
        return u == 0 ? $"{bytes:0} {units[u]}" : bytes >= 100 ? $"{bytes:0} {units[u]}" : $"{bytes:0.0} {units[u]}";
    }

    public static string Rate(double bytesPerSecond) =>
        bytesPerSecond < 1024 ? "0 KB/s" : Bytes(bytesPerSecond) + "/s";

    public static string Uptime(TimeSpan t) =>
        t.TotalDays >= 1 ? $"{(int)t.TotalDays}d {t.Hours}h" : t.TotalHours >= 1 ? $"{t.Hours}h {t.Minutes}m" : $"{t.Minutes}m";
}
