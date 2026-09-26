using Marrow.Core.Port;

namespace Marrow.Core.Tests.Port;

// Foundation unit F.ICUFormatters (IMPLEMENTING.md 4.3; R-FMT-2), decisions_draft.md E1: Duration.UnitsFormatStyle
// for the three B3 duration call sites. See SwiftFormattersTests.cs for the rest of F.ICUFormatters' surface note;
// this file adds only SwiftFormatters.DurationUnits(long, IReadOnlyCollection<SwiftFormatters.DurationUnit>, string),
// which that file doesn't cover.
//
// Swift call sites (B3 52bc7926097cd622d08d6294e0d8b08a48c7516e): Core/Models/Hydra.swift:1420-1423 (duration(_:),
// allowed {seconds, minutes}), App/Views/Chat/HydraPanel.swift:545-551 (elapsed, same allowed units, prefixed
// " in " by its own caller, not this helper), UI/Common/Components.swift:37-40 (RelativeTime.duration, allowed
// {seconds, minutes, hours}), called from App/Views/Chat/HydraHeadPopovers.swift:125. All three: width .narrow,
// maximumUnitCount 2, fractionalPart .hide(rounded: .down), no locale (Locale.autoupdatingCurrent, so the port's
// caller passes SwiftFormatters.CurrentLocale.Current); every expectation below fixes en_US instead, to keep the
// grouping and unit words stable across CI machines rather than exercise that ambient here (already covered for the
// rest of F.ICUFormatters by SwiftFormattersTests.cs).
//
// Evidence (E:/Droppy/work/b3/evidence_6_1/): sf-swift-6.2-RELEASE-Duration+UnitsFormatStyle.swift (unitsToUse
// :434-469; _getSkeletons/_formatFields :307-367; the list join :274-282, :369-419); sf-swift-6.2-RELEASE-DurUnitsTests.swift
// (zeroUnitsDisplay :474-484 and maximumUnitCounts :449-457, direct tests of unitsToUse that structurally confirm
// which units survive zero-dropping and the maximumUnitCount collapse for durations this file also uses, though both
// swift-foundation tests call unitsToUse with an unlimited trailingFractionalPartLength rather than the port's 0, so
// only the surviving *unit set* transfers, not their fractional values); dur.js/dur.out.txt (an independent model of
// the same algorithm with the en narrow patterns, cross-checked by hand against the table below).
//
// None of this has run against macOS Foundation.framework (ED-086), so every test here is Oracle O-65 (R-TEST-6): a
// settled run of the Windows swift.org toolchain, or of the real macOS app, could still change these strings.
public sealed class SwiftFormattersSecondTests
{
    private const string EnUS = "en_US";

    private static readonly SwiftFormatters.DurationUnit[] SecondsAndMinutes =
        [SwiftFormatters.DurationUnit.Seconds, SwiftFormatters.DurationUnit.Minutes];

    private static readonly SwiftFormatters.DurationUnit[] SecondsMinutesAndHours =
        [SwiftFormatters.DurationUnit.Seconds, SwiftFormatters.DurationUnit.Minutes, SwiftFormatters.DurationUnit.Hours];

    // ---- Hydra.duration / HydraPanel.elapsed: allowed {seconds, minutes} ------------------------------------------

    [Theory]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Oracle", "O-65")]
    [InlineData(0L, "0s")]      // :323-328 fallback to the smallest allowed unit (seconds) at zero
    [InlineData(1L, "1s")]
    [InlineData(45L, "45s")]
    [InlineData(59L, "59s")]
    [InlineData(60L, "1m")]     // seconds drops (zero), :440-442 (2 allowed, maximumUnitCount 2: the simple branch)
    [InlineData(61L, "1m 1s")]
    [InlineData(90L, "1m 30s")]
    [InlineData(119L, "1m 59s")]
    [InlineData(120L, "2m")]
    [InlineData(3599L, "59m 59s")]
    [InlineData(3600L, "60m")]  // hours is not an allowed unit here, so this never becomes "1h"
    [InlineData(3601L, "60m 1s")]
    [InlineData(3659L, "60m 59s")]
    [InlineData(3660L, "61m")]
    [InlineData(3661L, "61m 1s")]
    [InlineData(3725L, "62m 5s")]
    [InlineData(7200L, "120m")]
    [InlineData(7260L, "121m")]
    [InlineData(86399L, "1,439m 59s")] // grouping at 1,000 or more of one unit, ICU's default (UNVERIFIED detail, E1)
    [InlineData(90000L, "1,500m")]
    [InlineData(60000L, "1,000m")]
    public void Hydra_and_HydraPanel_allow_seconds_and_minutes(long seconds, string expected) =>
        // Hydra.swift:1420-1423, HydraPanel.swift:545-551
        Assert.Equal(expected, SwiftFormatters.DurationUnits(seconds, SecondsAndMinutes, EnUS));

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Oracle", "O-65")]
    public void A_negative_input_only_reaches_Hydra_duration_and_signs_only_the_first_field()
    {
        // decisions_draft.md E1: Hydra.duration is unclamped (Int(seconds), HydraPanel guards seconds > 0 first), so
        // a negative TimeInterval can reach this helper. Duration+UnitsFormatStyle.swift :337-358: the sign shows
        // only on the most significant (first) field of a negative value; every other field's numberFormatStyle is
        // .sign(strategy: .never), so its magnitude is shown unsigned.
        Assert.Equal("-1m 5s", SwiftFormatters.DurationUnits(-65L, SecondsAndMinutes, EnUS));
    }

