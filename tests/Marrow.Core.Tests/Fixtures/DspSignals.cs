namespace Marrow.Core.Tests.Fixtures;

// VT-2 test support: synthesised signals and level measurements for the resampler and the noise floor tests.
public static class DspSignals
{
    /// <summary>A sine of <paramref name="frequency"/> and <paramref name="amplitude"/> at <paramref name="rate"/>, <paramref name="seconds"/> long.</summary>
    public static float[] Sine(double frequency, double amplitude, int rate, double seconds)
    {
        var n = (int)Math.Round(rate * seconds);
        var x = new float[n];
        for (var i = 0; i < n; i++) x[i] = (float)(amplitude * Math.Sin(2 * Math.PI * frequency * i / rate));
        return x;
    }

    /// <summary>Deterministic white noise, uniform in +-<paramref name="amplitude"/>*sqrt(3), so its RMS is <paramref name="amplitude"/>.</summary>
    public static float[] Noise(double amplitude, int count, int seed)
    {
        var rng = new Random(seed);
        var x = new float[count];
        for (var i = 0; i < count; i++) x[i] = (float)(amplitude * Math.Sqrt(3) * (rng.NextDouble() * 2 - 1));
        return x;
    }

    public static double Rms(ReadOnlySpan<float> x)
    {
        double s = 0;
        foreach (var v in x) s += (double)v * v;
        return Math.Sqrt(s / x.Length);
    }

    /// <summary>Level of <paramref name="output"/> relative to <paramref name="inputRms"/> in dB, over the middle of the block (the filter's ends are skipped).</summary>
    public static double RelativeDb(IReadOnlyList<float> output, int outputRate, double inputRms)
    {
        var skip = outputRate / 5;
        var middle = output.Skip(skip).Take(output.Count - 2 * skip).ToArray();
        return 20 * Math.Log10(Rms(middle) / inputRms);
    }
}
