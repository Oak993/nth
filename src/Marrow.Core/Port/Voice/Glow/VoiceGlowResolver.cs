// PORT-ONLY, owner-ruled addition (OD-0097, second amendment; first-release voice typing, VT-4, voice-SPEC V-13.1).
using System.Globalization;
using System.Text.RegularExpressions;

namespace Marrow.Core.Port.Voice.Glow;

/// <summary>The prop resolution of <c>mountVoiceGlow</c> (voice-glow.js:1018-1150): theme and type presets, clamps, colours.</summary>
public static partial class VoiceGlowResolver
{
    private static readonly VoiceGlowBandColors DarkBandColors = new("255, 255, 255", "255, 70, 80", "90, 255, 150", "80, 140, 255");
    private static readonly VoiceGlowBandColors LightBandColors = new("197, 139, 255", "255, 122, 182", "126, 196, 255", "45, 255, 171");

    /// <summary>The lobes <c>we</c>: x 0, +-36, +-72, +-108; w 74/54/48/42; h 46/40/32/26 (V-13.1).</summary>
    public static IReadOnlyList<VoiceGlowLobe> Lobes { get; } =
    [
        new(0, 74, 46, 0),
        new(-36, 54, 40, 1),
        new(36, 54, 40, 1),
        new(-72, 48, 32, 2),
        new(72, 48, 32, 2),
        new(-108, 42, 26, 1),
        new(108, 42, 26, 1),
    ];