    // ---- RelativeTime.duration: allowed {seconds, minutes, hours} -------------------------------------------------

    [Theory]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Oracle", "O-65")]
    [InlineData(0L, "0s")]
    [InlineData(45L, "45s")]
    [InlineData(59L, "59s")]
    [InlineData(60L, "1m")]
    [InlineData(61L, "1m 1s")]
    [InlineData(90L, "1m 30s")]
    [InlineData(119L, "1m 59s")]
    [InlineData(120L, "2m")]
    [InlineData(3599L, "59m 59s")]
    [InlineData(3600L, "1h")]      // 3 allowed > maximumUnitCount 2: minutes and seconds both drop (zero)
    [InlineData(3601L, "1h 1s")]   // minutes (0) drops before the fit check; {hours, seconds} already fits in 2
    [InlineData(3659L, "1h 59s")]
    [InlineData(3660L, "1h 1m")]
    [InlineData(3661L, "1h 1m")]   // {hours, minutes, seconds} all non-zero: collapses to the 2 largest, recomputed
                                   // fresh from the total (:467), truncating the leftover second
    [InlineData(3725L, "1h 2m")]
    [InlineData(7200L, "2h")]
    [InlineData(7260L, "2h 1m")]
    [InlineData(86399L, "23h 59m")]
    [InlineData(90000L, "25h")]
    [InlineData(60000L, "16h 40m")]
    public void RelativeTime_duration_allows_seconds_minutes_and_hours(long seconds, string expected) =>
        // Components.swift:37-40, called from HydraHeadPopovers.swift:125
        Assert.Equal(expected, SwiftFormatters.DurationUnits(seconds, SecondsMinutesAndHours, EnUS));

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Oracle", "O-65")]
    public void A_zero_minute_field_drops_even_when_hours_and_seconds_both_survive()
    {
        // swift-foundation's own zeroUnitsDisplay() test (DurUnitsTests.swift:474-477) checks exactly this duration
        // (2h 0m 24s) through unitsToUse directly with dropZeroUnits true, and gets ([.hours, .seconds], [2, 24]): 2
        // non-zero units already fit maximumUnitCount 2, so RelativeTime.duration's configuration (which reaches the
        // same branch once minutes is dropped) never needs the collapse step for this duration.
        Assert.Equal("2h 24s", SwiftFormatters.DurationUnits(7224L, SecondsMinutesAndHours, EnUS));
    }

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Oracle", "O-65")]
    public void Three_nonzero_units_collapse_to_the_two_largest_recomputed_from_the_total()
    {
        // swift-foundation's maximumUnitCounts() test (DurUnitsTests.swift:449-456) confirms unitsToUse keeps
        // {.hours, .minutes} (not {.hours, .seconds} or {.minutes, .seconds}) for this exact duration and
        // maximumUnitCount. That test's own expected value is fractional (43.4 minutes) because it calls unitsToUse
        // directly with its default, unlimited trailingFractionalPartLength; RelativeTime.duration's
        // fractionalPart .hide(rounded: .down) instead passes trailingFractionalPartLength 0, which truncates the
        // recomputed minutes to a whole number (43, not 43.4).
        Assert.Equal("2h 43m", SwiftFormatters.DurationUnits(9804L, SecondsMinutesAndHours, EnUS));
    }
}
