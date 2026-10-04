// PORT-ONLY, owner-ruled addition (OD-0097, second amendment; first-release voice typing, VT-4, voice-SPEC V-13.1, V-13.2).
namespace Marrow.Core.Port.Voice.Glow;

/// <summary>
/// The numbers the stylesheet recipe <c>Ya</c> takes (voice-glow.js:1035-1047, without the element id): the layers' opacities, sizes and
/// masks. The recipe itself is drawn by <c>VoiceGlowView</c> (VT-9); resolving its inputs belongs with the other props.
/// </summary>
public sealed record VoiceGlowStyleArgs
{
    public double BorderRadius { get; init; }
    public double BorderWidth { get; init; }
    public double StrokeOpacity { get; init; }
    public double InnerOpacity { get; init; }
    public double BloomOpacity { get; init; }
    public required string InnerShadow { get; init; }
    public required string ColorVariant { get; init; }
    public IReadOnlyList<string?>? Colors { get; init; }
    public double Brightness { get; init; }
    public double Saturation { get; init; }
    public VoiceGlowTheme Theme { get; init; }
    public double HueBase { get; init; }
    public double GlowSize { get; init; }
    public double GlowWidth { get; init; }
    public double GlowHeight { get; init; }
    public double StrokeScale { get; init; }
    public double InnerScale { get; init; }
    public double InnerHeight { get; init; }
    public double BloomScale { get; init; }
    public double BloomHeight { get; init; }
    public double CoreSize { get; init; }
    public double CoreLight { get; init; }
    public double CoreLightWidth { get; init; }
    public double CoreLightHeight { get; init; }
    public double RangeWidth { get; init; }
    public double RangeHeight { get; init; }
    public double Softness { get; init; }
    public bool Distortion { get; init; }
    public double Scale { get; init; }
}

/// <summary>What <see cref="VoiceGlowResolver.Resolve"/> returns: the driver config, the stylesheet inputs and the wrapper's own values.</summary>
public sealed record VoiceGlowResolved(
    VoiceGlowConfig Config,
    VoiceGlowStyleArgs Style,
    double Strength,
    VoiceGlowType Type,
    bool Distort,
    bool Core,
    double Detail);
