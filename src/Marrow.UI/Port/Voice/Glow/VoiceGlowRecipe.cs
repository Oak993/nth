// PORT-ONLY, owner-ruled addition (OD-0097, second amendment; first-release voice typing, VT-9, voice-SPEC V-13.1, V-13.2): the stylesheet recipe `Ya` of voice-glow.js
// (lines 244-576) as numbers. Ya turns the resolved props into a stylesheet whose lengths are CSS calc() of the driver's --vb-* variables; this keeps the part of every
// expression that does not change per frame (the multipliers, the roundings, the stops, the opacities as the stylesheet's text rounds them) and VoiceGlowPainter
// supplies the per-frame variables. Names in the comments are the design's. voice-glow 0.2.1 (Jakub Antalik, MIT; THIRD_PARTY_NOTICES).
using Marrow.Core.Port.Voice.Glow;
using SkiaSharp;

namespace Marrow.UI.Port.Voice.Glow;

/// <summary>
/// One layer's seven radial gradients (<c>dt</c>): the lobes <c>we</c> sized by <see cref="WidthFactor"/> and <see cref="HeightFactor"/>, all fading to
/// transparent at <see cref="FadePercent"/> of their radii, from colours at <see cref="Alpha"/>.
/// </summary>
internal sealed record GlowLobeSet(double Alpha, double WidthFactor, double HeightFactor, double Y, double FadePercent, IReadOnlyList<GlowRgb> Colors)
{
    /// <summary>The lobe's width in layer px before the driver's variables: <c>Math.round(l.w * sw)</c>.</summary>
    public double Width(int lobe) => VoiceGlowCss.Round(VoiceGlowResolver.Lobes[lobe].W * WidthFactor);

    /// <summary>The lobe's height in layer px before the driver's variables: <c>Math.round(l.h * sh)</c>.</summary>
    public double Height(int lobe) => VoiceGlowCss.Round(VoiceGlowResolver.Lobes[lobe].H * HeightFactor);
}

/// <summary>The elliptical mask <c>ie(rw, rh, N, ae)</c>: white at the centre, half at N %, <c>ae</c> at 85 % when above 0, nothing at the edge.</summary>
internal sealed record GlowEllipseMask(double Width, double Height, double HalfPercent, double Tail);

/// <summary>The stroke's wash at the source (<c>Ze</c>): white on dark (<see cref="Grey"/> 255), black on light (0), at two alphas and fading out at <see cref="EndPercent"/>.</summary>
internal sealed record GlowWash(double Width, double Height, byte Grey, double First, double Second, double SecondPercent, double EndPercent);

/// <summary>The epicentre's gradient (<c>coreLight</c> above 0): the numbers of the block in <c>Ya</c> lines 442-474.</summary>
internal sealed record GlowCore(double Width, double Height, double SolidPercent, double MidPercent, double MidAlpha, double EndPercent, double OpacityScale, double Blur);

/// <summary>What <c>Ya</c> computes once per prop set.</summary>
internal sealed class VoiceGlowRecipe
{
    /// <summary>The corner radius <c>a</c> and the stroke's border width <c>r</c>.</summary>
    public double Radius { get; private init; }

    public double BorderWidth { get; private init; }

    /// <summary><c>P = max(0, a - r)</c>: the stroke ring's outer radius.</summary>
    public double RingRadius { get; private init; }

    /// <summary><c>R</c>: the inner light's edge mask depth.</summary>
    public double EdgeMargin { get; private init; }

    /// <summary><c>I</c>: the inner shadow's blur.</summary>
    public double ShadowBlur { get; private init; }

    public SKColor InnerShadow { get; private init; }

    public bool Dark { get; private init; }

    /// <summary>The layers' opacities as the stylesheet writes them (<c>toFixed(2)</c>), mono's 0.6 applied.</summary>
    public double StrokeOpacity { get; private init; }

    public double InnerOpacity { get; private init; }

    public double BloomOpacity { get; private init; }

    /// <summary><c>brightness(B)</c> and <c>saturate(j)</c> (<c>toFixed(2)</c>) and the hue base in degrees.</summary>
    public double Brightness { get; private init; }

    public double Saturation { get; private init; }

    public double HueBase { get; private init; }

    public required GlowLobeSet Stroke { get; init; }

    public required GlowLobeSet Inner { get; init; }

    public required GlowLobeSet Bloom { get; init; }

    public required GlowEllipseMask StrokeMask { get; init; }

    public required GlowEllipseMask InnerMask { get; init; }

    public required GlowEllipseMask BloomMask { get; init; }

    /// <summary>The stroke's wash at the source (<c>Ze</c>).</summary>
    public required GlowWash Wash { get; init; }

    /// <summary>The bloom's blur radius <c>lt(10, h)</c> in layer px (before the layer factor).</summary>
    public double BloomBlur { get; private init; }

    public GlowCore? Core { get; private init; }

    /// <summary>True when the distortion is on: the warp mirrors exist and the base layers split at the band line.</summary>
    public bool Distortion { get; private init; }

    /// <summary>The unit-less scale <c>W</c> of the props.</summary>
    public double Scale { get; private init; }

