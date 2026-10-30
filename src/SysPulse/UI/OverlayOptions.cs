namespace SysPulse.UI;

internal enum OverlayLayout { Stacked, OneLine }

internal enum OverlaySize { Small, Medium, Large }

/// <summary>Where the overlay sticks on the screen; it grows away from that edge as values change.</summary>
internal enum OverlayAnchor { TopLeft, TopCenter, TopRight, MiddleLeft, Center, MiddleRight, BottomLeft, BottomCenter, BottomRight }

/// <summary>
/// How the overlay looks and where it sits. Offsets are physical pixels from
/// the anchored edges of the screen's working area (inward), or a signed shift
/// for centered anchors.
/// </summary>
internal sealed record OverlayOptions(
    OverlayLayout Layout,
    OverlaySize Size,
    int Opacity,
    bool Background,
    OverlayAnchor Anchor,
    int OffsetX,
    int OffsetY,
    string? Screen)
{
    public const int MinOpacity = 20;

    public static OverlayOptions Default => new(
        OverlayLayout.Stacked, OverlaySize.Medium, 100, false, OverlayAnchor.TopRight,
        DefaultOffsetX(OverlayAnchor.TopRight), DefaultOffsetY(OverlayAnchor.TopRight), null);

    public static int DefaultOffsetX(OverlayAnchor a) => (int)a % 3 == 1 ? 0 : Theme.S(10);

    // Top row sits just under a maximized window's caption buttons.
    public static int DefaultOffsetY(OverlayAnchor a) => (int)a / 3 switch { 0 => Theme.S(40), 1 => 0, _ => Theme.S(10) };

    public static OverlayOptions Load()
    {
        var d = Default;
        var anchor = Enum.TryParse(AppSettings.Get("overlay.anchor"), out OverlayAnchor a) ? a : d.Anchor;
        return new OverlayOptions(
            Enum.TryParse(AppSettings.Get("overlay.layout"), out OverlayLayout l) ? l : d.Layout,
            Enum.TryParse(AppSettings.Get("overlay.size"), out OverlaySize s) ? s : d.Size,
            Math.Clamp(AppSettings.GetInt("overlay.opacity") ?? d.Opacity, MinOpacity, 100),
            AppSettings.GetBool("overlay.background", d.Background),
            anchor,
            // overlay.right / overlay.top: positions saved by the first version (always top-right).
            AppSettings.GetInt("overlay.dx") ?? AppSettings.GetInt("overlay.right") ?? DefaultOffsetX(anchor),
            AppSettings.GetInt("overlay.dy") ?? AppSettings.GetInt("overlay.top") ?? DefaultOffsetY(anchor),
            AppSettings.Get("overlay.screen"));
    }

    public void Save() => AppSettings.SetMany(
        ("overlay.layout", Layout.ToString()),
        ("overlay.size", Size.ToString()),
        ("overlay.opacity", Opacity.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ("overlay.background", Background ? "1" : "0"),
        ("overlay.anchor", Anchor.ToString()),
        ("overlay.dx", OffsetX.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ("overlay.dy", OffsetY.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ("overlay.screen", Screen),
        ("overlay.right", null),
        ("overlay.top", null));
}
