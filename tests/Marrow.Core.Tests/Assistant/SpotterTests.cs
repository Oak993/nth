using Marrow.Core.Port.Assistant;
using static Marrow.Core.Tests.Fixtures.DigestSamples;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-11 (DESIGN.md rev 5, 5.2; ED-NEW-dextra-as11-4): cheap rules, no model call, spot the four things worth a suggestion: an allowance
// about to reset unused, the owner doing the same thing by hand three times, a failing check in a project he works on, and a duty that keeps
// finding nothing. Each candidate carries its words and the plan Accept follows. The clock is 08:00 on Tuesday 13 October 2026, local time.
[Trait("Feature", "AS-11")]
[Trait("Decision", "ED-NEW-dextra-as11-4")]
public sealed class SpotterTests
{
    private static readonly Guid Store = new("11111111-1111-1111-1111-111111111111");

    private static SpotterInput Input() => new(Morning, Zone)
    {
        Projects = [new SpotProject(Store, "Store app", Morning.AddDays(-1))],
    };

    private static DigestAccount Codex(double used, double? floor, DateTimeOffset? resets) =>
        Account("Codex A", Window(ReserveWindowKind.FiveHour, 22), Window(ReserveWindowKind.Weekly, used, floor, resets));

    // ---- an allowance about to reset unused ----

    [Fact]
    public void A_weekly_window_resetting_in_six_hours_with_31_percent_unused_is_offered_with_the_canvas_words()
    {
        var found = Spotter.Spot(Input() with { Accounts = [Codex(69, null, Morning.AddHours(6))] });
        var candidate = Assert.Single(found);
        Assert.Equal(SuggestionKind.AllowanceUnused, candidate.Kind);
        Assert.Equal("31% of Codex A's weekly window is unused and resets at 14:00. Put up to 5% of it to work on Store app?", candidate.Line);
    }

    [Fact]
    public void The_spend_is_what_the_floor_leaves_up_to_five_points_and_an_offer_needs_three()
    {
        var wide = Spotter.Spot(Input() with { Accounts = [Codex(40, null, Morning.AddHours(3))] }).Single();
        Assert.Equal("Up to 5% of Codex A's weekly window", wide.Cost);
        Assert.Contains("Put up to 5% of it to work on Store app?", wide.Line, StringComparison.Ordinal);
        var tight = Spotter.Spot(Input() with { Accounts = [Codex(40, 44, Morning.AddHours(3))] }).Single();
        Assert.Equal("Up to 4% of Codex A's weekly window", tight.Cost);
        // Two points under the floor are not worth an offer.
        Assert.Empty(Spotter.Spot(Input() with { Accounts = [Codex(40, 42, Morning.AddHours(3))] }));
    }

    [Theory]
    [InlineData(81, 6)]          // 19% unused: under a fifth
    [InlineData(40, 13)]         // resets in more than 12 hours: nothing is about to be lost
    [InlineData(40, -1)]         // already reset
    public void An_allowance_with_little_unused_or_far_from_its_reset_is_not_offered(double used, double hours)
    {
        Assert.Empty(Spotter.Spot(Input() with { Accounts = [Codex(used, null, Morning.AddHours(hours))] }));
    }

    [Fact]
    public void Only_the_weekly_window_counts_and_an_offer_needs_a_project_to_work_in()
    {
        var fiveHour = Account("Codex A", Window(ReserveWindowKind.FiveHour, 10, null, Morning.AddHours(2)), Window(ReserveWindowKind.Day, 10, null, Morning.AddHours(2)));
        Assert.Empty(Spotter.Spot(Input() with { Accounts = [fiveHour] }));
        Assert.Empty(Spotter.Spot(new SpotterInput(Morning, Zone) { Accounts = [Codex(40, null, Morning.AddHours(3))] }));
    }

    [Fact]
    public void The_offer_goes_to_the_project_the_owner_used_last_and_names_the_window_in_its_brief()
    {
        var other = new SpotProject(Guid.NewGuid(), "Docs", Morning.AddHours(-2));
        var candidate = Spotter.Spot(Input() with { Projects = [new SpotProject(Store, "Store app", Morning.AddDays(-3)), other], Accounts = [Codex(40, null, Morning.AddHours(3))] }).Single();
        Assert.Equal(other.Id, candidate.Plan.ProjectId);
        Assert.Equal(SuggestionDo.StartMission, candidate.Plan.Do);
        Assert.Contains("weekly window", candidate.Plan.Brief, StringComparison.Ordinal);
        Assert.Contains("Change nothing outside the mission's own copy", candidate.Plan.Brief, StringComparison.Ordinal);
        Assert.Equal("allowance:Codex A:Weekly:20261013T1000", candidate.Key);
    }

