// PORT-ONLY: Dextra (DESIGN.md rev 5, 2.4 "Barge-in": speaking while it talks stops the speech within 200 ms; the mic is opened in
// communications mode so Windows' echo cancellation runs, and the Assistant's own speech is also fed as the reference signal to its
// voice detector; 16 "done when" for AS-18: its own speech fed back at -6 dB never triggers barge-in; ED-NEW-dextra-as18-2). No Swift twin.
// The gate that decides whether a frame of the microphone is the OWNER speaking over Dextra, or Dextra's own voice coming back
// through the speakers. Windows' own echo cancellation takes most of the echo out first; this is the second line, and the one that
// still works on a PC where the first does nothing. For each 32 ms frame (512 samples at 16 kHz, the voice detector's frame):
//   1. the detector must call it speech and it must reach the noise floor, else it is Quiet;
//   2. while Dextra is not speaking, that is the owner: NearEnd;
//   3. while it speaks, the frame is compared with what was just played. The reference is fitted to it at up to three delays (the
//      direct sound and its reflections; every delay up to MaxLag is searched, the sound card's buffer, the room and the capture
//      buffer together), and the share of the frame those explain is its coherence. A frame the reference explains (coherence at or
//      above EchoCoherence) is Echo; one far below what Dextra is saying (an echo canceller's residue) is Residual; anything
//      else is the owner: NearEnd.
// A barge-in is Frames near-end frames (leaky: a frame that is not near-end takes one back), so one click or one bad frame never
// cuts speech and a real voice does within about 100 ms. Pure: no clock, no device; the caller feeds frames in order.
namespace Marrow.Core.Port.Assistant;

/// <summary>What one microphone frame is while Dextra may be speaking.</summary>
public enum EchoVerdict
{
    /// <summary>The detector hears no speech, or the frame is under the noise floor.</summary>
    Quiet,

    /// <summary>The owner (or anyone in the room): speech the reference does not explain.</summary>
    NearEnd,

    /// <summary>Dextra's own voice coming back: the reference explains the frame.</summary>
    Echo,

    /// <summary>Speech-like, but far quieter than what Dextra says: what an echo canceller leaves behind.</summary>
    Residual,
}

/// <summary>The gate's numbers. The defaults are what the synthetic-audio tests prove; the Native echo test on the owner's PC tunes them.</summary>
public sealed record VoiceBargeInOptions
{
    /// <summary>The voice detector's probability at or above which a frame is speech.</summary>
    public float SpeechProbability { get; init; } = 0.6f;

    /// <summary>The frame's RMS (full scale 1) under which it is noise whatever the detector says.</summary>
    public float MicFloor { get; init; } = 0.006f;

    /// <summary>The coherence (0 to 1) with the reference at or above which the reference explains the frame.</summary>
    public float EchoCoherence { get; init; } = 0.8f;

    /// <summary>A frame must reach this share of the RMS Dextra's own speech has now, or it is Residual.</summary>
    public float ResidualShare { get; init; } = 0.25f;

    /// <summary>How many near-end frames (net) make a barge-in: two frames, 64 ms, then the stop.</summary>
    public int Frames { get; init; } = 2;

    /// <summary>The longest delay between the reference and its echo that is searched: sound-card buffer, room and capture together.</summary>
    public int MaxLagMs { get; init; } = 480;
}

public sealed class VoiceBargeIn
{
    /// <summary>The voice detector's frame: 512 samples at 16 kHz, 32 ms.</summary>
    public const int FrameSamples = 512;

    private const int Decimate = 4;
    private const int Fine = 6;
    private const int Taps = 3;
    private const int MinTapGap = 8;
    private const int ActiveWindow = VoiceEchoReference.Rate * 6 / 10;

    private readonly VoiceEchoReference reference;
    private readonly VoiceBargeInOptions o;
    private readonly int maxLag;
    private readonly float[] window;
    private readonly float[] mic4 = new float[FrameSamples / Decimate];
    private readonly float[] ref4;
    private readonly float[] prefix4;
    private readonly float[] left = new float[FrameSamples];
    private readonly int[] taps = new int[Taps];
    private readonly float[,] gram = new float[Taps, Taps + 1];
    private readonly float[] gains = new float[Taps];
    private int count;
    private int near;

