// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-2): voice-SPEC V-5.3 and V-5.4. The design reads Web Audio's
// AnalyserNode (voice-composer.js:114-127 fileRead = orb-audio.ts read()); this is that node's arithmetic, so a native build needs no
// browser. Its output is checked against bytes read from headless Chrome's own AnalyserNode (tests/fixtures/voice/analyser).
namespace Marrow.Core.Port.Voice.Dsp;

/// <summary>
/// An analyser equal to Web Audio's <c>AnalyserNode</c> with <c>fftSize</c> 2048 and <c>smoothingTimeConstant</c> 0.65 (minDecibels -100,
/// maxDecibels -30), plus the design's reading of it: <c>all</c> from the RMS of the last 2048 samples and the mean spectrum byte over three
/// bands. Feed the device-rate mono signal with <see cref="Push"/>; call <see cref="Read"/> at the fixed 60 Hz cadence (one call is one
/// analysis: the smoothing moves once per call, as once per <c>getByteFrequencyData</c>). Not thread safe: one thread (the voice processing
/// thread) pushes and reads.
/// </summary>
public sealed class LevelAnalyser
{
    public const int FftSize = 2048;
    public const int BinCount = FftSize / 2;
    public const double SmoothingTimeConstant = 0.65;
    public const double MinDecibels = -100;
    public const double MaxDecibels = -30;

    /// <summary>The design's dead zone and gain: <c>all = min(1, max(0, rms - 0.004) * 5.5)</c>.</summary>
    public const double DeadZone = 0.004;
    public const double LevelGain = 5.5;

    private static readonly double[] Window = MakeWindow();
    private static readonly int[] BitReverse = MakeBitReverse();
    private static readonly double[] Cos = MakeTwiddles(cos: true);
    private static readonly double[] Sin = MakeTwiddles(cos: false);

    private readonly float[] ring = new float[FftSize];
    private readonly double[] re = new double[FftSize];
    private readonly double[] im = new double[FftSize];
    private readonly double[] smoothed = new double[BinCount];
    private readonly byte[] spectrum = new byte[BinCount];
    private readonly (int From, int To) low, mid, high;
    private readonly NoiseFloor? floor;
    private int write;

