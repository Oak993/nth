// PORT-ONLY: web surfaces W3a, the look of a workspace pane (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.12 "pane
// cards ... are tinted layers over [the window's one blur] (tint, hairline specular rim lit from the top left, soft wide
// shadow, 12 DIP radius, 8 DIP gaps), not a second blur ... Header controls are plain glyphs with a tinted hover fill ...
// Focused pane: rim brightens slightly"; 10 "text at least 4.5:1"; ED-NEW-web-w3a-1). No Swift counterpart. The dark
// values are the approved canvas's (index.html: .gl, ".win .pane.gl" with no backdrop filter, .pane.exp, .ph, .hb,
// .tabg, --t1..--t3, --line, --fill2); the light values are their mirror on nth's light window. The third text step is
// .62, not the canvas's .48, so it keeps 4.5:1 on the card (as NthUiTokens does).
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.Core.Models;
using Marrow.UI.Port;

namespace Marrow.UI.Panes;

/// <summary>The colours and sizes of a workspace pane.</summary>
public static class PaneTokens
{
    /// <summary>A pane card's corner radius.</summary>
    public const double CornerRadius = 12;

    /// <summary>The header's height (3.12).</summary>
    public const double HeaderHeight = PaneKinds.HeaderHeight;

    /// <summary>A header button (canvas .hb).</summary>
    public const double ButtonSize = 26;

    /// <summary>The space between header items (canvas .ph gap).</summary>
    public const double HeaderGap = 6;

    /// <summary>The header's leading padding (canvas .ph padding-left).</summary>
    public const double HeaderLeading = 12;

    /// <summary>The header's trailing padding (canvas .ph padding-right).</summary>
    public const double HeaderTrailing = 5;

    /// <summary>The kind glyph in the header (canvas .ph > .ic).</summary>
    public const double GlyphSize = 14;

    private static SwiftColor Ink(double opacity) => new(red: 0, green: 0, blue: 0, opacity: opacity);

    private static SwiftColor Paper(double opacity) => new(red: 1, green: 1, blue: 1, opacity: opacity);

    private static SwiftColor Mist(double opacity) => new(red: 236 / 255.0, green: 236 / 255.0, blue: 238 / 255.0, opacity: opacity);

    /// <summary>The title and a chosen tab (canvas --t1).</summary>
    public static SwiftColor Text1 { get; } = SwiftColor.Dynamic(light: Ink(.86), dark: Mist(1));

    /// <summary>Header glyphs and buttons at rest (canvas --t2).</summary>
    public static SwiftColor Text2 { get; } = SwiftColor.Dynamic(light: Ink(.62), dark: Mist(.68));

    /// <summary>The title's detail and tabs not chosen (canvas --t3, raised to keep 4.5:1).</summary>
    public static SwiftColor Text3 { get; } = SwiftColor.Dynamic(light: Ink(.56), dark: Mist(.62));

    /// <summary>The header's hairline (canvas --line).</summary>
    public static SwiftColor Line { get; } = SwiftColor.Dynamic(light: Ink(.08), dark: Paper(.08));

    /// <summary>A header button under the pointer and a chosen tab (canvas --fill2).</summary>
    public static SwiftColor HoverFill { get; } = SwiftColor.Dynamic(light: Ink(.06), dark: Paper(.10));

    /// <summary>The working chip's tint (canvas .pchip: --ok at .13 with a .4 rim).</summary>
    public static SwiftColor ChipFill { get; } = SwiftColor.Dynamic(
        light: new SwiftColor(red: 0.16, green: 0.55, blue: 0.33, opacity: .12),
        dark: new SwiftColor(red: 91 / 255.0, green: 209 / 255.0, blue: 139 / 255.0, opacity: .13));

    public static SwiftColor ChipRim { get; } = SwiftColor.Dynamic(
        light: new SwiftColor(red: 0.16, green: 0.55, blue: 0.33, opacity: .35),
        dark: new SwiftColor(red: 91 / 255.0, green: 209 / 255.0, blue: 139 / 255.0, opacity: .40));

    public static SwiftColor ChipDot { get; } = SwiftColor.Dynamic(
        light: new SwiftColor(red: 0.13, green: 0.55, blue: 0.30),
        dark: new SwiftColor(red: 91 / 255.0, green: 209 / 255.0, blue: 139 / 255.0));

    private static bool IsDark(ThemeVariant? theme) => theme == ThemeVariant.Dark;

