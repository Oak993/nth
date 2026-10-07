using Marrow.App.Port.Voice;
using Marrow.App.Views.Composer;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Session;
using Xunit;
using static Marrow.App.Tests.Fixtures.VoicePillProbe;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Views.Composer;

// VT-12, V-T126 (voice-SPEC V-12.10; OD-0097 B and its amendment, no separator dot): the language named in the pill. Absent until the scripted engine reports a language and never before the label first shows the
// transcribing state; then the words + U+2002 + the name at weight 400 in one run of the label, no U+00B7 or other separator anywhere; the width includes it; it stays to the end and leaves with the fold; a different later
// language crossfades; never with a forced language; nothing in the Cross style.
[Collection("Headless")]
public sealed class ComposerVoiceLanguageNameTests(HeadlessSession headless)
{
    private const string E = "…";

    private static VoiceScript Said(string? language) => new(
    [
        new ScriptedWord("switch", 0.5, 0.9),
        new ScriptedWord("the", 1.0, 1.2),
        new ScriptedWord("theme", 1.3, 1.8),
        new ScriptedWord("please.", 2.0, 2.5),
    ], language);

    private static VoiceComposerRig Named(string? language, VoiceListeningStyle style = VoiceListeningStyle.Pill, string? forced = null)
    {
        var rig = new VoiceComposerRig(VoiceOrbSettings.Default with { LangMode = VoiceLangMode.Show, ListeningStyle = style }, VoiceTypingSettings.Default with { Lang = forced });
        rig.Main.OnMain(() => rig.App.Settings.VoiceNotes = new VoiceNotesSettings(Local: true, Cloud: true));
        rig.Speak(Said(language));
        return rig;
    }

    private static string AllRuns(VoicePill pill) => string.Concat(pill.Labels.SelectMany(label => label.Runs.Select(run => run.Text)));

