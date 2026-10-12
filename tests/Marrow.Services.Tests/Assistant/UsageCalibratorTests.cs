using System.Text.Json.Nodes;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-13 (DESIGN.md rev 5, 11.2; ED-NEW-dextra-as13-6): what one percent of a window costs in tokens, learned from the
// ledger and the readings. Intervals of at least 3 points, no outside use, a median that drops outliers beyond 3 MAD, a confidence
// that widens the p90, a 14-day window and the conservative default, written out literally.
public sealed class UsageCalibratorTests
{
    private const string Account = "claude:1";
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private static ReserveWindow Reading(double percent, DateTimeOffset at, DateTimeOffset? resets = null) =>
        new("w", ReserveWindowKind.FiveHour, percent, at, resets ?? T0.AddHours(5));

    private static (UsageCalibrator Calibrator, ManualTimeProvider Clock, CalibrationFeeder Feed) Rig(Func<string, ReserveWindowKind, double>? published = null)
    {
        var clock = new ManualTimeProvider(T0);
        var calibrator = new UsageCalibrator(clock, published);
        var feeder = new CalibrationFeeder(calibrator, clock, Account, "opus");
        feeder.Start();
        return (calibrator, clock, feeder);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-6")]
    public void Only_an_interval_spanning_three_points_of_change_is_fitted()
    {
        var (calibrator, _, feed) = Rig();
        var changed = 0;
        calibrator.Changed += () => changed++;

        feed.Interval(2, 300_000);                    // 2 points: kept open, not an interval
        Assert.Empty(calibrator.Intervals);
        Assert.Equal(0, changed);

        feed.Interval(1, 150_000);                    // now 3 points since the start: 450,000 tokens
        var all = calibrator.Intervals;
        Assert.Equal(2, all.Count);                   // once for the model, once for the all-model pool
        Assert.All(all, i => Assert.Equal(150_000, i.TokensPerPercent, 6));
        Assert.Equal(["*", "opus"], all.Select(i => i.Model).Order(StringComparer.Ordinal));
        Assert.Equal(1, changed);

        feed.Interval(2, 100_000);                    // under 3 again
        Assert.Equal(2, calibrator.Intervals.Count);
        feed.Interval(1, 10_000);                     // together 3: closed with all 110,000 tokens
        Assert.Equal(4, calibrator.Intervals.Count);
        Assert.Equal(110_000.0 / 3, calibrator.Intervals[^1].TokensPerPercent, 6);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-6")]
    public void An_interval_in_which_outside_use_was_found_is_dropped_and_the_next_starts_after_it()
    {
        var (calibrator, _, feed) = Rig();
        feed.Interval(4, 200_000, outside: true);     // somebody else used part of those 4 points
        Assert.Empty(calibrator.Intervals);
        feed.Interval(4, 200_000);                    // a clean one after it
        Assert.Equal(2, calibrator.Intervals.Count);
        Assert.Equal(50_000, calibrator.Intervals[0].TokensPerPercent, 6);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-6")]
    public void An_interval_with_no_spend_of_nth_s_is_dropped_and_a_reset_or_a_fall_starts_over()
    {
        var (calibrator, _, feed) = Rig();
        feed.Interval(5, 0);                          // nth spent nothing: the points were somebody else's
        Assert.Empty(calibrator.Intervals);

        // The window reset: a reading with a later reset time and a lower percent starts a new interval, whatever was spent.
        calibrator.Spend(Account, "opus", 100_000);
        calibrator.Observe(Account, Reading(2, T0.AddHours(6), T0.AddHours(11)), false);
        calibrator.Spend(Account, "opus", 120_000);
        calibrator.Observe(Account, Reading(6, T0.AddHours(6.5), T0.AddHours(11)), false);
        var only = Assert.Single(calibrator.Intervals, i => i.Model == "opus");
        Assert.Equal(30_000, only.TokensPerPercent, 6); // 120,000 tokens over 4 points, not the 100,000 spent before the reset

        // A reading that fell without a reset time moving on (a rolling decay) is not comparable either.
        calibrator.Observe(Account, Reading(3, T0.AddHours(7), T0.AddHours(11)), false);
        calibrator.Spend(Account, "opus", 90_000);
        calibrator.Observe(Account, Reading(5, T0.AddHours(7.5), T0.AddHours(11)), false); // only 2 points since the fall
        Assert.Single(calibrator.Intervals, i => i.Model == "opus");
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-6")]
    public void An_account_with_fewer_than_five_qualifying_intervals_keeps_the_conservative_default()
    {
        var (calibrator, _, feed) = Rig((_, _) => 250_000);
        for (var i = 0; i < 4; i++) feed.Interval(3, 300_000);
        var rate = calibrator.RateFor(Account, "claude", "opus", ReserveWindowKind.FiveHour);
        Assert.False(rate.Calibrated);
        Assert.True(rate.Rough);
        Assert.Equal(150_000, rate.TokensPerPercent, 6);          // 0.6 of the published rate
        Assert.Equal(75_000, rate.PessimisticTokensPerPercent, 6); // with the p90 doubled
        Assert.Equal(4, calibrator.IntervalCount(Account, "opus", ReserveWindowKind.FiveHour));

        feed.Interval(3, 300_000);                                  // the fifth
        var learned = calibrator.RateFor(Account, "claude", "opus", ReserveWindowKind.FiveHour);
        Assert.True(learned.Calibrated);
        Assert.False(learned.Rough);
        Assert.Equal(100_000, learned.TokensPerPercent, 6);
        Assert.Equal(5, learned.Intervals);
        Assert.Equal(100_000 * (1 - 1.2816 * 0), learned.PessimisticTokensPerPercent, 3); // identical rates have no spread
        Assert.True(learned.Confidence is > 0.4 and < 0.6);       // 5 of the 10 intervals that earn full trust

        // Another account, or an unseen window, still has nothing.
        Assert.False(calibrator.RateFor("claude:2", "claude", "opus", ReserveWindowKind.FiveHour).Calibrated);
        Assert.False(calibrator.RateFor(Account, "claude", "opus", ReserveWindowKind.Weekly).Calibrated);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-6")]
    public void The_fit_is_the_median_with_outliers_beyond_three_MAD_dropped_and_the_spread_is_the_confidence()
    {
        var fit = UsageCalibrator.FitRates([100_000, 102_000, 98_000, 101_000, 99_000, 100_000, 500_000])!;
        Assert.Equal(6, fit.Intervals);                       // the 500,000 is beyond 3 MAD of 1,000 (10 intervals' worth is 0.6 of full trust)
        Assert.Equal(100_000, fit.TokensPerPercent, 6);
        Assert.Equal(100_000 * (1 - 1.2816 * 0.016013), fit.PessimisticTokensPerPercent, 0);
        Assert.Equal(0.983987 * 0.6, fit.Confidence, 4);
        Assert.True(fit.Calibrated);

        // A wider spread pulls the pessimistic end further below the median and lowers the confidence.
        var wide = UsageCalibrator.FitRates([60_000, 80_000, 100_000, 120_000, 140_000, 90_000, 110_000, 100_000, 70_000, 130_000])!;
        Assert.Equal(100_000, wide.TokensPerPercent, 6);
        Assert.True(wide.PessimisticTokensPerPercent < 100_000 * 0.8, "the p90 widens with the spread");
        Assert.True(wide.Confidence < 0.8);
        var tight = UsageCalibrator.FitRates([100_000, 100_100, 99_900, 100_050, 99_950, 100_000, 100_020, 99_980, 100_010, 99_990])!;
        Assert.True(tight.PessimisticTokensPerPercent > 99_000);
        Assert.True(tight.Confidence > 0.98);

        // However wide, the pessimistic end stays above 0.3 of the median.
        var wild = UsageCalibrator.FitRates([10_000, 40_000, 100_000, 160_000, 190_000, 100_000, 30_000, 170_000, 100_000, 100_000])!;
        Assert.True(wild.PessimisticTokensPerPercent >= 0.3 * wild.TokensPerPercent - 1e-9);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-6")]
    public void A_fit_needs_five_rates_before_and_after_the_outliers_go()
    {
        Assert.Null(UsageCalibrator.FitRates([100, 100, 100, 100]));
        Assert.Null(UsageCalibrator.FitRates([]));
        // Identical rates have a MAD of 0: a hair of difference must not drop any of them.
        Assert.Equal(5, UsageCalibrator.FitRates([100_000, 100_000, 100_000, 100_000, 100_500])!.Intervals);
        // Four that agree and one far off: the far one goes, and four are left.
        Assert.Null(UsageCalibrator.FitRates([100_000, 100_000, 100_000, 100_000, 900_000]));
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-6")]
    public void Only_the_last_14_days_are_fitted()
    {
        var (calibrator, clock, feed) = Rig((_, _) => 250_000);
        for (var i = 0; i < 5; i++) feed.Interval(3, 300_000);
        Assert.True(calibrator.RateFor(Account, "claude", "opus", ReserveWindowKind.FiveHour).Calibrated);
        clock.Advance(TimeSpan.FromDays(13));
        Assert.True(calibrator.RateFor(Account, "claude", "opus", ReserveWindowKind.FiveHour).Calibrated);
        clock.Advance(TimeSpan.FromDays(1.1));
        Assert.False(calibrator.RateFor(Account, "claude", "opus", ReserveWindowKind.FiveHour).Calibrated);
        Assert.Equal(0, calibrator.IntervalCount(Account, "opus", ReserveWindowKind.FiveHour));
        Assert.Equal(14, UsageCalibrator.FitWindow.TotalDays);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-6")]
    public void A_model_with_no_intervals_of_its_own_takes_the_all_model_pool_a_little_less_sure_of()
    {
        var (calibrator, _, feed) = Rig();
        for (var i = 0; i < 5; i++) feed.Interval(3, 300_000, spentModel: "opus");
        var own = calibrator.RateFor(Account, "claude", "opus", ReserveWindowKind.FiveHour);
        var pooled = calibrator.RateFor(Account, "claude", "sonnet", ReserveWindowKind.FiveHour);
        Assert.True(own.Calibrated);
        Assert.True(pooled.Calibrated);
        Assert.Equal(own.TokensPerPercent, pooled.TokensPerPercent, 6);
        Assert.True(pooled.Confidence < own.Confidence);
        Assert.True(pooled.PessimisticTokensPerPercent < own.PessimisticTokensPerPercent);
        Assert.Equal(pooled, calibrator.RateFor(Account, "claude", null, ReserveWindowKind.FiveHour));
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-6")]
    public void An_interval_that_mixed_models_joins_the_pool_but_no_one_model()
    {
        var (calibrator, _, feed) = Rig();
        calibrator.Spend(Account, "opus", 150_000);
        calibrator.Spend(Account, "sonnet", 150_000);
        feed.Interval(3, 0);
        Assert.Equal(["*"], calibrator.Intervals.Select(i => i.Model));
        Assert.Equal(100_000, calibrator.Intervals[0].TokensPerPercent, 6);

        // Ninety percent from one model is that model's.
        calibrator.Spend(Account, "opus", 270_000);
        calibrator.Spend(Account, "sonnet", 30_000);
        feed.Interval(3, 0);
        Assert.Equal(["*", "*", "opus"], calibrator.Intervals.Select(i => i.Model).Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-6")]
    public void The_most_intervals_kept_are_the_newest_two_hundred()
    {
        var (calibrator, _, feed) = Rig();
        for (var i = 0; i < 230; i++) feed.Interval(3, 300_000 + i);
        Assert.Equal(UsageCalibrator.MaxIntervals, calibrator.Intervals.Count(i => i.Model == "*"));
        Assert.Equal(UsageCalibrator.MaxIntervals, calibrator.Intervals.Count(i => i.Model == "opus"));
        Assert.True(calibrator.Intervals.Min(i => i.Tokens) >= 300_000 + 30, "the oldest went first");
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-7")]
    public void The_document_carries_a_version_and_round_trips_with_what_this_build_does_not_know()
    {
        var (calibrator, clock, feed) = Rig();
        for (var i = 0; i < 6; i++) feed.Interval(3, 300_000);
        var stored = calibrator.ToJson(new JsonArray())!;
        Assert.Equal(UsageCalibrator.FormatVersion, stored["version"]!.GetValue<int>());
        Assert.Equal(12, stored["intervals"]!.AsArray().Count);

        // A later build added fields at the root and in an interval: they survive a load and a save.
        stored["futureSetting"] = new JsonObject { ["a"] = 1 };
        stored["intervals"]![0]!["note"] = "kept";
        var reloaded = new UsageCalibrator(clock);
        reloaded.Load(stored);
        Assert.Equal(12, reloaded.Intervals.Count);
        Assert.Equal(calibrator.RateFor(Account, "claude", "opus", ReserveWindowKind.FiveHour), reloaded.RateFor(Account, "claude", "opus", ReserveWindowKind.FiveHour));
        var again = reloaded.ToJson()!;
        Assert.Equal(1, again["futureSetting"]!["a"]!.GetValue<int>());
        Assert.Single(again["intervals"]!.AsArray(), i => i!["note"] is not null);
        Assert.Equal("kept", again["intervals"]!.AsArray().Single(i => i!["note"] is not null)!["note"]!.GetValue<string>());
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-7")]
    public void A_document_from_a_newer_version_is_read_for_nothing_and_never_written_over()
    {
        var calibrator = new UsageCalibrator(new ManualTimeProvider(T0));
        calibrator.Load(new JsonObject { ["version"] = 2, ["intervals"] = new JsonArray(new JsonObject { ["account"] = Account }) });
        Assert.True(calibrator.ReadOnly);
        Assert.Empty(calibrator.Intervals);
        Assert.Null(calibrator.ToJson());
        // It still learns in memory for this run, but it has nothing to store.
        calibrator.Load(new JsonObject { ["version"] = 1, ["intervals"] = new JsonArray() });
        Assert.False(calibrator.ReadOnly);
        Assert.NotNull(calibrator.ToJson());
        // No version at all is the first one.
        calibrator.Load(new JsonObject { ["intervals"] = new JsonArray() });
        Assert.False(calibrator.ReadOnly);
        calibrator.Load(null);
        Assert.Empty(calibrator.Intervals);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-7")]
    public void A_line_that_is_not_an_interval_is_skipped_never_repaired()
    {
        var good = new JsonObject { ["account"] = Account, ["model"] = "opus", ["kind"] = "FiveHour", ["at"] = "2026-10-01T08:00:00.0000000Z", ["points"] = 3.0, ["tokens"] = 300_000.0 };
        JsonObject Bad(string name, JsonNode? value)
        {
            var copy = (JsonObject)good.DeepClone();
            copy[name] = value;
            return copy;
        }
        var lines = new JsonArray(
            good.DeepClone(), Bad("points", 2.0), Bad("points", "three"), Bad("tokens", -5.0), Bad("tokens", 0.0), Bad("kind", "Eon"),
            Bad("at", "not a date"), Bad("account", ""), Bad("model", 7), JsonValue.Create("text"), new JsonObject());
        var calibrator = new UsageCalibrator(new ManualTimeProvider(T0));
        calibrator.Load(new JsonObject { ["version"] = 1, ["intervals"] = lines });
        Assert.Single(calibrator.Intervals);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-6")]
    public void A_non_finite_reading_is_ignored_and_the_default_takes_the_providers_published_rate()
    {
        var (calibrator, _, _) = Rig((provider, kind) => provider == "codex" ? 80_000 : 250_000);
        calibrator.Observe(Account, Reading(double.NaN, T0.AddMinutes(30)), false);
        Assert.Empty(calibrator.Intervals);
        Assert.Equal(48_000, calibrator.RateFor(Account, "codex", null, ReserveWindowKind.FiveHour).TokensPerPercent, 6);
        Assert.Equal(150_000, calibrator.RateFor(Account, "claude", null, ReserveWindowKind.FiveHour).TokensPerPercent, 6);
    }
}
