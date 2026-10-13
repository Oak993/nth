using Marrow.Core.Port.Assistant;
using static Marrow.Core.Tests.Fixtures.DigestSamples;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-11 (DESIGN.md rev 5, 5.2, 6; ED-NEW-dextra-as11-5): at most three suggestions a day, never a push; Accept and Dismiss answer one for
// good; three dismissals of a kind turn that kind off; a new day lets yesterday's go (their facts are stale) and keeps what the owner taught.
[Trait("Feature", "AS-11")]
[Trait("Decision", "ED-NEW-dextra-as11-5")]
public sealed class SuggestionLedgerTests
{
    private static readonly DateOnly Day = new(2026, 10, 13);

    private static IEnumerable<SuggestionCandidate> Many(int count, SuggestionKind kind = SuggestionKind.AllowanceUnused, string prefix = "k") =>
        Enumerable.Range(1, count).Select(i => Candidate(prefix + i, kind, urgency: 100 - i));

    private static SuggestionLedger Offered(params string[] keys) =>
        SuggestionLedger.Empty.Admit(keys.Select(k => Candidate(k)), Morning, Day).Ledger;

    [Fact]
    public void At_most_three_are_offered_in_a_day_the_most_urgent_first()
    {
        var (ledger, offered) = SuggestionLedger.Empty.Admit(Many(5), Morning, Day);
        Assert.Equal(["k1", "k2", "k3"], offered.Select(s => s.Key));
        Assert.Equal(3, ledger.OfferedToday);
        Assert.Equal(SuggestionLedger.MaxPerDay, ledger.Open.Count);
        // A second digest the same day offers nothing more, however many candidates there are.
        var (again, more) = ledger.Admit(Many(9, prefix: "m"), Morning.AddHours(5), Day);
        Assert.Empty(more);
        Assert.Equal(ledger.Today, again.Today);
    }

    [Fact]
    public void An_answered_suggestion_still_uses_its_place_so_the_day_has_three_places_not_three_open_ones()
    {
        var ledger = Offered("a", "b", "c");
        ledger = ledger.Accept("a", Morning)!;
        ledger = ledger.Dismiss("b", Morning)!;
        var (_, more) = ledger.Admit(Many(3, prefix: "z"), Morning.AddHours(1), Day);
        Assert.Empty(more);
        Assert.Equal(3, ledger.OfferedToday);
        Assert.Equal(["c"], ledger.Open.Select(s => s.Key));
    }

    [Fact]
    public void Fewer_than_three_leave_room_for_a_later_digest_the_same_day()
    {
        var first = SuggestionLedger.Empty.Admit(Many(1), Morning, Day);
        var (ledger, more) = first.Ledger.Admit(Many(4, prefix: "z"), Morning.AddHours(5), Day);
        Assert.Equal(["z1", "z2"], more.Select(s => s.Key));
        Assert.Equal(3, ledger.OfferedToday);
    }

    [Fact]
    public void The_same_suggestion_is_never_offered_twice_in_a_day_and_a_dismissed_or_accepted_one_never_again()
    {
        var ledger = Offered("a");
        Assert.Empty(ledger.Admit([Candidate("a")], Morning, Day).Offered);
        ledger = ledger.Dismiss("a", Morning)!;
        Assert.Empty(ledger.Admit([Candidate("a")], Morning, Day).Offered);
        Assert.Empty(ledger.OnDay(Day.AddDays(1)).Admit([Candidate("a")], Morning.AddDays(1), Day.AddDays(1)).Offered);
        var accepted = Offered("b").Accept("b", Morning)!;
        Assert.Empty(accepted.OnDay(Day.AddDays(3)).Admit([Candidate("b")], Morning.AddDays(3), Day.AddDays(3)).Offered);
    }

    [Fact]
    public void Accept_answers_the_suggestion_and_says_so_and_a_second_answer_is_refused()
    {
        var ledger = Offered("a", "b");
        var accepted = ledger.Accept("a", Morning.AddMinutes(2))!;
        Assert.Equal(SuggestionState.Accepted, accepted.Today.Single(s => s.Key == "a").State);
        Assert.Equal(Morning.AddMinutes(2), accepted.Today.Single(s => s.Key == "a").Settled);
        Assert.Equal(SuggestionState.Open, accepted.Today.Single(s => s.Key == "b").State);
        Assert.Null(accepted.Accept("a", Morning));
        Assert.Null(accepted.Dismiss("a", Morning));
        Assert.Null(accepted.Accept("nope", Morning));
    }

    [Fact]
    public void Three_dismissals_of_one_kind_turn_the_kind_off_and_take_back_the_rest_of_it()
    {
        var ledger = SuggestionLedger.Empty.OnDay(Day);
        var kinds = new[] { SuggestionKind.IdleDuty, SuggestionKind.IdleDuty, SuggestionKind.IdleDuty };
        var candidates = kinds.Select((k, i) => Candidate("i" + i, k, 10 - i)).ToList();
        ledger = ledger.Admit(candidates, Morning, Day).Ledger;
        ledger = ledger.Dismiss("i0", Morning)!;
        Assert.False(ledger.IsOff(SuggestionKind.IdleDuty));
        Assert.Equal(1, ledger.DismissalsOf(SuggestionKind.IdleDuty));
        ledger = ledger.Dismiss("i1", Morning)!;
        Assert.False(ledger.IsOff(SuggestionKind.IdleDuty));
        ledger = ledger.Dismiss("i2", Morning)!;
        Assert.True(ledger.IsOff(SuggestionKind.IdleDuty));
        Assert.Equal(3, ledger.DismissalsOf(SuggestionKind.IdleDuty));
        // From then on a candidate of that kind is not offered, on any day; another kind still is.
        var tomorrow = ledger.OnDay(Day.AddDays(1));
        var (_, offered) = tomorrow.Admit([Candidate("x", SuggestionKind.IdleDuty), Candidate("y", SuggestionKind.FailingCheck)], Morning.AddDays(1), Day.AddDays(1));
        Assert.Equal(["y"], offered.Select(s => s.Key));
    }