    /// <summary>Resolves a prop set as <c>mountVoiceGlow</c>'s <c>resolve()</c> does.</summary>
    public static VoiceGlowResolved Resolve(VoiceGlowProps props)
    {
        var p = props;
        var type = p.Type;
        var theme = p.Theme;
        var c = VoiceGlowTypeDefaults.For(type, theme);
        var f = Math.Max(0.05, p.Scale ?? c.Scale);
        var glowSize = p.GlowSize ?? c.GlowSize;
        var strokeOpacity = p.StrokeOpacity ?? c.StrokeOpacity;
        var innerOpacity = p.InnerOpacity ?? c.InnerOpacity;
        var bloomOpacity = p.BloomOpacity ?? c.BloomOpacity;
        var distortion = p.Distortion ?? c.Distortion;
        var coreLight = Math.Max(0, Math.Min(3, p.CoreLight ?? c.CoreLight));
        var themePreset = VoiceGlowThemePreset.For(theme);
        var radius = p.BorderRadius ?? 16;
        var typeTheme = VoiceGlowTypeTheme.For(type, theme);
        var strength = p.Strength ?? typeTheme.Strength ?? themePreset.Strength ?? 1;
        var hueRange = p.HueRange ?? themePreset.HueRange ?? 24;
        var hueDuration = p.HueDuration ?? themePreset.HueDuration ?? 12;
        var brightness = p.Brightness ?? typeTheme.Brightness ?? themePreset.Brightness;
        var saturation = p.Saturation ?? typeTheme.Saturation ?? themePreset.Saturation;
        var rangeWidth = (p.RangeWidth ?? c.RangeWidth) * f;
        var rangeHeight = (p.RangeHeight ?? c.RangeHeight) * f;
        var variant = string.IsNullOrEmpty(p.ColorVariant) ? "colorful" : p.ColorVariant;

        var style = new VoiceGlowStyleArgs
        {
            BorderRadius = radius,
            BorderWidth = 1,
            StrokeOpacity = themePreset.StrokeOpacity * strokeOpacity,
            InnerOpacity = themePreset.InnerOpacity * innerOpacity,
            BloomOpacity = themePreset.BloomOpacity * bloomOpacity,
            InnerShadow = themePreset.InnerShadow,
            ColorVariant = variant,
            Colors = p.Colors,
            Brightness = brightness,
            Saturation = saturation,
            Theme = theme,
            HueBase = themePreset.HueBase ?? 0,
            GlowSize = glowSize * f,
            GlowWidth = (p.GlowWidth ?? c.GlowWidth) * f,
            GlowHeight = (p.GlowHeight ?? c.GlowHeight) * f,
            StrokeScale = p.StrokeScale ?? c.StrokeScale,
            InnerScale = p.InnerScale ?? c.InnerScale,
            InnerHeight = p.InnerHeight ?? c.InnerHeight,
            BloomScale = p.BloomScale ?? c.BloomScale,
            BloomHeight = p.BloomHeight ?? c.BloomHeight,
            CoreSize = (p.CoreSize ?? c.CoreSize) * f,
            CoreLight = coreLight,
            CoreLightWidth = p.CoreLightWidth ?? c.CoreLightWidth,
            CoreLightHeight = p.CoreLightHeight ?? c.CoreLightHeight,
            RangeWidth = rangeWidth,
            RangeHeight = rangeHeight,
            Softness = p.Softness ?? c.Softness,
            Distortion = distortion > 0,
            Scale = f,
        };

        var defaultColors = theme == VoiceGlowTheme.Light ? LightBandColors : DarkBandColors;
        var given = p.BandColors;
        var config = new VoiceGlowConfig
        {
            Sensitivity = Math.Max(0, p.Sensitivity ?? 3.1),
            Threshold = Math.Max(0, Math.Min(0.95, p.Threshold ?? 0.015)),
            Attack = Math.Max(0, p.Attack ?? 0.325),
            Release = Math.Max(0, p.Release ?? 0.86),
            Idle = Math.Max(0, Math.Min(1, p.Idle ?? c.Idle)),
            BreatheDuration = Math.Max(0.2, p.BreatheDuration ?? 5.2),
            Reach = Math.Max(0, p.Reach ?? c.Reach),
            Spread = Math.Max(0, p.Spread ?? c.Spread),
            Bands = p.Bands ?? true,
            Flow = (p.Flow ?? c.Flow) * f,
            LobeSpacing = Math.Max(0.1, (p.LobeSpacing ?? c.LobeSpacing) * f),
            Bend = Math.Max(0, (p.Bend ?? c.Bend) * f),
            BandStrength = Math.Max(0, p.BandStrength ?? c.BandStrength),
            BandWidth = Math.Max(0, (p.BandWidth ?? c.BandWidth) * f),
            BandPosition = Math.Max(0, p.BandPosition ?? c.BandPosition),
            BandCurve = Math.Max(0.3, p.BandCurve ?? c.BandCurve),
            BandSpread = Math.Max(0.05, p.BandSpread ?? c.BandSpread),
            BandSkew = Math.Max(-0.9, Math.Min(0.9, p.BandSkew ?? c.BandSkew)),
            BandOffset = (p.BandOffset ?? c.BandOffset) * f,
            BandTail = Math.Max(0, Math.Min(1.5, p.BandTail ?? c.BandTail)),
            BandTailPosition = Math.Max(0, Math.Min(0.98, p.BandTailPosition ?? c.BandTailPosition)),
            BandTailCurve = Math.Max(0.5, p.BandTailCurve ?? c.BandTailCurve),
            BandTailOverflow = Math.Max(0, (p.BandTailOverflow ?? c.BandTailOverflow) * f),
            BandAberration = Math.Max(0, Math.Min(1, p.BandAberration ?? c.BandAberration)),
            RangeWidth = rangeWidth,
            RangeHeight = rangeHeight,
            Theme = theme,
            BandColors = new VoiceGlowBandColors(
                ColorTriple(given?.Core) ?? defaultColors.Core, ColorTriple(given?.Above) ?? defaultColors.Above,
                ColorTriple(given?.Mid) ?? defaultColors.Mid, ColorTriple(given?.Below) ?? defaultColors.Below),
            Distortion = Math.Max(0, Math.Min(1, distortion)),
            CoreLight = coreLight,
            Scale = f,
            Radius = radius,
            Processing = p.Processing,
            ProcessingDuration = Math.Max(0.05, p.ProcessingDuration ?? c.ProcessingDuration),
            ProcessingLevel = Math.Max(0, Math.Min(1, p.ProcessingLevel ?? c.ProcessingLevel)),
            ProcessingEase = Math.Max(0.05, p.ProcessingEase ?? 0.6),
            ProcessingTravel = Math.Max(0, p.ProcessingTravel ?? c.ProcessingTravel),
            ProcessingCurve = Math.Max(1, p.ProcessingCurve ?? c.ProcessingCurve),
            CornerFollow = Math.Max(0, Math.Min(1, p.CornerFollow ?? c.CornerFollow)),
            HueRange = Math.Max(0, hueRange),
            HueDuration = Math.Max(0.5, hueDuration),
            StaticColors = variant == "mono" || p.StaticColors,
            ReducedMotion = p.ReducedMotion,
        };

        var detail = (p.DistortionDetail ?? c.DistortionDetail) / f;
        return new VoiceGlowResolved(config, style, Math.Max(0, Math.Min(1, strength)), type, distortion > 0, coreLight > 0, detail);
    }

    /// <summary><c>Ne(text)</c>: <c>"r, g, b"</c> for a <c>#rgb</c>, <c>#rrggbb</c> or <c>rgb()</c> / <c>rgba()</c> text, else null.</summary>
    internal static string? ColorTriple(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var t = text.Trim();
        var hex = HexColor().Match(t);
        if (hex.Success)
        {
            var digits = hex.Groups[1].Value;
            if (digits.Length == 3) digits = string.Concat(digits.Select(ch => new string(ch, 2)));
            return string.Join(
                ", ",
                Enumerable.Range(0, 3).Select(i => int.Parse(digits.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)));
        }

        var rgb = RgbColor().Match(t);
        if (!rgb.Success) return null;
        return string.Join(", ", Enumerable.Range(1, 3).Select(i => Round(rgb.Groups[i].Value)));
    }

    // Math.round(+text): half up; an unparseable number is NaN
    private static string Round(string number) =>
        double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? Math.Floor(v + 0.5).ToString("0", CultureInfo.InvariantCulture)
            : "NaN";

    [GeneratedRegex("^#([0-9a-f]{3}|[0-9a-f]{6})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HexColor();

    [GeneratedRegex("^rgba?\\(\\s*([0-9.]+)\\s*,\\s*([0-9.]+)\\s*,\\s*([0-9.]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RgbColor();
}
