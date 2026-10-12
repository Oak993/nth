using System.Globalization;
using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-13 (DESIGN.md rev 5, 11.4, 11.5; ED-NEW-dextra-as13-8): the plan line, its reading, the four reasons the owner must tap
// Start, and the after-report's words, written out with the canvas's own example (Codex A, Claude B, Claude 20x).
public sealed class ReservePlanTests
{
    private static PlanAccountLine Account(string name, double used, double p50, double p90, double? floor, bool rough = false, ReserveWindowKind kind = ReserveWindowKind.FiveHour) =>
        new("key:" + name, name, kind, "", used, p50, p90, floor, rough);

    private static ReservePlan Canvas() => new([
        Account("Codex A", 22, 12, 14, 76.6, kind: ReserveWindowKind.Weekly),
        Account("Claude B", 41, 8, 10, 82.3, kind: ReserveWindowKind.Weekly),
        Account("Claude 20x", 60, 4, 6, 70),
    ])
    {
        Reading = "Keeping 30% of what's left on each account",
        Reserves = [new PlanReserve("Claude 20x", 30, ReserveLifetime.Standing)],
        Team = "Hydra team of 4",
        Duration = TimeSpan.FromMinutes(25),
    };

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-8")]
    public void The_plan_line_is_the_canvas_s_with_the_tightest_account_last()
    {
        Assert.Equal("About 12% of Codex A, 8% of Claude B and up to 6% of Claude 20x. Your 30% reserve on Claude 20x stays.", Canvas().Line);
        // Whatever order the planner lists them in, the account with the least room goes last.
        var shuffled = new ReservePlan([.. Canvas().Accounts.Reverse()]) { Reserves = Canvas().Reserves };
        Assert.Equal(Canvas().Line, shuffled.Line);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-8")]
    public void The_details_state_the_reading_the_team_the_time_and_whether_start_needs_the_owner()
    {
        Assert.Equal("Keeping 30% of what's left on each account · Hydra team of 4 · about 25 min · start needs you (over 5% of a window; a floor is close)", Canvas().Details);
        var easy = new ReservePlan([Account("Codex A", 20, 2, 3, 70)]) { Reading = "Keeping 30% of what's left on each account" };
        Assert.Equal("Keeping 30% of what's left on each account · starts on its own", easy.Details);
        Assert.False(easy.NeedsStartTap);
        Assert.True(Canvas().NeedsStartTap);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-8")]
    public void An_account_that_is_not_calibrated_is_marked_estimate_rough_and_amounts_round_up()
    {
        var plan = new ReservePlan([Account("Codex A", 22, 11.2, 13, 76.6, rough: true), Account("Claude B", 41, 7.01, 9, 82.3)]);
        Assert.Equal("About 12% of Codex A (estimate rough) and up to 9% of Claude B.", plan.Line);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-8")]
    public void With_no_reserve_on_an_account_it_is_listed_plainly_and_with_none_the_line_says_so()
    {
        Assert.Equal("About 12% of Codex A and 8% of Claude B.", new ReservePlan([Account("Codex A", 22, 12, 14, null), Account("Claude B", 41, 8, 10, null)]).Line);
        Assert.Equal("About 12% of Codex A.", new ReservePlan([Account("Codex A", 22, 12, 14, 100)]).Line); // a floor of 100 is no reserve
        Assert.Equal("Up to 6% of Claude 20x. Your 30% reserve on Claude 20x stays.",
            new ReservePlan([Account("Claude 20x", 60, 4, 6, 70)]) { Reserves = [new PlanReserve("Claude 20x", 30, ReserveLifetime.Standing)] }.Line);
        Assert.Equal("Nothing is planned to use.", new ReservePlan([]).Line);
        Assert.EndsWith("Your reserves stay.", new ReservePlan([Account("A", 10, 2, 3, 70), Account("B", 10, 2, 3, 70)])
            { Reserves = [new PlanReserve("A", 30, ReserveLifetime.Task), new PlanReserve("B", 30, ReserveLifetime.Task)] }.Line, StringComparison.Ordinal);
        Assert.Contains("22.5% reserve", new ReservePlan([Account("A", 10, 2, 3, 70)]) { Reserves = [new PlanReserve("A", 22.5, ReserveLifetime.Task)] }.Line, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-8")]
    public void The_owner_taps_start_when_the_p90_is_over_5_percent_a_floor_is_within_twice_the_estimate_over_8_agents_or_over_2_dollars()
    {
        static ReservePlan One(double used, double p90, double? floor) => new([Account("A", used, p90 / 2, p90, floor)]);
        Assert.Empty(One(10, 5.0, 90).StartNeeds);                                  // exactly 5%: not over
        Assert.Equal(["over 5% of a window"], One(10, 5.01, 90).StartNeeds);
        Assert.Empty(One(10, 4, 90).StartNeeds);
        Assert.Empty(One(60, 4, 70).StartNeeds);                                    // 10 points of room, twice the 4% p90 is 8: not close
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-8")]
    public void The_floor_rule_compares_the_room_to_twice_the_p90()
    {
        PlanAccountLine A(double used, double floor) => Account("A", used, 2, 4, floor);
        Assert.Equal(["a floor is close"], new ReservePlan([A(62, 70)]).StartNeeds); // room 8 = 2 x 4: close
        Assert.Empty(new ReservePlan([A(61.9, 70)]).StartNeeds);                     // room 8.1: not
        Assert.Empty(new ReservePlan([A(95, 100)]).StartNeeds);                      // a floor of 100 is no reserve
        Assert.Empty(new ReservePlan([A(95, 100) with { Floor = null }]).StartNeeds);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-8")]
    public void More_than_eight_agents_over_two_dollars_and_the_always_ask_setting_each_need_the_owner()
    {
        var quiet = new ReservePlan([Account("A", 10, 1, 2, 90)]);
        Assert.Empty((quiet with { Agents = 8 }).StartNeeds);
        Assert.Equal(["9 agents planned"], (quiet with { Agents = 9 }).StartNeeds);
        Assert.Empty((quiet with { ApiDollars = 2m }).StartNeeds);
        Assert.Equal(["about $2.01 of API spend"], (quiet with { ApiDollars = 2.01m }).StartNeeds);
        Assert.Equal(["you asked to be asked"], (quiet with { AlwaysAsk = true }).StartNeeds);
        Assert.Equal(["over 5% of a window", "9 agents planned", "about $3 of API spend", "you asked to be asked"],
            (new ReservePlan([Account("A", 10, 3, 6, 90)]) { Agents = 9, ApiDollars = 3m, AlwaysAsk = true }).StartNeeds);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-8")]
    public void A_plan_whose_p90_reaches_a_floor_says_so()
    {
        Assert.False(Canvas().CrossesAFloor);
        Assert.Equal(4.0, Canvas().Accounts[2].Room!.Value, 9);
        Assert.True(new ReservePlan([Account("A", 60, 5, 12, 70)]).CrossesAFloor);   // room -2
        Assert.False(new ReservePlan([Account("A", 60, 5, 10, 70)]).CrossesAFloor);  // room 0 is not a crossing
        Assert.Null(Account("A", 60, 5, 10, null).Room);
        Assert.Null(Account("A", 60, 5, 10, 100).Room);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-8")]
    public void Windows_have_the_words_of_the_bar_s_label_and_times_read_in_minutes_then_hours()
    {
        Assert.Equal("weekly", Account("A", 1, 1, 1, 70, kind: ReserveWindowKind.Weekly).WindowLabel);
        Assert.Equal("5-hour window", Account("A", 1, 1, 1, 70).WindowLabel);
        Assert.Equal("today", Account("A", 1, 1, 1, 70, kind: ReserveWindowKind.Day).WindowLabel);
        Assert.Equal("this month", Account("A", 1, 1, 1, 70, kind: ReserveWindowKind.Month).WindowLabel);
        Assert.Equal("25 min", ReservePlanWords.Minutes(TimeSpan.FromMinutes(25)));
        Assert.Equal("1 min", ReservePlanWords.Minutes(TimeSpan.FromSeconds(10)));
        Assert.Equal("89 min", ReservePlanWords.Minutes(TimeSpan.FromMinutes(89)));
        Assert.Equal("1 h 30 min", ReservePlanWords.Minutes(TimeSpan.FromMinutes(90)));
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-8")]
    public void The_after_report_says_every_reserve_intact_or_how_far_one_dipped_and_lists_outside_use_apart()
    {
        var rows = new[] { new ReportRow("Codex A", 12, 11.6, "weekly"), new ReportRow("Claude 20x", 6, 6.4, "5-hour window") };
        var since = new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);
        var intact = new ReserveAfterReport("Thesis outline", rows, [], [new ReportReserve("Claude 20x", "Always keep 30% on Claude 20x", 0, false)]);
        Assert.Equal("Every reserve intact.", intact.ReserveLine);
        Assert.Empty(intact.OutsideLines);

        var once = new ReserveAfterReport("Thesis outline", rows, [],
            [new ReportReserve("Claude 20x", "Always keep 30% on Claude 20x", 0.6, true), new ReportReserve("Codex A", "x", 0, false)]);
        Assert.Equal("Reserve on Claude 20x dipped by 0.6% this once, as you said.", once.ReserveLine);

        var outside = new ReserveAfterReport("Thesis outline", rows, [new ReportOutside("Claude 20x", "5-hour window", 8, since)],
            [new ReportReserve("Claude 20x", "r", 1.5, false)]);
        Assert.Equal("Reserve on Claude 20x dipped by 1.5%; use outside nth took it.", outside.ReserveLine);
        var local = since.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
        Assert.Equal([$"8% of Claude 20x's 5-hour window was used outside nth since {local}."], outside.OutsideLines);
        Assert.True(new ReportReserve("A", "r", 0, false).Intact);
        Assert.False(new ReportReserve("A", "r", 0.1, false).Intact);
    }
}
