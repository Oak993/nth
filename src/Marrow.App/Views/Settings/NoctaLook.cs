// PORT-ONLY (OWNER OD-0143, ED-NEW-nocta-1): the materials of the Nocta switch, the owner's locked toggle, measured from design 7 "Nocta" of
// E:\Droppy\work\design\toggles\index.html (CSS "7 Nocta" and the shared .tg rules). Every fixed brush, pen, shape and shadow is made once for
// the process (immutable, so any UI thread may draw it); the accent's lights are per switch and are recoloured in place, never rebuilt.
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Marrow.App.Views.Settings;

internal static class NoctaLook
{
    internal const double TrackWidth = 46, TrackHeight = 26, Inset = 2, KnobSize = 22, Travel = TrackWidth - 2 * Inset - KnobSize; // 20
    internal const double LumeSize = 5, LumeInset = 8.5; // .lume: left/top 8.5, 5 x 5, at the dome's centre
    /// <summary>--acc-l: color-mix(in oklab, accent 55%, #fff), the accent as light.</summary>
    internal const double LumeWhite = 0.45;

    private static readonly RelativePoint Top = new(0, 0, RelativeUnit.Relative), Bottom = new(0, 1, RelativeUnit.Relative);
    private static Color Rgb(uint rgb, double alpha = 1) => Color.FromArgb((byte)Math.Round(alpha * 255), (byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
    private static Color White(double alpha) => Rgb(0xFFFFFF, alpha);
    private static Color Black(double alpha) => Rgb(0x000000, alpha);
    private static ImmutableGradientStop Stop(double offset, Color color) => new(offset, color);
    private static ImmutableLinearGradientBrush Vertical(params ImmutableGradientStop[] stops) =>
        new(stops, 1, null, null, GradientSpreadMethod.Pad, Top, Bottom);

    /// <summary>.tg: 46 x 26, radius 13.</summary>
    internal static readonly RoundedRect TrackShape = new(new Rect(0, 0, TrackWidth, TrackHeight), TrackHeight / 2);
    /// <summary>.k: a 22 circle, drawn in the knob's own space.</summary>
    internal static readonly RoundedRect KnobShape = new(new Rect(0, 0, KnobSize, KnobSize), KnobSize / 2);
    internal static readonly RoundedRect LumeShape = new(new Rect(LumeInset, LumeInset, LumeSize, LumeSize), LumeSize / 2);
    internal static readonly Point KnobCentre = new(KnobSize / 2, KnobSize / 2);

    /// <summary>.t-noc .black: linear-gradient(180deg, #09090a, #141416 55%, #0a0a0b).</summary>
    internal static readonly IImmutableBrush Track = Vertical(Stop(0, Rgb(0x09090A)), Stop(.55, Rgb(0x141416)), Stop(1, Rgb(0x0A0A0B)));
    /// <summary>.black's box-shadow: inset 0 0 0 .5px rgba(255,255,255,.09), inset 0 1px 0 rgba(255,255,255,.06), inset 0 -1px 2px rgba(0,0,0,.6),
    /// 0 1px 2px rgba(0,0,0,.5).</summary>
    internal static readonly BoxShadows TrackShadows = new(
        new BoxShadow { OffsetY = 1, Blur = 2, Color = Black(.5) },
        [new BoxShadow { Spread = .5, Color = White(.09), IsInset = true }, new BoxShadow { OffsetY = 1, Color = White(.06), IsInset = true },
         new BoxShadow { OffsetY = -1, Blur = 2, Color = Black(.6), IsInset = true }]);
    /// <summary>.t-noc .refl: linear-gradient(180deg, rgba(255,255,255,.07) 0, transparent 40%, transparent 80%, rgba(255,255,255,.03) 100%).</summary>
    internal static readonly IImmutableBrush Reflection = Vertical(Stop(0, White(.07)), Stop(.4, White(0)), Stop(.8, White(0)), Stop(1, White(.03)));

    /// <summary>The ground the contact shadows are cast from: the dome's own outer black, wholly under the dome (which is never smaller).</summary>
    internal static readonly IImmutableBrush ShadowGround = new ImmutableSolidColorBrush(Rgb(0x060607));
    /// <summary>.t-noc .sh: 0 1px 1px rgba(0,0,0,.8), 0 3px 7px rgba(0,0,0,.6).</summary>
    internal static readonly BoxShadows Contact = new(new BoxShadow { OffsetY = 1, Blur = 1, Color = Black(.8) }, [new BoxShadow { OffsetY = 3, Blur = 7, Color = Black(.6) }]);
    /// <summary>.t-noc .shl (hover and press): 0 1px 1.5px rgba(0,0,0,.8), 0 5px 11px rgba(0,0,0,.6).</summary>
    internal static readonly BoxShadows ContactDeep = new(new BoxShadow { OffsetY = 1, Blur = 1.5, Color = Black(.8) }, [new BoxShadow { OffsetY = 5, Blur = 11, Color = Black(.6) }]);

    /// <summary>.t-noc .dome: radial-gradient(circle at 50% 112%, #3a3d44 0, #16171a 46%, #060607 82%); the circle's radius is the farthest corner,
    /// hypot(11, 24.64) = 26.98, 1.226 of the knob.</summary>
    internal static readonly IImmutableBrush Dome = new ImmutableRadialGradientBrush(
        [Stop(0, Rgb(0x3A3D44)), Stop(.46, Rgb(0x16171A)), Stop(.82, Rgb(0x060607))], 1, null, null, GradientSpreadMethod.Pad,
        new RelativePoint(.5, 1.12, RelativeUnit.Relative), new RelativePoint(.5, 1.12, RelativeUnit.Relative),
        new RelativeScalar(Math.Sqrt(11 * 11 + 24.64 * 24.64) / KnobSize, RelativeUnit.Relative), new RelativeScalar(Math.Sqrt(11 * 11 + 24.64 * 24.64) / KnobSize, RelativeUnit.Relative));

    /// <summary>.t-noc .soft, the softbox on the crown: left/right 21%, top 6%, height 27% of the knob; radius 50% 50% 45% 45% / 70% 70% 30% 30%.</summary>
    internal static readonly RoundedRect SoftShape = Soft();
    private static RoundedRect Soft()
    {
        var rect = new Rect(.21 * KnobSize, .06 * KnobSize, .58 * KnobSize, .27 * KnobSize);
        var top = new Vector(.5 * rect.Width, .7 * rect.Height); var bottom = new Vector(.45 * rect.Width, .3 * rect.Height);
        return new RoundedRect(rect, top, top, bottom, bottom);
    }
    /// <summary>.soft: linear-gradient(180deg, rgba(255,255,255,.66), rgba(255,255,255,.14) 62%, transparent).</summary>
    internal static readonly IImmutableBrush SoftLight = Vertical(Stop(0, White(.66)), Stop(.62, White(.14)), Stop(1, White(0)));

    /// <summary>.t-noc .rim: inset 0 0 0 .5px rgba(255,255,255,.2), inset 0 -1.5px 2px rgba(255,255,255,.13), inset 0 1px 1px rgba(0,0,0,.5).</summary>
    internal static readonly BoxShadows Rim = new(
        new BoxShadow { Spread = .5, Color = White(.2), IsInset = true },
        [new BoxShadow { OffsetY = -1.5, Blur = 2, Color = White(.13), IsInset = true }, new BoxShadow { OffsetY = 1, Blur = 1, Color = Black(.5), IsInset = true }]);
    internal static readonly IImmutableBrush Clear = new ImmutableSolidColorBrush(Colors.Transparent);

    /// <summary>.lumeoff: inset 0 0 0 .8px rgba(255,255,255,.28), an empty ring (stroked on its centre line, radius 2.1).</summary>
    internal static readonly ImmutablePen LumeOffRing = new(new ImmutableSolidColorBrush(White(.28)), .8);
    internal const double LumeOffRadius = LumeSize / 2 - .4;
    /// <summary>.t-noc .hv (hover): inset 0 0 0 .8px rgba(255,255,255,.4).</summary>
    internal static readonly ImmutablePen HoverRing = new(new ImmutableSolidColorBrush(White(.4)), .8);
    internal const double HoverRadius = KnobSize / 2 - .4;

    /// <summary>.tg::after (focus-visible): inset -3px, radius 16, box-shadow 0 0 0 2px; the 2 px ring is stroked 4 out from the track, radius 17.</summary>
    internal static readonly RoundedRect FocusShape = new(new Rect(-4, -4, TrackWidth + 8, TrackHeight + 8), TrackHeight / 2 + 4);

    /// <summary>.tg::before, inset -8px -6px: the zone that hovers and presses the switch, 6 beyond the track at either side and 8 above and below
    /// (a plain rectangle: the pseudo-element has no radius).</summary>
    internal static readonly Rect HitZone = new(-6, -8, TrackWidth + 12, TrackHeight + 16);

    /// <summary>--acc-l = color-mix(in oklab, accent 55%, #fff): the accent 0.45 of the way to white in Oklab, its alpha kept (the accent is opaque).
    /// Computed with doubles only, so a live accent change allocates nothing.</summary>
    internal static Color Lume(Color accent)
    {
        var (l, a, b) = ToOklab(accent.R / 255.0, accent.G / 255.0, accent.B / 255.0);
        var (wl, wa, wb) = ToOklab(1, 1, 1);
        var (r, g, bl) = FromOklab(l + (wl - l) * LumeWhite, a + (wa - a) * LumeWhite, b + (wb - b) * LumeWhite);
        return Color.FromArgb(accent.A, Byte(r), Byte(g), Byte(bl));
    }

    /// <summary>A colour at a share of its alpha (color-mix(in oklab, c p%, transparent) for an opaque c).</summary>
    internal static Color Faded(Color color, double alpha) => Color.FromArgb((byte)Math.Round(color.A * alpha), color.R, color.G, color.B);

    private static byte Byte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255, MidpointRounding.AwayFromZero);
    private static double Linear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    private static double Encoded(double v) { v = Math.Clamp(v, 0, 1); return v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055; }
    private static (double L, double A, double B) ToOklab(double red, double green, double blue)
    {
        double r = Linear(red), g = Linear(green), b = Linear(blue);
        var l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        var m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        var s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        return (0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s, 1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s, 0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
    }
    private static (double R, double G, double B) FromOklab(double lightness, double a, double b)
    {
        var l = Math.Pow(lightness + 0.3963377774 * a + 0.2158037573 * b, 3);
        var m = Math.Pow(lightness - 0.1055613458 * a - 0.0638541728 * b, 3);
        var s = Math.Pow(lightness - 0.0894841775 * a - 1.2914855480 * b, 3);
        return (Encoded(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s), Encoded(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s), Encoded(-0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s));
    }
}
