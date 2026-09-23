using System.Globalization;
using System.Text;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

// Source: DroppyCode/Core/Support/ISO8601.swift (B2 1fbf81e), :1-32.
// Expected values are transcribed from the Swift and from PR-STORE-168, PR-STORE-169 and PR-STORE-170.
// UNK-0003 is closed as permanently blocked (OD-0001), so no Foundation fixture will exist: shapes beyond the
// two documented stamps, the date-only time zone and the sub-second drop are asserted only where the port's
// parked rule (R-TIME-3, O-17) names them, never under a Req trait.
public sealed class ISO8601Tests
{
    [Fact]
    [Trait("Req", "PR-STORE-168")] // Core/Support/ISO8601.swift:9-10, :18-21
    public void A_fractional_stamp_parses_to_its_instant_in_UTC()
    {
        var parsed = ISO8601.Date("2026-09-13T18:28:42.057Z");
        Assert.NotNull(parsed);
        var value = parsed!.Value;
        Assert.Equal(TimeSpan.Zero, value.Offset);
        var whole = new DateTimeOffset(2026, 9, 13, 18, 28, 42, TimeSpan.Zero);
        Assert.Equal(0.057, SwiftDate.TimeIntervalSince(value, whole), 6);
    }

    [Fact]
    [Trait("Req", "PR-STORE-168")] // Core/Support/ISO8601.swift:12-13, :18-21
    public void A_whole_second_stamp_parses_to_its_instant_in_UTC()
    {
        var parsed = ISO8601.Date("2026-09-13T18:28:42Z");
        Assert.NotNull(parsed);
        Assert.Equal(new DateTimeOffset(2026, 9, 13, 18, 28, 42, TimeSpan.Zero), parsed!.Value);
        Assert.Equal(TimeSpan.Zero, parsed.Value.Offset);
    }

    [Fact]
    [Trait("Req", "PR-STORE-168")] // Core/Support/ISO8601.swift:18-21
    [Trait("Req", "PR-STORE-169")] // Core/Support/ISO8601.swift:15-16, Services/Providers/CopilotSession.swift:470-472
    public void A_bare_date_empty_text_and_prose_are_not_stamps()
    {
        Assert.Null(ISO8601.Date("2026-09-13"));
        Assert.Null(ISO8601.Date(""));
        Assert.Null(ISO8601.Date("not a date"));
    }

    [Fact]
    [Trait("Req", "PR-STORE-168")] // Core/Support/ISO8601.swift:18-21, both parses are try?
    public void Text_neither_style_parses_never_throws()
    {
        // Only the absence of a throw is asserted: which of these Foundation rejects is UNK-0003.
        var inputs = new[]
        {
            "2026-13-45T25:61:61Z", "2026-09-13T18:28:42.Z", "2026-09-13T", "T18:28:42Z", "Z", ".", "-",
            new string((char)0, 1), "2026-09-13T18:28:42.057Z trailing", new string('9', 4096),
        };
        foreach (var input in inputs)
        {
            Assert.Null(Record.Exception(() => ISO8601.Date(input)));
            Assert.Null(Record.Exception(() => ISO8601.DateOnly(input)));
        }
    }

    [Fact]
    [Trait("Oracle", "O-17")]
    public void A_numeric_offset_is_read_as_the_parked_rule_says()
    {
        // UNVERIFIED O-17: whether ISO8601FormatStyle accepts +HH:MM is not settled (PR-STORE-168 failure
        // cases, UNK-0003). The parked choice (IMPLEMENTING.md Appendix B) accepts it; this is not a Req proof.
        Assert.Equal(new DateTimeOffset(2026, 9, 13, 18, 28, 42, TimeSpan.Zero), ISO8601.Date("2026-09-13T21:28:42+03:00"));
    }

    [Fact]
    [Trait("Req", "PR-STORE-169")] // Core/Support/ISO8601.swift:15-16, :23-26
    public void A_bare_date_parses_only_through_the_date_only_helper()
    {
        const string bare = "2026-09-13";
        Assert.NotNull(ISO8601.DateOnly(bare));
        Assert.Null(ISO8601.Date(bare));
        // CopilotSession.swift:470-472: ISO8601.date(from: text) ?? ISO8601.dateOnly(from: text).
        Assert.Equal(ISO8601.DateOnly(bare), ISO8601.Date(bare) ?? ISO8601.DateOnly(bare));
        Assert.Equal(TimeSpan.Zero, ISO8601.DateOnly(bare)!.Value.Offset); // R-TIME-1: a Date is a UTC DateTimeOffset
    }

    [Fact]
    [Trait("Req", "PR-STORE-169")] // Services/Providers/CopilotSession.swift:470-472
    public void A_full_stamp_is_taken_by_the_first_branch_of_the_Copilot_fallback()
    {
        const string stamp = "2026-09-13T18:28:42Z";
        var first = ISO8601.Date(stamp);
        Assert.NotNull(first);
        Assert.Equal(new DateTimeOffset(2026, 9, 13, 18, 28, 42, TimeSpan.Zero), first ?? ISO8601.DateOnly(stamp));
    }

