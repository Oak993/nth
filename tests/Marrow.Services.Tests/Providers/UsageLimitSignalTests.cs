using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/UsageLimitSignal.swift (B2 1fbf81e), :1-54.
public sealed class UsageLimitSignalTests
{
    private const string EnUS = "en_US";
    private const string NarrowNoBreakSpace = "\U0000202F"; // U+202F NARROW NO-BREAK SPACE

    // ---- PR-PROVREG-219: matches ----------------------------------------------------------------------------

    [Theory]
    [Trait("Req", "PR-PROVREG-219")] // Services/Providers/UsageLimitSignal.swift:6-10
    [InlineData("Your usage limit is spent.")]
    [InlineData("YOU HIT YOUR LIMIT for today.")]
    [InlineData("error: usage_limit exceeded")]
    [InlineData("You have limit reached on this plan.")]
    [InlineData("You are out of extra usage this month.")]
    [InlineData("Your USAGE LIMITS are close.")] // substring containment, not a word match
    public void Matches_true_for_each_of_the_five_recognised_substrings(string text)
    {
        Assert.True(UsageLimitSignal.Matches(text));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-219")] // Services/Providers/UsageLimitSignal.swift:6-10
    public void Matches_false_when_none_of_the_five_substrings_are_present()
    {
        Assert.False(UsageLimitSignal.Matches("The request failed for an unrelated reason."));
    }

    // ---- PR-PROVREG-220: resetTime, the bar-then-timestamp path ---------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-220")] // Services/Providers/UsageLimitSignal.swift:16-21
    public void ResetTime_reads_a_nine_digit_unix_timestamp_after_a_bar_as_seconds()
    {
        var result = UsageLimitSignal.ResetTime("usage limit reached|1757865600");
        Assert.Equal(SwiftDate.From1970Seconds(1757865600), result);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-220")] // Services/Providers/UsageLimitSignal.swift:19-20
    public void ResetTime_divides_a_timestamp_above_1e11_by_1000_as_milliseconds()
    {
        var result = UsageLimitSignal.ResetTime("usage limit reached|1757865600123");
        Assert.Equal(SwiftDate.From1970Seconds(1757865600.123), result);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-220")] // Services/Providers/UsageLimitSignal.swift:16
    public void ResetTime_at_exactly_1e11_is_treated_as_seconds_not_milliseconds()
    {
        // 1e11 is NOT greater than 1e11, so the seconds branch is taken (the comparison is strict).
        var result = UsageLimitSignal.ResetTime("usage limit reached|100000000000");
        Assert.Equal(SwiftDate.From1970Seconds(100000000000d), result);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-220")] // Services/Providers/UsageLimitSignal.swift:16
    public void ResetTime_requires_at_least_nine_digits_after_the_bar()
    {
        // Only eight digits: the timestamp pattern does not match, and with no clock phrase either, the result is null.
        var result = UsageLimitSignal.ResetTime("usage limit reached|12345678");
        Assert.Null(result);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-220")] // Services/Providers/UsageLimitSignal.swift:16-38
    public void ResetTime_tries_the_timestamp_path_before_the_clock_path_and_the_timestamp_wins()
    {
        var result = UsageLimitSignal.ResetTime("usage limit reached|1757865600, resets at 3:30 pm");
        Assert.Equal(SwiftDate.From1970Seconds(1757865600), result);
    }

    // ---- PR-PROVREG-221: resetTime, the clock-time path --------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-221")] // Services/Providers/UsageLimitSignal.swift:23-38
    public void ResetTime_returns_the_next_occurrence_of_the_clock_time_later_the_same_day()
    {
        using var zone = SwiftFormatters.CurrentTimeZone.Override(TimeZoneInfo.Utc);
        var now = new DateTimeOffset(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);
        var result = UsageLimitSignal.ResetTime("resets at 3:30 pm", now);
        Assert.Equal(new DateTimeOffset(2026, 9, 23, 15, 30, 0, TimeSpan.Zero), result);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-221")] // Services/Providers/UsageLimitSignal.swift:23-38
    public void ResetTime_rolls_to_tomorrow_when_the_clock_time_has_already_passed_today()
    {
        using var zone = SwiftFormatters.CurrentTimeZone.Override(TimeZoneInfo.Utc);
        var now = new DateTimeOffset(2026, 9, 23, 16, 0, 0, TimeSpan.Zero);
        var result = UsageLimitSignal.ResetTime("resets at 3:30 pm", now);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 15, 30, 0, TimeSpan.Zero), result);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-221")] // Services/Providers/UsageLimitSignal.swift:26-27
    public void ResetTime_defaults_the_minute_to_zero_when_no_colon_minute_is_given()
    {
        using var zone = SwiftFormatters.CurrentTimeZone.Override(TimeZoneInfo.Utc);
        var now = new DateTimeOffset(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);
        var result = UsageLimitSignal.ResetTime("resets 3pm", now);
        Assert.Equal(new DateTimeOffset(2026, 9, 23, 15, 0, 0, TimeSpan.Zero), result);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-221")] // Services/Providers/UsageLimitSignal.swift:28-30
    public void ResetTime_treats_12pm_as_noon_and_12am_as_midnight()
    {
        using var zone = SwiftFormatters.CurrentTimeZone.Override(TimeZoneInfo.Utc);
        var now = new DateTimeOffset(2026, 9, 23, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero), UsageLimitSignal.ResetTime("resets 12pm", now));
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero), UsageLimitSignal.ResetTime("resets 12am", now));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-221")] // Services/Providers/UsageLimitSignal.swift:32-35
    public void ResetTime_honours_a_parenthesised_zone_identifier_found_anywhere_in_the_message()
    {
        // now: 2026-09-23T10:00:00Z = 06:00 in New York (UTC-4, EDT). "resets at 9:00 am (America/New_York)"
        // means 09:00 New York time, which is 13:00 UTC, later than now.
        using var zone = SwiftFormatters.CurrentTimeZone.Override(TimeZoneInfo.Utc);
        var now = new DateTimeOffset(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);
        var result = UsageLimitSignal.ResetTime("Please retry after your limit resets at 9:00 am. Region: (America/New_York)", now);
        Assert.Equal(new DateTimeOffset(2026, 9, 23, 13, 0, 0, TimeSpan.Zero), result);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-221")] // Services/Providers/UsageLimitSignal.swift:32-35, ED-034
    public void ResetTime_falls_back_to_the_current_zone_when_the_parenthesised_identifier_is_unrecognised()
    {
        using var zone = SwiftFormatters.CurrentTimeZone.Override(TimeZoneInfo.Utc);
        var now = new DateTimeOffset(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);
        var result = UsageLimitSignal.ResetTime("resets at 3:30 pm (Not/AZone)", now);
        Assert.Equal(new DateTimeOffset(2026, 9, 23, 15, 30, 0, TimeSpan.Zero), result);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-221")] // Services/Providers/UsageLimitSignal.swift:37-39
    public void ResetTime_is_null_with_no_am_or_pm_marker()
    {
        var result = UsageLimitSignal.ResetTime("resets at 15:30");
        Assert.Null(result);
    }

    [Theory]
    [Trait("Req", "PR-PROVREG-221")] // Services/Providers/UsageLimitSignal.swift:37; Calendar.MatchingPolicy.nextTime
    [InlineData(60, 37, 0)]
    [InlineData(30, 15, 0)]
    [InlineData(30, 15, 30)]
    public void ResetTime_moves_a_missing_clock_time_to_the_first_existing_instant(
        int daylightMinutes, int requestedMinute, int transitionSecond)
    {
        using var runtime = new TestRuntime();
        var timeZone = SpringForwardZone(daylightMinutes, transitionSecond);
        using var zone = SwiftFormatters.CurrentTimeZone.Override(timeZone);
        var now = new DateTimeOffset(2026, 3, 8, 6, 0, 0, TimeSpan.Zero);
        var expected = new DateTimeOffset(2026, 3, 8, 7, 0, transitionSecond, TimeSpan.Zero);
        var missingWall = new DateTime(2026, 3, 8, 2, requestedMinute, 0, DateTimeKind.Unspecified);

        Assert.True(timeZone.IsInvalidTime(missingWall));
        // GetUtcOffset alone assigns an offset to the nonexistent wall time; it does not apply .nextTime.
        Assert.NotEqual(expected, new DateTimeOffset(missingWall, timeZone.GetUtcOffset(missingWall)));
        Assert.Equal(expected, UsageLimitSignal.ResetTime($"resets 2:{requestedMinute:D2} am", now));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-221")] // Services/Providers/UsageLimitSignal.swift:37
    public void ResetTime_resolves_tomorrows_clock_time_when_tomorrow_has_a_gap()
    {
        using var runtime = new TestRuntime();
        using var zone = SwiftFormatters.CurrentTimeZone.Override(SpringForwardZone(60));
        var now = new DateTimeOffset(2026, 3, 7, 8, 0, 0, TimeSpan.Zero);

        Assert.Equal(new DateTimeOffset(2026, 3, 8, 7, 0, 0, TimeSpan.Zero),
            UsageLimitSignal.ResetTime("resets 2:37 am", now));
    }

    [Theory]
    [Trait("Req", "PR-PROVREG-221")] // Services/Providers/UsageLimitSignal.swift:37
    [InlineData(0)]
    [InlineData(15)]
    public void ResetTime_at_or_after_the_gap_end_keeps_the_requested_time_for_tomorrow(int minute)
    {
        using var runtime = new TestRuntime();
        using var zone = SwiftFormatters.CurrentTimeZone.Override(SpringForwardZone(60));
        var now = new DateTimeOffset(2026, 3, 8, 7, minute, 0, TimeSpan.Zero);

        Assert.Equal(new DateTimeOffset(2026, 3, 9, 6, 37, 0, TimeSpan.Zero),
            UsageLimitSignal.ResetTime("resets 2:37 am", now));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-221")] // Services/Providers/UsageLimitSignal.swift:37; default repeatedTimePolicy is .first
    public void ResetTime_still_uses_the_first_occurrence_of_a_repeated_clock_time()
    {
        using var runtime = new TestRuntime();
        using var zone = SwiftFormatters.CurrentTimeZone.Override(SpringForwardZone(60));
        var now = new DateTimeOffset(2026, 11, 1, 4, 0, 0, TimeSpan.Zero);

        Assert.Equal(new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero),
            UsageLimitSignal.ResetTime("resets 1:30 am", now));
    }

    // A fixed, in-memory rule keeps these clock-parser cases independent of the host's zone database.
    private static TimeZoneInfo SpringForwardZone(int daylightMinutes, int transitionSecond = 0)
    {
        var start = TimeZoneInfo.TransitionTime.CreateFixedDateRule(
            new DateTime(1, 1, 1, 2, 0, transitionSecond), 3, 8);
        var end = TimeZoneInfo.TransitionTime.CreateFixedDateRule(
            new DateTime(1, 1, 1, 2, 0, 0), 11, 1);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2026, 1, 1), new DateTime(2026, 12, 31),
            TimeSpan.FromMinutes(daylightMinutes), start, end);
        return TimeZoneInfo.CreateCustomTimeZone(
            $"UsageLimitSignal-{daylightMinutes}-{transitionSecond}", TimeSpan.FromHours(-5),
            "Usage limit test zone", "Standard", "Daylight", [rule]);
    }

    // ---- PR-PROVREG-222: format ------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-222")] // Services/Providers/UsageLimitSignal.swift:44-46
    public void Format_renders_a_time_later_today_as_the_shortened_time_alone()
    {
        using var clockScope = SwiftClock.Provider.Override(new ManualTimeProvider(new DateTimeOffset(2026, 9, 23, 10, 0, 0, TimeSpan.Zero)));
        using var locale = SwiftFormatters.CurrentLocale.Override(EnUS);
        using var zone = SwiftFormatters.CurrentTimeZone.Override(TimeZoneInfo.Utc);
        var result = UsageLimitSignal.Format(new DateTimeOffset(2026, 9, 23, 15, 0, 0, TimeSpan.Zero));
        Assert.Equal("3:00" + NarrowNoBreakSpace + "PM", result);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-222")] // Services/Providers/UsageLimitSignal.swift:44, 47
    public void Format_renders_tomorrow_with_the_tomorrow_at_prefix()
    {
        using var clockScope = SwiftClock.Provider.Override(new ManualTimeProvider(new DateTimeOffset(2026, 9, 23, 10, 0, 0, TimeSpan.Zero)));
        using var locale = SwiftFormatters.CurrentLocale.Override(EnUS);
        using var zone = SwiftFormatters.CurrentTimeZone.Override(TimeZoneInfo.Utc);
        var result = UsageLimitSignal.Format(new DateTimeOffset(2026, 9, 24, 9, 0, 0, TimeSpan.Zero));
        Assert.Equal("tomorrow at 9:00" + NarrowNoBreakSpace + "AM", result);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-222")] // Services/Providers/UsageLimitSignal.swift:44, 48-49
    public void Format_renders_a_date_within_six_days_as_the_wide_weekday_name()
    {
        // now: Wednesday 2026-09-23T10:00:00Z; now + 6 days = Tuesday 2026-09-29T10:00:00Z.
        using var clockScope = SwiftClock.Provider.Override(new ManualTimeProvider(new DateTimeOffset(2026, 9, 23, 10, 0, 0, TimeSpan.Zero)));
        using var locale = SwiftFormatters.CurrentLocale.Override(EnUS);
        using var zone = SwiftFormatters.CurrentTimeZone.Override(TimeZoneInfo.Utc);
        // Sunday 2026-09-27, inside the window.
        var result = UsageLimitSignal.Format(new DateTimeOffset(2026, 9, 27, 9, 0, 0, TimeSpan.Zero));
        Assert.Equal("Sunday at 9:00" + NarrowNoBreakSpace + "AM", result);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-222")] // Services/Providers/UsageLimitSignal.swift:44, 50-51
    public void Format_renders_a_date_six_or_more_days_out_as_the_abbreviated_date()
    {
        using var clockScope = SwiftClock.Provider.Override(new ManualTimeProvider(new DateTimeOffset(2026, 9, 23, 10, 0, 0, TimeSpan.Zero)));
        using var locale = SwiftFormatters.CurrentLocale.Override(EnUS);
        using var zone = SwiftFormatters.CurrentTimeZone.Override(TimeZoneInfo.Utc);
        // Monday 2026-10-05, well past now + 6 days.
        var result = UsageLimitSignal.Format(new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero));
        Assert.Equal("Oct 5, 2026, 9:00" + NarrowNoBreakSpace + "AM", result);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-222")] // Services/Providers/UsageLimitSignal.swift:48
    public void Format_measures_the_six_day_boundary_from_now_not_from_the_start_of_the_day()
    {
        // now: 2026-09-23T23:00:00Z. now + 6 days = 2026-09-29T23:00:00Z.
        using var clockScope = SwiftClock.Provider.Override(new ManualTimeProvider(new DateTimeOffset(2026, 9, 23, 23, 0, 0, TimeSpan.Zero)));
        using var locale = SwiftFormatters.CurrentLocale.Override(EnUS);
        using var zone = SwiftFormatters.CurrentTimeZone.Override(TimeZoneInfo.Utc);
        // 2026-09-29T22:00:00Z is one hour before the boundary: still inside the window (weekday branch).
        var inWindow = UsageLimitSignal.Format(new DateTimeOffset(2026, 9, 29, 22, 0, 0, TimeSpan.Zero));
        Assert.Equal("Tuesday at 10:00" + NarrowNoBreakSpace + "PM", inWindow);
        // 2026-09-30T00:00:00Z is past the boundary: the abbreviated-date branch.
        var pastWindow = UsageLimitSignal.Format(new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal("Sep 30, 2026, 12:00" + NarrowNoBreakSpace + "AM", pastWindow);
    }
}
