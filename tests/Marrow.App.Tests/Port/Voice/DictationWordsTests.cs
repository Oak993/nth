using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice.Transcript;
using Xunit;

namespace Marrow.App.Tests.Port.Voice;

// VT-11, V-T76 to V-T79 (voice-SPEC V-7.5, V-8.4, V-8.6, V-8.7; the design's voice-composer.html .txt .w rules): the motion of the dictated words, as a model on instants (no window, no
// clock). Every frame is tested against the design's curve itself (CssCurves, solved the way a browser solves it), not against a share of the travel it may cover in one frame.
// The design's numbers: a word rises in (opacity .3 s ease, translate 6 px to 0 over .4 s ez), the tentative colour turns solid (.45 s ease), a revision lifts the old text away
// (opacity .22 s ease, 0 to -6 px over .3 s ez, gone at .34 s) while the new rises in (the same, from +6 px), a word leaves (opacity .26 s ease, 5 px down over .3 s ez, gone at .34 s).
public sealed class DictationWordsTests
{
    private const double Frame = 1.0 / 60;

    private static ContinuousInstant T(double seconds) => new((long)Math.Round(seconds * TimeSpan.TicksPerSecond));

    private static DictationWords WithWords(params string[] texts)
    {
        var words = new DictationWords();
        for (var i = 0; i < texts.Length; i++) words.Apply(WordOp.Add(i, texts[i], tentative: false), T(-10));
        words.Settle(T(-5));
        return words;
    }

    private static void Frames(double from, double to, Action<double> check)
    {
        for (var k = 0; from + k * Frame <= to + 1e-9; k++) check(from + k * Frame);
    }

    private static void Near(double expected, double actual, string what) =>
        // a frame's instant is a whole number of 100 ns ticks and the curve is steep at its start: 1e-5 is what a tick can move it
        Assert.True(Math.Abs(expected - actual) < 1e-5, $"{what}: {actual} against the curve's {expected}");

    [Fact] // V-T76, V-7.5: Add: opacity .3 s ease, translate Y 6 to 0 over .4 s ez; its first frame is the hidden state
    [Trait("Decision", "ED-292")]
    public void An_added_word_rises_in_on_the_designs_curves_frame_by_frame()
    {
        var words = new DictationWords();
        words.Apply(WordOp.Add(0, "switch", tentative: true), T(1));
        var word = Assert.Single(words.Words);
        var first = words.Look(word, T(1));
        Assert.Equal(0, first.Opacity);
        Assert.Equal(6, first.Rise);
        Frames(1, 1.6, t =>
        {
            var look = words.Look(word, T(t));
            Near(CssCurves.At(CssCurves.Ease, t - 1, 0.3), look.Opacity, $"opacity at {t - 1:F3} s");
            Near(6 * (1 - CssCurves.At(CssCurves.Ez, t - 1, 0.4)), look.Rise, $"rise at {t - 1:F3} s");
        });
        var done = words.Look(word, T(1.4));
        Assert.Equal(1, done.Opacity);
        Assert.Equal(0, done.Rise);
        Assert.False(words.IsMoving(T(1.5)) && words.Look(word, T(1.5)).Opacity < 1);
    }

    [Fact] // V-8.4: a tentative word is drawn at the text colour's 0.6; a word added solid is solid at once
    [Trait("Decision", "ED-292")]
    public void A_tentative_word_is_light_and_a_solid_one_is_not()
    {
        var words = new DictationWords();
        words.Apply(WordOp.Add(0, "a", tentative: true), T(0));
        words.Apply(WordOp.Add(1, "b", tentative: false), T(0));
        Assert.Equal(1, words.Look(words.Words[0], T(0.5)).Tentative);
        Assert.Equal(0, words.Look(words.Words[1], T(0.5)).Tentative);
        Assert.Equal(0.6, DictationInk.TentativeAlpha);
        Assert.Equal(0.6, DictationInk.AlphaOf(1));
        Assert.Equal(1, DictationInk.AlphaOf(0));
        Assert.Equal(0.8, DictationInk.AlphaOf(0.5), 12);
    }