    [Fact] // V-T126
    [Trait("Decision", "ED-292")]
    public void The_name_is_absent_until_the_engine_reports_a_language_and_never_before_the_label_first_shows_transcribing()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Named("en");
            var (view, window) = rig.Open(900);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            var firstTranscribing = -1;
            var firstName = -1;
            var frames = Frames(rig, window, 200, () => (Label: Pill(view).State, Runs: AllRuns(Pill(view))));
            for (var k = 0; k < frames.Count; k++)
            {
                if (firstTranscribing < 0 && frames[k].Label == VoiceLabelState.Transcribing) firstTranscribing = k;
                if (firstName < 0 && frames[k].Runs.Contains("English", StringComparison.Ordinal)) firstName = k;
            }
            Assert.True(firstTranscribing > 0, "the label showed Transcribing");
            Assert.True(firstName > 0, "the language was named");
            Assert.True(firstName >= firstTranscribing, "never before the label first shows the transcribing state");
            // the engine reports only after its first live pass (the word arrives 0.4 s after it ends): the name is not there at the start
            Assert.DoesNotContain("English", frames[10].Runs, StringComparison.Ordinal);
            rig.Close(window);
        });
    }

    [Fact] // V-T126: the runs, the width, no separator, it stays to the end and leaves with the fold
    [Trait("Decision", "ED-292")]
    public void Once_heard_the_name_follows_the_words_at_weight_400_in_one_label_with_no_separator_and_the_width_includes_it_and_it_leaves_with_the_fold()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Named("en");
            var (view, window) = rig.Open(900);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            Run(rig, window, 3.2); // the words were said, the name is known
            rig.Main.OnMain(() =>
            {
                var pill = Pill(view);
                var shown = pill.Labels[(int)pill.State];
                Assert.Equal([new VoicePillRun(shown.Words, 600), new VoicePillRun(" English", 400)], shown.Runs);
                foreach (var label in pill.Labels) Assert.Equal([new VoicePillRun(label.Words, 600), new VoicePillRun(" English", 400)], label.Runs); // with the listening and finishing words too
                var text = AllRuns(pill);
                Assert.DoesNotContain("·", text, StringComparison.Ordinal);
                Assert.DoesNotContain("•", text, StringComparison.Ordinal);
                Assert.DoesNotContain("|", text, StringComparison.Ordinal);
            });
            var state = rig.Main.OnMain(() => Pill(view).State);
            var words = rig.Main.OnMain(() => Pill(view).Labels[(int)state].Words);
            Assert.Equal(OpenWidth(40, words, 13.5, "English"), rig.Main.OnMain(() => Pill(view).TargetWidth), 0.01);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget)); // stop: the name stays through Finishing
            Run(rig, window, 0.3);
            rig.Main.OnMain(() => Assert.Contains("English", AllRuns(Pill(view)), StringComparison.Ordinal));
            Run(rig, window, 4); // landed and folded
            rig.Main.OnMain(() =>
            {
                Assert.False(Pill(view).Open);
                Assert.DoesNotContain("English", AllRuns(Pill(view)), StringComparison.Ordinal);
            });
            rig.Close(window);
        });
    }

    [Fact] // V-T126: the name fades in over 0.3 s after 0.1 s when it arrives during a label, with the label's own crossfade when it arrives as the label changes
    [Trait("Decision", "ED-292")]
    public void The_name_fades_in_after_0_1_over_0_3_or_arrives_with_a_label_that_changes_in_that_moment()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig();
            var (pill, window) = Standalone(rig);
            rig.Main.OnMain(() =>
            {
                pill.Open = true;
                pill.State = VoiceLabelState.Transcribing;
            });
            Run(rig, window, 1.2);
            rig.Main.OnMain(() => pill.Language = "English");
            var seen = Frames(rig, window, 30, () => (pill.NameOpacity, pill.Labels[1].Runs.Count, pill.TargetWidth));
            for (var k = 0; k < seen.Count; k++) Assert.Equal(CssCurves.At(CssCurves.Ease, k * FrameTime - 0.1, 0.3), seen[k].NameOpacity, 1e-4);
            Assert.Equal(2, seen[0].Count); // in the layout from the first frame, drawn clear
            Assert.Equal(OpenWidth(40, "Transcribing" + E, 13.5, "English"), seen[0].TargetWidth, 0.01);
            // with the label: the label changes and the name arrives in the same moment
            rig.Main.OnMain(() => pill.Language = null);
            Run(rig, window, 0.2);
            rig.Main.OnMain(() =>
            {
                pill.State = VoiceLabelState.Finishing;
                pill.Language = "English";
            });
            Assert.Equal(1, rig.Main.OnMain(() => pill.NameOpacity)); // the label's own crossfade carries it
            rig.Close(window);
        });
    }

    [Fact] // V-T126: a different later language crossfades (out 0.1 s, in 0.2 s after 0.08 s) and the width eases
    [Trait("Decision", "ED-292")]
    public void A_different_later_language_crossfades_and_the_width_eases()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig();
            var (pill, window) = Standalone(rig);
            rig.Main.OnMain(() =>
            {
                pill.Open = true;
                pill.State = VoiceLabelState.Transcribing;
                pill.Language = "English";
            });
            Run(rig, window, 1.5);
            var from = rig.Main.OnMain(() => pill.ShownWidth);
            rig.Main.OnMain(() => pill.Language = "Hindi");
            var to = OpenWidth(40, "Transcribing" + E, 13.5, "Hindi");
            var seen = Frames(rig, window, 40, () => (Out: pill.Labels[1].OutgoingOpacity, In: pill.NameOpacity, Out2: pill.Labels[1].OutgoingName, Name: pill.Labels[1].LanguageName, Width: pill.ShownWidth));
            for (var k = 0; k < seen.Count; k++)
            {
                var t = k * FrameTime;
                Assert.Equal(1 - CssCurves.At(CssCurves.Ease, t, 0.1), seen[k].Out, 1e-4);
                Assert.Equal(CssCurves.At(CssCurves.Ease, t - 0.08, 0.2), seen[k].In, 1e-4);
                Assert.Equal("Hindi", seen[k].Name);
                Assert.Equal(from + (to - from) * CssCurves.At(CssCurves.Ez, t, 0.42), seen[k].Width, 1e-2);
            }
            Assert.Equal("English", seen[0].Out2);
            Assert.Null(seen[^1].Out2); // gone once it has faded
            rig.Close(window);
        });
    }

    [Theory] // V-T126: never with a forced language, nor under the other defaults
    [InlineData("en")]
    [InlineData("hi")]
    [Trait("Decision", "ED-292")]
    public void A_forced_language_is_never_named(string forced)
    {
        headless.OnUiThread(() =>
        {
            using var rig = Named(forced, forced: forced);
            var (view, window) = rig.Open(900);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            var frames = Frames(rig, window, 200, () => AllRuns(Pill(view)));
            Assert.All(frames, runs => Assert.DoesNotContain("English", runs, StringComparison.Ordinal));
            Assert.All(frames, runs => Assert.DoesNotContain("Hindi", runs, StringComparison.Ordinal));
            rig.Close(window);
        });
    }

    [Fact] // V-T126: nothing in the Cross style
    [Trait("Decision", "ED-292")]
    public void The_cross_style_shows_no_name()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Named("en", VoiceListeningStyle.Cross);
            var (view, window) = rig.Open(900);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            var frames = Frames(rig, window, 200, () => (AllRuns(Pill(view)), Pill(view).ShownOpacity));
            Assert.All(frames, f =>
            {
                Assert.DoesNotContain("English", f.Item1, StringComparison.Ordinal);
                Assert.Equal(0, f.Item2);
            });
            rig.Close(window);
        });
    }
}
