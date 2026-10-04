// PORT-ONLY, owner-ruled addition (OD-0097, second amendment; first-release voice typing, VT-4, voice-SPEC V-13.1, V-13.2).
namespace Marrow.Core.Port.Voice.Glow;

/// <summary>
/// The driver's configuration, the <c>cfg</c> object <c>mountVoiceGlow</c> builds (voice-glow.js:1055-1081) without its element id and its
/// <c>paused</c> flag. Two equal configs are one registration: a change makes the driver's next frame a 1/60 s one (see <see cref="VoiceGlowDriver"/>).
/// </summary>
public sealed record VoiceGlowConfig
{
    public double Sensitivity { get; init; }
    public double Threshold { get; init; }
    public double Attack { get; init; }
    public double Release { get; init; }
    public double Idle { get; init; }
    public double BreatheDuration { get; init; }
    public double Reach { get; init; }
    public double Spread { get; init; }
    public bool Bands { get; init; }
    public double Flow { get; init; }
    public double LobeSpacing { get; init; }
    public double Bend { get; init; }
    public double BandStrength { get; init; }
    public double BandWidth { get; init; }
    public double BandPosition { get; init; }
    public double BandCurve { get; init; }
    public double BandSpread { get; init; }
    public double BandSkew { get; init; }
    public double BandOffset { get; init; }
    public double BandTail { get; init; }
    public double BandTailPosition { get; init; }
    public double BandTailCurve { get; init; }
    public double BandTailOverflow { get; init; }
    public double BandAberration { get; init; }
    public double RangeWidth { get; init; }
    public double RangeHeight { get; init; }
    public VoiceGlowTheme Theme { get; init; }
    public required VoiceGlowBandColors BandColors { get; init; }
    public double Distortion { get; init; }
    public double CoreLight { get; init; }
    public double Scale { get; init; }

    /// <summary>The corner radius of the shape the glow is drawn in (<c>borderRadius</c>, the composer's 22).</summary>
    public double Radius { get; init; }

    public bool Processing { get; init; }
    public double ProcessingDuration { get; init; }
    public double ProcessingLevel { get; init; }
    public double ProcessingEase { get; init; }
    public double ProcessingTravel { get; init; }
    public double ProcessingCurve { get; init; }
    public double CornerFollow { get; init; }
    public double HueRange { get; init; }
    public double HueDuration { get; init; }
    public bool StaticColors { get; init; }
    public bool ReducedMotion { get; init; }
}