    [Fact] // V-7.5 Confirm, V-8.7: tentative colour to solid over .45 s ease
    [Trait("Decision", "ED-292")]
    public void Confirm_turns_the_tentative_colour_solid_over_0_45_s_on_ease_frame_by_frame()
    {
        var words = new DictationWords();
        words.Apply(WordOp.Add(0, "theme", tentative: true), T(0));
        words.Apply(WordOp.Confirm(0), T(2));
        var word = words.Words[0];
        Near(1, words.Look(word, T(2)).Tentative, "at the confirm");
        Frames(2, 2.6, t => Near(1 - CssCurves.At(CssCurves.Ease, t - 2, 0.45), words.Look(word, T(t)).Tentative, $"colour at {t - 2:F3} s"));
        Assert.Equal(0, words.Look(word, T(2.45)).Tentative);
    }

    [Fact] // V-8.7: the landing's ink: tentative words turn solid 25 ms apart (the plan's times), each on its own .45 s
    [Trait("Decision", "ED-292")]
    public void Landing_inks_each_word_on_its_own_start_so_the_colour_wave_runs_25_ms_apart()
    {
        var words = new DictationWords();
        for (var i = 0; i < 4; i++) words.Apply(WordOp.Add(i, "w" + i, tentative: true), T(0));
        var starts = new[] { 3.0, 3.025, 3.05, 3.075 };
        for (var i = 0; i < 4; i++) words.Apply(WordOp.Confirm(i), T(starts[i]));
        Frames(3, 3.6, t =>
        {
            for (var i = 0; i < 4; i++)
                Near(1 - CssCurves.At(CssCurves.Ease, t - starts[i], 0.45), words.Look(words.Words[i], T(t)).Tentative, $"word {i} at {t - 3:F3} s");
        });
    }

    [Fact] // V-7.5 Revise: the old text lifts away, the new rises in; both on the design's curves
    [Trait("Decision", "ED-292")]
    public void A_revision_lifts_the_old_text_away_while_the_new_text_rises_in_frame_by_frame()
    {
        var words = WithWords("add", "team");
        var word = words.Words[1];
        words.Apply(WordOp.Revise(1, "theme"), T(1));
        Assert.Equal("theme", word.Text);
        Frames(1, 1.33, t =>
        {
            var look = words.Look(word, T(t));
            var old = Assert.Single(look.Old);
            Assert.Equal("team", old.Text);
            Near(1 - CssCurves.At(CssCurves.Ease, t - 1, 0.22), old.Opacity, $"old opacity at {t - 1:F3} s");
            Near(-6 * CssCurves.At(CssCurves.Ez, t - 1, 0.3), old.Rise, $"old rise at {t - 1:F3} s");
            Near(CssCurves.At(CssCurves.Ease, t - 1, 0.22), look.TextOpacity, $"new opacity at {t - 1:F3} s");
            Near(6 * (1 - CssCurves.At(CssCurves.Ez, t - 1, 0.3)), look.TextRise, $"new rise at {t - 1:F3} s");
            Assert.Equal(1, look.Opacity); // the word itself stays where it is
        });
        Assert.Empty(words.Look(word, T(1.35)).Old); // the old text is gone at .34 s
        Assert.Equal(1, words.Look(word, T(1.35)).TextOpacity);
        Assert.Equal(0, words.Look(word, T(1.35)).TextRise);
    }

    [Fact] // V-7.5: a revision to the same text is no operation; a revision keeps the word's tentative state
    [Trait("Decision", "ED-292")]
    public void A_revision_to_the_same_text_does_nothing_and_a_revision_keeps_the_colour()
    {
        var words = new DictationWords();
        words.Apply(WordOp.Add(0, "team", tentative: true), T(0));
        words.Settle(T(1));
        words.Apply(WordOp.Revise(0, "team"), T(1));
        Assert.Empty(words.Look(words.Words[0], T(1.05)).Old);
        words.Apply(WordOp.Revise(0, "theme"), T(1.1));
        Assert.Equal(1, words.Look(words.Words[0], T(1.2)).Tentative);
    }