    [Fact]
    public void The_allowance_that_resets_sooner_is_offered_first()
    {
        var soon = Account("Soon", Window(ReserveWindowKind.Weekly, 10, null, Morning.AddHours(2)));
        var later = Account("Later", Window(ReserveWindowKind.Weekly, 10, null, Morning.AddHours(9)));
        Assert.Equal(["Soon", "Later"], Spotter.Spot(Input() with { Accounts = [later, soon] }).Select(c => c.Key.Split(':')[1]));
    }

    // ---- the same thing by hand three times ----

    private static DateTimeOffset Friday(int weeksAgo, int hour = 16) => new DateTimeOffset(2026, 10, 9, hour, 5, 0, TimeSpan.FromHours(1)).AddDays(-7 * weeksAgo);

    private static ChatHabit Habit(params DateTimeOffset[] starts) => new("store:reprice", "Re-price listings", Store, "Store app", starts);

    [Fact]
    public void Three_fridays_in_a_row_of_the_same_chat_in_the_same_project_is_a_habit_the_owner_may_make_a_duty()
    {
        var found = Spotter.Spot(Input() with { Habits = [Habit(Friday(0), Friday(1, 15), Friday(2, 16))] });
        var candidate = Assert.Single(found);
        Assert.Equal(SuggestionKind.DoneByHand, candidate.Kind);
        Assert.Equal("You started “Re-price listings” by hand three Fridays running. Make it a duty? You'll see the duty card first.", candidate.Line);
        Assert.Equal(SuggestionDo.NewDuty, candidate.Plan.Do);
        Assert.Equal(DayOfWeek.Friday, candidate.Plan.Day);
        Assert.Equal(new TimeOnly(16, 0), candidate.Plan.At);          // the middle of 16:05, 15:05 and 16:05, to the quarter hour
        Assert.Equal(Store, candidate.Plan.ProjectId);
        Assert.Equal("Re-price listings", candidate.Plan.Title);
        Assert.Contains("Ask him before anything that sends, pays, deletes or installs.", candidate.Plan.Instructions, StringComparison.Ordinal);
        Assert.Equal("hand:store:reprice:Friday", candidate.Key);
    }

    [Fact]
    public void A_gap_a_mix_of_days_a_stale_habit_or_a_habit_already_made_a_duty_is_not_a_habit()
    {
        Assert.Empty(Spotter.Spot(Input() with { Habits = [Habit(Friday(0), Friday(1), Friday(3))] }));                       // a week missing
        Assert.Empty(Spotter.Spot(Input() with { Habits = [Habit(Friday(0), Friday(1).AddDays(1), Friday(2))] }));             // one of them on a Saturday
        Assert.Empty(Spotter.Spot(Input() with { Habits = [Habit(Friday(1), Friday(2), Friday(3))] }));                       // the newest three ended two weeks ago: it is not running
        Assert.Empty(Spotter.Spot(Input() with { Habits = [Habit(Friday(5), Friday(6), Friday(7))] }));                       // a habit he dropped two months ago
        Assert.Empty(Spotter.Spot(Input() with { Habits = [Habit(Friday(0), Friday(1))] }));                                  // twice is not three times
        Assert.Empty(Spotter.Spot(Input() with { Habits = [Habit(Friday(0), Friday(1), Friday(2))], DutyNames = ["Re-price listings every Friday"] }));
    }

    [Fact]
    public void A_habit_from_a_chat_on_a_local_model_or_a_private_account_stays_local()
    {
        var found = Spotter.Spot(Input() with { Habits = [Habit(Friday(0), Friday(1), Friday(2)) with { LocalOnly = true }] });
        var candidate = Assert.Single(found);
        Assert.True(candidate.LocalOnly);
    }

    // ---- a failing check ----

    [Fact]
    public void A_mission_that_failed_its_review_in_a_project_the_owner_works_in_is_offered_a_look()
    {
        var failing = new FailingCheck("m1", "Fix the login timeout", Store, "Store app", Morning.AddHours(-3));
        var candidate = Assert.Single(Spotter.Spot(Input() with { Failing = [failing] }));
        Assert.Equal(SuggestionKind.FailingCheck, candidate.Kind);
        Assert.Equal("“Fix the login timeout” failed its review in Store app. Want me to find out why?", candidate.Line);
        Assert.Equal(SuggestionDo.StartMission, candidate.Plan.Do);
        Assert.Equal(Store, candidate.Plan.ProjectId);
        Assert.Contains("Change nothing in the owner's files.", candidate.Plan.Brief, StringComparison.Ordinal);
        Assert.Equal("check:m1", candidate.Key);
    }

    // ---- a duty that keeps finding nothing ----

    private static SpotDuty Duty(int every, int quiet, int hours = 30, bool enabled = true) =>
        new("d1", "Espresso price watch", every, [.. Enumerable.Repeat(DutyRunOutcome.NothingChanged, quiet)], TimeSpan.FromHours(hours), enabled);

