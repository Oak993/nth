// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-3): synthesised sample sources for ScriptedCapture
// (voice-SPEC V-2.4 "PCM from WAV fixtures or synthesised signals").
namespace Marrow.Core.Port.Voice.Seams;

/// <summary>Deterministic signals: a function from a frame index (at the capture's rate) to a sample.</summary>
public static class ScriptedSignals
{
    /// <summary>Exactly 0.0 for every sample: what Windows hands a blocked desktop app (V-4.5).</summary>
    public static Func<long, float> AllZero { get; } = static _ => 0f;

    /// <summary>
    /// A room's own noise: a tiny deterministic dither (+-1e-4) that is never exactly 0.0, so a silent but unblocked microphone does
    /// not look like a blocked one (V-4.5 checks for exact zeros).
    /// </summary>
    public static Func<long, float> RoomNoise { get; } = static i => (((i * 2654435761L) >> 7) & 1) == 0 ? 1e-4f : -1e-4f;

    /// <summary>A sine of <paramref name="frequency"/> Hz and <paramref name="amplitude"/> at <paramref name="sampleRate"/>.</summary>
    public static Func<long, float> Tone(double frequency, double amplitude, int sampleRate) =>
        i => (float)(amplitude * Math.Sin(2 * Math.PI * frequency * i / sampleRate));

    /// <summary>
    /// <paramref name="speech"/> inside the given spans (seconds) and <see cref="RoomNoise"/> outside, at <paramref name="sampleRate"/>.
    /// </summary>
    public static Func<long, float> SpeechSpans(IReadOnlyList<(double Start, double End)> spans, Func<long, float> speech, int sampleRate) =>
        i =>
        {
            var t = (double)i / sampleRate;
            foreach (var (start, end) in spans)
                if (t >= start && t < end) return speech(i);
            return RoomNoise(i);
        };
}