    [Fact]
    [Trait("Req", "PR-STORE-169")] // Core/Support/ISO8601.swift:23-26
    public void Text_the_year_month_day_style_cannot_read_returns_nil()
    {
        Assert.Null(ISO8601.DateOnly("13/09/2026"));
        Assert.Null(ISO8601.DateOnly(""));
        Assert.Null(ISO8601.DateOnly("not a date"));
    }

    [Fact]
    [Trait("Req", "PR-STORE-169")] // Core/Support/ISO8601.swift:15-16, :23-26
    public void A_date_that_is_not_ISO_8601_returns_nil()
    {
        // The style is Date.ISO8601FormatStyle().year().month().day(), not a general-purpose date parser: neither
        // input starts with a dash-separated four-digit year, so no edge shape of UNK-0003 is involved.
        Assert.Null(ISO8601.DateOnly("09/13/2026"));
        Assert.Null(ISO8601.DateOnly("Sep 13 2026"));
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void The_general_purpose_parser_accepts_dates_that_are_not_ISO_8601()
    {
        // Negative control for A_date_that_is_not_ISO_8601_returns_nil: the obvious .NET API reads both inputs
        // (observed on .NET 10, Windows 11), so it cannot be the port of ISO8601.dateOnly(from:).
        Assert.True(DateTimeOffset.TryParse("09/13/2026", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _));
        Assert.True(DateTimeOffset.TryParse("Sep 13 2026", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _));
    }

    [Fact]
    [Trait("Rule", "R-TIME-3")]
    public void A_bare_date_is_midnight_UTC_as_the_parked_rule_says()
    {
        // UNVERIFIED (PR-STORE-169 failure cases, UNK-0003 closed by OD-0001): the default time zone of
        // Date.ISO8601FormatStyle() was not run. R-TIME-3 maps dateOnly to ParseDateOnly, midnight UTC; the
        // source's own support is ISO8601.swift:28 ("UTC") for the plain style built by the same initializer.
        Assert.Equal(new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero), ISO8601.DateOnly("2026-09-13"));
    }

    [Fact]
    [Trait("Req", "PR-STORE-170")] // Core/Support/ISO8601.swift:12-13, :28-31
    public void A_stamp_is_written_in_whole_seconds_UTC_with_a_Z_suffix()
    {
        var utc = new DateTimeOffset(2026, 9, 13, 18, 28, 42, TimeSpan.Zero);
        var written = ISO8601.String(utc);
        Assert.Equal(Encoding.UTF8.GetBytes("2026-09-13T18:28:42Z"), Encoding.UTF8.GetBytes(written));

        // The same instant held with a local offset writes the same UTC text.
        var local = new DateTimeOffset(2026, 9, 13, 21, 28, 42, TimeSpan.FromHours(3));
        Assert.Equal("2026-09-13T18:28:42Z", ISO8601.String(local));
        var west = new DateTimeOffset(2026, 9, 13, 10, 58, 42, TimeSpan.FromHours(-7.5));
        Assert.Equal("2026-09-13T18:28:42Z", ISO8601.String(west));

        // The callers' shapes (HangWatchdog.swift:88-89, OpenAICompatibleSession.swift:1212).
        Assert.Equal("hang-2026-09-13T18-28-42Z.txt", "hang-" + written.Replace(":", "-", StringComparison.Ordinal) + ".txt");
        Assert.Equal("- Today's date is 2026-09-13T18:28:42Z.", "- Today's date is " + written + ".");
    }

    [Fact]
    [Trait("Req", "PR-STORE-170")] // Core/Support/ISO8601.swift:28-31; windows_path: invariant culture, Gregorian
    public void Regional_settings_cannot_change_digits_separators_or_calendar()
    {
        var date = new DateTimeOffset(2026, 9, 13, 18, 28, 42, TimeSpan.Zero);
        var saved = CultureInfo.CurrentCulture;
        var savedUI = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var name in new[] { "th-TH", "ar-SA", "fa-IR", "de-DE", "en-US" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(name);
                CultureInfo.CurrentUICulture = new CultureInfo(name);
                Assert.Equal("2026-09-13T18:28:42Z", ISO8601.String(date));
            }

            // The pitfall: the culture's own calendar renumbers the year (observed on .NET 10, Windows 11).
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");
            Assert.Equal("2569-09-13T18:28:42Z", date.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.CurrentCulture));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
            CultureInfo.CurrentUICulture = savedUI;
        }
    }

    [Fact]
    [Trait("Rule", "R-TIME-3")]
    public void A_sub_second_part_is_dropped_as_the_parked_rule_says()
    {
        // UNVERIFIED (PR-STORE-170 failure cases, UNK-0003 closed by OD-0001): truncation or rounding was not
        // run. R-TIME-3 maps string(from:) to FoundationISO8601.Format, whole seconds by floor.
        var late = new DateTimeOffset(2026, 9, 13, 18, 28, 42, 999, TimeSpan.Zero);
        Assert.Equal("2026-09-13T18:28:42Z", ISO8601.String(late));
    }
}
