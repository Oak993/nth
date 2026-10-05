using Marrow.App.Port.Voice;
using Marrow.App.Views.Composer;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;
using Xunit;
using static Marrow.App.Tests.Fixtures.VoicePillProbe;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Views.Composer;

// VT-12, V-T125 (voice-SPEC V-12.9; OD-0097 L1 to L6): the pill's words. The table is written out here from the spec (U+2026 as …; L4's "Speak now" and all of L6 have none).
[Collection("Headless")]
public sealed class ComposerVoicePillWordsTests(HeadlessSession headless)
{
    private const string E = "…";

    public static TheoryData<VoicePillWords, string, string, string> Sets => new()
    {
        { VoicePillWords.L1, "Listening" + E, "Transcribing" + E, "Finishing" + E },
        { VoicePillWords.L2, "Listening" + E, "Writing" + E, "Finishing" + E },
        { VoicePillWords.L3, "Hearing you" + E, "Writing" + E, "Tidying up" + E },
        { VoicePillWords.L4, "Speak now", "Transcribing" + E, "Almost done" + E },
        { VoicePillWords.L5, "Listening" + E, "Typing" + E, "Wrapping up" + E },
        { VoicePillWords.L6, "Listening", "Transcribing", "Finishing" },
    };

    [Theory] // V-T125: each set's words in the pill per state, inline and float, and the open width from the shown words
    [MemberData(nameof(Sets))]
    [Trait("Decision", "ED-292")]
    public void Each_set_shows_its_words_per_state_in_both_pills_and_the_open_width_follows_them(VoicePillWords set, string listening, string transcribing, string finishing)
    {
        headless.OnUiThread(() =>
        {
            foreach (var style in new[] { VoiceListeningStyle.Pill, VoiceListeningStyle.Float })
            {
                using var rig = new VoiceComposerRig();
                var (pill, window) = Standalone(rig, style);
                var height = style == VoiceListeningStyle.Float ? 44 : 40;
                var size = style == VoiceListeningStyle.Float ? 15 : 13.5;
                rig.Main.OnMain(() =>
                {
                    pill.Words = set;
                    pill.Open = true;
                });
                var words = new[] { listening, transcribing, finishing };
                var states = new[] { VoiceLabelState.Listening, VoiceLabelState.Transcribing, VoiceLabelState.Finishing };
                for (var i = 0; i < 3; i++)
                {
                    rig.Main.OnMain(() => pill.State = states[i]);
                    Run(rig, window, 0.8);
                    rig.Main.OnMain(() =>
                    {
                        Assert.Equal([new VoicePillRun(words[i], 600)], pill.Labels[i].Runs);
                        Assert.Equal(1, pill.LabelOpacity(states[i]));
                        Assert.Equal(OpenWidth(height, words[i], size), pill.ShownWidth, 0.01);
                    });
                }
                rig.Close(window);
            }
        });
    }

