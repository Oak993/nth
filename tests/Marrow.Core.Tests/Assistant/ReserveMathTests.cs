using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-13 (DESIGN.md rev 5, 11.3, 11.5; ED-NEW-dextra-as13-1 and -3): the floor of a rule, a standing floor easing near a
// reset, the estimate between polls, the margin, outside use, and the words, written out literally.
public sealed class ReserveMathTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    private static ReserveRule Task(double keep, ReserveBasis basis = ReserveBasis.WhatIsLeft, ReserveWindows windows = ReserveWindows.Both) =>
        new("t", ReserveScope.Every, ReserveLifetime.Task) { KeepPercent = keep, Basis = basis, Windows = windows };

    private static ReserveRule Standing(double keep, ReserveWindows windows = ReserveWindows.Both) =>
        new("s", ReserveScope.Every, ReserveLifetime.Standing) { KeepPercent = keep, Basis = ReserveBasis.WholeWindow, Windows = windows };

    private static ReserveWindow Window(ReserveWindowKind kind, double percent, DateTimeOffset? resets = null) =>
        new(kind.ToString(), kind, percent, Now, resets);

    // Each line: keep, basis (L left, W whole), used at start, expected floor. The owner's example is the first line.
    [Theory]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-3")]
    [InlineData(30, 'L', 22, 76.6)]
    [InlineData(30, 'L', 60, 88.0)]
    [InlineData(30, 'L', 0, 70.0)]
    [InlineData(50, 'L', 40, 70.0)]
    [InlineData(30, 'W', 22, 70.0)]
    [InlineData(30, 'W', 60, 70.0)]
    [InlineData(25, 'W', 0, 75.0)]
    [InlineData(0, 'L', 22, 100.0)]
    [InlineData(0, 'W', 22, 100.0)]
    [InlineData(100, 'L', 22, 22.0)]
    [InlineData(100, 'W', 22, 0.0)]
    [InlineData(30, 'L', 100, 100.0)]
    public void The_floor_of_a_rule_is_of_what_is_left_or_of_the_whole_window(double keep, char basis, double used, double expected)
    {
        var rule = Task(keep, basis == 'L' ? ReserveBasis.WhatIsLeft : ReserveBasis.WholeWindow);
        Assert.Equal(expected, ReserveMath.FloorOf(rule, used), 6);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-1")]
    public void A_standing_rule_is_always_of_the_whole_window_and_a_bad_amount_keeps_everything()
    {
        var standing = new ReserveRule("s", ReserveScope.Every, ReserveLifetime.Standing) { KeepPercent = 30, Basis = ReserveBasis.WhatIsLeft };
        Assert.Equal(ReserveBasis.WholeWindow, standing.Normalized().Basis);
        Assert.Equal(70.0, ReserveMath.FloorOf(standing, 60), 6);

        Assert.Equal(0.0, ReserveMath.FloorOf(Standing(double.NaN), 10), 6); // NaN keeps all of the window
        Assert.Equal(100.0, ReserveMath.FloorOf(Task(-5), 22), 6);           // below 0 is 0 kept
        Assert.Equal(22.0, ReserveMath.FloorOf(Task(250), 22), 6);           // over 100 is 100 kept
        Assert.Equal(100.0, ReserveMath.FloorOf(Task(30), double.NaN), 6);   // an unknown start: no room is promised
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-3")]
    public void A_dollar_rule_is_a_share_of_the_keys_budget_and_needs_the_budget()
    {
        var keep5 = new ReserveRule("d", ReserveScope.Every, ReserveLifetime.Standing) { KeepDollars = 5m, Windows = ReserveWindows.Day };
        Assert.Equal(75.0, ReserveMath.FloorOf(keep5, 0, 20m), 6);
        Assert.Equal(100.0, ReserveMath.FloorOf(keep5, 0, null), 6);

        var day = Window(ReserveWindowKind.Day, 10) with { LimitDollars = 20m };
        Assert.Equal(75.0, ReserveMath.EffectiveFloor([new(keep5, 0)], day, Now)!.Percent, 6);
        Assert.Null(ReserveMath.EffectiveFloor([new(keep5, 0)], Window(ReserveWindowKind.Day, 10), Now)); // no budget, no floor
        Assert.Null(ReserveMath.EffectiveFloor([new(keep5, 0)], Window(ReserveWindowKind.FiveHour, 10) with { LimitDollars = 20m }, Now)); // wrong window
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-3")]
    public void Both_windows_each_have_their_own_floor_and_a_rule_for_one_window_leaves_the_other_free()
    {
        var both = Task(30);
        var fiveHour = Window(ReserveWindowKind.FiveHour, 60);
        var weekly = Window(ReserveWindowKind.Weekly, 22);
        Assert.Equal(88.0, ReserveMath.EffectiveFloor([new(both, 60)], fiveHour, Now)!.Percent, 6);
        Assert.Equal(76.6, ReserveMath.EffectiveFloor([new(both, 22)], weekly, Now)!.Percent, 6);

        var weeklyOnly = Task(30, ReserveBasis.WhatIsLeft, ReserveWindows.Weekly);
        Assert.Null(ReserveMath.EffectiveFloor([new(weeklyOnly, 60)], fiveHour, Now));
        Assert.NotNull(ReserveMath.EffectiveFloor([new(weeklyOnly, 22)], weekly, Now));
        Assert.Null(ReserveMath.EffectiveFloor([new(both, 0)], Window(ReserveWindowKind.Other, 10), Now)); // an unknown window is not protected
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-3")]
    public void The_lowest_floor_binds_and_a_task_rule_wins_a_tie()
    {
        var window = Window(ReserveWindowKind.FiveHour, 60);
        // The canvas: a task rule "30% of what's left" (floor 88 from 60 used) and the standing 30% (floor 70): 70 binds.
        var floor = ReserveMath.EffectiveFloor([new(Task(30), 60), new(Standing(30), 0)], window, Now)!;
        Assert.Equal(70.0, floor.Percent, 6);
        Assert.Equal(ReserveLifetime.Standing, floor.Rule.Lifetime);

        // A tie: 30% of what is left from 0 used is 70, like the standing rule; the owner's task rule is named.
        var tie = ReserveMath.EffectiveFloor([new(Standing(30), 0), new(Task(30), 0)], window, Now)!;
        Assert.Equal(70.0, tie.Percent, 6);
        Assert.Equal(ReserveLifetime.Task, tie.Rule.Lifetime);
        Assert.Null(ReserveMath.EffectiveFloor([], window, Now));
    }

    // Each line: window kind, minutes to the reset, expected eased floor of a standing 70 (a 5-hour eases over 45 min, a week over 3 h).
    [Theory]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-3")]
    [InlineData(ReserveWindowKind.FiveHour, 120, 70.0)]
    [InlineData(ReserveWindowKind.FiveHour, 45, 70.0)]
    [InlineData(ReserveWindowKind.FiveHour, 22.5, 85.0)]
    [InlineData(ReserveWindowKind.FiveHour, 9, 94.0)]
    [InlineData(ReserveWindowKind.FiveHour, 0, 100.0)]
    [InlineData(ReserveWindowKind.FiveHour, -5, 100.0)]
    [InlineData(ReserveWindowKind.Weekly, 400, 70.0)]
    [InlineData(ReserveWindowKind.Weekly, 180, 70.0)]
    [InlineData(ReserveWindowKind.Weekly, 90, 85.0)]
    [InlineData(ReserveWindowKind.Weekly, 0, 100.0)]
    [InlineData(ReserveWindowKind.Day, 5, 70.0)]
    [InlineData(ReserveWindowKind.Month, 5, 70.0)]
    public void A_standing_floor_eases_in_a_straight_line_to_no_floor_at_the_reset(ReserveWindowKind kind, double minutes, double expected)
    {
        var window = Window(kind, 50, Now.AddMinutes(minutes));
        var everyWindow = Standing(30, ReserveWindows.Both | ReserveWindows.Day | ReserveWindows.Month);
        Assert.Equal(expected, ReserveMath.EffectiveFloor([new(everyWindow, 0)], window, Now)!.Percent, 6);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-3")]
    public void A_task_rule_is_kept_exactly_and_Hold_reserves_turns_easing_off()
    {
        var nearReset = Window(ReserveWindowKind.FiveHour, 60, Now.AddMinutes(10));
        var taskFloor = ReserveMath.EffectiveFloor([new(Task(30), 60)], nearReset, Now)!;
        Assert.Equal(88.0, taskFloor.Percent, 6);
        Assert.False(taskFloor.Eased);

        var easing = ReserveMath.EffectiveFloor([new(Standing(30), 0)], nearReset, Now)!;
        Assert.True(easing.Eased);
        Assert.Equal(70.0, easing.Unadjusted, 6);
        Assert.True(easing.Percent > 90);

        var held = ReserveMath.EffectiveFloor([new(Standing(30), 0)], nearReset, Now, holdReserves: true)!;
        Assert.Equal(70.0, held.Percent, 6);
        Assert.False(held.Eased);

        // A window with no reset time cannot ease.
        Assert.Equal(70.0, ReserveMath.EffectiveFloor([new(Standing(30), 0)], Window(ReserveWindowKind.FiveHour, 60), Now)!.Percent, 6);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-3")]
    public void Using_the_reserve_once_raises_the_floor_by_what_the_owner_agreed_and_never_past_100()
    {
        var window = Window(ReserveWindowKind.FiveHour, 68);
        Assert.Equal(74.0, ReserveMath.EffectiveFloor([new(Standing(30), 0)], window, Now, allowance: 4)!.Percent, 6);
        Assert.Equal(100.0, ReserveMath.EffectiveFloor([new(Standing(30), 0)], window, Now, allowance: 60)!.Percent, 6);
        Assert.Equal(70.0, ReserveMath.EffectiveFloor([new(Standing(30), 0)], window, Now, allowance: -3)!.Percent, 6);
    }

    [Theory]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-3")]
    [InlineData(50, 0, 50_000, 50.0)]       // nothing since the reading: the reading itself
    [InlineData(50, 100_000, 50_000, 52.0)] // exactly 2 points
    [InlineData(50, 100_001, 50_000, 53.0)] // a hair over rounds up
    [InlineData(50, 1, 50_000, 51.0)]       // any spend rounds up to the next point
    [InlineData(50.4, 0, 50_000, 50.4)]     // a fractional reading with no spend is kept as read
    [InlineData(50, 5_000, 0, 100.0)]       // an unusable rate fails closed
    [InlineData(50, 5_000, -3, 100.0)]
    public void The_estimate_between_polls_is_the_reading_plus_the_ledger_rounded_up(double reading, double tokens, double rate, double expected)
    {
        Assert.Equal(expected, ReserveMath.Estimate(reading, tokens, rate), 6);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-3")]
    public void The_margin_is_the_in_flight_p90s_plus_staleness_times_burn_and_never_under_one_point()
    {
        Assert.Equal(1.0, ReserveMath.Margin([], TimeSpan.Zero, 0), 6);
        Assert.Equal(1.0, ReserveMath.Margin([0.2], TimeSpan.FromSeconds(10), 0.01), 6); // 0.2 + 0.1 is under the floor of 1
        Assert.Equal(1.5 + 2.0 + 120 * 0.01, ReserveMath.Margin([1.5, 2.0], TimeSpan.FromSeconds(120), 0.01), 6);
        Assert.Equal(16 * 0.4, ReserveMath.Margin(Enumerable.Repeat(0.4, 16), TimeSpan.Zero, 0), 6); // up to 16 agents
        Assert.Equal(3.0, ReserveMath.Margin([], TimeSpan.FromMinutes(5), 0.01), 6);   // a reading left to go stale widens it
        Assert.Equal(1.0, ReserveMath.Margin([-4], TimeSpan.FromSeconds(-30), -1), 6);  // nothing negative reduces it
        Assert.Equal(100.0, ReserveMath.Margin([double.PositiveInfinity], TimeSpan.Zero, 0), 6);
        Assert.Equal(100.0, ReserveMath.Margin([double.NaN], TimeSpan.Zero, 0), 6);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-3")]
    public void A_projection_crosses_a_floor_only_when_it_is_over_it_and_a_floor_of_100_is_no_reserve()
    {
        Assert.Equal(66.0, ReserveMath.Projected(60, 5, 1), 6);
        Assert.False(ReserveMath.Crosses(70.0, 70.0));
        Assert.True(ReserveMath.Crosses(70.01, 70.0));
        Assert.False(ReserveMath.Crosses(130, 100));
        Assert.Equal(5.0, ReserveMath.AllowanceNeeded(74.2, 70), 6);
        Assert.Equal(1.0, ReserveMath.AllowanceNeeded(70.0001, 70), 6);
        Assert.Equal(4.0, ReserveMath.AllowanceNeeded(74.0, 70), 6);
    }

    // Each line: reading, estimate, the outside use reported (0 when the gap is 2 points or less).
    [Theory]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-3")]
    [InlineData(60, 57, 3.0)]
    [InlineData(60, 58, 0.0)]   // exactly 2 points is not outside use
    [InlineData(60, 58.01, 0.0)]
    [InlineData(60, 57.99, 2.01)]
    [InlineData(60, 60, 0.0)]
    [InlineData(60, 64, 0.0)]   // lower than the estimate: the estimate was high, nobody else used anything
    public void A_reading_more_than_two_points_over_the_estimate_is_outside_use(double reading, double estimate, double expected)
    {
        Assert.Equal(expected, ReserveMath.OutsideUse(reading, estimate), 6);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-3")]
    public void Spend_more_than_a_quarter_over_the_estimate_re_plans()
    {
        Assert.False(ReserveMath.RunsOver(1_250, 1_000));
        Assert.True(ReserveMath.RunsOver(1_251, 1_000));
        Assert.False(ReserveMath.RunsOver(5_000, 0));
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-3")]
    public void Percent_words_drop_a_whole_number_s_decimal()
    {
        Assert.Equal("70%", ReserveMath.Percent(70));
        Assert.Equal("68.6%", ReserveMath.Percent(68.6));
        Assert.Equal("76.6%", ReserveMath.Percent(76.6));
        Assert.Equal("0.1%", ReserveMath.Percent(0.14));
    }

    [Theory]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-1")]
    [InlineData(300L, ReserveWindowKind.FiveHour)]
    [InlineData(10080L, ReserveWindowKind.Weekly)]
    [InlineData(1440L, ReserveWindowKind.Day)]
    [InlineData(43200L, ReserveWindowKind.Month)]
    [InlineData(123L, ReserveWindowKind.Other)]
    public void A_window_s_kind_comes_from_its_reported_length(long minutes, ReserveWindowKind expected)
    {
        Assert.Equal(expected, ReserveWindowKinds.Of(minutes, "5-hour limit"));
    }

    [Theory]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-1")]
    [InlineData("5-hour limit", ReserveWindowKind.FiveHour)]
    [InlineData("Weekly limit", ReserveWindowKind.Weekly)]
    [InlineData("Monthly credits", ReserveWindowKind.Month)]
    [InlineData("Daily spend", ReserveWindowKind.Day)]
    [InlineData("Something else", ReserveWindowKind.Other)]
    public void Without_a_length_the_title_is_the_fallback(string title, ReserveWindowKind expected)
    {
        Assert.Equal(expected, ReserveWindowKinds.Of(null, title));
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-1")]
    public void A_scope_matches_accounts_by_the_names_the_owner_gave_them()
    {
        var claude20 = new ReserveSubject("claude:1", "Claude 20x", "claude", "main");
        var codexA = new ReserveSubject("codex:2", "Codex A", "codex", "main");
        Assert.True(ReserveScope.Every.Covers(claude20));
        Assert.True(ReserveScope.Named("claude 20x").Covers(claude20));
        Assert.False(ReserveScope.Named("claude 20x").Covers(codexA));
        Assert.True(ReserveScope.Named("Codex A", "Claude 20x").Covers(codexA));
        Assert.True(ReserveScope.Provider("CODEX").Covers(codexA));
        Assert.False(ReserveScope.Provider("codex").Covers(claude20));
        Assert.True(ReserveScope.Assistant("main").Covers(claude20));
        Assert.False(ReserveScope.Assistant("channel-a").Covers(claude20));
        Assert.False(ReserveScope.Assistant("main").Covers(claude20 with { AssistantId = null }));
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-1")]
    public void Rules_read_back_in_the_canvas_s_words()
    {
        var always = new ReserveRule("1", ReserveScope.Named("Claude 20x"), ReserveLifetime.Standing) { KeepPercent = 30, Windows = ReserveWindows.Both };
        Assert.Equal("Always keep 30% on Claude 20x", ReserveWords.Title(always));
        Assert.Equal("Standing · whole 5-hour and weekly windows · all assistants", ReserveWords.Detail(always));

        var channel = new ReserveRule("2", ReserveScope.Assistant("channel-a"), ReserveLifetime.Standing) { KeepPercent = 85 };
        Assert.EndsWith("channel-a only", ReserveWords.Detail(channel), StringComparison.Ordinal);

        var dollars = new ReserveRule("3", ReserveScope.Named("OpenRouter key"), ReserveLifetime.Standing) { KeepDollars = 5m, Windows = ReserveWindows.Day };
        Assert.Equal("Keep $5 a day on OpenRouter key", ReserveWords.Title(dollars));
        Assert.Equal("Standing · API key, dollars · all assistants", ReserveWords.Detail(dollars));

        var task = new ReserveRule("4", ReserveScope.Every, ReserveLifetime.Task) { KeepPercent = 30 };
        Assert.Equal("Keep 30% of what's left", ReserveWords.Title(task));
        Assert.Equal("This task · what's left in 5-hour and weekly windows · all assistants", ReserveWords.Detail(task));
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-2")]
    public void A_new_account_starts_from_the_published_rate_made_forty_percent_dearer_with_the_p90_doubled()
    {
        var rate = CalibratedRate.Conservative(250_000);
        Assert.Equal(150_000, rate.TokensPerPercent, 6);          // 1% buys 0.6 of the default tokens
        Assert.Equal(75_000, rate.PessimisticTokensPerPercent, 6); // the p90 cost is twice the median cost
        Assert.False(rate.Calibrated);
        Assert.True(rate.Rough);
        Assert.Equal(0, rate.Intervals);
        Assert.Equal(2.0, rate.Percent(300_000), 6);
        Assert.Equal(4.0, rate.PercentP90(300_000), 6);
        Assert.Equal(60_000, CalibratedRate.Conservative(double.NaN).TokensPerPercent, 6); // an unknown published rate takes the fallback
        Assert.Equal(60_000, CalibratedRate.Conservative(-1).TokensPerPercent, 6);
        Assert.Equal(0, rate.Percent(-5), 6);
        Assert.Equal(5, CalibratedRate.IntervalsToCalibrate);
    }
}