    public VoiceBargeIn(VoiceEchoReference reference, VoiceBargeInOptions? options = null)
    {
        this.reference = reference ?? throw new ArgumentNullException(nameof(reference));
        o = options ?? new VoiceBargeInOptions();
        maxLag = Math.Clamp(o.MaxLagMs * VoiceEchoReference.Rate / 1000, Decimate * 8, VoiceEchoReference.RingSamples - FrameSamples) / Decimate * Decimate;
        window = new float[maxLag + FrameSamples];
        ref4 = new float[window.Length / Decimate];
        prefix4 = new float[ref4.Length + 1];
    }

    /// <summary>The verdict on the last frame.</summary>
    public EchoVerdict Last { get; private set; }

    /// <summary>The last frame's coherence with the reference (0 to 1), or 0 when none was looked for.</summary>
    public float LastCoherence { get; private set; }

    /// <summary>The leaky count of near-end frames now.</summary>
    public int NearFrames => near;

    /// <summary>Forgets the count (a new utterance, a new turn).</summary>
    public void Reset()
    {
        near = 0;
        Last = EchoVerdict.Quiet;
        LastCoherence = 0;
    }

    /// <summary>
    /// One 512-sample frame of the microphone at 16 kHz and the voice detector's probability for it. True when this frame completes a
    /// barge-in. Frames of any other length are refused.
    /// </summary>
    public bool Process(ReadOnlySpan<float> frame, float speechProbability)
    {
        if (frame.Length != FrameSamples) throw new ArgumentException("A frame is 512 samples.", nameof(frame));
        var verdict = Judge(frame, speechProbability);
        Last = verdict;
        near = verdict == EchoVerdict.NearEnd ? Math.Min(near + 1, o.Frames) : Math.Max(near - 1, 0);
        return near >= o.Frames;
    }

    private EchoVerdict Judge(ReadOnlySpan<float> frame, float probability)
    {
        LastCoherence = 0;
        var micEnergy = Energy(frame);
        var micRms = MathF.Sqrt(micEnergy / FrameSamples);
        if (!(probability >= o.SpeechProbability) || micRms < o.MicFloor) return EchoVerdict.Quiet;
        if (!reference.AudibleWithin(ActiveWindow)) return EchoVerdict.NearEnd;

        reference.CopyBack(window, 0);
        var recent = MathF.Sqrt(Energy(window) / window.Length);
        if (micRms < o.ResidualShare * recent) return EchoVerdict.Residual;
        var coherence = Explained(frame, micEnergy);
        LastCoherence = coherence;
        return coherence >= o.EchoCoherence ? EchoVerdict.Echo : EchoVerdict.NearEnd;
    }

    // How much of the frame the reference explains, as a coherence (0 to 1: the square root of the share of the frame's energy). A room
    // answers with the direct sound and its reflections, so one delay is not enough: at the tail of a word the reflection of the loud
    // part is most of what the microphone hears. The reference is fitted greedily with up to Taps delays (matching pursuit): the delay
    // that best matches what is left of the frame is found (coarse on a 4 kHz copy over every delay, then fine on the 16 kHz samples),
    // the gains of all the delays chosen so far are solved together (least squares), and what they do not explain is searched again.
    // The owner's voice matches no delay, so it stays unexplained however many are tried. window[] ends at the newest reference sample,
    // so the reference that ended <lag> samples ago starts at window[maxLag - lag].
    private float Explained(ReadOnlySpan<float> frame, float micEnergy)
    {
        if (micEnergy <= 0) return 0;
        for (var i = 0; i < ref4.Length; i++) ref4[i] = Mean(window.AsSpan(i * Decimate, Decimate));
        prefix4[0] = 0;
        for (var i = 0; i < ref4.Length; i++) prefix4[i + 1] = prefix4[i] + ref4[i] * ref4[i];
        Array.Clear(taps);
        count = 0;
        frame.CopyTo(left);
        var leftEnergy = micEnergy;
        for (var step = 0; step < Taps; step++)
        {
            var lag = BestLag(leftEnergy);
            if (lag < 0 || NearATap(lag)) break;
            taps[count++] = lag;
            leftEnergy = Fit(frame);
            if (leftEnergy <= 0.03f * micEnergy) break;
        }
        return count == 0 ? 0 : MathF.Sqrt(Math.Clamp(1 - leftEnergy / micEnergy, 0f, 1f));
    }

