// PORT-ONLY, owner-ruled addition (OD-0097, second amendment; first-release voice typing, VT-4, voice-SPEC V-13.1).
// voice-glow 0.2.1 (Jakub Antalik, MIT), dist/index.es.js via the design's voice-glow.js: the theme presets Ga (26-44), the defaults Rn (863-907),
// the type presets Pn (908-951), the per-type brightness and strength Ln / Cn (952-971), Wn, In, En (972-996) and the band colours Ue (996-999).
namespace Marrow.Core.Port.Voice.Glow;

/// <summary>The numbers a type and theme give each prop (<c>En(type, theme)</c> in the package).</summary>
internal sealed record VoiceGlowTypeDefaults
{
    public double Scale { get; init; } = 1;
    public double GlowSize { get; init; } = 1;
    public double ProcessingDuration { get; init; } = 1.1;
    public double ProcessingLevel { get; init; } = 0.55;
    public double ProcessingTravel { get; init; } = 1.55;
    public double ProcessingCurve { get; init; } = 2.1;
    public double CornerFollow { get; init; } = 0.45;
    public double StrokeOpacity { get; init; } = 1;
    public double InnerOpacity { get; init; } = 1;
    public double BloomOpacity { get; init; } = 1;
    public double Idle { get; init; } = 0.18;
    public double Reach { get; init; } = 1.2;
    public double Spread { get; init; } = 1.05;
    public double Flow { get; init; } = 48;
    public double Bend { get; init; } = 60;
    public double BandStrength { get; init; } = 1.55;
    public double BandWidth { get; init; } = 2.15;
    public double BandPosition { get; init; } = 0.35;
    public double BandCurve { get; init; } = 1.75;
    public double BandSpread { get; init; } = 0.87;
    public double BandSkew { get; init; } = 0.12;
    public double BandOffset { get; init; } = -27;
    public double BandTail { get; init; } = 0.59;
    public double BandTailPosition { get; init; } = 0.67;
    public double BandTailCurve { get; init; } = 2.4;
    public double BandTailOverflow { get; init; } = 15;
    public double BandAberration { get; init; } = 0.89;
    public double Distortion { get; init; } = 0.62;
    public double DistortionDetail { get; init; } = 2.3;
    public double GlowWidth { get; init; } = 0.65;
    public double GlowHeight { get; init; } = 1.25;
    public double LobeSpacing { get; init; } = 0.85;
    public double RangeWidth { get; init; } = 0.75;
    public double RangeHeight { get; init; } = 1;
    public double Softness { get; init; } = 1.07;
    public double CoreSize { get; init; } = 1;
    public double CoreLight { get; init; }
    public double CoreLightWidth { get; init; } = 1;
    public double CoreLightHeight { get; init; } = 1;
    public double StrokeScale { get; init; } = 1;
    public double InnerScale { get; init; } = 1;
    public double InnerHeight { get; init; } = 1;
    public double BloomScale { get; init; } = 1;
    public double BloomHeight { get; init; } = 1;

    /// <summary><c>En(type, theme)</c>: the package defaults, the light theme's changes (reach, spread and core light unless the type fixes them), the type preset, and the light band strength.</summary>
    internal static VoiceGlowTypeDefaults For(VoiceGlowType type, VoiceGlowTheme theme)
    {
        var d = new VoiceGlowTypeDefaults();
        var light = theme == VoiceGlowTheme.Light;
        if (light)
        {
            // `a.reach === void 0 && (r.reach = 1.8)`, `a.spread === void 0 && (r.spread = 0.8)`, `a.coreLight === void 0 && (r.coreLight = 1.8)`
            // (only the default type leaves reach and spread open; none of the three presets sets coreLight)
            if (type == VoiceGlowType.Default) d = d with { Reach = 1.8, Spread = 0.8 };
            d = d with { CoreLight = 1.8 };
        }

        d = type switch
        {
            VoiceGlowType.Default => d,
            VoiceGlowType.Pill => d with
            {
                Scale = 0.45, GlowSize = 0.95, StrokeOpacity = 1.2, InnerOpacity = 0.85, Reach = 1.35, Spread = 1.1, Flow = 0, Bend = 23,
                BandStrength = 1.55, BandWidth = 1.85, BandCurve = 1.95, BandSpread = 0.38, BandOffset = -16, BandTail = 0, ProcessingTravel = 2,
                CornerFollow = 0, Distortion = 0.45, DistortionDetail = 3, GlowWidth = 0.65, GlowHeight = 0.95, LobeSpacing = 0.45, RangeWidth = 0.8,
                RangeHeight = 0.7, Softness = 0.88, CoreSize = 0.25, StrokeScale = 1.25, InnerScale = 0.95, BloomScale = 1.05, BloomHeight = 2.25,
            },
            VoiceGlowType.Mobile => d with
            {
                Scale = 1.25, Spread = 0.45, Reach = 3, Flow = 60, Bend = 70, BandWidth = 2.4, BandCurve = 1.55, BandSpread = 0.9, BandOffset = -50,
                BandTail = 0.62, BandTailPosition = 0.42, BandTailCurve = 2.7, BandTailOverflow = 22, ProcessingDuration = 1.05, ProcessingLevel = 0.35,
                ProcessingTravel = 1, CornerFollow = 0.4, BandStrength = 1.8, DistortionDetail = 2, GlowWidth = 1.15, GlowHeight = 2.1, LobeSpacing = 1.35,
                RangeWidth = 1.25, RangeHeight = 1.2, Softness = 1.1,
            },
        };

        // In[type]: the band on white, whatever the dark strengths
        if (light) d = d with { BandStrength = type == VoiceGlowType.Pill ? 2 : 1.7 };
        return d;
    }
}

/// <summary>The theme presets <c>Ga</c> (what a colour scheme sets before the type does).</summary>
internal sealed record VoiceGlowThemePreset(
    double StrokeOpacity,
    double InnerOpacity,
    double BloomOpacity,
    string InnerShadow,
    double Saturation,
    double Brightness,
    double? HueRange,
    double? HueDuration,
    double? HueBase,
    double? Strength)
{
    internal static VoiceGlowThemePreset Dark { get; } = new(1.16, 0.47, 0.89, "rgba(255, 255, 255, 0.1)", 1.2, 1.1, null, null, null, null);

    internal static VoiceGlowThemePreset Light { get; } = new(1.2, 0.85, 0.5, "rgba(0, 0, 0, 0.08)", 1.6, 0.95, 40, 8.5, 5, 0.8);

    internal static VoiceGlowThemePreset For(VoiceGlowTheme theme) => theme == VoiceGlowTheme.Light ? Light : Dark;
}

/// <summary>What a type adds on a theme: <c>Wn(type, theme)</c> (<c>Ln</c> on dark, <c>Cn</c> on light).</summary>
internal readonly record struct VoiceGlowTypeTheme(double? Strength, double? Brightness, double? Saturation)
{
    internal static VoiceGlowTypeTheme For(VoiceGlowType type, VoiceGlowTheme theme) => theme == VoiceGlowTheme.Light
        ? type == VoiceGlowType.Mobile ? new VoiceGlowTypeTheme(1, null, null) : default
        : type switch
        {
            VoiceGlowType.Default => new VoiceGlowTypeTheme(null, 1.15, null),
            VoiceGlowType.Pill => new VoiceGlowTypeTheme(null, 1.35, 1.5),
            VoiceGlowType.Mobile => new VoiceGlowTypeTheme(1, 1.2, 1.5),
        };
}