    /// <summary>
    /// The card's tint: the canvas .gl gradient (white .075 at the top, .022 at 55 %, .04 at the foot) over its dark base
    /// (rgba(22, 22, 27, .48)); an expanded pane is denser (canvas .pane.exp: .06 to .02 over rgba(18, 18, 22, .94)), since
    /// it sits over the other panes.
    /// </summary>
    public static IBrush CardFill(ThemeVariant? theme, bool expanded)
    {
        var dark = IsDark(theme);
        GradientStops stops;
        if (expanded)
        {
            stops = dark
                ? [Stop(Over(Color.FromArgb(15, 255, 255, 255), Color.FromArgb(240, 18, 18, 22)), 0), Stop(Over(Color.FromArgb(5, 255, 255, 255), Color.FromArgb(240, 18, 18, 22)), 1)]
                : [Stop(Color.FromArgb(246, 252, 252, 253), 0), Stop(Color.FromArgb(242, 247, 247, 249), 1)];
        }
        else
        {
            stops = dark
                ? [Stop(Over(Color.FromArgb(19, 255, 255, 255), Color.FromArgb(122, 22, 22, 27)), 0), Stop(Over(Color.FromArgb(6, 255, 255, 255), Color.FromArgb(122, 22, 22, 27)), .55), Stop(Over(Color.FromArgb(10, 255, 255, 255), Color.FromArgb(122, 22, 22, 27)), 1)]
                : [Stop(Color.FromArgb(178, 255, 255, 255), 0), Stop(Color.FromArgb(150, 255, 255, 255), .55), Stop(Color.FromArgb(160, 255, 255, 255), 1)];
        }
        return new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative), GradientStops = stops };
    }

    /// <summary>
    /// The hairline specular rim lit from the top left (canvas .gl::after: a 140° gradient of white .5, .08 at 28 %, .03 at
    /// 62 % and .2). A focused pane's rim brightens slightly (3.12).
    /// </summary>
    public static IBrush Rim(ThemeVariant? theme, bool focused)
    {
        var lift = focused ? 1.35 : 1;
        GradientStops stops = IsDark(theme)
            ? [Stop(White(.5 * lift), 0), Stop(White(.08 * lift), .28), Stop(White(.03 * lift), .62), Stop(White(.2 * lift), 1)]
            : [Stop(White(.9), 0), Stop(Black(.07 * lift), .28), Stop(Black(.06 * lift), .62), Stop(Black(.09 * lift), 1)];
        // 140°: from the top left toward the bottom right, a little steeper than the diagonal.
        return new LinearGradientBrush { StartPoint = new RelativePoint(0.18, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0.82, 1, RelativeUnit.Relative), GradientStops = stops };
    }

    /// <summary>The soft wide shadow (canvas .gl: inset 0 0 10 white .035, 0 1 2 black .35, 0 18 44 -14 black .62).</summary>
    public static BoxShadows Shadow(ThemeVariant? theme, bool lifted = false)
    {
        if (IsDark(theme))
        {
            var deep = lifted ? new BoxShadow { OffsetY = 30, Blur = 70, Spread = -10, Color = Black(.8) } : new BoxShadow { OffsetY = 18, Blur = 44, Spread = -14, Color = Black(.62) };
            return new BoxShadows(new BoxShadow { IsInset = true, Blur = 10, Color = White(.035) }, [new BoxShadow { OffsetY = 1, Blur = 2, Color = Black(.35) }, deep]);
        }
        var soft = lifted ? new BoxShadow { OffsetY = 26, Blur = 60, Spread = -12, Color = Black(.22) } : new BoxShadow { OffsetY = 14, Blur = 40, Spread = -14, Color = Black(.16) };
        return new BoxShadows(new BoxShadow { OffsetY = 1, Blur = 2, Color = Black(.08) }, [soft]);
    }

    private static GradientStop Stop(Color color, double offset) => new(color, offset);

    private static Color White(double alpha) => Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), 255, 255, 255);

    private static Color Black(double alpha) => Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), 0, 0, 0);

    // Source-over of a tint on a base colour, alpha kept as the union.
    private static Color Over(Color top, Color under)
    {
        double ta = top.A / 255.0, ua = under.A / 255.0;
        var a = ta + ua * (1 - ta);
        if (a <= 0) return Colors.Transparent;
        byte Mix(byte t, byte u) => (byte)Math.Round((t * ta + u * ua * (1 - ta)) / a);
        return Color.FromArgb((byte)Math.Round(a * 255), Mix(top.R, under.R), Mix(top.G, under.G), Mix(top.B, under.B));
    }
}
