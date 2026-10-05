// PORT-ONLY, owner-ruled addition (OD-0097, second amendment; first-release voice typing, VT-9, voice-SPEC V-13.3): the raw level the glow listens to.
namespace Marrow.UI.Port.Voice.Glow;

/// <summary>
/// The raw level <c>all</c> (V-5.3, unsmoothed, 0 to 1) the voice session reads each frame: the design's <c>level: () =&gt; vlev</c>. The host writes it as often as it likes from any thread
/// and the render thread reads the latest value each frame, so the audio rate never makes the view send a message. The driver smooths it. A NaN counts as 0.
/// </summary>
public sealed class VoiceGlowLevel
{
    private double value;

    public double Value
    {
        get => Volatile.Read(ref value);
        set => Volatile.Write(ref this.value, double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 1));
    }
}
