using Marrow.Core.Port.Voice.Seams;

namespace Marrow.Testing;

/// <summary>
/// A voice detector that calls a frame speech when it is audible (RMS 0.01 or more): enough for synthetic voices, and, like a real detector, it calls an
/// echo of speech speech too. It reads the samples (the scripted one reads none), so the barge-in tests can tell a silent room from a talking one.
/// </summary>
public sealed class EnergyVad : IVoiceActivityDetector
{
    public int FrameSamples => 512;

    public int ResetCount { get; private set; }

    public float Probability(ReadOnlySpan<float> frame) => SyntheticSpeech.EnergyProbability(frame);

    public void Reset() => ResetCount++;
}
