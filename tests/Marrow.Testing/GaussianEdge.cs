namespace Marrow.Testing;

/// <summary>
/// The standard deviation of a rendered shadow, fitted to the profile a Gaussian blur gives a straight edge or a band
/// (unit rec-shadow; finding B of the lead's E rulings: SwiftUI <c>.shadow(radius: r)</c> is a Gaussian of sigma r).
/// A shape of opacity <c>A</c> covering the band <c>[low, high]</c> along one axis (a half-plane when <c>high</c> is
/// infinite), blurred by a Gaussian of standard deviation sigma, has the coverage
/// <c>A * (Phi((p - low) / sigma) - Phi((p - high) / sigma))</c> at the pixel centre <c>p</c>. The fit is a
/// least-squares search over sigma alone; the band and the opacity are the caller's known geometry.
/// </summary>
public static class GaussianEdge
{
    /// <summary>The standard normal cumulative distribution (Abramowitz and Stegun 7.1.26 erf, error below 1.5e-7).</summary>
    public static double Phi(double z)
    {
        var x = Math.Abs(z) / Math.Sqrt(2);
        var t = 1 / (1 + 0.3275911 * x);
        var erf = 1 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
        return z >= 0 ? 0.5 * (1 + erf) : 0.5 * (1 - erf);
    }

    /// <summary>The model's coverage at <paramref name="position"/>.</summary>
    public static double Coverage(double position, double low, double high, double sigma, double amplitude) =>
        amplitude * (Phi((position - low) / sigma) - (double.IsPositiveInfinity(high) ? 0 : Phi((position - high) / sigma)));

    /// <summary>
    /// The sigma (in the samples' units) that best fits <paramref name="samples"/> (pixel-centre position, coverage
    /// 0..1) and the root-mean-square residual of that fit.
    /// </summary>
    public static (double Sigma, double Residual) FitSigma(IReadOnlyList<(double Position, double Coverage)> samples,
        double low, double high, double amplitude)
    {
        if (samples.Count < 3) throw new ArgumentException("at least three samples", nameof(samples));
        double Error(double sigma)
        {
            double sum = 0;
            foreach (var (p, c) in samples)
            {
                var e = c - Coverage(p, low, high, sigma, amplitude);
                sum += e * e;
            }
            return sum;
        }
        // A coarse logarithmic grid from 0.05 to 200, then a golden-section search around the best grid point.
        double best = 0.05, bestError = double.MaxValue;
        for (var s = 0.05; s <= 200; s *= 1.02)
        {
            var e = Error(s);
            if (e < bestError) { best = s; bestError = e; }
        }
        double a = best / 1.02, b = best * 1.02;
        var g = (Math.Sqrt(5) - 1) / 2;
        for (var i = 0; i < 80; i++)
        {
            double c = b - g * (b - a), d = a + g * (b - a);
            if (Error(c) < Error(d)) b = d; else a = c;
        }
        var sigma = (a + b) / 2;
        return (sigma, Math.Sqrt(Error(sigma) / samples.Count));
    }

    /// <summary>
    /// The three box widths Skia's CPU (raster) blur uses for the Gaussian of standard deviation
    /// <paramref name="sigma"/>: the W3C filter-effects approximation, d = floor(sigma * 3 * sqrt(2 pi) / 4 + 0.5), three
    /// passes of d when d is odd and of d, d, d + 1 when it is even.
    /// </summary>
    public static int[] RasterBoxes(double sigma)
    {
        var d = Math.Max(1, (int)Math.Floor(sigma * 3 * Math.Sqrt(2 * Math.PI) / 4 + 0.5));
        return d % 2 == 1 ? [d, d, d] : [d, d, d + 1];
    }

    /// <summary>
    /// What <see cref="FitSigma"/> reads on a straight edge that Skia's raster blur draws for the nominal sigma
    /// <paramref name="sigma"/> (device px): the edge of an opaque half-plane convolved with <see cref="RasterBoxes"/>,
    /// stored in 8 bits, then fitted as the measurement fits a rendered one. The box approximation draws a little less
    /// than the nominal sigma (about 0.94 to 0.97 of it, in window steps of about 0.5 px).
    /// </summary>
    public static double RasterSigma(double sigma)
    {
        const int edge = 900;
        double[] kernel = [1];
        foreach (var box in RasterBoxes(sigma))
        {
            var next = new double[kernel.Length + box - 1];
            for (var i = 0; i < kernel.Length; i++)
                for (var j = 0; j < box; j++) next[i + j] += kernel[i] / box;
            kernel = next;
        }
        var centre = (kernel.Length - 1) / 2;
        var samples = new List<(double, double)>();
        for (var x = 0; x < edge; x++)
        {
            double coverage = 0;
            for (var j = 0; j < kernel.Length; j++)
                if (x + centre - j >= edge) coverage += kernel[j];
            samples.Add((x + .5, Math.Round(Math.Min(1, coverage) * 255) / 255));
        }
        return FitSigma(samples, edge, double.PositiveInfinity, 1).Sigma;
    }
}