    [Fact] // V-7.5 (the design reviseWord): a second revision while the first is still showing keeps the first old text until its own .34 s
    [Trait("Decision", "ED-292")]
    public void A_second_revision_keeps_the_first_old_text_fading_until_its_own_time()
    {
        var words = WithWords("team");
        words.Apply(WordOp.Revise(0, "tool"), T(1));
        words.Apply(WordOp.Revise(0, "tooltip"), T(1.1));
        var look = words.Look(words.Words[0], T(1.2));
        Assert.Equal(["team", "tool"], look.Old.Select(o => o.Text));
        Assert.Equal(["tool"], words.Look(words.Words[0], T(1.35)).Old.Select(o => o.Text)); // the first one's .34 s has passed (1.34)
        Assert.Empty(words.Look(words.Words[0], T(1.5)).Old);
        Assert.Equal("tooltip", words.Words[0].Text);
    }

    [Fact] // V-8.6, V-7.5 Remove: opacity .26 s ease to 0, 5 px down over .3 s ez, gone at .34 s
    [Trait("Decision", "ED-292")]
    public void A_removed_word_leaves_on_the_withdraw_curves_frame_by_frame_and_is_gone_at_0_34_s()
    {
        var words = WithWords("one", "two");
        var word = words.Words[1];
        words.Apply(WordOp.Remove(1), T(2));
        Assert.Equal(1, words.Count); // the logical words: the leaver is not one of them
        Assert.Equal(2, words.Words.Count); // but it still takes its place until it is gone
        Frames(2, 2.33, t =>
        {
            var look = words.Look(word, T(t));
            Near(1 - CssCurves.At(CssCurves.Ease, t - 2, 0.26), look.Opacity, $"opacity at {t - 2:F3} s");
            Near(5 * CssCurves.At(CssCurves.Ez, t - 2, 0.3), look.Rise, $"rise at {t - 2:F3} s");
        });
        Assert.False(words.Settle(T(2.339)));
        Assert.Equal(2, words.Words.Count);
        Assert.True(words.Settle(T(2.34))); // the layout changes: the word has left it
        Assert.Single(words.Words);
        Assert.False(words.Settle(T(2.5)));
    }

    [Fact] // V-8.6: the withdraw's stagger: the words leave from the end, 30 ms apart, each on its own start
    [Trait("Decision", "ED-292")]
    public void A_withdraw_takes_the_words_from_the_end_30_ms_apart()
    {
        var words = WithWords("a", "b", "c");
        var plan = TranscriptOps.PlanWithdraw(3);
        var originals = words.Words.ToList();
        foreach (var scheduled in plan) words.Apply(scheduled.Op, T(5) + scheduled.At);
        Assert.Equal(0, words.Count);
        Frames(5, 5.5, t =>
        {
            for (var k = 0; k < 3; k++)
            {
                var start = 5 + k * 0.03;
                var word = originals[2 - k];
                if (!words.Words.Contains(word)) continue;
                var look = words.Look(word, T(t));
                Near(1 - CssCurves.At(CssCurves.Ease, t - start, 0.26), look.Opacity, $"word {2 - k} opacity at {t - 5:F3} s");
                Near(5 * CssCurves.At(CssCurves.Ez, t - start, 0.3), look.Rise, $"word {2 - k} rise at {t - 5:F3} s");
            }
        });
        Assert.True(words.Settle(T(5.4)));
        Assert.Empty(words.Words); // the last one started at .06 s and left at .40 s
    }

    [Fact] // V-7.5: a value retargeted in flight goes on from where it is (the browser's rule), so no frame jumps back
    [Trait("Decision", "ED-292")]
    public void A_word_removed_while_it_is_still_rising_goes_on_from_where_it_is()
    {
        var words = new DictationWords();
        words.Apply(WordOp.Add(0, "x", tentative: true), T(0));
        var word = words.Words[0];
        var risen = CssCurves.At(CssCurves.Ease, 0.1, 0.3);
        var before = words.Look(word, T(0.1));
        Near(risen, before.Opacity, "opacity just before the removal");
        words.Apply(WordOp.Remove(0), T(0.1));
        Near(risen, words.Look(word, T(0.1)).Opacity, "opacity at the removal");
        Frames(0.1, 0.4, t => Near(risen * (1 - CssCurves.At(CssCurves.Ease, t - 0.1, 0.26)), words.Look(word, T(t)).Opacity, $"opacity at {t - 0.1:F3} s after it"));
    }

