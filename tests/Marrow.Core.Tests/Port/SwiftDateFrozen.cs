using System.Numerics;
using Marrow.Core.Port;

namespace Marrow.Core.Tests.Port;

// ED-NEW-lean-2: a frozen copy of SwiftDate's BigInteger conversions as they were before the lean pass (claude/release-integration
// 0a946c558), kept only as the oracle SwiftDateLeanEquivalenceTests compares the shipped code with. Do not change it: it is the reference.
internal static class SwiftDateFrozen
{
    private const long ReferenceTicks = 631139040000000000L; // 2001-01-01T00:00:00Z

    public static double ReferenceSeconds(DateTimeOffset date) =>
        date.UtcTicks == 0 ? SwiftDate.DistantPastSeconds : NearestDouble(date.UtcTicks - ReferenceTicks, TimeSpan.TicksPerSecond);

    public static DateTimeOffset FromReferenceSeconds(double seconds) =>
        seconds == SwiftDate.DistantPastSeconds ? SwiftDate.DistantPast : new(ReferenceTicks + NearestTicks(seconds), TimeSpan.Zero);

    public static long NearestTicks(double seconds)
    {
        if (!double.IsFinite(seconds)) throw new SwiftTrapException("A Date cannot hold a non-finite interval.");
        var (mantissa, exponent) = Decompose(seconds);
        var scaled = mantissa * TimeSpan.TicksPerSecond;
        if (exponent >= 0) return checked((long)(scaled << exponent));
        var denominator = BigInteger.One << -exponent;
        var quotient = BigInteger.DivRem(BigInteger.Abs(scaled), denominator, out var remainder);
        var twice = remainder * 2;
        if (twice > denominator || (twice == denominator && !quotient.IsEven)) quotient += 1;
        return checked((long)(scaled.Sign < 0 ? -quotient : quotient));
    }

    public static double NearestDouble(long numerator, long denominator)
    {
        var approximate = numerator / (double)denominator;
        var best = approximate;
        BigInteger? bestError = null;
        for (var step = -3; step <= 3; step++)
        {
            var candidate = approximate;
            for (var i = 0; i < Math.Abs(step); i++) candidate = step < 0 ? Math.BitDecrement(candidate) : Math.BitIncrement(candidate);
            var (mantissa, exponent) = Decompose(candidate);
            var shift = Math.Max(0, -exponent);
            var error = BigInteger.Abs(mantissa * denominator * (BigInteger.One << (exponent + shift)) - (BigInteger)numerator * (BigInteger.One << shift));
            error <<= 1100 - shift;
            if (bestError is null || error < bestError || (error == bestError && (BitConverter.DoubleToInt64Bits(candidate) & 1) == 0))
            {
                best = candidate;
                bestError = error;
            }
        }
        return best;
    }

    private static (BigInteger Mantissa, int Exponent) Decompose(double value)
    {
        var bits = BitConverter.DoubleToInt64Bits(value);
        var biased = (int)((bits >> 52) & 0x7FF);
        var fraction = bits & 0xFFFFFFFFFFFFFL;
        var mantissa = biased == 0 ? (BigInteger)fraction : (BigInteger)(fraction | (1L << 52));
        return (bits < 0 ? -mantissa : mantissa, biased == 0 ? -1074 : biased - 1075);
    }
}
