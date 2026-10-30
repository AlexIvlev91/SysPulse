using System.Drawing.Drawing2D;

namespace SysPulse.UI.Controls;

/// <summary>One card holding a row of small label/value tiles (disk, network, battery…).</summary>
internal sealed class StatStrip : Control
{
    public record Tile(string Glyph, string Label, string Value, string Detail);

    private Tile[] _tiles = [];

    public StatStrip()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
        Margin = Theme.S(0, 0, 0, 12);
    }

    public void SetTiles(params Tile[] tiles)
    {
        _tiles = tiles;
        Invalidate();
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
        if (_tiles.Length == 0)
            return;

        int pad = Theme.S(16);
        int inner = Width - pad * 2;
        using var divider = new Pen(Theme.Border);
        for (int i = 0; i < _tiles.Length; i++)
        {
            var t = _tiles[i];
            int x0 = pad + inner * i / _tiles.Length;
            int x1 = pad + inner * (i + 1) / _tiles.Length;
            if (i > 0)
                g.DrawLine(divider, x0 - Theme.S(8), Theme.S(14), x0 - Theme.S(8), Height - Theme.S(14));
            int w = x1 - x0 - Theme.S(16);
            int y = (Height - Theme.S(56)) / 2;
            Theme.DrawGlyph(g, t.Glyph, 9f, Theme.TextSubtle, new Rectangle(x0, y, Theme.S(16), Theme.S(18)));
            Theme.DrawText(g, t.Label, Theme.Small, Theme.TextSubtle, new Rectangle(x0 + Theme.S(22), y, w - Theme.S(22), Theme.S(18)));
            Theme.DrawText(g, t.Value, Theme.Stat, Theme.Text, new Rectangle(x0, y + Theme.S(18), w, Theme.S(22)));
            Theme.DrawText(g, t.Detail, Theme.Small, Theme.TextMuted, new Rectangle(x0, y + Theme.S(40), w, Theme.S(16)));
        }
    }
}
