using System.Buffers;
using System.Numerics;
using Marrow.Core.Port;

namespace Marrow.UI.Theme;

/// <summary>
/// PORT-ONLY (ED-NEW-perf-memory-2). The separable Gaussian that <see cref="WallpaperStore.Blurred"/> applies, the same maths as before (a normalised
/// Gaussian of sigma = radius, three sigma wide, the edge pixel repeated, the horizontal result rounded to a byte before the vertical pass) but
/// without the per-sample clamp and double arithmetic of the first port: each pass adds whole rows scaled by one kernel weight (a symmetric kernel,
/// so a pair of rows share a weight), with <see cref="Vector{T}"/>, over the rows in parallel. On a 3200 px picture at the largest softness that is
/// well under a second where it took many seconds of one core. A pixel differs from the old result by one level at most (float rounding).
/// </summary>
internal static class WallpaperBlur
{
    /// <summary>Half the logical cores: a burst that finishes sooner, and the UI thread still has cores of its own.</summary>
    private static readonly int Workers = Math.Max(1, Environment.ProcessorCount / 2);

    public static DecodedBitmap Apply(DecodedBitmap source, double sigma)
    {
        var width = source.Width;
        var height = source.Height;
        var stride = checked(width * 4);
        var extent = checked((int)Math.Ceiling(sigma * 3));
        var kernel = Kernel(sigma, extent);

        var horizontal = new byte[source.Pixels.Length];
        var output = new byte[source.Pixels.Length];
        var options = new ParallelOptions { MaxDegreeOfParallelism = Workers };

        // Horizontal: each row is padded with its edge pixel to the left and right so the taps never leave the row.
        Parallel.For(0, height, options, () => new RowScratch(stride, checked(stride + extent * 8)), (y, _, scratch) =>
        {
            var padded = scratch.Padded;
            var row = source.Pixels.AsSpan(checked(y * stride), stride);
            // The first pixel repeated extent times, then the row, then the last pixel repeated extent times.
            for (var pad = 0; pad < extent; pad++)
            {
                row[..4].CopyTo(padded.AsSpan(pad * 4, 4));
                row[(stride - 4)..].CopyTo(padded.AsSpan((extent + width + pad) * 4, 4));
            }
            row.CopyTo(padded.AsSpan(extent * 4, stride));

            Array.Clear(scratch.Sum);
            var sum = scratch.Sum.AsSpan(0, stride);
            for (var tap = 0; tap < extent; tap++)
                AddPair(sum, padded.AsSpan(tap * 4, stride), padded.AsSpan((2 * extent - tap) * 4, stride), kernel[tap]);
            AddPair(sum, padded.AsSpan(extent * 4, stride), padded.AsSpan(extent * 4, stride), kernel[extent] / 2);
            Store(sum, horizontal.AsSpan(y * stride, stride));
            return scratch;
        }, static _ => { });

        // Vertical: each output row is the weighted sum of the rows around it, the first and last row repeated beyond the picture.
        Parallel.For(0, height, options, () => new RowScratch(stride, 0), (y, _, scratch) =>
        {
            Array.Clear(scratch.Sum);
            var sum = scratch.Sum.AsSpan(0, stride);
            for (var tap = 0; tap < extent; tap++)
            {
                var above = Math.Clamp(y - extent + tap, 0, height - 1);
                var below = Math.Clamp(y + extent - tap, 0, height - 1);
                AddPair(sum, horizontal.AsSpan(above * stride, stride), horizontal.AsSpan(below * stride, stride), kernel[tap]);
            }
            var middle = horizontal.AsSpan(y * stride, stride);
            AddPair(sum, middle, middle, kernel[extent] / 2);
            Store(sum, output.AsSpan(y * stride, stride));
            return scratch;
        }, static _ => { });

        return new DecodedBitmap(width, height, stride, output);
    }

    private sealed class RowScratch(int stride, int paddedLength)
    {
        public readonly float[] Sum = new float[stride];
        public readonly byte[] Padded = new byte[paddedLength];
    }

    // The normalised Gaussian of the first port, in double, then the float the passes use. Index 0 is the tap furthest out; index extent is the middle.
    private static float[] Kernel(double sigma, int extent)
    {
        var weights = new double[checked(extent * 2 + 1)];
        var total = 0.0;
        for (var offset = -extent; offset <= extent; offset++)
        {
            var weight = Math.Exp(-(offset * (double)offset) / (2 * sigma * sigma));
            weights[offset + extent] = weight;
            total += weight;
        }
        var result = new float[extent + 1];
        for (var index = 0; index <= extent; index++) result[index] = (float)(weights[index] / total);
        return result;
    }

    // sum += weight * (first + second), element by element, the two sources being byte rows of the sum's length.
    private static void AddPair(Span<float> sum, ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, float weight)
    {
        var step = Vector<byte>.Count;
        var lanes = Vector<float>.Count;
        var weights = new Vector<float>(weight);
        var at = 0;
        for (; at <= sum.Length - step; at += step)
        {
            Vector.Widen(new Vector<byte>(first[at..]), out var firstLow, out var firstHigh);
            Vector.Widen(new Vector<byte>(second[at..]), out var secondLow, out var secondHigh);
            Vector.Widen(firstLow + secondLow, out var a, out var b);
            Vector.Widen(firstHigh + secondHigh, out var c, out var d);
            AddScaled(sum, at, Vector.ConvertToSingle(a), weights);
            AddScaled(sum, at + lanes, Vector.ConvertToSingle(b), weights);
            AddScaled(sum, at + 2 * lanes, Vector.ConvertToSingle(c), weights);
            AddScaled(sum, at + 3 * lanes, Vector.ConvertToSingle(d), weights);
        }
        for (; at < sum.Length; at++) sum[at] += weight * (first[at] + second[at]);
    }

    private static void AddScaled(Span<float> sum, int at, Vector<float> values, Vector<float> weights) =>
        (new Vector<float>(sum[at..]) + values * weights).CopyTo(sum[at..]);

    // The sums are never negative and never above 255 by more than float rounding: add a half and drop the fraction.
    private static void Store(ReadOnlySpan<float> sum, Span<byte> destination)
    {
        for (var at = 0; at < sum.Length; at++) destination[at] = (byte)Math.Min(255, (int)(sum[at] + 0.5f));
    }
}
