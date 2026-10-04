// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-2): voice-SPEC V-5.2 (RESEARCH 3.2 item 2), the resampler seam of ED-292 (3).
using System.Collections.Concurrent;
using Marrow.Core.Port.Voice.Seams;

namespace Marrow.Core.Port.Voice.Dsp;

/// <summary>
/// Rational L/M polyphase FIR conversion to 16 kHz (48 kHz is 1/3, 44.1 kHz is 160/441, any other rate its reduced ratio). The filter is a
/// Kaiser-windowed sinc, symmetric (linear phase) with its group delay taken out so the output is aligned with the input in time: passband
/// to 7.2 kHz within 0.1 dB, 8 kHz and above at least 60 dB down (designed for 70). A 16 kHz input is passed through unchanged.
/// Streaming output is identical, bit for bit, to one-shot output: an output sample depends only on the input samples around it and is summed
/// in a fixed order, whatever the blocks. Single thread (the voice processing thread); it allocates only when its history has to grow.
/// </summary>
public sealed class PolyphaseResampler : IStreamingResampler
{
    public const int TargetRate = 16000;

    /// <summary>Stopband attenuation the filter is designed for, dB (V-5.2 asks for 60).</summary>
    public const double DesignStopbandDb = 70;

    /// <summary>The largest L: a rate whose reduced ratio to 16 kHz needs more phases is refused (no Windows mix format does).</summary>
    public const int MaxPhases = 2048;

    private static readonly ConcurrentDictionary<int, FilterDesign> Designs = new();

    private float[] history = new float[4096];
    private int historyCount;
    private long historyStart;          // input index of history[0]
    private long inputCount;            // input samples received since the stream began
    private long nextOutput;            // index of the next output sample
    private int currentRate;
    private FilterDesign? design;

    public void Process(int inputRate, ReadOnlySpan<float> mono, List<float> output)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(inputRate, 0);
        if (inputRate != currentRate)
        {
            Reset();
            currentRate = inputRate;
            design = inputRate == TargetRate ? null : Designs.GetOrAdd(inputRate, static r => FilterDesign.Make(r));
        }

        if (design is null)
        {
            foreach (var sample in mono) output.Add(sample);
            return;
        }

        Append(mono);
        inputCount += mono.Length;

        var d = design;
        var phases = d.L;
        var taps = d.TapsPerPhase;
        while (true)
        {
            var u = d.Center + nextOutput * d.M;                  // the output instant on the up-sampled grid, plus the filter's delay
            var newest = u / phases;                             // the newest input sample the filter reaches
            if (newest >= inputCount) break;

            var coefficients = d.Coefficients.AsSpan((int)(u % phases) * taps, taps);
            double sum = 0;
            for (var t = 0; t < taps; t++)
            {
                var n = newest - t;
                if (n < 0) break;
                sum += coefficients[t] * history[n - historyStart];
            }

            output.Add((float)sum);
            nextOutput++;
        }

        Trim(d);
    }

    public void Reset()
    {
        historyCount = 0;
        historyStart = 0;
        inputCount = 0;
        nextOutput = 0;
        currentRate = 0;
        design = null;
    }

    private void Append(ReadOnlySpan<float> mono)
    {
        if (historyCount + mono.Length > history.Length)
            Array.Resize(ref history, Math.Max(history.Length * 2, historyCount + mono.Length));
        mono.CopyTo(history.AsSpan(historyCount));
        historyCount += mono.Length;
    }

    // Keeps only what a later output can still reach: from (newest of the next output) - (taps - 1).
    private void Trim(FilterDesign d)
    {
        var newest = (d.Center + nextOutput * d.M) / d.L;
        var keepFrom = Math.Max(historyStart, newest - (d.TapsPerPhase - 1));
        var drop = (int)Math.Min(historyCount, keepFrom - historyStart);
        if (drop <= 0) return;
        Array.Copy(history, drop, history, 0, historyCount - drop);
        historyCount -= drop;
        historyStart += drop;
    }

    /// <summary>The filter for one input rate, split into its L phases.</summary>
    private sealed class FilterDesign
    {
        public required int L { get; init; }

        public required int M { get; init; }

        /// <summary>Coefficients per phase (the longest phase; shorter ones are padded with 0).</summary>
        public required int TapsPerPhase { get; init; }

        /// <summary>The prototype's centre (its group delay) in up-sampled samples.</summary>
        public required int Center { get; init; }

        /// <summary>Phase p occupies [p * TapsPerPhase, (p + 1) * TapsPerPhase): h[p + t * L].</summary>
        public required double[] Coefficients { get; init; }

        public static FilterDesign Make(int rate)
        {
            var g = Gcd(TargetRate, rate);
            var l = TargetRate / g;
            var m = rate / g;
            if (l > MaxPhases) throw new ArgumentOutOfRangeException(nameof(rate), rate, "the ratio to 16 kHz needs more than " + MaxPhases + " phases");

            // The passband ends 800 Hz below the stopband; an input slower than 16 kHz has its own Nyquist as the stopband.
            var nyquist = rate / 2.0;
            var stop = Math.Min(8000.0, nyquist);
            var pass = stop >= 8000.0 ? 7200.0 : 0.9 * stop;
            var cutoff = (pass + stop) / 2;
            double upRate = (double)rate * l;

            // Kaiser's design rule: beta from the attenuation, the length from the transition width.
            var beta = 0.1102 * (DesignStopbandDb - 8.7);
            var transition = 2 * Math.PI * (stop - pass) / upRate;
            var length = (int)Math.Ceiling((DesignStopbandDb - 7.95) / (2.285 * transition)) + 1;
            if (length % 2 == 0) length++;
            var center = (length - 1) / 2;

            var h = new double[length];
            var i0Beta = BesselI0(beta);
            double sum = 0;
            for (var n = 0; n < length; n++)
            {
                double k = n - center;
                var x = 2 * cutoff / upRate;
                var sinc = k == 0 ? 1.0 : Math.Sin(Math.PI * x * k) / (Math.PI * x * k);
                var r = k / center;
                h[n] = x * sinc * BesselI0(beta * Math.Sqrt(Math.Max(0, 1 - r * r))) / i0Beta;
                sum += h[n];
            }

            // A DC gain of L: the up-sampler spreads each input over L samples, so a unity passband needs the filter to sum to L.
            var scale = l / sum;
            var perPhase = (length + l - 1) / l;
            var coefficients = new double[l * perPhase];
            for (var n = 0; n < length; n++) coefficients[n % l * perPhase + n / l] = h[n] * scale;

            return new FilterDesign { L = l, M = m, TapsPerPhase = perPhase, Center = center, Coefficients = coefficients };
        }

        private static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);

        // Modified Bessel function of the first kind, order 0, by its power series.
        private static double BesselI0(double x)
        {
            double term = 1, sum = 1;
            var q = x * x / 4;
            for (var k = 1; k < 200; k++)
            {
                term *= q / ((double)k * k);
                sum += term;
                if (term < sum * 1e-17) break;
            }

            return sum;
        }
    }
}