    [Fact]
    public void A_duty_that_looked_every_two_hours_and_found_nothing_eight_times_in_a_row_is_offered_a_slower_look()
    {
        var candidate = Assert.Single(Spotter.Spot(Input() with { Duties = [Duty(120, 12)] }));
        Assert.Equal(SuggestionKind.IdleDuty, candidate.Kind);
        Assert.Equal("“Espresso price watch” found nothing in its last 12 runs. Look every 6 h instead of every 2 h?", candidate.Line);
        Assert.Equal("Saves about 8 runs a day.", candidate.Cost);
        Assert.Equal(SuggestionDo.ChangeDuty, candidate.Plan.Do);
        Assert.Equal("d1", candidate.Plan.DutyId);
        Assert.Equal(360, candidate.Plan.EveryMinutes);
    }

    [Theory]
    [InlineData(120, 7, 30, true)]        // too few quiet runs
    [InlineData(120, 12, 20, true)]       // not over a day yet
    [InlineData(300, 12, 30, true)]       // it already looks only a few times a day
    [InlineData(120, 12, 30, false)]      // switched off
    public void A_duty_that_is_not_often_quiet_or_not_running_is_left_alone(int every, int quiet, int hours, bool enabled)
    {
        Assert.Empty(Spotter.Spot(Input() with { Duties = [Duty(every, quiet, hours, enabled)] }));
    }

    [Fact]
    public void A_duty_whose_recent_run_found_something_is_not_idle_and_one_without_an_interval_cannot_be_slowed()
    {
        var found = new SpotDuty("d2", "Price", 60, [DutyRunOutcome.Done, .. Enumerable.Repeat(DutyRunOutcome.NothingChanged, 12)], TimeSpan.FromDays(2), true);
        var daily = new SpotDuty("d3", "Digest", null, [.. Enumerable.Repeat(DutyRunOutcome.NothingChanged, 12)], TimeSpan.FromDays(12), true);
        Assert.Empty(Spotter.Spot(Input() with { Duties = [found, daily] }));
    }

    [Fact]
    public void Only_the_quiet_streak_at_the_head_counts_and_it_is_told_by_its_own_length()
    {
        // Nine quiet runs, then one that found something, then more quiet ones: the streak is nine, not eleven.
        DutyRunOutcome[] recent = [.. Enumerable.Repeat(DutyRunOutcome.NothingChanged, 9), DutyRunOutcome.Done, DutyRunOutcome.NothingChanged, DutyRunOutcome.NothingChanged];
        var duty = new SpotDuty("d1", "Espresso price watch", 120, recent, TimeSpan.FromHours(30), true);
        var candidate = Assert.Single(Spotter.Spot(Input() with { Duties = [duty] }));
        Assert.Equal("“Espresso price watch” found nothing in its last 9 runs. Look every 6 h instead of every 2 h?", candidate.Line);
        var brokenEarly = new SpotDuty("d2", "Price", 120, [.. Enumerable.Repeat(DutyRunOutcome.NothingChanged, 7), DutyRunOutcome.Done, .. Enumerable.Repeat(DutyRunOutcome.NothingChanged, 9)], TimeSpan.FromHours(30), true);
        Assert.Empty(Spotter.Spot(Input() with { Duties = [brokenEarly] }));
    }

    [Theory]
    [InlineData(15, 60, 72)]
    [InlineData(30, 120, 36)]
    [InlineData(60, 180, 16)]
    [InlineData(240, 720, 4)]
    public void The_slower_interval_is_the_first_clean_step_at_least_three_times_the_old_one(int every, int slower, int saved)
    {
        var candidate = Spotter.Spot(Input() with { Duties = [Duty(every, 12)] }).Single();
        Assert.Equal(slower, candidate.Plan.EveryMinutes);
        Assert.Equal($"Saves about {saved} runs a day.", candidate.Cost);
    }

    // ---- all together ----

    [Fact]
    public void Candidates_are_ordered_by_how_soon_they_matter_and_are_never_more_than_twelve()
    {
        var failing = Enumerable.Range(1, 15).Select(i => new FailingCheck("m" + i, "Mission " + i, Store, "Store app", Morning.AddHours(-i))).ToList();
        var found = Spotter.Spot(Input() with { Failing = failing, Accounts = [Codex(40, null, Morning.AddHours(3))], Duties = [Duty(120, 12)] });
        Assert.Equal(Spotter.MaxCandidates, found.Count);
        Assert.Equal(SuggestionKind.AllowanceUnused, found[0].Kind);
        Assert.Equal(SuggestionKind.FailingCheck, found[1].Kind);
    }

    [Fact]
    public void The_same_input_gives_the_same_candidates_in_the_same_order()
    {
        var input = Input() with { Failing = [new FailingCheck("m1", "A", Store, "Store app", Morning)], Accounts = [Codex(40, null, Morning.AddHours(3))], Duties = [Duty(120, 12)] };
        Assert.Equal(Spotter.Spot(input).Select(c => c.Key), Spotter.Spot(input).Select(c => c.Key));
    }
}
