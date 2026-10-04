// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-3): the voice activity seam (voice-SPEC V-2.4, V-6.1).
namespace Marrow.Core.Port.Voice.Seams;

/// <summary>
/// Silero VAD in frames (V-6.1): <see cref="FrameSamples"/> is 512 samples at 16 kHz (32 ms); <see cref="Probability"/> gives the
/// speech probability of one frame; <see cref="Reset"/> clears the detector's state for a new session. Production is
/// <c>SileroVad</c> (VT-6); tests bind <see cref="ScriptedVad"/>.
/// </summary>
public interface IVoiceActivityDetector
{
    /// <summary>512 at 16 kHz.</summary>
    int FrameSamples { get; }

    float Probability(ReadOnlySpan<float> frame);

    void Reset();
}
