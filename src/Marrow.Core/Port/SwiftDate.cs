using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Marrow.Core.Port;

/// <summary>
/// PORT-ONLY. Foundation <c>Date</c> is a Double of seconds since 2001-01-01T00:00:00Z. The port keeps
/// DateTimeOffset (UTC, offset zero) as the model type, as MCPConnection.ConnectedAt already does, and does
/// Swift's Double arithmetic through these helpers (ED-073). From 2018-01-05 (2^29 s after the reference
/// date) a Double's spacing exceeds one 100 ns tick, so Double to ticks to Double is the identity and
/// persisted Doubles survive a round trip bit for bit. Source: draft A (proto-A Support/SwiftDate.cs).
/// </summary>
public static class SwiftDate
{
    private const long ReferenceTicks = 631139040000000000L; // 2001-01-01T00:00:00Z

    /// <summary><c>Date.timeIntervalBetween1970AndReferenceDate</c>.</summary>
    public const double ReferenceTo1970 = 978307200d;

    /// <summary><c>Date.distantPast.timeIntervalSinceReferenceDate</c>: Julian 0001-01-01, which is proleptic
    /// Gregorian 0000-12-30, two days before DateTimeOffset.MinValue.</summary>
    public const double DistantPastSeconds = -63114076800d;

    public static DateTimeOffset ReferenceDate { get; } = new(ReferenceTicks, TimeSpan.Zero);

    /// <summary>
    /// <c>Date.distantPast</c>. Not representable, so DateTimeOffset.MinValue is its SENTINEL: it sorts before
    /// every real date and converts back to exactly <see cref="DistantPastSeconds"/>. If the source does any
    /// other arithmetic with distantPast, stop and report (section 6).
    /// </summary>
    public static DateTimeOffset DistantPast { get; } = DateTimeOffset.MinValue;

    /// <summary><c>Date.distantFuture</c>: 4001-01-01T00:00:00Z (63113904000 s).</summary>
    public static DateTimeOffset DistantFuture { get; } = FromReferenceSeconds(63113904000d);

    /// <summary><c>Date()</c> / <c>Date.now</c> / <c>.now</c>, through <see cref="SwiftClock.Provider"/>, so tests fix it.</summary>
    public static DateTimeOffset Now => SwiftClock.Provider.Current.GetUtcNow().ToUniversalTime();

    /// <summary><c>date.timeIntervalSinceReferenceDate</c>: the Double nearest to the tick count (exact rounding).</summary>
    public static double ReferenceSeconds(DateTimeOffset date)
    {
        if (date.UtcTicks == 0) return DistantPastSeconds;
        var offset = date.UtcTicks - ReferenceTicks;
        // Within 2^53 ticks (1972 to mid-2029) the offset and 10^7 are exact Doubles and IEEE division is correctly rounded, ties to
        // even: the nearest Double with no BigInteger work (ED-NEW-lean-2). Further out the exact neighbour comparison stays.
        return offset is >= -ExactTicks and <= ExactTicks ? offset / (double)TimeSpan.TicksPerSecond : NearestDouble(offset, TimeSpan.TicksPerSecond);
    }

    private const long ExactTicks = 1L << 53;

    /// <summary><c>Date(timeIntervalSinceReferenceDate:)</c>: the tick nearest to the Double (exact, ties to even).</summary>
    public static DateTimeOffset FromReferenceSeconds(double seconds) =>
        seconds == DistantPastSeconds ? DistantPast : new(ReferenceTicks + NearestTicks(seconds), TimeSpan.Zero);

    /// <summary><c>date.timeIntervalSince1970</c>.</summary>
    public static double Seconds1970(DateTimeOffset date) => ReferenceSeconds(date) + ReferenceTo1970;

    /// <summary><c>Date(timeIntervalSince1970:)</c>.</summary>
    public static DateTimeOffset From1970Seconds(double seconds) => FromReferenceSeconds(seconds - ReferenceTo1970);

    /// <summary><c>date.addingTimeInterval(s)</c>: the sum is taken in Double, as Swift does, then stored.</summary>
    public static DateTimeOffset AddingTimeInterval(DateTimeOffset date, double seconds) => FromReferenceSeconds(ReferenceSeconds(date) + seconds);

    /// <summary><c>a.timeIntervalSince(b)</c>: a Double difference, not a TimeSpan.</summary>
    public static double TimeIntervalSince(DateTimeOffset date, DateTimeOffset other) => ReferenceSeconds(date) - ReferenceSeconds(other);

    // seconds = m x 2^e exactly; ticks = round(m x 10^7 x 2^e), half to even, in exact integer arithmetic. m < 2^53, so m x 10^7 < 2^77
    // fits UInt128 with no BigInteger (ED-NEW-lean-2).
    private static long NearestTicks(double seconds)
    {
        if (!double.IsFinite(seconds)) throw new SwiftTrapException("A Date cannot hold a non-finite interval.");
        var bits = BitConverter.DoubleToInt64Bits(seconds);
        var biased = (int)((bits >> 52) & 0x7FF);
        var fraction = (ulong)(bits & 0xFFFFFFFFFFFFFL);
        var scaled = (UInt128)(biased == 0 ? fraction : fraction | (1UL << 52)) * (ulong)TimeSpan.TicksPerSecond;
        var exponent = biased == 0 ? -1074 : biased - 1075;
        // A non-negative exponent means a normal value of at least 2^52 s, over 4 x 10^22 ticks: never an Int64.
        if (exponent >= 0) throw new OverflowException("Value was either too large or too small for an Int64.");
        var shift = -exponent;
        UInt128 quotient = 0;
        // From a shift of 78 the value is below half a tick (twice the remainder < 2^78 <= 2^shift), so it rounds to 0.
        if (shift < 78)
        {
            quotient = scaled >> shift;
            var twice = (scaled - (quotient << shift)) << 1;
            var denominator = UInt128.One << shift;
            if (twice > denominator || (twice == denominator && (quotient & 1) != 0)) quotient += 1;
        }
        return checked((long)(bits < 0 ? -(Int128)quotient : (Int128)quotient));
    }

