using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace SysPulse.UI;

/// <summary>Dark palette, fonts, icon glyphs and DPI scaling shared by every screen.</summary>
internal static class Theme
{
    // ---- surfaces & ink ----------------------------------------------------
    public static readonly Color Window = Hex("#111110");
    public static readonly Color Surface = Hex("#1A1A19");
    public static readonly Color SurfaceAlt = Hex("#222221");
    public static readonly Color Hover = Hex("#2A2A28");
    public static readonly Color Border = Hex("#2C2C2A");
    public static readonly Color BorderStrong = Hex("#383835");
    public static readonly Color Grid = Hex("#262624");

    public static readonly Color Text = Hex("#F0EFEC");
    public static readonly Color TextMuted = Hex("#C3C2B7");
    public static readonly Color TextSubtle = Hex("#8A8983");

    public static readonly Color Accent = Hex("#3987E5");
    public static readonly Color AccentHover = Hex("#5598E7");
    public static readonly Color AccentPressed = Hex("#2A78D6");
    public static readonly Color AccentSoft = Hex("#172436");

    // ---- series (fixed categorical order: blue, orange, aqua) ---------------
    public static readonly Color CpuSeries = Hex("#3987E5");
    public static readonly Color GpuSeries = Hex("#D95926");
    public static readonly Color MemorySeries = Hex("#199E70");

    // ---- status (reserved: always shown with a label or icon) --------------
    public static readonly Color Good = Hex("#0CA30C");
    public static readonly Color Warning = Hex("#FAB219");
    public static readonly Color Serious = Hex("#EC835A");
    public static readonly Color Critical = Hex("#D03B3B");
    public static readonly Color CriticalHover = Hex("#B83232");

    /// <summary>Status color for a temperature, or null when it's in the normal range.</summary>
    public static Color? TempStatus(double? celsius) => celsius switch
    {
        >= 95 => Critical,
        >= 85 => Serious,
        >= 75 => Warning,
        _ => null,
    };

    // ---- type --------------------------------------------------------------
    public const string FontName = "Segoe UI";
    public static readonly Font Body = new(FontName, 9f);
    public static readonly Font BodyBold = new("Segoe UI Semibold", 9f);
    public static readonly Font Small = new(FontName, 8.25f);
    public static readonly Font SmallBold = new("Segoe UI Semibold", 8.25f);
    public static readonly Font Label = new("Segoe UI Semibold", 8f);
    public static readonly Font Title = new("Segoe UI Semibold", 15f);
    public static readonly Font Section = new("Segoe UI Semibold", 10.5f);
    public static readonly Font Hero = new("Segoe UI Semibold", 24f);
    public static readonly Font HeroUnit = new("Segoe UI Semibold", 12f);
    public static readonly Font Stat = new("Segoe UI Semibold", 11f);

    public static readonly string IconFontName = PickIconFont();
    private static readonly Dictionary<float, Font> IconFonts = [];

    public static Font Icon(float size)
    {
        if (!IconFonts.TryGetValue(size, out var f))
            IconFonts[size] = f = new Font(IconFontName, size, GraphicsUnit.Point);
        return f;
    }

    private static string PickIconFont()
    {
        using var installed = new InstalledFontCollection();
        var names = installed.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return names.Contains("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    }

    // ---- DPI ---------------------------------------------------------------
    public static float DpiScale { get; private set; } = 1f;

    public static void InitDpi()
    {
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        DpiScale = Math.Max(1f, g.DpiX / 96f);
    }

    /// <summary>Scale a 96-DPI pixel measurement to the current DPI.</summary>
    public static int S(int px) => (int)Math.Round(px * DpiScale);

    public static float S(float px) => px * DpiScale;

    public static Padding S(int left, int top, int right, int bottom) => new(S(left), S(top), S(right), S(bottom));

    public static Size S(int w, int h) => new(S(w), S(h));

    // ---- drawing helpers ---------------------------------------------------
    public static Color Hex(string hex) => ColorTranslator.FromHtml(hex);

    public static Color Blend(Color a, Color b, double t) => Color.FromArgb(
        (int)Math.Round(a.R + (b.R - a.R) * t),
        (int)Math.Round(a.G + (b.G - a.G) * t),
        (int)Math.Round(a.B + (b.B - a.B) * t));

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void DrawGlyph(Graphics g, string glyph, float size, Color color, Rectangle bounds)
    {
        TextRenderer.DrawText(g, glyph, Icon(size), bounds, color,
            TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
    }

    public const TextFormatFlags LeftLine = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
    public const TextFormatFlags RightLine = LeftLine | TextFormatFlags.Right;

    public static void DrawText(Graphics g, string text, Font font, Color color, Rectangle bounds, TextFormatFlags flags = LeftLine) =>
        TextRenderer.DrawText(g, text, font, bounds, color, flags);

    public static int TextWidth(string text, Font font) =>
        TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
}

/// <summary>Segoe Fluent Icons / MDL2 code points.</summary>
internal static class Glyph
{
    public const string Cpu = "";
    public const string Gpu = "";
    public const string Memory = "";
    public const string Rocket = "";
    public const string Drive = "";
    public const string Network = "";
    public const string Battery = "";
    public const string Plug = "";
    public const string Clock = "";
    public const string Shield = "";
    public const string Close = "";
    public const string Warning = "";
    public const string Info = "";
    public const string Leaf = "";
    public const string Speed = "";
    public const string Window = "";
    public const string Background = "";
    public const string Thermometer = "";
    public const string Overlay = "";
    public const string Pulse = "";
    public const string Settings = "";
}
