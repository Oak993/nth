// PORT-ONLY: VT-15, V-2.5 / V-6.1. Construction and startup never load the bundled VAD; the first session opens it.
using Marrow.Core.Port.Voice.Seams;

namespace Marrow.App.Port.Voice;

internal sealed class DeferredVoiceVad(Func<IVoiceActivityDetector> open) : IVoiceActivityDetector
{
    private readonly Lazy<IVoiceActivityDetector> vad = new(open);
    public int FrameSamples => 512;
    public void Reset() { if (vad.IsValueCreated) vad.Value.Reset(); }
    public float Probability(ReadOnlySpan<float> frame) => vad.Value.Probability(frame);
}
