using Marrow.Core.Port;

namespace Marrow.Core.Tests.Port;

// ED-NEW-lean-2: the BigInteger-free conversions must give the exact answers of the frozen BigInteger code (SwiftDateFrozen). One million
// fixed-seed cases each way. FromReferenceSeconds (NearestTicks): uniform values across and just past the DateTimeOffset range, m x 2^e
// with random 53-bit mantissas and exponents from -100 to 10 (the sub-tick, cut-off and overflow edges), exact half-tick ties and other
// binary fractions of a tick, raw random bit patterns (NaN, infinities, subnormals) and present-day values a few ulps either side of a
// tick. ReferenceSeconds: tick offsets inside +-2^53 (the division path), at that edge, across the whole DateTimeOffset range and whole
// seconds. Results are compared bit for bit (ticks, Double bits), and a thrown exception by its type.
[Trait("Decision", "ED-NEW-lean-2")]
public sealed class SwiftDateLeanEquivalenceTests
{
    private const int Cases = 1_000_000;
    private const long ReferenceTicks = 631139040000000000L;

    [Fact]
    public void FromReferenceSeconds_matches_the_frozen_BigInteger_rounding_on_a_million_doubles()
    {
        var random = new Random(5004);
        double[] specials =
        [
            0d, -0d, double.Epsilon, -double.Epsilon, double.MaxValue, double.MinValue, double.NaN, double.PositiveInfinity,
            double.NegativeInfinity, SwiftDate.DistantPastSeconds, Math.BitIncrement(SwiftDate.DistantPastSeconds),
            Math.BitDecrement(SwiftDate.DistantPastSeconds), 63113904000d, 0.5e-7, 1.5e-7, 2.5e-7, -0.5e-7, -1.5e-7, 1d / 256, 3d / 256,
            -1d / 256, Math.ScaleB(1, -24), Math.ScaleB(1, -25), Math.ScaleB(1, -77), Math.ScaleB(1, -78), Math.ScaleB((1L << 53) - 1, -77),
            Math.ScaleB((1L << 53) - 1, -78), Math.ScaleB((1L << 53) - 1, -101), long.MaxValue / 1e7, -(long.MaxValue / 1e7),
            Math.BitIncrement(long.MaxValue / 1e7), Math.ScaleB(1, 39), Math.ScaleB(1, 40), Math.ScaleB(1, 52), Math.ScaleB(1, 53),
        ];
        var mismatches = new List<string>();
        foreach (var value in specials) Compare(value, mismatches);
        for (var i = 0; i < Cases; i++) Compare(Sample(random, i), mismatches);
        Assert.True(mismatches.Count == 0, $"{mismatches.Count} mismatches, first: {string.Join(" | ", mismatches.Take(5))}");
    }

    [Fact]
    public void ReferenceSeconds_matches_the_frozen_BigInteger_rounding_on_a_million_dates()
    {
        var random = new Random(5005);
        var maxTicks = DateTimeOffset.MaxValue.UtcTicks;
        long[] specials =
        [
            0, 1, maxTicks, maxTicks - 1, ReferenceTicks, ReferenceTicks + 1, ReferenceTicks - 1, ReferenceTicks + (1L << 53),
            ReferenceTicks - (1L << 53), ReferenceTicks + (1L << 53) + 1, ReferenceTicks - (1L << 53) - 1, ReferenceTicks + (1L << 53) - 1,
            ReferenceTicks - (1L << 53) + 1, ReferenceTicks + 10_000_000, ReferenceTicks - 10_000_000,
        ];
        var mismatches = new List<string>();
        foreach (var ticks in specials) Compare(new DateTimeOffset(ticks, TimeSpan.Zero), mismatches);
        for (var i = 0; i < Cases; i++)
        {
            var offset = (i % 10) switch
            {
                < 5 => random.NextInt64(-(1L << 53), (1L << 53) + 1),
                5 => (random.Next(2) == 0 ? 1 : -1) * ((1L << 53) + random.NextInt64(-1_000, 1_001)),
                6 => random.NextInt64(-(1L << 20), 1L << 20),
                7 => random.NextInt64(-63_113_904_000L, 252_423_993_600L) * TimeSpan.TicksPerSecond,
                _ => random.NextInt64(0, maxTicks + 1) - ReferenceTicks,
            };
            var ticks = Math.Clamp(ReferenceTicks + offset, 0, maxTicks);
            Compare(new DateTimeOffset(ticks, TimeSpan.Zero), mismatches);
        }
        Assert.True(mismatches.Count == 0, $"{mismatches.Count} mismatches, first: {string.Join(" | ", mismatches.Take(5))}");
    }

    private static double Sample(Random random, int i) => (i % 10) switch
    {
        < 4 => -64_000_000_000d + random.NextDouble() * 317_000_000_000d,
        4 or 5 => (random.Next(2) == 0 ? 1 : -1) * Math.ScaleB(random.NextInt64(1L << 52, 1L << 53), random.Next(-100, 11)),
        6 => (random.NextInt64(-(1L << 40), 1L << 40) * 2 + 1) / 256d,
        7 => random.NextInt64(-(1L << 45), 1L << 45) / Math.ScaleB(1, random.Next(0, 31)),
        8 => BitConverter.Int64BitsToDouble(random.NextInt64(long.MinValue, long.MaxValue)),
        _ => Nudge(random, random.NextInt64(-(1L << 53), 1L << 53) / 1e7),
    };

    private static double Nudge(Random random, double value)
    {
        var steps = random.Next(-2, 3);
        for (var s = 0; s < Math.Abs(steps); s++) value = steps < 0 ? Math.BitDecrement(value) : Math.BitIncrement(value);
        return value;
    }

    private static void Compare(double seconds, List<string> mismatches)
    {
        var expected = Outcome(() => SwiftDateFrozen.FromReferenceSeconds(seconds).UtcTicks);
        var actual = Outcome(() => SwiftDate.FromReferenceSeconds(seconds).UtcTicks);
        if (expected != actual) mismatches.Add($"{seconds:R} ({BitConverter.DoubleToInt64Bits(seconds):X16}): frozen {expected}, shipped {actual}");
    }

    private static void Compare(DateTimeOffset date, List<string> mismatches)
    {
        var expected = BitConverter.DoubleToInt64Bits(SwiftDateFrozen.ReferenceSeconds(date));
        var actual = BitConverter.DoubleToInt64Bits(SwiftDate.ReferenceSeconds(date));
        if (expected != actual) mismatches.Add($"ticks {date.UtcTicks}: frozen {expected:X16}, shipped {actual:X16}");
    }

    private static string Outcome(Func<long> run)
    {
        try { return run().ToString(System.Globalization.CultureInfo.InvariantCulture); }
        catch (Exception error) { return error.GetType().Name; }
    }
}
