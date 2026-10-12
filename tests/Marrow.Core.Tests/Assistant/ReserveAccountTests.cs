using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-13 (DESIGN.md rev 5, 11.5; ED-NEW-dextra-as13-4): one account as the reserve guard sees it. The estimate between
// polls, outside use found and reported, resets, the recent burn, the margin and the check, written out literally. The
// rate is 50,000 tokens per percent (40,000 at its p90) for every model, so a number reads straight off the tokens.
public sealed class ReserveAccountTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    private static readonly CalibratedRate Rate = new(50_000, 40_000, 0.9, 8, true);

    // The tests that read the formula of 11.5 alone use ReserveSafety.Formula; the allowances have tests of their own below.
    private static ReserveAccount Account(Func<string?, ReserveWindowKind, CalibratedRate>? rates = null, ReserveSafety? safety = null) =>
        new("claude:1", "Claude 20x", "claude", rates ?? ((_, _) => Rate), safety ?? ReserveSafety.Formula);

    private static ReserveWindow W(double percent, DateTimeOffset at, ReserveWindowKind kind = ReserveWindowKind.FiveHour, DateTimeOffset? resets = null) =>
        new(kind.ToString(), kind, percent, at, resets ?? at.AddHours(3));

    private static readonly ReserveRule AnyRule = new("r", ReserveScope.Every, ReserveLifetime.Standing) { KeepPercent = 30 };

    private static Func<ReserveWindow, ReserveFloor?> Floors(double fiveHour, double? weekly = null) => window => window.Kind switch
    {
        ReserveWindowKind.FiveHour => new ReserveFloor(fiveHour, AnyRule, fiveHour, false),
        ReserveWindowKind.Weekly when weekly is { } w => new ReserveFloor(w, AnyRule, w, false),
        _ => null,
    };

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-4")]
    public void Between_polls_the_estimate_is_the_last_reading_plus_the_ledgers_tokens_since()
    {
        var account = Account();
        account.Observe([W(40, T0)]);
        Assert.Equal(40, account.Estimate("FiveHour", T0.AddSeconds(5)));

        account.Spend(T0.AddSeconds(10), 100_000, "m"); // 2 points
        Assert.Equal(42, account.Estimate("FiveHour", T0.AddSeconds(20)));
        account.Spend(T0.AddSeconds(15), 1, "m");        // a hair over rounds up to the next point
        Assert.Equal(43, account.Estimate("FiveHour", T0.AddSeconds(20)));

        // The next poll replaces the estimate; what the ledger saw before it is in the reading.
        account.Observe([W(41, T0.AddSeconds(60))]);
        Assert.Equal(41, account.Estimate("FiveHour", T0.AddSeconds(61)));
        account.Spend(T0.AddSeconds(70), 50_000, "m");
        Assert.Equal(42, account.Estimate("FiveHour", T0.AddSeconds(80)));
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-4")]
    public void An_unknown_window_has_no_estimate_and_a_non_finite_reading_is_ignored()
    {
        var account = Account();
        Assert.Equal(0, account.Estimate("FiveHour", T0));
        account.Observe([W(double.NaN, T0), W(double.PositiveInfinity, T0)]);
        Assert.Empty(account.Windows);
        account.Observe([W(140, T0)]);
        Assert.Equal(100, account.Window("FiveHour")!.Percent); // a reading is held to 0..100
        account.Observe([W(-5, T0.AddSeconds(60))]);
        Assert.Equal(0, account.Window("FiveHour")!.Percent);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-4")]
    public void A_reading_beating_the_estimate_by_more_than_two_points_is_outside_use_and_is_reported()
    {
        var account = Account();
        account.Observe([W(40, T0)]);
        account.Spend(T0.AddSeconds(5), 100_000, "m");                  // nth: 2 points, so the estimate is 42

        var first = Assert.Single(account.Observe([W(47, T0.AddSeconds(60))]));
        Assert.Equal(42, first.Estimate);
        Assert.Equal(5, first.OutsidePoints, 6);                        // 47 - 42 was somebody else
        var use = Assert.Single(account.OutsideUses);
        Assert.Equal(5, use.Points, 6);
        Assert.Equal(T0, use.Since);                                    // "since 09:00": the previous reading
        Assert.Equal(T0.AddSeconds(60), use.At);

        Assert.Equal(0, Assert.Single(account.Observe([W(49, T0.AddSeconds(120))])).OutsidePoints); // 2 over: not reported
        Assert.Equal(3, Assert.Single(account.Observe([W(52, T0.AddSeconds(180))])).OutsidePoints, 6);
        Assert.Equal(8, account.OutsideSince(T0), 6);
        Assert.Equal(3, account.OutsideSince(T0.AddSeconds(100)), 6);
        Assert.Equal(0, account.OutsideSince(T0, "Weekly"));
        Assert.Equal(8, account.OutsideSince(T0, "FiveHour"), 6);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-4")]
    public void Nth_s_own_spend_is_never_reported_as_outside_use_and_a_low_reading_just_replaces_the_estimate()
    {
        var account = Account();
        account.Observe([W(40, T0)]);
        account.Spend(T0.AddSeconds(10), 300_000, "m");                 // 6 points
        Assert.Equal(0, Assert.Single(account.Observe([W(46, T0.AddSeconds(60))])).OutsidePoints, 6);
        account.Spend(T0.AddSeconds(70), 300_000, "m");                 // the estimate would be 52
        var low = Assert.Single(account.Observe([W(48, T0.AddSeconds(120))]));
        Assert.Equal(52, low.Estimate);
        Assert.Equal(0, low.OutsidePoints);
        Assert.Equal(48, account.Estimate("FiveHour", T0.AddSeconds(121)));
        Assert.Empty(account.OutsideUses);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-4")]
    public void A_window_that_reset_is_not_outside_use_and_a_rolling_decay_is_not_a_reset()
    {
        var account = Account();
        account.Observe([W(90, T0, resets: T0.AddMinutes(30))]);
        account.Observe([W(95, T0.AddMinutes(10), resets: T0.AddMinutes(30))]);
        var reset = Assert.Single(account.Observe([W(4, T0.AddMinutes(31), resets: T0.AddHours(5.5))]));
        Assert.True(reset.Reset);
        Assert.Equal(0, reset.OutsidePoints);
        Assert.Equal(95, reset.Previous!.Percent);
        Assert.Equal(4, account.Estimate("FiveHour", T0.AddMinutes(31)));
        Assert.Equal(ReserveDefaults.MinimumBurnPerSecond, account.BurnPerSecond("FiveHour", T0.AddMinutes(31)), 9); // the old window's rises are gone

        var decay = Assert.Single(account.Observe([W(2, T0.AddMinutes(60), resets: T0.AddHours(5.5))]));
        Assert.False(decay.Reset);                                      // the reset time did not move on
        Assert.Equal(0, decay.OutsidePoints);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-4")]
    public void A_late_answer_to_an_older_request_does_not_replace_a_newer_reading()
    {
        var account = Account();
        account.Observe([W(50, T0.AddSeconds(60))]);
        account.Observe([W(10, T0)]);
        Assert.Equal(50, account.Window("FiveHour")!.Percent);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-4")]
    public void The_recent_burn_is_the_peak_of_recent_rises_and_never_below_a_quantum_per_poll()
    {
        var account = Account();
        var floor = ReserveDefaults.MinimumBurnPerSecond;
        Assert.Equal(1.0 / 60, floor, 9);
        account.Observe([W(40, T0)]);
        Assert.Equal(floor, account.BurnPerSecond("FiveHour", T0.AddSeconds(30)), 9);

        account.Observe([W(43, T0.AddSeconds(60))]);                    // 3 points in 60 s
        Assert.Equal(0.05, account.BurnPerSecond("FiveHour", T0.AddSeconds(61)), 9);
        account.Observe([W(44, T0.AddSeconds(120))]);                   // a slower rise does not lower the peak
        Assert.Equal(0.05, account.BurnPerSecond("FiveHour", T0.AddSeconds(121)), 9);
        Assert.Equal(0.05, account.BurnPerSecond("FiveHour", T0.AddSeconds(60).Add(ReserveDefaults.BurnLookback)), 9);     // still within 15 minutes
        Assert.Equal(floor, account.BurnPerSecond("FiveHour", T0.AddSeconds(60).Add(ReserveDefaults.BurnLookback).AddSeconds(1)), 9); // the peak aged out

        // Two readings close together cannot make an enormous rate: a rise is never divided by less than 30 s.
        account.Observe([W(46, T0.AddSeconds(125))]);
        Assert.Equal(2.0 / 30, account.BurnPerSecond("FiveHour", T0.AddSeconds(126)), 9);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-4")]
    public void The_margin_adds_every_turn_in_flight_and_the_staleness_times_the_burn()
    {
        var account = Account();
        account.Observe([W(40, T0)]);
        Assert.Equal(1.0, account.Margin("FiveHour", T0), 9);           // nothing in flight, nothing stale: the one-point floor

        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        account.TurnStarted(a, "m", 60_000);                            // 1.5 points at its p90
        account.TurnStarted(b, "m", 80_000);                            // 2.0 points
        Assert.Equal(3.5, account.Margin("FiveHour", T0), 9);
        Assert.Equal(3.5 + 1.0, account.Margin("FiveHour", T0.AddSeconds(60)), 9); // a minute stale at a quantum per poll
        Assert.Equal(2, account.InFlight);

        account.TurnEnded(a);
        Assert.Equal(2.0, account.Margin("FiveHour", T0), 9);
        Assert.False(account.IsInFlight(a));
        Assert.True(account.IsInFlight(b));

        // Midway through b, only what it has still to spend counts for a check between turns.
        account.Spend(T0.AddSeconds(10), 60_000, "m", b);
        Assert.Equal(1.0, account.Margin("FiveHour", T0, remainingOnly: true), 9);   // 20,000 tokens left = 0.5, under the one-point floor
        Assert.Equal(2.0, account.Margin("FiveHour", T0, remainingOnly: false), 9);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-4")]
    public void A_turn_is_sent_only_when_estimate_plus_its_p90_plus_the_margin_stays_at_or_under_the_floor()
    {
        var account = Account();
        account.Observe([W(60, T0)]);

        var go = account.Check(Floors(70), "m", 200_000, T0);           // p90 5 points: 60 + 5 + 1 = 66
        Assert.True(go.Go);
        Assert.Equal(66, go.Binding!.Projected, 9);
        Assert.Equal(4, go.Binding.Room, 9);

        var exactly = account.Check(Floors(70), "m", 360_000, T0);      // 60 + 9 + 1 = 70: at the floor, not over it
        Assert.True(exactly.Go);
        Assert.Equal(0, exactly.Binding!.Room, 9);

        var over = account.Check(Floors(70), "m", 400_000, T0);         // 60 + 10 + 1 = 71
        Assert.False(over.Go);
        Assert.Equal(71, over.Binding!.Projected, 9);
        Assert.Equal(-1, over.Binding.Room, 9);
        Assert.True(over.Binding.Crosses);
        Assert.Equal(60, over.Binding.Estimate, 9);
        Assert.Equal(10, over.Binding.TurnP90, 9);
        Assert.Equal(1, over.Binding.Margin, 9);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-4")]
    public void With_both_windows_the_tighter_one_binds()
    {
        var account = Account();
        account.Observe([W(50, T0), W(85, T0, ReserveWindowKind.Weekly, T0.AddDays(3))]);

        var go = account.Check(Floors(70, 88), "m", 80_000, T0);        // 2 points: 5-hour 53 (room 17), weekly 88 (room 0)
        Assert.True(go.Go);
        Assert.Equal("Weekly", go.Binding!.Window.Id);
        Assert.Equal(0, go.Binding.Room, 9);
        Assert.Equal(2, go.Windows.Count);

        var stop = account.Check(Floors(70, 88), "m", 120_000, T0);     // 3 points: weekly 89 crosses 88
        Assert.False(stop.Go);
        Assert.Equal("Weekly", stop.Binding!.Window.Id);
        Assert.True(stop.Windows.Single(c => c.Window.Id == "FiveHour").Room > 0);

        var onlyFiveHour = account.Check(Floors(70), "m", 120_000, T0); // a rule for the 5-hour window leaves the weekly one free
        Assert.True(onlyFiveHour.Go);
        Assert.Equal("FiveHour", onlyFiveHour.Binding!.Window.Id);
        Assert.Single(onlyFiveHour.Windows);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-4")]
    public void Turns_already_in_flight_take_room_up_to_sixteen_agents()
    {
        var account = Account();
        account.Observe([W(60, T0)]);
        var floors = Floors(70);
        foreach (var count in new[] { 0, 3, 4, 8 })
        {
            var fresh = Account();
            fresh.Observe([W(60, T0)]);
            for (var i = 0; i < count; i++) fresh.TurnStarted(Guid.NewGuid(), "m", 60_000); // 1.5 points each
            var check = fresh.Check(floors, "m", 40_000, T0);           // this turn: 1 point
            var margin = Math.Max(1, count * 1.5);
            Assert.Equal(60 + 1 + margin, check.Binding!.Projected, 9);
            Assert.Equal(60 + 1 + margin <= 70, check.Go);
        }
        for (var i = 0; i < 16; i++) account.TurnStarted(Guid.NewGuid(), "m", 60_000);
        Assert.Equal(16 * 1.5, account.Margin("FiveHour", T0), 9);
        Assert.False(account.Check(floors, "m", 40_000, T0).Go);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-4")]
    public void A_stale_reading_widens_the_margin_by_the_recent_burn()
    {
        var account = Account();
        account.Observe([W(40, T0)]);
        account.Observe([W(60, T0.AddSeconds(60))]);                    // a fast rise: 20 points in a minute
        var floors = Floors(90);
        var fresh = account.Check(floors, "m", 40_000, T0.AddSeconds(60));
        Assert.Equal(60 + 1 + 1, fresh.Binding!.Projected, 9);          // a fresh reading: just the one-point floor
        var stale = account.Check(floors, "m", 40_000, T0.AddSeconds(120)); // a minute later: 60 s x 0.333 = 20 more
        Assert.Equal(60 + 1 + 20, stale.Binding!.Projected, 6);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-4")]
    public void Between_turns_a_check_with_no_new_turn_counts_only_what_the_turns_in_flight_have_left()
    {
        var account = Account();
        account.Observe([W(60, T0)]);
        var turn = Guid.NewGuid();
        account.TurnStarted(turn, "m", 400_000);                        // 10 points at its p90
        account.Spend(T0.AddSeconds(30), 360_000, "m", turn);           // 7.2 points spent so far

        var before = account.Check(Floors(70), "m", 100_000, T0.AddSeconds(31)); // a new turn would count all 10 in flight
        Assert.False(before.Go);
        var between = account.Check(Floors(70), "m", 0, T0.AddSeconds(31));      // only 40,000 tokens (1 point) are left in it
        Assert.True(between.Go);
        Assert.Equal(68 + 0 + (1.0 + 31.0 / 60), between.Binding!.Projected, 9); // estimate 67.2 rounded up, no new turn, margin 1 point of turn left + 31 s stale
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-4")]
    public void No_floor_or_a_floor_of_100_is_no_reserve_and_an_unusable_rate_fails_closed()
    {
        var account = Account();
        account.Observe([W(99, T0)]);
        Assert.True(account.Check(_ => null, "m", 1_000_000, T0).Go);
        Assert.Same(ReserveCheck.Free, account.Check(_ => null, "m", 1_000_000, T0));
        Assert.True(account.Check(Floors(100), "m", 1_000_000, T0).Go);

        var unknown = Account((_, _) => new CalibratedRate(0, 0, 0, 0, false));
        unknown.Observe([W(10, T0)]);
        unknown.Spend(T0.AddSeconds(1), 1, "m");
        Assert.Equal(100, unknown.Estimate("FiveHour", T0.AddSeconds(2)));
        Assert.False(unknown.Check(Floors(70), "m", 1, T0.AddSeconds(2)).Go);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-4")]
    public void A_flood_of_spends_between_two_polls_stays_bounded_and_keeps_the_total()
    {
        var account = Account();
        account.Observe([W(40, T0)]);
        for (var i = 0; i < 12_000; i++)
            account.Spend(T0.AddSeconds(2 + i * 1.5), 1_000, i % 2 == 0 ? "a" : "b"); // 12 million tokens, two models, never the same second
        Assert.Equal(40 + 240, account.Estimate("FiveHour", T0.AddSeconds(30_000)));  // 12,000,000 / 50,000 = 240 points
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-4")]
    public void A_model_with_its_own_rate_converts_by_that_rate()
    {
        var account = Account((model, _) => model == "big" ? new CalibratedRate(10_000, 8_000, 0.9, 8, true) : Rate);
        account.Observe([W(40, T0)]);
        account.Spend(T0.AddSeconds(5), 50_000, "small"); // 1 point
        account.Spend(T0.AddSeconds(6), 50_000, "big");   // 5 points
        Assert.Equal(46, account.Estimate("FiveHour", T0.AddSeconds(7)));
        account.TurnStarted(Guid.NewGuid(), "big", 8_000); // 1 point at its own p90
        Assert.Equal(1.0, account.Margin("FiveHour", T0), 9);
        account.TurnStarted(Guid.NewGuid(), "big", 24_000); // 3 more
        Assert.Equal(4.0, account.Margin("FiveHour", T0), 9);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-5")]
    public void A_whole_point_reading_adds_a_point_and_the_new_turn_is_counted_at_one_and_a_half_times_its_p90()
    {
        var account = Account(safety: ReserveSafety.Production);
        account.Observe([W(60, T0)]);
        var check = account.Check(Floors(70), "m", 200_000, T0);        // p90 5 points x 1.5 = 7.5; 60 + 7.5 + 1 + 1 = 69.5
        Assert.Equal(7.5, check.Binding!.TurnP90, 9);
        Assert.Equal(1.0, check.Binding.Quantum, 9);
        Assert.Equal(69.5, check.Binding.Projected, 9);
        Assert.True(check.Go);
        Assert.False(account.Check(Floors(70), "m", 220_000, T0).Go);   // 60 + 8.25 + 1 + 1 = 70.25

        // A reading with a fraction of a point shows it is not whole: nothing is hiding behind it.
        var fine = Account(safety: ReserveSafety.Production);
        fine.Observe([W(60.4, T0)]);
        Assert.Equal(0.0, fine.Check(Floors(70), "m", 200_000, T0).Binding!.Quantum, 9);

        // A check with no new turn adds the point but not the tail.
        Assert.Equal(60 + 0 + 1 + 1, account.Check(Floors(70), "m", 0, T0).Binding!.Projected, 9);

        // The bare formula adds neither.
        var bare = Account();
        bare.Observe([W(60, T0)]);
        Assert.Equal(66.0, bare.Check(Floors(70), "m", 200_000, T0).Binding!.Projected, 9);
        Assert.Equal(1.0, ReserveSafety.Production.QuantumAllowance, 9);
        Assert.Equal(1.5, ReserveSafety.Production.NewTurnTail, 9);
        Assert.Equal(0.0, ReserveSafety.Formula.QuantumAllowance, 9);
        Assert.Equal(1.0, ReserveSafety.Formula.NewTurnTail, 9);
    }
}