    // The delay whose reference best matches what is left of the frame: -1 when none matches at all.
    private int BestLag(float leftEnergy)
    {
        if (leftEnergy <= 1e-12f) return -1;
        for (var i = 0; i < mic4.Length; i++) mic4[i] = Mean(left.AsSpan(i * Decimate, Decimate));
        var micEnergy4 = Energy(mic4);
        if (micEnergy4 <= 0) return -1;
        var lags = maxLag / Decimate;
        var bestLag = -1;
        var best = 0f;
        for (var lag = 0; lag <= lags; lag++)
        {
            var start = lags - lag;
            var refEnergy = prefix4[start + mic4.Length] - prefix4[start];
            if (refEnergy <= 1e-12f) continue;
            var dot = 0f;
            var segment = ref4.AsSpan(start, mic4.Length);
            for (var i = 0; i < mic4.Length; i++) dot += mic4[i] * segment[i];
            var match = dot / MathF.Sqrt(micEnergy4 * refEnergy);
            if (match > best)
            {
                best = match;
                bestLag = lag;
            }
        }
        if (bestLag < 0) return -1;
        var centre = bestLag * Decimate;
        var fineLag = -1;
        var fineBest = 0f;
        for (var lag = Math.Max(0, centre - Fine); lag <= Math.Min(maxLag, centre + Fine); lag++)
        {
            var segment = window.AsSpan(maxLag - lag, FrameSamples);
            var refEnergy = Energy(segment);
            if (refEnergy <= 1e-12f) continue;
            var dot = 0f;
            for (var i = 0; i < FrameSamples; i++) dot += left[i] * segment[i];
            var match = dot / MathF.Sqrt(leftEnergy * refEnergy);
            if (match > fineBest)
            {
                fineBest = match;
                fineLag = lag;
            }
        }
        return fineBest > 0 ? fineLag : -1;
    }

    private bool NearATap(int lag)
    {
        for (var i = 0; i < count; i++)
        {
            if (Math.Abs(taps[i] - lag) < MinTapGap) return true;
        }
        return false;
    }

    // Solves the gains of the chosen delays together (the normal equations, a matrix of at most 3 by 3, the right-hand side in the last
    // column), leaves what they do not explain in left[], and returns its energy. A delay that adds nothing the others do not (a
    // singular matrix) is dropped.
    private float Fit(ReadOnlySpan<float> frame)
    {
        var n = count;
        for (var i = 0; i < n; i++)
        {
            var a = window.AsSpan(maxLag - taps[i], FrameSamples);
            var b = 0f;
            for (var s = 0; s < FrameSamples; s++) b += a[s] * frame[s];
            gram[i, Taps] = b;
            for (var j = 0; j <= i; j++)
            {
                var c = window.AsSpan(maxLag - taps[j], FrameSamples);
                var g = 0f;
                for (var s = 0; s < FrameSamples; s++) g += a[s] * c[s];
                gram[i, j] = g;
                gram[j, i] = g;
            }
            gram[i, i] *= 1.000001f;
        }
        for (var col = 0; col < n; col++)
        {
            var pivot = col;
            for (var row = col + 1; row < n; row++)
            {
                if (Math.Abs(gram[row, col]) > Math.Abs(gram[pivot, col])) pivot = row;
            }
            if (Math.Abs(gram[pivot, col]) < 1e-9f)
            {
                n = col;
                break;
            }
            if (pivot != col)
            {
                for (var k = 0; k < Taps; k++) (gram[col, k], gram[pivot, k]) = (gram[pivot, k], gram[col, k]);
                (gram[col, Taps], gram[pivot, Taps]) = (gram[pivot, Taps], gram[col, Taps]);
            }
            for (var row = col + 1; row < n; row++)
            {
                var factor = gram[row, col] / gram[col, col];
                for (var k = col; k < Taps; k++) gram[row, k] -= factor * gram[col, k];
                gram[row, Taps] -= factor * gram[col, Taps];
            }
        }
        for (var row = n - 1; row >= 0; row--)
        {
            var sum = gram[row, Taps];
            for (var k = row + 1; k < n; k++) sum -= gram[row, k] * gains[k];
            gains[row] = sum / gram[row, row];
        }
        count = n;
        frame.CopyTo(left);
        var leftEnergy = 0f;
        for (var s = 0; s < FrameSamples; s++)
        {
            var estimate = 0f;
            for (var i = 0; i < n; i++) estimate += gains[i] * window[maxLag - taps[i] + s];
            left[s] -= estimate;
            leftEnergy += left[s] * left[s];
        }
        return leftEnergy;
    }

    private static float Energy(ReadOnlySpan<float> samples)
    {
        var sum = 0f;
        foreach (var sample in samples) sum += sample * sample;
        return sum;
    }

    private static float Mean(ReadOnlySpan<float> samples)
    {
        var sum = 0f;
        foreach (var sample in samples) sum += sample;
        return sum / samples.Length;
    }
}
