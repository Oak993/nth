// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-3): the VAD double (voice-SPEC V-2.4 "probabilities by time").
namespace Marrow.Core.Port.Voice.Seams;

/// <summary>
/// <see cref="IVoiceActivityDetector"/> that answers by audio position: the n-th frame since the last <see cref="Reset"/> starts at
/// n x 32 ms, and its probability is that of the span it falls in (the baseline outside every span). It reads no sample.
/// </summary>
public sealed class ScriptedVad : IVoiceActivityDetector
{
    private readonly IReadOnlyList<(double Start, double End, float Probability)> spans;
    private readonly float baseline;
    private int frame;

    /// <param name="spans">Seconds of audio, [start, end), with the probability inside them.</param>
    /// <param name="baseline">The probability everywhere else.</param>
    public ScriptedVad(IEnumerable<(double Start, double End, float Probability)> spans, float baseline = 0f)
    {
        this.spans = spans.ToList();
        this.baseline = baseline;
    }

    /// <summary>Speech (probability 0.9) inside the spans, silence (0.02) elsewhere.</summary>
    public static ScriptedVad Speech(params (double Start, double End)[] speech) =>
        new(speech.Select(s => (s.Start, s.End, 0.9f)), 0.02f);

    /// <summary>Silence throughout.</summary>
    public static ScriptedVad Silence() => new([], 0.02f);

    public int FrameSamples => 512;

    /// <summary>Frames asked about since the last reset.</summary>
    public int FramesSeen => frame;

    public int ResetCount { get; private set; }

    public float Probability(ReadOnlySpan<float> frameSamples)
    {
        var start = frame * (double)FrameSamples / 16000;
        frame += 1;
        foreach (var (spanStart, spanEnd, probability) in spans)
            if (start >= spanStart && start < spanEnd) return probability;
        return baseline;
    }

    public void Reset()
    {
        frame = 0;
        ResetCount += 1;
    }
}
