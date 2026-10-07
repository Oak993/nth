// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-11, the VT-3 session's addition for the orb): voice-SPEC V-2.2 items 2 and 4, V-5.3.
using Marrow.Core.Port.Voice.Seams;

namespace Marrow.Core.Port.Voice.Dsp;

/// <summary>
/// The raw level and bands of the audio being captured, published for the UI thread (V-2.2: "publish Level, Bands ... as volatile values"). The processing thread calls
/// <see cref="Feed"/> with each block the capture drained, in the device's format: the channels are averaged (V-5.1), the device-rate mono signal goes into a
/// <see cref="LevelAnalyser"/>, and the analyser is read at the fixed 60 Hz cadence of the signal itself (read k when k x rate / 60 frames have arrived: a block of any size gives the
/// reads that fall inside it, V-5.3). After the first block of a format nothing allocates. <see cref="Latest"/> may be read from any thread: the four values are one publication's
/// (a sequence counter, so a read never mixes two). <see cref="Feed"/> and <see cref="Reset"/> belong to one thread at a time: Reset is for when no capture is feeding.
/// </summary>
public sealed class AudioLevelMeter
{
    /// <summary>V-5.3: the analysis rate.</summary>
    public const int ReadsPerSecond = 60;

    private const int Chunk = 512;

    private readonly float[] mono = new float[Chunk];
    private LevelAnalyser? analyser;
    private CaptureFormat format;
    private long fed;
    private long reads;

    // The published values: a sequence lock. The writer makes the sequence odd, writes, and makes it even; a reader that sees the same even sequence before and after copying has one
    // publication's values.
    private int sequence;
    private double all;
    private double low;
    private double mid;
    private double high;

    /// <summary>The values of the last analysis; zero before the first, and after <see cref="Reset"/>.</summary>
    public LevelBands Latest
    {
        get
        {
            var spin = new SpinWait();
            while (true)
            {
                var before = Volatile.Read(ref sequence);
                if ((before & 1) == 0)
                {
                    var seen = new LevelBands(all, low, mid, high);
                    Thread.MemoryBarrier();
                    if (Volatile.Read(ref sequence) == before) return seen;
                }
                spin.SpinOnce();
            }
        }
    }

    /// <summary>One block of interleaved samples in <paramref name="captureFormat"/>. A format other than the running one starts the analysis afresh.</summary>
    public void Feed(CaptureFormat captureFormat, ReadOnlySpan<float> interleavedSamples)
    {
        if (captureFormat.SampleRate <= 0) return;
        var channels = Math.Max(1, captureFormat.Channels);
        if (analyser is null || captureFormat != format)
        {
            analyser = new LevelAnalyser(captureFormat.SampleRate);
            format = captureFormat;
            fed = 0;
            reads = 0;
            Publish(LevelBands.Zero);
        }
        var frames = interleavedSamples.Length / channels;
        for (var at = 0; at < frames;)
        {
            var untilRead = Math.Max(1, NextReadAt() - fed);
            var take = (int)Math.Min(Math.Min(frames - at, untilRead), Chunk);
            for (var f = 0; f < take; f++)
            {
                var first = (at + f) * channels;
                if (channels == 1)
                {
                    mono[f] = interleavedSamples[first];
                    continue;
                }
                float sum = 0;
                for (var c = 0; c < channels; c++) sum += interleavedSamples[first + c];
                mono[f] = sum / channels; // V-5.1: the average of the channels
            }
            analyser.Push(mono.AsSpan(0, take));
            fed += take;
            at += take;
            if (fed < NextReadAt()) continue;
            reads += 1;
            Publish(analyser.Read());
        }
    }

    /// <summary>A new session: the signal, the analysis and the published values are forgotten. Not while a capture is feeding.</summary>
    public void Reset()
    {
        analyser = null;
        fed = 0;
        reads = 0;
        Publish(LevelBands.Zero);
    }

    private long NextReadAt() => (reads + 1) * format.SampleRate / ReadsPerSecond;

    private void Publish(LevelBands bands)
    {
        Volatile.Write(ref sequence, sequence + 1);
        Thread.MemoryBarrier();
        all = bands.All;
        low = bands.Low;
        mid = bands.Mid;
        high = bands.High;
        Thread.MemoryBarrier();
        Volatile.Write(ref sequence, sequence + 1);
    }
}
