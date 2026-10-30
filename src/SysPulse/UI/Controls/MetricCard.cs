using System.Drawing.Drawing2D;
using SysPulse.Monitoring;

namespace SysPulse.UI.Controls;

/// <summary>A hero value, an optional temperature, a load sparkline and detail rows.</summary>
internal sealed class MetricCard : Control
{
    private float[] _history = [];
    private int? _hover;
    private Rectangle _plot, _linkBounds;

    public MetricCard(string title, string glyph, Color series)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Title = title;
        GlyphText = glyph;
        Series = series;
        BackColor = Theme.Surface;
        Margin = Theme.S(0, 0, 12, 12);
    }

    public string Title { get; }
    public string GlyphText { get; }
    public Color Series { get; }

    public string Subtitle { get; set; } = "";
    public string Value { get; set; } = "—";
    public string ValueUnit { get; set; } = "";
    public string ValueCaption { get; set; } = "";

    /// <summary>Temperature in °C; null shows a dash (or <see cref="TempLink"/>).</summary>
    public double? Temperature { get; set; }
    public bool ShowTemperature { get; set; } = true;
    public string TempCaption { get; set; } = "temperature";

    /// <summary>When set and there's no temperature, shows this as a link under the dash.</summary>
    public string? TempLink { get; set; }

    /// <summary>Replaces the value caption, e.g. "Sleeping — saving battery".</summary>
    public string? Notice { get; set; }

    public (string Label, string Value)[] Details { get; set; } = [];

    public event EventHandler? TempLinkClicked;

    public void SetHistory(float[] values)
    {
        _history = values;
        Invalidate();
    }

    public override Size GetPreferredSize(Size proposedSize) => new(Theme.S(250), Theme.S(260));

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hover = _plot.Contains(e.Location) || Math.Abs(e.Y - _plot.Bottom) < Theme.S(6)
            ? Sparkline.HitTest(_plot, _history.Length, SystemMonitor.HistoryLength, e.X)
            : null;
        Cursor = TempLink is not null && Temperature is null && _linkBounds.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
        if (hover != _hover)
        {
            _hover = hover;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = null;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (TempLink is not null && Temperature is null && _linkBounds.Contains(e.Location))
            TempLinkClicked?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.EffectiveBack(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.S(10)))
        using (var brush = new SolidBrush(Theme.Surface))
        using (var pen = new Pen(Theme.Border))
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }

        int pad = Theme.S(16);
        int x = pad, w = Width - pad * 2, y = Theme.S(14);

        // Title row: series swatch + glyph + title, subtitle under it.
        using (var swatch = new SolidBrush(Series))
            g.FillRectangle(swatch, x, y + Theme.S(4), Theme.S(3), Theme.S(12));
        Theme.DrawGlyph(g, GlyphText, 10f, Theme.TextMuted, new Rectangle(x + Theme.S(8), y, Theme.S(18), Theme.S(20)));
        Theme.DrawText(g, Title, Theme.Section, Theme.Text, new Rectangle(x + Theme.S(30), y, w - Theme.S(30), Theme.S(20)));
        y += Theme.S(20);
        Theme.DrawText(g, Subtitle, Theme.Small, Theme.TextSubtle, new Rectangle(x, y, w, Theme.S(18)));
        y += Theme.S(24);

        // Hero row: value on the left, temperature on the right.
        int heroH = Theme.S(40);
        int valueW = Theme.TextWidth(Value, Theme.Hero);
        Theme.DrawText(g, Value, Theme.Hero, Theme.Text, new Rectangle(x - Theme.S(2), y, valueW + 4, heroH));
        if (ValueUnit.Length > 0)
            Theme.DrawText(g, ValueUnit, Theme.HeroUnit, Theme.TextMuted,
                new Rectangle(x + valueW, y + Theme.S(6), Theme.S(60), heroH - Theme.S(6)));

        if (ShowTemperature)
        {
            if (Temperature is double t)
            {
                string temp = $"{t:0}°";
                int tw = Theme.TextWidth(temp, Theme.Hero);
                Theme.DrawText(g, temp, Theme.Hero, Theme.Text, new Rectangle(x + w - tw - Theme.S(14), y, tw + 4, heroH));
                Theme.DrawText(g, "C", Theme.HeroUnit, Theme.TextMuted, new Rectangle(x + w - Theme.S(14), y + Theme.S(6), Theme.S(16), heroH - Theme.S(6)));
            }
            else
            {
                int tw = Theme.TextWidth("—", Theme.Hero);
                Theme.DrawText(g, "—", Theme.Hero, Theme.TextSubtle, new Rectangle(x + w - tw, y, tw + 4, heroH));
            }
        }
        y += heroH;

        // Captions under the hero values.
        int capH = Theme.S(18);
        Theme.DrawText(g, Notice ?? ValueCaption, Theme.Small, Notice is null ? Theme.TextSubtle : Theme.TextMuted,
            new Rectangle(x, y, ShowTemperature ? w / 2 : w, capH));
        _linkBounds = Rectangle.Empty;
        if (ShowTemperature)
        {
            var status = Theme.TempStatus(Temperature);
            if (Temperature is null && TempLink is not null)
            {
                int lw = Theme.TextWidth(TempLink, Theme.SmallBold);
                _linkBounds = new Rectangle(x + w - lw, y, lw, capH);
                Theme.DrawText(g, TempLink, Theme.SmallBold, Theme.AccentHover, _linkBounds, Theme.RightLine);
            }
            else if (status is Color sc)
            {
                // Status ships with a label, never color alone.
                string label = Temperature >= 95 ? "Critical" : Temperature >= 85 ? "Hot" : "Warm";
                int lw = Theme.TextWidth(label, Theme.SmallBold);
                var r = new Rectangle(x + w - lw, y, lw, capH);
                Theme.DrawText(g, label, Theme.SmallBold, Theme.Text, r, Theme.RightLine);
                using var dot = new SolidBrush(sc);
                float d = Theme.S(8f);
                g.FillEllipse(dot, r.Left - d - Theme.S(5), y + (capH - d) / 2f, d, d);
            }
            else
            {
                Theme.DrawText(g, TempCaption, Theme.Small, Theme.TextSubtle, new Rectangle(x + w / 2, y, w / 2, capH), Theme.RightLine);
            }
        }
        y += capH + Theme.S(18);

        // Sparkline, then detail rows anchored to the bottom.
        int detailRows = (Details.Length + 1) / 2;
        int detailsH = detailRows * Theme.S(20);
        int plotBottom = Height - Theme.S(14) - detailsH - Theme.S(12);
        _plot = new Rectangle(x, y, w, Math.Max(Theme.S(16), plotBottom - y));
        Sparkline.Draw(g, _plot, _history, SystemMonitor.HistoryLength, Series, _hover, "%");

        int dy = Height - Theme.S(14) - detailsH;
        for (int i = 0; i < Details.Length; i++)
        {
            var (label, value) = Details[i];
            int col = i % 2, row = i / 2;
            var cell = new Rectangle(x + col * (w / 2), dy + row * Theme.S(20), w / 2 - Theme.S(6), Theme.S(20));
            int lw = Theme.TextWidth(label, Theme.Small) + Theme.S(6);
            Theme.DrawText(g, label, Theme.Small, Theme.TextSubtle, new Rectangle(cell.X, cell.Y, lw, cell.Height));
            Theme.DrawText(g, value, Theme.SmallBold, Theme.TextMuted, new Rectangle(cell.X + lw, cell.Y, cell.Width - lw, cell.Height));
        }
    }
}
