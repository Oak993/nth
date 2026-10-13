// PORT-ONLY: Dextra (DESIGN.md rev 5, 2.4 "Barge-in": the Assistant's own speech is fed as the reference signal to its voice
// detector; unit AS-18; ED-NEW-dextra-as18-2). No Swift twin.
// What Dextra has just said, as a ring of 16 kHz mono samples on the clock of its own playback. The player appends each block it
// hands to the sound card; the barge-in gate (VoiceBargeIn) looks back into it to tell the Assistant's own voice, coming back
// through the speakers into the microphone, from the owner's.
namespace Marrow.Core.Port.Assistant;

public sealed class VoiceEchoReference
{
    /// <summary>The rate the reference and the microphone are both at when the gate compares them.</summary>
    public const int Rate = 16_000;

    /// <summary>How much of what was said is kept: 2 s covers the longest sound-card buffer and room delay.</summary>
    public const int RingSamples = Rate * 2;

    /// <summary>A sample at or above this level (full scale is 1) counts as audible.</summary>
    public const float AudibleLevel = 0.002f;

    private readonly float[] ring = new float[RingSamples];
    private readonly Lock gate = new();
    private long written;
    private long lastAudible = -1;

    /// <summary>How many samples have been appended since the last <see cref="Clear"/>: the reference's own clock.</summary>
    public long Written
    {
        get
        {
            lock (gate) return written;
        }
    }

    /// <summary>Appends the samples just handed to the sound card (the player's thread).</summary>
    public void Append(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return;
        lock (gate)
        {
            for (var i = 0; i < samples.Length; i++)
            {
                var sample = float.IsFinite(samples[i]) ? samples[i] : 0f;
                ring[(int)((written + i) % RingSamples)] = sample;
                if (Math.Abs(sample) >= AudibleLevel) lastAudible = written + i;
            }
            written += samples.Length;
        }
    }

    /// <summary>Starts a new utterance: nothing of the old one is looked at again.</summary>
    public void Clear()
    {
        lock (gate)
        {
            Array.Clear(ring);
            written = 0;
            lastAudible = -1;
        }
    }

    /// <summary>Whether anything audible was appended within the last <paramref name="samples"/> samples of the reference's clock.</summary>
    public bool AudibleWithin(int samples)
    {
        lock (gate) return lastAudible >= 0 && written - lastAudible <= samples;
    }

    /// <summary>
    /// Copies the <c>destination.Length</c> samples that ended <paramref name="back"/> samples before the newest one. Anything
    /// before the first sample, or older than the ring, is zero.
    /// </summary>
    public void CopyBack(Span<float> destination, int back)
    {
        lock (gate)
        {
            var end = written - back;
            var start = end - destination.Length;
            for (var i = 0; i < destination.Length; i++)
            {
                var at = start + i;
                destination[i] = at < 0 || at < written - RingSamples || at >= written ? 0f : ring[(int)(at % RingSamples)];
            }
        }
    }
}