    [Fact] // V-7.5: a confirm of a word whose colour is already turning does not restart it
    [Trait("Decision", "ED-292")]
    public void A_second_confirm_of_a_word_does_not_restart_its_colour()
    {
        var words = new DictationWords();
        words.Apply(WordOp.Add(0, "y", tentative: true), T(0));
        var y = words.Words[0];
        words.Apply(WordOp.Confirm(0), T(1));
        var mid = words.Look(y, T(1.2)).Tentative;
        Near(1 - CssCurves.At(CssCurves.Ease, 0.2, 0.45), mid, "mid-turn");
        words.Apply(WordOp.Confirm(0), T(1.2));
        Near(mid, words.Look(y, T(1.2)).Tentative, "at the second confirm");
        Near(1 - CssCurves.At(CssCurves.Ease, 0.3, 0.45), words.Look(y, T(1.3)).Tentative, "later on the first one's curve");
    }

    [Fact] // V-7.5: operations name words by their place among the logical words; a leaver is not one
    [Trait("Decision", "ED-292")]
    public void Operations_name_words_by_their_place_among_the_logical_words()
    {
        var words = WithWords("one", "two", "three");
        words.Apply(WordOp.Remove(2), T(1));
        words.Apply(WordOp.Revise(1, "TWO"), T(1.1));
        words.Apply(WordOp.Add(2, "four", tentative: true), T(1.2));
        Assert.Equal(["one", "TWO", "three", "four"], words.Words.Select(w => w.Text)); // "three" is still leaving; "four" comes after it
        Assert.Equal(3, words.Count);
        Assert.Equal(["one", "TWO", "four"], words.Logical.Select(w => w.Text));
        words.Settle(T(2));
        Assert.Equal(["one", "TWO", "four"], words.Words.Select(w => w.Text));
    }

    [Fact] // V-7.5: an add whose index is not the next place is taken as the next place; an operation on a place that is not there does nothing
    [Trait("Decision", "ED-292")]
    public void Operations_that_name_a_place_that_is_not_there_never_throw()
    {
        var words = WithWords("one");
        words.Apply(WordOp.Add(7, "two", tentative: false), T(0));
        words.Apply(WordOp.Revise(9, "x"), T(0));
        words.Apply(WordOp.Remove(9), T(0));
        words.Apply(WordOp.Confirm(-1), T(0));
        Assert.Equal(["one", "two"], words.Words.Select(w => w.Text));
    }

    [Fact] // OD-0076 and the design's reduced motion: every transition lands at once
    [Trait("Decision", "ED-292")]
    public void Under_reduce_motion_every_transition_lands_at_once()
    {
        var words = new DictationWords { Still = true };
        words.Apply(WordOp.Add(0, "team", tentative: true), T(0));
        var word = words.Words[0];
        var added = words.Look(word, T(0));
        Assert.Equal(1, added.Opacity);
        Assert.Equal(0, added.Rise);
        words.Apply(WordOp.Revise(0, "theme"), T(1));
        var revised = words.Look(word, T(1));
        Assert.Empty(revised.Old);
        Assert.Equal(1, revised.TextOpacity);
        Assert.Equal(0, revised.TextRise);
        words.Apply(WordOp.Confirm(0), T(2));
        Assert.Equal(0, words.Look(word, T(2)).Tentative);
        words.Apply(WordOp.Remove(0), T(3));
        Assert.True(words.Settle(T(3)));
        Assert.Empty(words.Words);
    }

    [Fact] // the model asks for frames only while something moves
    [Trait("Decision", "ED-292")]
    public void The_model_is_moving_only_while_a_transition_runs()
    {
        var words = new DictationWords();
        Assert.False(words.IsMoving(T(0)));
        words.Apply(WordOp.Add(0, "x", tentative: true), T(0));
        Assert.True(words.IsMoving(T(0.1)));
        Assert.True(words.IsMoving(T(0.39)));
        Assert.False(words.IsMoving(T(0.41)));
        words.Apply(WordOp.Confirm(0), T(1));
        Assert.True(words.IsMoving(T(1.4)));
        Assert.False(words.IsMoving(T(1.46)));
        words.Apply(WordOp.Revise(0, "y"), T(2));
        Assert.True(words.IsMoving(T(2.33)));
        Assert.False(words.IsMoving(T(2.35)));
    }
}