    // The Double nearest to numerator / denominator, ties to even: the division is within a few ulps, so its
    // neighbours are compared exactly.
    private static double NearestDouble(long numerator, long denominator)
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
            // Compare at a common scale of 2^1100 so candidates with different exponents are comparable.
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

/// <summary>
/// PORT-ONLY. <c>Date.ISO8601FormatStyle</c> as the source uses it: the <c>.iso8601</c> strategy of
/// <c>JSONEncoder/Decoder.storage</c> (Coding.swift:19-34) and the shapes behind ISO8601.swift (whose own port,
/// Support/ISO8601.cs, is built from these). Source: draft A (proto-A SwiftISO8601), with the storage reading
/// kept at the existing Coding.cs behaviour until the oracle settles O-02.
/// </summary>
public static partial class FoundationISO8601
{
    /// <summary>
    /// <c>ISO8601FormatStyle().format(date)</c>, the <c>.iso8601</c> ENCODING strategy and ISO8601.string(from:):
    /// whole seconds, UTC, 'Z'. The fraction is dropped by FLOOR (toward the past), also before 1970.
    /// </summary>
    public static string Format(DateTimeOffset date)
    {
        if (date.UtcTicks == 0) return "0000-12-30T00:00:00Z"; // Date.distantPast (UNVERIFIED rendering)
        var ticks = date.UtcTicks;
        var whole = ticks - (((ticks % TimeSpan.TicksPerSecond) + TimeSpan.TicksPerSecond) % TimeSpan.TicksPerSecond);
        return new DateTimeOffset(whole, TimeSpan.Zero).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The <c>.iso8601</c> DECODING strategy as the port reads it today: whole or fractional seconds, 'Z' or
    /// +HH:MM. This is the existing Coding.cs rule (T-M2-059). Swift very likely rejects the fractional form
    /// (ISO8601.swift:6-7 says the plain style does); UNVERIFIED O-02. Rejecting would drop a record Swift
    /// might keep, so the lenient reading stays until the oracle answers; then this becomes
    /// <see cref="StorageDateWholeSecondsOnly"/>.
    /// </summary>
    public static DateTimeOffset? StorageDate(string text) => Parse(text, allowFraction: true, requireFraction: false);

    /// <summary>The strict reading, PARKED until O-02 is settled: a fractional stamp is rejected.</summary>
    public static DateTimeOffset? StorageDateWholeSecondsOnly(string text) => Parse(text, allowFraction: false, requireFraction: false);

    /// <summary><c>ISO8601FormatStyle(includingFractionalSeconds: true).parse</c>: a fraction is required.</summary>
    public static DateTimeOffset? ParseFractional(string text) => Parse(text, allowFraction: true, requireFraction: true);

    /// <summary><c>ISO8601FormatStyle().parse</c>: whole seconds only (ISO8601.swift:6-7).</summary>
    public static DateTimeOffset? ParsePlain(string text) => Parse(text, allowFraction: false, requireFraction: false);

    /// <summary><c>ISO8601FormatStyle().year().month().day().parse</c>: yyyy-MM-dd at 00:00:00 UTC.</summary>
    public static DateTimeOffset? ParseDateOnly(string text)
    {
        var match = DateOnlyPattern().Match(text);
        if (!match.Success) return null;
        try { return new DateTimeOffset(Int(match, 1), Int(match, 2), Int(match, 3), 0, 0, 0, TimeSpan.Zero); }
        catch (ArgumentException) { return null; }
    }

    private static DateTimeOffset? Parse(string text, bool allowFraction, bool requireFraction)
    {
        var match = StampPattern().Match(text);
        if (!match.Success) return null;
        var fraction = match.Groups[7].Value;
        if (fraction.Length > 0 && !allowFraction) return null;
        if (fraction.Length == 0 && requireFraction) return null;
        try
        {
            var zone = match.Groups[8].Value;
            var offset = zone == "Z" ? TimeSpan.Zero : new TimeSpan(Int(zone[1..3]), Int(zone[4..6]), 0) * (zone[0] == '-' ? -1 : 1);
            var whole = new DateTimeOffset(Int(match, 1), Int(match, 2), Int(match, 3), Int(match, 4), Int(match, 5), Int(match, 6), offset).ToUniversalTime();
            return fraction.Length == 0 ? whole
                : SwiftDate.AddingTimeInterval(whole, double.Parse("0" + fraction, CultureInfo.InvariantCulture));
        }
        catch (ArgumentException) { return null; }
    }

    private static int Int(Match match, int group) => Int(match.Groups[group].Value);

    private static int Int(string digits) => int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\A([0-9]{4})-([0-9]{2})-([0-9]{2})T([0-9]{2}):([0-9]{2}):([0-9]{2})(\.[0-9]+)?(Z|[+-][0-9]{2}:[0-9]{2})\z", RegexOptions.CultureInvariant)]
    private static partial Regex StampPattern();

    [GeneratedRegex(@"\A([0-9]{4})-([0-9]{2})-([0-9]{2})\z", RegexOptions.CultureInvariant)]
    private static partial Regex DateOnlyPattern();
}