    [Fact]
    public void The_third_dismissal_takes_back_what_else_of_the_kind_was_open_without_counting_it()
    {
        var ledger = SuggestionLedger.Empty.Admit([Candidate("a", SuggestionKind.DoneByHand, 3), Candidate("b", SuggestionKind.DoneByHand, 2), Candidate("c", SuggestionKind.DoneByHand, 1)], Morning, Day).Ledger;
        var seeded = ledger with { Dismissals = ledger.Dismissals.Mutating(m => m[SuggestionKind.DoneByHand] = 2) };
        var off = seeded.Dismiss("a", Morning)!;
        Assert.True(off.IsOff(SuggestionKind.DoneByHand));
        Assert.Equal(3, off.DismissalsOf(SuggestionKind.DoneByHand));
        Assert.Empty(off.Open);
        Assert.Equal(SuggestionState.Dismissed, off.Today.Single(s => s.Key == "b").State);
    }

    [Fact]
    public void Dismissals_of_one_kind_do_not_count_against_another()
    {
        var ledger = SuggestionLedger.Empty.Admit([Candidate("a", SuggestionKind.IdleDuty, 3), Candidate("b", SuggestionKind.FailingCheck, 2)], Morning, Day).Ledger;
        ledger = ledger.Dismiss("a", Morning)!.Dismiss("b", Morning)!;
        Assert.Equal(1, ledger.DismissalsOf(SuggestionKind.IdleDuty));
        Assert.Equal(1, ledger.DismissalsOf(SuggestionKind.FailingCheck));
        Assert.Equal(0, ledger.DismissalsOf(SuggestionKind.AllowanceUnused));
    }

    [Fact]
    public void A_new_day_lets_yesterdays_open_suggestions_go_and_keeps_what_the_owner_taught()
    {
        var ledger = Offered("a", "b");
        ledger = ledger.Dismiss("a", Morning)!;
        var tomorrow = ledger.OnDay(Day.AddDays(1));
        Assert.Empty(tomorrow.Today);
        Assert.Equal(0, tomorrow.OfferedToday);
        Assert.Equal(1, tomorrow.DismissalsOf(SuggestionKind.AllowanceUnused));
        Assert.Contains("a", tomorrow.Settled);
        // Yesterday's open one was not answered, so it may be spotted again if it is still true.
        Assert.DoesNotContain("b", tomorrow.Settled);
        var (_, offered) = tomorrow.Admit([Candidate("b")], Morning.AddDays(1), Day.AddDays(1));
        Assert.Equal(["b"], offered.Select(s => s.Key));
    }

    [Fact]
    public void The_owner_turning_a_kind_back_on_starts_its_count_again()
    {
        var ledger = SuggestionLedger.Empty with
        {
            Day = Day,
            Dismissals = SuggestionLedger.Empty.Dismissals.Mutating(m => m[SuggestionKind.IdleDuty] = 3),
            Off = new Marrow.Core.Port.ValueList<SuggestionKind>([SuggestionKind.IdleDuty]),
        };
        var on = ledger.TurnOn(SuggestionKind.IdleDuty);
        Assert.False(on.IsOff(SuggestionKind.IdleDuty));
        Assert.Equal(0, on.DismissalsOf(SuggestionKind.IdleDuty));
        Assert.Same(on, on.TurnOn(SuggestionKind.IdleDuty));
    }

    [Fact]
    public void A_models_wording_replaces_the_line_and_the_cost_of_an_open_suggestion_and_never_the_plan()
    {
        var ledger = Offered("a", "b");
        var plan = ledger.Today[0].Plan;
        var reworded = ledger.Reworded("a", "A better line.", "About 3%.");
        Assert.Equal("A better line.", reworded.Today[0].Line);
        Assert.Equal("About 3%.", reworded.Today[0].Cost);
        Assert.Equal(plan, reworded.Today[0].Plan);
        Assert.Equal(ledger.Today[1], reworded.Today[1]);
        var answered = ledger.Accept("a", Morning)!.Reworded("a", "Late words.", "Late.");
        Assert.Equal("A line for a.", answered.Today[0].Line);
    }

    [Fact]
    public void The_answered_keys_are_kept_to_a_bound_and_the_oldest_go_first()
    {
        var ledger = SuggestionLedger.Empty.OnDay(Day);
        for (var i = 0; i < SuggestionLedger.SettledKept + 20; i++)
        {
            ledger = ledger.OnDay(Day.AddDays(i)).Admit([Candidate("k" + i)], Morning.AddDays(i), Day.AddDays(i)).Ledger;
            // The owner keeps turning the kind back on, so the lesson never stops this loop.
            ledger = ledger.Dismiss("k" + i, Morning.AddDays(i))!.TurnOn(SuggestionKind.AllowanceUnused);
        }
        Assert.Equal(SuggestionLedger.SettledKept, ledger.Settled.Count);
        Assert.DoesNotContain("k0", ledger.Settled);
        Assert.Contains("k" + (SuggestionLedger.SettledKept + 19), ledger.Settled);
    }

    [Fact]
    public void A_suggestion_from_a_private_chat_keeps_its_local_only_mark()
    {
        var candidate = Candidate("a") with { LocalOnly = true };
        var (ledger, offered) = SuggestionLedger.Empty.Admit([candidate], Morning, Day);
        Assert.True(offered[0].LocalOnly);
        Assert.True(ledger.Today[0].LocalOnly);
    }
}
