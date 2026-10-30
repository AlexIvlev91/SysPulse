using System.Drawing.Drawing2D;

namespace SysPulse.UI.Controls;

/// <summary>
/// Single-series area sparkline over a fixed 0..100 scale, newest sample on the
/// right, with a hover crosshair and value readout. Gaps (NaN) break the line.
/// </summary>
internal static class Sparkline
{
    public static void Draw(Graphics g, Rectangle r, float[] values, int capacity, Color color, int? hoverIndex, string unit)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Recessive grid: baseline + midline.
        using (var grid = new Pen(Theme.Grid))
        {
            g.DrawLine(grid, r.Left, r.Bottom, r.Right, r.Bottom);
            using var dash = new Pen(Theme.Grid) { DashStyle = DashStyle.Dot };
            g.DrawLine(dash, r.Left, r.Top + r.Height / 2f, r.Right, r.Top + r.Height / 2f);
        }
        if (values.Length < 2)
            return;

        float step = r.Width / (float)(capacity - 1);
        float X(int i) => r.Right - (values.Length - 1 - i) * step;
        float Y(float v) => r.Bottom - Math.Clamp(v, 0, 100) / 100f * r.Height;

        // Split into runs of real samples.
        var runs = new List<List<PointF>>();
        List<PointF>? run = null;
        for (int i = 0; i < values.Length; i++)
        {
            if (float.IsNaN(values[i]))
            {
                run = null;
                continue;
            }
            if (run is null)
                runs.Add(run = []);
            run.Add(new PointF(X(i), Y(values[i])));
        }

        using var fill = new SolidBrush(Color.FromArgb(46, color));
        using var line = new Pen(color, Theme.S(2f)) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        foreach (var pts in runs)
        {
            if (pts.Count < 2)
                continue;
            var area = new List<PointF>(pts) { new(pts[^1].X, r.Bottom), new(pts[0].X, r.Bottom) };
            g.FillPolygon(fill, area.ToArray());
            g.DrawLines(line, pts.ToArray());
        }

        if (hoverIndex is int hi && hi >= 0 && hi < values.Length && !float.IsNaN(values[hi]))
        {
            float x = X(hi), y = Y(values[hi]);
            using var cross = new Pen(Theme.TextSubtle);
            g.DrawLine(cross, x, r.Top, x, r.Bottom);
            float dot = Theme.S(8f);
            using (var ring = new SolidBrush(Theme.Surface))
                g.FillEllipse(ring, x - dot / 2 - Theme.S(2f), y - dot / 2 - Theme.S(2f), dot + Theme.S(4f), dot + Theme.S(4f));
            using (var brush = new SolidBrush(color))
                g.FillEllipse(brush, x - dot / 2, y - dot / 2, dot, dot);

            int ago = values.Length - 1 - hi;
            string text = $"{values[hi]:0}{unit}  ·  {(ago == 0 ? "now" : $"{ago}s ago")}";
            int tw = Theme.TextWidth(text, Theme.SmallBold) + Theme.S(16);
            int th = Theme.S(22);
            int tx = (int)Math.Clamp(x - tw / 2f, r.Left, r.Right - tw);
            var box = new Rectangle(tx, r.Top - th - Theme.S(4), tw, th);
            using var path = Theme.RoundedRect(box, Theme.S(4));
            using var back = new SolidBrush(Theme.BorderStrong);
            g.FillPath(back, path);
            Theme.DrawText(g, text, Theme.SmallBold, Theme.Text, box, Theme.LeftLine | TextFormatFlags.HorizontalCenter);
        }
    }

    /// <summary>Index of the sample nearest to <paramref name="x"/>, or null when outside the plot.</summary>
    public static int? HitTest(Rectangle r, int count, int capacity, int x)
    {
        if (count < 2 || x < r.Left - 4 || x > r.Right + 4)
            return null;
        float step = r.Width / (float)(capacity - 1);
        int fromRight = (int)Math.Round((r.Right - x) / step);
        int i = count - 1 - fromRight;
        return i >= 0 && i < count ? i : null;
    }
}
