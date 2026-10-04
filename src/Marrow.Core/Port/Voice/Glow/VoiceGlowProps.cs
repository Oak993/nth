// PORT-ONLY, owner-ruled addition (OD-0097, second amendment; first-release voice typing, VT-4, voice-SPEC V-13.1, V-13.3).
namespace Marrow.Core.Port.Voice.Glow;

/// <summary>
/// The props <c>mountVoiceGlow</c> resolves (voice-glow.js:1018-1150): every one the component takes except the host-side ones (id,
/// callbacks, <c>active</c>, <c>paused</c>, the level source). A null leaves the type and theme's default.
/// </summary>
public sealed record VoiceGlowProps
{
    public VoiceGlowType Type { get; init; } = VoiceGlowType.Default;
    public VoiceGlowTheme Theme { get; init; } = VoiceGlowTheme.Dark;
    public double? Scale { get; init; }
    public double? GlowSize { get; init; }
    public double? StrokeOpacity { get; init; }
    public double? InnerOpacity { get; init; }
    public double? BloomOpacity { get; init; }
    public double? Distortion { get; init; }
    public double? DistortionDetail { get; init; }
    public double? CoreLight { get; init; }
    public double? BorderRadius { get; init; }
    public double? Strength { get; init; }
    public double? HueRange { get; init; }
    public double? HueDuration { get; init; }
    public double? Brightness { get; init; }
    public double? Saturation { get; init; }
    public double? RangeWidth { get; init; }
    public double? RangeHeight { get; init; }
    public double? GlowWidth { get; init; }
    public double? GlowHeight { get; init; }
    public double? StrokeScale { get; init; }
    public double? InnerScale { get; init; }
    public double? InnerHeight { get; init; }
    public double? BloomScale { get; init; }
    public double? BloomHeight { get; init; }
    public double? CoreSize { get; init; }
    public double? CoreLightWidth { get; init; }
    public double? CoreLightHeight { get; init; }
    public double? Softness { get; init; }
    public double? Sensitivity { get; init; }
    public double? Threshold { get; init; }
    public double? Attack { get; init; }
    public double? Release { get; init; }
    public double? Idle { get; init; }
    public double? BreatheDuration { get; init; }
    public double? Reach { get; init; }
    public double? Spread { get; init; }
    public bool? Bands { get; init; }
    public double? Flow { get; init; }
    public double? LobeSpacing { get; init; }
    public double? Bend { get; init; }
    public double? BandStrength { get; init; }
    public double? BandWidth { get; init; }
    public double? BandPosition { get; init; }
    public double? BandCurve { get; init; }
    public double? BandSpread { get; init; }
    public double? BandSkew { get; init; }
    public double? BandOffset { get; init; }
    public double? BandTail { get; init; }
    public double? BandTailPosition { get; init; }
    public double? BandTailCurve { get; init; }
    public double? BandTailOverflow { get; init; }
    public double? BandAberration { get; init; }
    public bool Processing { get; init; }
    public double? ProcessingDuration { get; init; }
    public double? ProcessingLevel { get; init; }
    public double? ProcessingEase { get; init; }
    public double? ProcessingTravel { get; init; }
    public double? ProcessingCurve { get; init; }
    public double? CornerFollow { get; init; }

    /// <summary><c>colorful</c> (default) or <c>mono</c> (which also stops the hue drift).</summary>
    public string? ColorVariant { get; init; }

    /// <summary>The package's <c>colors</c>: texts the stylesheet reads as colours (null entries and unparseable texts fall back).</summary>
    public IReadOnlyList<string?>? Colors { get; init; }

    public VoiceGlowBandColorProps? BandColors { get; init; }
    public bool StaticColors { get; init; }
    public bool ReducedMotion { get; init; }
}
