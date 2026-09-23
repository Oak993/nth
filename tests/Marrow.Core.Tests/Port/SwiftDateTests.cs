using System.Text;
using System.Text.Json;
using Marrow.Core.Port;

namespace Marrow.Core.Tests.Port;

// R-TIME rules (IMPLEMENTING.md 3.12). Source: draft A (proto-A SwiftDateTests), with the storage reading kept
// at the existing Coding.cs behaviour until O-02.
public sealed class SwiftDateTests
{
    [Fact]
    [Trait("Rule", "R-TIME-3")]
    [Trait("Oracle", "O-46")]
    public void ISO8601_writes_whole_seconds_by_floor_in_UTC()
    {
        var date = new DateTimeOffset(2026, 9, 22, 5, 0, 1, TimeSpan.FromHours(3)).AddMilliseconds(999);
        Assert.Equal("2026-09-22T02:00:01Z", FoundationISO8601.Format(date));
        Assert.Equal("1969-12-31T23:59:59Z", FoundationISO8601.Format(SwiftDate.From1970Seconds(-0.5)));
    }

    [Fact]
    [Trait("Rule", "R-TIME-3")]
    [Trait("Oracle", "O-02")]
    public void Storage_decoding_keeps_the_existing_lenient_rule_and_the_strict_one_is_parked()
    {
        Assert.NotNull(FoundationISO8601.StorageDate("2026-09-13T18:28:42.057Z"));
        Assert.Null(FoundationISO8601.StorageDateWholeSecondsOnly("2026-09-13T18:28:42.057Z"));
        Assert.Equal(FoundationISO8601.StorageDate("2026-09-13T18:28:42Z"), FoundationISO8601.StorageDate("2026-09-13T21:28:42+03:00"));
    }

    [Fact]
    [Trait("Rule", "R-TIME-3")]
    public void The_fractional_and_plain_styles_each_reject_the_other_shape()
    {
        // ISO8601.swift:6-7: "A style that requires fractional seconds rejects a whole-second stamp and vice versa."
        Assert.Null(FoundationISO8601.ParseFractional("2026-09-13T18:28:42Z"));
        Assert.Null(FoundationISO8601.ParsePlain("2026-09-13T18:28:42.057Z"));
        var fractional = FoundationISO8601.ParseFractional("2026-09-13T18:28:42.057Z")!.Value;
        Assert.Equal(0.057, SwiftDate.TimeIntervalSince(fractional, FoundationISO8601.ParsePlain("2026-09-13T18:28:42Z")!.Value), 6);
        Assert.Null(FoundationISO8601.ParsePlain("2026-09-13 18:28:42Z"));
    }

    [Fact]
    [Trait("Rule", "R-TIME-3")]
    [Trait("Oracle", "O-45")]
    public void A_bare_date_parses_to_midnight_UTC_as_the_parked_rule_says()
    {
        // UNVERIFIED: the default time zone of the date-only style is oracle item O-45 (PR-STORE-169 failure case 2).
        Assert.Equal(new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero), FoundationISO8601.ParseDateOnly("2026-09-13"));
    }

    [Fact]
    [Trait("Rule", "R-TIME-1")]
    [Trait("Decision", "ED-073")]
    public void Reference_seconds_round_trip_exactly_for_present_day_dates()
    {
        var random = new Random(20260922);
        for (var i = 0; i < 100_000; i++)
        {
            var seconds = Math.ScaleB(1.0, 29) + random.NextDouble() * (Math.ScaleB(1.0, 33) - Math.ScaleB(1.0, 29));
            Assert.Equal(seconds, SwiftDate.ReferenceSeconds(SwiftDate.FromReferenceSeconds(seconds)));
        }
    }

    [Fact]
    [Trait("Rule", "R-TIME-1")]
    public void Before_2018_a_tick_is_coarser_than_the_Double()
    {
        const double seconds = 12345.123456789;
        Assert.NotEqual(seconds, SwiftDate.ReferenceSeconds(SwiftDate.FromReferenceSeconds(seconds)));
    }

    [Fact]
    [Trait("Rule", "R-CODE-6")]
    public void A_plain_JSONEncoder_date_is_reference_date_seconds()
    {
        var date = new DateTimeOffset(2001, 1, 1, 0, 0, 1, TimeSpan.Zero);
        Assert.Equal("1", SwiftNumber.JsonText(SwiftDate.ReferenceSeconds(date)));
        Assert.Equal(978307201.0, SwiftDate.Seconds1970(date));
        using var document = JsonDocument.Parse(Encoding.UTF8.GetBytes("780000000.25"));
        Assert.Equal("780000000.25", SwiftNumber.JsonText(SwiftDate.ReferenceSeconds(SwiftDecoding.ReadDeferredDate(document.RootElement))));
    }

    [Fact]
    [Trait("Rule", "R-TIME-1")]
    public void Arithmetic_is_in_Double_seconds()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(1.5, SwiftDate.TimeIntervalSince(SwiftDate.AddingTimeInterval(start, 1.5), start));
        Assert.Equal(-240.0, SwiftDate.TimeIntervalSince(start, SwiftDate.AddingTimeInterval(start, 4 * 60)));
    }

    [Fact]
    [Trait("Rule", "R-TIME-1")]
    public void Distant_past_is_a_sentinel_and_distant_future_is_exact()
    {
        Assert.Equal(DateTimeOffset.MinValue, SwiftDate.DistantPast); // Swift's value is two days earlier
        Assert.Equal(-63114076800d, SwiftDate.ReferenceSeconds(SwiftDate.DistantPast));
        Assert.Equal(SwiftDate.DistantPast, SwiftDate.FromReferenceSeconds(-63114076800d));
        Assert.True(SwiftDate.DistantPast < new DateTimeOffset(1, 1, 1, 0, 0, 1, TimeSpan.Zero));
        Assert.Equal(new DateTimeOffset(4001, 1, 1, 0, 0, 0, TimeSpan.Zero), SwiftDate.DistantFuture);
    }
}