    /// <param name="sampleRate">The device rate of the signal pushed.</param>
    /// <param name="subtractNoiseFloor">
    /// V-5.4: subtract the rolling noise floor from the RMS before the dead zone. False gives the AnalyserNode response exactly as the design
    /// reads it (the fixtures' comparison).
    /// </param>
    public LevelAnalyser(int sampleRate, bool subtractNoiseFloor = true)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sampleRate, 0);
        floor = subtractNoiseFloor ? new NoiseFloor(sampleRate) : null;
        low = BinRange(30, 200, sampleRate);
        mid = BinRange(200, 2000, sampleRate);
        high = BinRange(2000, 16000, sampleRate);
    }

    /// <summary>What <c>getByteFrequencyData</c> returned at the last <see cref="Read"/>: 1024 bytes, 0 to 255.</summary>
    public ReadOnlySpan<byte> Spectrum => spectrum;

    /// <summary>The RMS of the 2048 samples at the last <see cref="Read"/>, before the noise floor.</summary>
    public double Rms { get; private set; }

    /// <summary>The ambient noise floor now (0 when the analyser does not subtract it, or before its first 50 ms window completes).</summary>
    public double NoiseFloorValue => floor?.Value ?? 0;

    public void Push(ReadOnlySpan<float> mono)
    {
        foreach (var raw in mono)
        {
            ring[write] = float.IsFinite(raw) ? raw : 0f;
            write = (write + 1) & (FftSize - 1);
        }

        floor?.Push(mono);
    }

    public LevelBands Read()
    {
        // The last 2048 samples, oldest first (zeros before the stream began), as AnalyserNode's time-domain buffer.
        double sumSquares = 0;
        for (var i = 0; i < FftSize; i++)
        {
            double x = ring[(write + i) & (FftSize - 1)];
            sumSquares += x * x;
            re[BitReverse[i]] = x * Window[i];
            im[BitReverse[i]] = 0;
        }

        Rms = Math.Sqrt(sumSquares / FftSize);
        Transform();

        // magnitude / fftSize, smoothed: X = 0.65 X_prev + 0.35 |X|; dB = 20 log10 X; byte = floor(255 (dB + 100) / 70), clamped.
        for (var i = 0; i < BinCount; i++)
        {
            var magnitude = Math.Sqrt(re[i] * re[i] + im[i] * im[i]) / FftSize;
            smoothed[i] = SmoothingTimeConstant * smoothed[i] + (1 - SmoothingTimeConstant) * magnitude;
            var scaled = 255.0 * (20 * Math.Log10(smoothed[i]) - MinDecibels) / (MaxDecibels - MinDecibels);
            spectrum[i] = scaled > 0 ? (byte)Math.Min(255.0, Math.Floor(scaled)) : (byte)0;
        }

        var effective = floor is null ? Rms : Math.Max(0, Rms - floor.Value);
        var all = Math.Min(1, Math.Max(0, effective - DeadZone) * LevelGain);
        return new LevelBands(all, Average(low, all), Average(mid, all), Average(high, all));
    }

    /// <summary>Forgets the signal, the smoothing and the noise floor (a new session).</summary>
    public void Reset()
    {
        Array.Clear(ring);
        Array.Clear(smoothed);
        Array.Clear(spectrum);
        write = 0;
        Rms = 0;
        floor?.Reset();
    }

    private double Average((int From, int To) range, double all)
    {
        if (all == 0) return 0;
        double sum = 0;
        for (var i = range.From; i <= range.To; i++) sum += spectrum[i];
        return Math.Min(1, sum / (range.To - range.From + 1) / 255 * 2);
    }

    // bin(hz) = clamp(round(hz / (rate / 2) * 1024), 0, 1023); JavaScript's Math.round rounds halves up.
    private static (int From, int To) BinRange(double fromHz, double toHz, int sampleRate)
    {
        int Bin(double hz) => (int)Math.Max(0, Math.Min(BinCount - 1, Math.Floor(hz / (sampleRate / 2.0) * BinCount + 0.5)));
        return (Bin(fromHz), Bin(toHz));
    }

    // Iterative radix-2 decimation in time; the input is already in bit-reversed order.
    private void Transform()
    {
        for (var size = 2; size <= FftSize; size <<= 1)
        {
            var half = size / 2;
            var step = FftSize / size;
            for (var start = 0; start < FftSize; start += size)
            {
                for (var k = 0; k < half; k++)
                {
                    var c = Cos[k * step];
                    var s = Sin[k * step];
                    var a = start + k;
                    var b = a + half;
                    var tr = re[b] * c - im[b] * s;
                    var ti = re[b] * s + im[b] * c;
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                }
            }
        }
    }

    // Blackman: a0 0.42, a1 0.5, a2 0.08 (alpha 0.16): w[i] = 0.42 - 0.5 cos(2 pi i / N) + 0.08 cos(4 pi i / N).
    private static double[] MakeWindow()
    {
        var w = new double[FftSize];
        for (var i = 0; i < FftSize; i++)
            w[i] = 0.42 - 0.5 * Math.Cos(2 * Math.PI * i / FftSize) + 0.08 * Math.Cos(4 * Math.PI * i / FftSize);
        return w;
    }

    private static int[] MakeBitReverse()
    {
        var bits = (int)Math.Log2(FftSize);
        var table = new int[FftSize];
        for (var i = 0; i < FftSize; i++)
        {
            var r = 0;
            for (var b = 0; b < bits; b++) r |= ((i >> b) & 1) << (bits - 1 - b);
            table[i] = r;
        }

        return table;
    }

    // exp(-2 pi i k / N): the forward transform's twiddles.
    private static double[] MakeTwiddles(bool cos)
    {
        var t = new double[FftSize / 2];
        for (var k = 0; k < t.Length; k++)
            t[k] = cos ? Math.Cos(2 * Math.PI * k / FftSize) : -Math.Sin(2 * Math.PI * k / FftSize);
        return t;
    }
}
