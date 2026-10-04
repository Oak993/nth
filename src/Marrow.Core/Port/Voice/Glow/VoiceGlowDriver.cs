// PORT-ONLY, owner-ruled addition (OD-0097, second amendment; first-release voice typing, VT-4, voice-SPEC V-13.1, V-13.2).
namespace Marrow.Core.Port.Voice.Glow;

/// <summary>
/// The glow's driver (voice-glow.js <c>ka</c>, lines 759-816): the envelope, the flow phase, the processing scan, the corner follow, the lobes,
/// the hue drift, the warp and the band polyline. Deterministic per frame delta; the frame cadence belongs to the view.
/// </summary>
public sealed class VoiceGlowDriver
{
    public VoiceGlowDriver(VoiceGlowConfig config) => throw new NotImplementedException();

    /// <summary>The frame the last <see cref="Step"/> filled.</summary>
    public VoiceGlowFrame Frame => throw new NotImplementedException();

    /// <summary>Applies a (possibly changed) config; a change makes the next step a 1/60 s one, as a re-registration does in the page.</summary>
    public void Configure(VoiceGlowConfig config) => throw new NotImplementedException();

    /// <summary>The next step is a 1/60 s one (the page's registration resets its last timestamp).</summary>
    public void Restart() => throw new NotImplementedException();

    /// <summary>Advances the driver by <paramref name="dtSeconds"/> with the raw level and the size of the shape.</summary>
    public VoiceGlowFrame Step(double dtSeconds, double level, double widthPx, double heightPx) => throw new NotImplementedException();
}
