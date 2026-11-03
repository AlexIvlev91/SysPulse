using System.Drawing.Drawing2D;
using SysPulse.Native;

namespace SysPulse.UI;

/// <summary>
/// The app/tray icon, drawn at runtime: a pulse line on a rounded tile, blue
/// while turbo is on and gray while it's off, so the tray shows the state.
/// </summary>
internal static class TrayIcons
{
    public static Icon AppIcon { get; } = Create(turbo: true, 32);

    public static Icon Create(bool turbo, int size)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            float s = size / 32f;
            using var tile = Theme.RoundedRect(new RectangleF(1 * s, 1 * s, 30 * s, 30 * s), 7 * s);
            using var fill = new SolidBrush(turbo ? Theme.Accent : Theme.Hex("#5C5B57"));
            g.FillPath(fill, tile);
            using var pen = new Pen(Color.White, 3.2f * s) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLines(pen, new[]
            {
                new PointF(5 * s, 17 * s), new PointF(10 * s, 17 * s), new PointF(13.5f * s, 8 * s),
                new PointF(18.5f * s, 25 * s), new PointF(22 * s, 15 * s), new PointF(27 * s, 15 * s),
            });
        }
        IntPtr h = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(h).Clone();
        Win32.DestroyIcon(h);
        return icon;
    }
}

/// <summary>Dark context menu to match the app.</summary>
internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkColors()) => RoundedEdges = false;

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        // The right-hand column (shortcut, or a level's description) is muted.
        bool secondary = e.Item is ToolStripMenuItem { ShortcutKeyDisplayString: { Length: > 0 } s } && e.Text == s;
        e.TextColor = !e.Item.Enabled || secondary ? Theme.TextSubtle : Theme.Text;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r = new Rectangle(e.ImageRectangle.X - 2, e.ImageRectangle.Y - 2, e.ImageRectangle.Width + 4, e.ImageRectangle.Height + 4);
        Theme.DrawGlyph(e.Graphics, "", 9f, Theme.Text, r);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Theme.TextMuted;
        base.OnRenderArrow(e);
    }

    private sealed class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Surface;
        public override Color ImageMarginGradientBegin => Theme.Surface;
        public override Color ImageMarginGradientMiddle => Theme.Surface;
        public override Color ImageMarginGradientEnd => Theme.Surface;
        public override Color MenuBorder => Theme.BorderStrong;
        public override Color MenuItemBorder => Theme.Hover;
        public override Color MenuItemSelected => Theme.Hover;
        public override Color MenuItemSelectedGradientBegin => Theme.Hover;
        public override Color MenuItemSelectedGradientEnd => Theme.Hover;
        public override Color MenuItemPressedGradientBegin => Theme.Hover;
        public override Color MenuItemPressedGradientEnd => Theme.Hover;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Border;
        public override Color CheckBackground => Color.Transparent;
        public override Color CheckSelectedBackground => Color.Transparent;
        public override Color CheckPressedBackground => Color.Transparent;
    }
}
