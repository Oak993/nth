using Marrow.Core.Port.Voice.Transcript;

namespace Marrow.Core.Tests.Port.Voice;

// VT-3 (voice-SPEC V-7.5, V-7.6, V-8.7). Numbers are the clauses': adds at least 60 ms apart; the final schedule starts 0.18 s after the
// result, adds 0.07 s apart and revisions 0.12 s apart; Processing lasts max(0.75 s, scheduleEnd + 0.3 s) from the click; tentative words ink
// 25 ms apart. The design's planFinal (voice-composer.js:305-323, read on unit/integ-vt3): `let at = now + 0.18; ... at += e.kind === 'add' ?
// 0.07 : 0.12; return at - now;` so the gap after an operation is that operation's own kind's spacing, and the schedule end is the slot after
// the last operation (0.18 s with nothing scheduled). Remove (not in the design) is spaced as a revision.
public sealed class TranscriptOpsTests
{
    private static TranscriptWord Solid(string text) => new(text, false);

    private static TranscriptWord Tent(string text) => new(text, true);

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T22")]
    [Trait("Decision", "ED-267")] // V-7.5: the difference between the shown tail and the new tail; a revised tail word is Revise(index, text)
    public void A_changed_tail_word_is_a_Revise_and_a_new_word_an_Add()
    {
        var shown = new[] { Solid("hello"), Tent("wor") };
        var wanted = new[] { Solid("hello"), Tent("world"), Tent("how") };
        Assert.Equal([WordOp.Revise(1, "world"), WordOp.Add(2, "how", true)], TranscriptOps.Diff(shown, wanted));
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T22")]
    [Trait("Decision", "ED-267")] // V-7.5: Confirm(index) turns a tentative word solid; an unchanged word needs no operation
    public void A_word_that_became_committed_is_a_Confirm_and_an_unchanged_one_is_nothing()
    {
        var shown = new[] { Tent("hello"), Tent("world"), Tent("how") };
        var wanted = new[] { Solid("hello"), Tent("world"), Tent("how") };
        Assert.Equal([WordOp.Confirm(0)], TranscriptOps.Diff(shown, wanted));
        Assert.Empty(TranscriptOps.Diff(wanted, wanted));
        Assert.Empty(TranscriptOps.Diff([], []));
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T22")]
    [Trait("Decision", "ED-267")] // V-7.5: Remove(index) for a tail word the new hypothesis dropped, last word first
    public void Dropped_tail_words_are_removed_from_the_end()
    {
        var shown = new[] { Solid("a"), Tent("b"), Tent("c"), Tent("d") };
        var wanted = new[] { Solid("a"), Tent("b") };
        Assert.Equal([WordOp.Remove(3), WordOp.Remove(2)], TranscriptOps.Diff(shown, wanted));
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T22")]
    [Trait("Decision", "ED-267")] // V-7.5: a word revised and committed in one step is a Revise then a Confirm
    public void A_revised_word_that_is_now_solid_is_a_Revise_then_a_Confirm()
    {
        var shown = new[] { Tent("tool") };
        var wanted = new[] { Solid("tooltip") };
        Assert.Equal([WordOp.Revise(0, "tooltip"), WordOp.Confirm(0)], TranscriptOps.Diff(shown, wanted));
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T25")]
    [Trait("Decision", "ED-267")] // V-7.5: successive adds at least 60 ms apart; other operations at once
    public void Live_adds_are_paced_60_ms_apart_and_other_operations_are_not_delayed()
    {
        var ops = new[] { WordOp.Revise(1, "world"), WordOp.Add(2, "how", true), WordOp.Add(3, "are", true), WordOp.Add(4, "you", true), WordOp.Confirm(0) };
        var paced = TranscriptOps.PaceLive(ops, null);
        Assert.Equal(S(0), paced.Single(p => p.Op.Kind == WordOpKind.Revise).At);
        Assert.Equal(S(0), paced.Single(p => p.Op.Kind == WordOpKind.Confirm).At);
        var adds = paced.Where(p => p.Op.Kind == WordOpKind.Add).Select(p => p.At).ToList();
        Assert.Equal([S(0), S(0.06), S(0.12)], adds);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T25")]
    [Trait("Decision", "ED-267")] // V-7.5: the first add of a batch is no sooner than 60 ms after the last add of the batch before
    public void The_first_add_of_a_batch_waits_for_the_spacing_since_the_last_add()
    {
        var paced = TranscriptOps.PaceLive([WordOp.Add(0, "a", true), WordOp.Add(1, "b", true)], S(0.02));
        Assert.Equal([S(0.04), S(0.10)], paced.Select(p => p.At).ToList());
        var settled = TranscriptOps.PaceLive([WordOp.Add(0, "a", true)], S(0.5));
        Assert.Equal(S(0), settled.Single().At);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T25")]
    [Trait("Decision", "ED-267")] // V-7.6: the schedule starts 0.18 s after the result; adds 0.07 s apart
    public void The_final_schedule_starts_0_18_s_after_the_result_with_adds_0_07_s_apart()
    {
        var plan = TranscriptOps.PlanFinal([Solid("a"), Tent("b")], [Solid("a"), Tent("b"), Tent("c"), Tent("d"), Tent("e")]);
        Assert.Equal([S(0.18), S(0.25), S(0.32)], plan.Ops.Select(p => p.At).ToList());
        Assert.Equal([WordOp.Add(2, "c", true), WordOp.Add(3, "d", true), WordOp.Add(4, "e", true)], plan.Ops.Select(p => p.Op).ToList());
        Assert.Equal(S(0.39), plan.ScheduleEnd); // the design's `at` after the last add: 0.32 + 0.07
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T25")]
    [Trait("Decision", "ED-267")] // V-7.6: revisions 0.12 s apart
    public void Final_revisions_are_0_12_s_apart()
    {
        var plan = TranscriptOps.PlanFinal([Solid("a"), Tent("tool"), Tent("team"), Tent("too")], [Solid("a"), Tent("tooltip"), Tent("theme"), Tent("too.")]);
        Assert.Equal([S(0.18), S(0.30), S(0.42)], plan.Ops.Select(p => p.At).ToList());
        Assert.All(plan.Ops, p => Assert.Equal(WordOpKind.Revise, p.Op.Kind));
        Assert.Equal(S(0.54), plan.ScheduleEnd); // 0.42 + 0.12
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T25")]
    [Trait("Decision", "ED-267")] // V-7.6, the design's planFinal: the gap after an operation is that operation's kind's (a revision: 0.12 s)
    public void A_mixed_final_schedule_spaces_after_each_operation_by_that_operations_kind()
    {
        var plan = TranscriptOps.PlanFinal([Solid("a"), Tent("tool")], [Solid("a"), Tent("tooltip"), Tent("works")]);
        Assert.Equal([WordOp.Revise(1, "tooltip"), WordOp.Add(2, "works", true)], plan.Ops.Select(p => p.Op).ToList());
        Assert.Equal([S(0.18), S(0.30)], plan.Ops.Select(p => p.At).ToList());
        Assert.Equal(S(0.37), plan.ScheduleEnd);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T25")]
    [Trait("Decision", "ED-267")] // fixture: the design's flow (SYN rng(91), STREAM rng(7)) clicked 7.6 s after the start, all live words written
    public void The_designs_flow_clicked_after_every_live_word_revises_the_last_word_to_its_full_stop()
    {
        var shown = "Add a shortcut for the theme switch, and show it in the tooltip".Split(' ').Select(Solid).Append(Tent("too")).ToArray();
        var final = "Add a shortcut for the theme switch, and show it in the tooltip too.".Split(' ').Select(Tent).ToArray();
        var plan = TranscriptOps.PlanFinal(shown, final);
        Assert.Equal([new ScheduledOp(S(0.18), WordOp.Revise(13, "too."))], plan.Ops);
        Assert.Equal(S(0.30), plan.ScheduleEnd); // planFinal returns 0.30; Processing max(0.75, 0.30 + 0.3) = 0.75
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T25")]
    [Trait("Decision", "ED-267")] // V-7.6: committed words in the final list are not operations; Confirm is Landing's business
    public void The_final_schedule_holds_no_Confirm_and_no_operation_for_unchanged_words()
    {
        var plan = TranscriptOps.PlanFinal([Solid("a"), Tent("b"), Tent("c")], [Solid("a"), Tent("b"), Tent("c")]);
        Assert.Empty(plan.Ops);
        Assert.Equal(S(0.18), plan.ScheduleEnd); // planFinal with an empty plan returns 0.18
    }

    [Theory]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T25")]
    [Trait("Decision", "ED-267")] // V-7.6: Processing lasts max(0.75 s, scheduleEnd + 0.3 s) from the click, the schedule starting after the result
    [InlineData(100, 320, 750)] // 0.10 + 0.32 + 0.3 = 0.72 < 0.75
    [InlineData(200, 320, 820)] // 0.20 + 0.32 + 0.3 = 0.82
    [InlineData(0, 0, 750)] // nothing to schedule: the minimum
    [InlineData(1500, 180, 1980)] // a slow pass: 1.5 + 0.18 + 0.3
    public void Processing_lasts_the_longer_of_0_75_s_and_the_schedule_end_plus_0_3_s(int resultAfterClickMs, int scheduleEndMs, int expectedMs)
    {
        var plan = new FinalPlan([], TimeSpan.FromMilliseconds(scheduleEndMs));
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), plan.ProcessingDuration(TimeSpan.FromMilliseconds(resultAfterClickMs)));
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T25")]
    [Trait("Decision", "ED-267")] // V-8.7: tentative words ink to solid 25 ms apart
    public void Landing_inks_each_tentative_word_25_ms_after_the_one_before()
    {
        var landing = TranscriptOps.PlanLanding([Solid("a"), Tent("b"), Tent("c"), Tent("d")]);
        Assert.Equal([WordOp.Confirm(1), WordOp.Confirm(2), WordOp.Confirm(3)], landing.Select(p => p.Op).ToList());
        Assert.Equal([S(0), S(0.025), S(0.05)], landing.Select(p => p.At).ToList());
        Assert.Empty(TranscriptOps.PlanLanding([Solid("a")]));
    }
}