    /// <summary>The stroke ring's area and the elements of V-13.2 do not depend on anything else; build the recipe of a resolved prop set.</summary>
    public static VoiceGlowRecipe From(VoiceGlowStyleArgs n)
    {
        var dark = n.Theme == VoiceGlowTheme.Dark;
        double R = VoiceGlowCss.Round(28 * n.Scale * 10) / 10, I = VoiceGlowCss.Round(9 * n.Scale * 10) / 10;
        var P = Math.Max(0, n.BorderRadius - n.BorderWidth);
        var v = VoiceGlowCss.Round(Math.Max(40, Math.Min(95, 70 * n.Softness)));
        double k(double x) => n.GlowWidth * x;
        double w(double x) => n.GlowHeight * x;

        // re: the palette, with the caller's colours over it where they parse
        var palette = VoiceGlowPalettes.Of(n.ColorVariant, dark);
        var colors = new GlowRgb[7];
        for (var a = 0; a < 7; a++)
        {
            var given = n.Colors is not null && a < n.Colors.Count ? VoiceGlowCss.Parse(n.Colors[a]) : null;
            colors[a] = given ?? palette[a];
        }

        var mono = n.ColorVariant == "mono" ? 0.6 : 1;
        var shadow = ParseShadow(n.InnerShadow);

        // the three lobe sets (dt arguments, Ya lines 276)
        var stroke = new GlowLobeSet(1, k(n.StrokeScale), w(n.StrokeScale), 2, v, colors);
        var inner = new GlowLobeSet(Alpha(0.46), k(0.9 * n.InnerScale), w(0.9 * n.InnerScale * n.InnerHeight), 0, v, colors);
        var bloom = new GlowLobeSet(Alpha(dark ? 0.9 : 0.7), k(1.15 * n.BloomScale), w(1.5 * n.BloomScale * n.BloomHeight), 0, Math.Min(95, v + 2), colors);

        GlowEllipseMask mask(double rw, double rh, double halfPercent, double tail) =>
            new(VoiceGlowCss.Tenth(rw * n.RangeWidth), VoiceGlowCss.Tenth(rh * n.RangeHeight), halfPercent, tail);

        // Ze: dark white, light black
        var wash = dark
            ? new GlowWash(VoiceGlowCss.Tenth(30 * n.CoreSize), VoiceGlowCss.Tenth(30 * n.CoreSize), 255, 0.45, 0.14, 30, 65)
            : new GlowWash(VoiceGlowCss.Tenth(40 * n.CoreSize), VoiceGlowCss.Tenth(30 * n.CoreSize), 0, 0.55, 0.22, 35, 70);

        GlowCore? core = null;
        if (n.CoreLight > 0)
        {
            var c = n.CoreLight;
            var d = Math.Max(0, Math.Min(2, c - 1)); // $
            var A = Math.Min(1, d);
            var N = Math.Max(0, d - 1);
            var ae = 1 + 0.3 * d;
            core = new GlowCore(
                Width: VoiceGlowCss.Tenth(120 * n.CoreLightWidth * ae * n.Scale),
                Height: VoiceGlowCss.Tenth(70 * n.CoreLightHeight * ae * n.Scale),
                SolidPercent: VoiceGlowCss.Tenth(45 * A + 27 * N),
                MidPercent: VoiceGlowCss.Tenth(40 + 25 * A + 15 * N),
                MidAlpha: VoiceGlowCss.Fixed(Math.Min(1, 0.55 + 0.35 * A + 0.1 * N), 2),
                EndPercent: VoiceGlowCss.Tenth(72 + 14 * A + 8 * N),
                OpacityScale: VoiceGlowCss.Fixed(Math.Min(1, c), 2) * VoiceGlowCss.Fixed(1.6 + 1.4 * d, 2),
                Blur: VoiceGlowCss.Tenth(VoiceGlowCss.Lt(8, n.GlowSize)));
        }

        return new VoiceGlowRecipe
        {
            Radius = n.BorderRadius,
            BorderWidth = n.BorderWidth,
            RingRadius = P,
            EdgeMargin = R,
            ShadowBlur = I,
            InnerShadow = shadow,
            Dark = dark,
            StrokeOpacity = VoiceGlowCss.Fixed(n.StrokeOpacity * mono, 2),
            InnerOpacity = VoiceGlowCss.Fixed(n.InnerOpacity * mono, 2),
            BloomOpacity = VoiceGlowCss.Fixed(n.BloomOpacity * mono, 2),
            Brightness = VoiceGlowCss.Fixed(n.Brightness, 2),
            Saturation = VoiceGlowCss.Fixed(n.Saturation, 2),
            HueBase = n.HueBase,
            Stroke = stroke,
            Inner = inner,
            Bloom = bloom,
            StrokeMask = mask(170, 64, 45, 0),
            InnerMask = mask(170, 64, 45, 0.3),
            BloomMask = mask(200, 130, 35, 0),
            Wash = wash,
            BloomBlur = VoiceGlowCss.Tenth(VoiceGlowCss.Lt(10, n.GlowSize)),
            Core = core,
            Distortion = n.Distortion,
            Scale = n.Scale,
        };
    }

    // ja(colour, alpha): the alpha as the stylesheet text writes it, to two digits (an alpha of 1 or more is the plain colour)
    private static double Alpha(double a) => a >= 1 ? 1 : VoiceGlowCss.Fixed(a, 2);

    // the inner shadow text of a theme preset ("rgba(255, 255, 255, 0.1)")
    private static SKColor ParseShadow(string text)
    {
        var m = System.Text.RegularExpressions.Regex.Match(text, @"^rgba\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*([\d.]+)\s*\)$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (!m.Success) return SKColors.Transparent;
        var c = System.Globalization.CultureInfo.InvariantCulture;
        return new SKColor(byte.Parse(m.Groups[1].Value, c), byte.Parse(m.Groups[2].Value, c), byte.Parse(m.Groups[3].Value, c),
            (byte)Math.Clamp(VoiceGlowCss.Round(double.Parse(m.Groups[4].Value, c) * 255), 0, 255));
    }
}