    [Fact] // V-T125: a shorter set gives a narrower pill (L6 against L1 by the measured difference of their words)
    [Trait("Decision", "ED-292")]
    public void L6_is_narrower_than_L1_by_the_measured_difference_of_the_words()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig();
            var (pill, window) = Standalone(rig);
            rig.Main.OnMain(() => pill.Open = true);
            Run(rig, window, 0.8);
            var l1 = rig.Main.OnMain(() => pill.ShownWidth);
            rig.Main.OnMain(() => pill.Words = VoicePillWords.L6);
            Run(rig, window, 1.2);
            var l6 = rig.Main.OnMain(() => pill.ShownWidth);
            Assert.True(l6 < l1);
            Assert.Equal(Measure("Listening" + E, 13.5, true) - Measure("Listening", 13.5, true), l1 - l6, 0.01);
            rig.Close(window);
        });
    }

    [Fact] // V-T125: a change while open fades the label out over 0.14 s, swaps at 0.16 s, fades in (0.26 s after 0.14 s) and eases the width over 0.42 s ez, each frame against the curves
    [Trait("Decision", "ED-292")]
    public void A_change_while_open_fades_the_label_out_swaps_the_words_at_0_16_and_fades_in_while_the_width_eases()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig();
            var (pill, window) = Standalone(rig);
            rig.Main.OnMain(() => pill.Open = true);
            Run(rig, window, 1.2);
            var from = rig.Main.OnMain(() => pill.ShownWidth);
            var to = OpenWidth(40, "Hearing you" + E, 13.5);
            rig.Main.OnMain(() => pill.Words = VoicePillWords.L3);
            var seen = Frames(rig, window, 60, () => (Words: pill.Labels[0].Words, Label: pill.LabelOpacity(VoiceLabelState.Listening), Width: pill.ShownWidth));
            var swapped = seen.FindIndex(s => s.Words == "Hearing you" + E);
            Assert.True(swapped >= 0, "the words were replaced");
            Assert.InRange(swapped * FrameTime, 0.16 - 1e-6, 0.16 + FrameTime + 1e-6); // 0.16 s after the change
            for (var k = 0; k < swapped; k++)
            {
                Assert.Equal("Listening" + E, seen[k].Words); // the old words are still there while they fade
                Assert.Equal(1 - CssCurves.At(CssCurves.Ease, k * FrameTime, 0.14), seen[k].Label, 1e-4); // out over 0.14 s
                Assert.Equal(from, seen[k].Width, 1e-3); // the width has not moved yet
            }
            // after the swap: its own crossfade (opacity .26s ease after .14s) and the width .42s ez, the start where the swap happened (the timer's due time or the frame that ran it)
            var starts = new[] { 0.16, swapped * FrameTime };
            var fits = starts.Where(start => Enumerable.Range(swapped, seen.Count - swapped).All(k =>
                Math.Abs(seen[k].Label - CssCurves.At(CssCurves.Ease, k * FrameTime - start - 0.14, 0.26)) < 2e-3
                && Math.Abs(seen[k].Width - (from + (to - from) * CssCurves.At(CssCurves.Ez, k * FrameTime - start, 0.42))) < 0.05)).ToList();
            Assert.True(fits.Count > 0, "the new label fades in and the width eases from the swap");
            Assert.Equal(1, seen[^1].Label, 1e-6);
            rig.Close(window);
        });
    }

    [Fact] // V-T125: a change while closed applies at the next opening
    [Trait("Decision", "ED-292")]
    public void A_change_while_closed_applies_at_the_next_opening()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig();
            var (pill, window) = Standalone(rig);
            rig.Main.OnMain(() => pill.Words = VoicePillWords.L3);
            var seen = Frames(rig, window, 20, () => (pill.LabelOpacity(VoiceLabelState.Listening), pill.ShownWidth, pill.ShownOpacity));
            Assert.All(seen, s => Assert.Equal((0.0, 40.0, 0.0), (s.Item1, s.Item2, s.Item3))); // nothing moves
            rig.Main.OnMain(() => pill.Open = true);
            Run(rig, window, 1.2);
            rig.Main.OnMain(() => Assert.Equal("Hearing you" + E, pill.Labels[0].Words));
            Assert.Equal(OpenWidth(40, "Hearing you" + E, 13.5), rig.Main.OnMain(() => pill.ShownWidth), 0.01);
            rig.Close(window);
        });
    }

    [Fact] // V-T125, V-12.9 / V-14.2 / V-14.5: announcements and the orb's names keep their wording for every set, and the Cross style draws no pill and no words
    [Trait("Decision", "ED-292")]
    public void Announcements_and_orb_names_do_not_follow_the_words_and_the_cross_style_draws_none()
    {
        headless.OnUiThread(() =>
        {
            using var rig = ComposerVoicePillGeometryTests.Quiet(VoiceOrbSettings.Default with { Labels = VoicePillWords.L3, ListeningStyle = VoiceListeningStyle.Cross });
            var (view, window) = rig.Open(900);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            var opacities = Frames(rig, window, 60, () => (Pill(view).ShownOpacity, Pill(view).LabelOpacity(VoiceLabelState.Listening), Pill(view).Open));
            Assert.All(opacities, o => Assert.Equal((0.0, 0.0, false), o)); // no pill, no words
            rig.Main.OnMain(() =>
            {
                Assert.Equal(VoiceAnnouncements.Listening, view.Voice!.Announcement);
                Assert.Equal("Stop and finish the text", view.Voice.OrbName);
            });
            rig.Close(window);
        });
    }
}
