using Avalonia.Controls;
using Avalonia.Input;
using Marrow.App.Port.Voice;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Session;
using Xunit;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Views.Composer;

// VT-10, V-T83 (voice-SPEC V-14.6; OD-0099 Q-V-7 A and Q-V-8 A; OD-0106 Notes set A, exactly one link each): the note layer. A note crossfades with the placeholder
// in the field (in 0.26 s after 0.05 s, rising 3 px over 0.38 s; out 0.2 s and 0.3 s), or, when the draft holds text, shows in the composer's notice line instead
// (never hide the user's text). The first use of each engine shows its privacy line for 5 s, once.
[Collection("Headless")]
public sealed class ComposerVoiceNotesTests(HeadlessSession headless)
{
    private static IReadOnlyList<VoiceNoteLine> Lines(Control view) => LogicalQuery.SelfAndDescendants<VoiceNoteLine>(view).ToList();

    private static void StartThenCancel(VoiceComposerRig rig, ComposerView view, Avalonia.Controls.Window window)
    {
        rig.Main.OnMain(() => Click(Slot(view).HitTarget));
        rig.Elapse(window, 0.3);
        Assert.True(rig.Main.OnMain(() => Press(view, Key.Escape)));
        rig.Elapse(window, 0.05);
    }

    private static VoiceOrbSlot Slot(Control view) => LogicalQuery.SelfAndDescendants<VoiceOrbSlot>(view).Single();

    // Past the privacy line the first use of an engine shows: the settings say it was shown already.
    private static VoiceComposerRig Seen() => new();

    private static void MarkPrivacySeen(VoiceComposerRig rig) =>
        rig.Main.OnMain(() => rig.App.Settings.VoiceNotes = new VoiceNotesSettings(Local: true, Cloud: true));

    [Fact] // V-T83: cancelled, nothing written
    [Trait("Rule", "R-TEST-10")]
    public void Cancelling_with_nothing_written_shows_Voice_input_cancelled_in_the_field_for_1_6_s_over_the_placeholder()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            MarkPrivacySeen(rig);
            rig.QuietRoom();
            var (view, window) = rig.Open();
            var box = rig.Main.OnMain(() => One<ComposerNSTextView>(view));
            Assert.Equal(1, rig.Main.OnMain(() => box.PlaceholderOpacity));
            StartThenCancel(rig, view, window);
            Assert.Equal("Voice input cancelled", rig.Main.OnMain(() => view.Voice!.Note!.Text));
            var line = Assert.Single(rig.Main.OnMain(() => Lines(view)));
            rig.Main.OnMain(() =>
            {
                Assert.Equal(VoiceNotePlace.Field, line.Place);
                Assert.Equal("Voice input cancelled", line.Text);
                Assert.Null(line.Link);
            });
            // the crossfade: the placeholder leaves while the note arrives (0.05 s late, 0.26 s long)
            rig.Elapse(window, 0.1);
            var mid = rig.Main.OnMain(() => (Note: line.Opacity, Placeholder: box.PlaceholderOpacity));
            Assert.InRange(mid.Note, 0.001, 0.999);
            Assert.InRange(mid.Placeholder, 0.001, 0.999);
            rig.Elapse(window, 0.5);
            Assert.Equal(1, rig.Main.OnMain(() => line.Opacity), 3);
            Assert.Equal(0, rig.Main.OnMain(() => box.PlaceholderOpacity), 3);
            // gone after 1.6 s, the placeholder back
            rig.Elapse(window, 1.3);
            Assert.Null(rig.Main.OnMain(() => view.Voice!.Note));
            rig.Elapse(window, 0.6);
            Assert.Empty(rig.Main.OnMain(() => Lines(view)));
            Assert.Equal(1, rig.Main.OnMain(() => box.PlaceholderOpacity), 3);
            rig.Close(window);
        });
    }

    [Fact] // V-T83: it holds for 1.6 s, not less
    [Trait("Rule", "R-TEST-10")]
    public void The_cancelled_note_is_still_there_just_before_1_6_s()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            MarkPrivacySeen(rig);
            rig.QuietRoom();
            var (view, window) = rig.Open();
            StartThenCancel(rig, view, window);
            rig.Elapse(window, 1.5);
            Assert.NotNull(rig.Main.OnMain(() => view.Voice!.Note));
            rig.Elapse(window, 0.1);
            Assert.Null(rig.Main.OnMain(() => view.Voice!.Note));
            rig.Close(window);
        });
    }

    [Fact] // V-T83: words were written, so they leaving is the answer
    [Trait("Rule", "R-TEST-10")]
    public void Cancelling_after_words_were_written_shows_no_note()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            MarkPrivacySeen(rig);
            rig.Speak(VoiceComposerRig.SwitchTheme());
            var (view, window) = rig.Open();
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            rig.Elapse(window, 3.0);
            Assert.NotEmpty(rig.Main.OnMain(() => view.Voice!.Session.Words));
            Assert.True(rig.Main.OnMain(() => Press(view, Key.Escape)));
            rig.Elapse(window, 0.5);
            Assert.Null(rig.Main.OnMain(() => view.Voice!.Note));
            Assert.Empty(rig.Main.OnMain(() => Lines(view)));
            rig.Close(window);
        });
    }

    [Fact] // V-T83: no speech
    [Trait("Rule", "R-TEST-10")]
    public void Stopping_with_no_speech_heard_shows_No_speech_was_heard_for_1_6_s()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            MarkPrivacySeen(rig);
            rig.QuietRoom();
            var (view, window) = rig.Open();
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            rig.Elapse(window, 1.5);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            rig.Elapse(window, 0.6);
            Assert.Equal("No speech was heard", rig.Main.OnMain(() => view.Voice!.Note!.Text));
            Assert.Equal("No speech was heard", Assert.Single(rig.Main.OnMain(() => Lines(view))).Text);
            rig.Elapse(window, 1.5);
            Assert.Null(rig.Main.OnMain(() => view.Voice!.Note));
            rig.Close(window);
        });
    }

    [Fact] // V-T83: blocked, one underlined link that opens the Windows page
    [Trait("Rule", "R-TEST-10")]
    public void A_blocked_microphone_shows_the_note_with_its_one_link_for_5_2_s_and_the_link_opens_the_Windows_privacy_page()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            MarkPrivacySeen(rig);
            rig.Microphone.Current = MicrophoneAccessStatus.Denied;
            var (view, window) = rig.Open();
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            rig.Elapse(window, 0.6);
            Assert.Equal(VoiceSessionState.Blocked, view.Voice!.Session.State);
            var line = Assert.Single(rig.Main.OnMain(() => Lines(view)));
            rig.Main.OnMain(() =>
            {
                Assert.Equal(VoiceNotePlace.Field, line.Place);
                Assert.Equal("Microphone blocked. Allow Nth in ", line.Text);
                var link = Assert.IsType<Button>(line.Link);
                Assert.Equal("Privacy & security > Microphone", Avalonia.Automation.AutomationProperties.GetName(link));
                Click(link);
            });
            Assert.Equal(["ms-settings:privacy-microphone"], rig.Opened);
            rig.Elapse(window, 4.0);
            Assert.NotNull(rig.Main.OnMain(() => view.Voice!.Note)); // 4.6 s in: still there
            rig.Elapse(window, 0.7);
            Assert.Null(rig.Main.OnMain(() => view.Voice!.Note)); // 5.3 s: gone
            rig.Close(window);
        });
    }

    [Fact] // V-T83: with text in the draft, the note shows in the notice line instead
    [Trait("Rule", "R-TEST-10")]
    public void With_text_in_the_draft_the_blocked_note_shows_in_the_notice_line_and_the_field_keeps_the_users_text()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            MarkPrivacySeen(rig);
            rig.Microphone.Current = MicrophoneAccessStatus.Denied;
            rig.Main.OnMain(() => rig.Runtime.Draft = new ComposerDraft { Text = "keep this" });
            var (view, window) = rig.Open();
            var box = rig.Main.OnMain(() => One<ComposerNSTextView>(view));
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            rig.Elapse(window, 0.8);
            var line = Assert.Single(rig.Main.OnMain(() => Lines(view)));
            rig.Main.OnMain(() =>
            {
                Assert.Equal(VoiceNotePlace.Notice, line.Place);
                Assert.Equal("Microphone blocked. Allow Nth in ", line.Text);
                Assert.NotNull(line.Link);
                Assert.NotNull(Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<NoticeSlot>(line));
                Assert.Equal("keep this", box.Text);
                Assert.Equal("keep this", rig.Runtime.Draft.Text);
            });
            rig.Elapse(window, 5.4);
            Assert.Null(rig.Main.OnMain(() => view.Voice!.Note));
            rig.Elapse(window, 1.0);
            Assert.Empty(rig.Main.OnMain(() => Lines(view)));
            Assert.Equal("keep this", rig.Runtime.Draft.Text);
            rig.Close(window);
        });
    }

    [Fact] // V-T83: a note in the field gives way when the user types
    [Trait("Rule", "R-TEST-10")]
    public void Typing_while_a_field_note_shows_takes_it_away()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            MarkPrivacySeen(rig);
            rig.Microphone.Current = MicrophoneAccessStatus.Denied;
            var (view, window) = rig.Open();
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            rig.Elapse(window, 0.8);
            var line = Assert.Single(rig.Main.OnMain(() => Lines(view)));
            Assert.Equal(1, rig.Main.OnMain(() => line.Opacity), 3);
            rig.Main.OnMain(() => rig.Runtime.Draft = new ComposerDraft { Text = "typed" });
            rig.Elapse(window, 0.5);
            Assert.Empty(rig.Main.OnMain(() => Lines(view)));
            rig.Close(window);
        });
    }

    [Fact] // Q-V-8 A: the first use of each engine shows its privacy line for about 5 s, once
    [Trait("Rule", "R-TEST-10")]
    public void The_first_use_of_the_local_engine_shows_its_privacy_line_for_5_s_and_never_again()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig();
            rig.QuietRoom();
            var (view, window) = rig.Open();
            Assert.Equal(new VoiceNotesSettings(false, false), rig.Main.OnMain(() => rig.App.Settings.VoiceNotes));
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            rig.Elapse(window, 0.6);
            Assert.Equal(VoiceSessionState.Listening, view.Voice!.Session.State);
            Assert.Equal("Voice typing runs on this computer. Your voice is not sent anywhere, and recordings are never kept.", rig.Main.OnMain(() => view.Voice.Note!.Text));
            Assert.Equal(new VoiceNotesSettings(true, false), rig.Main.OnMain(() => rig.App.Settings.VoiceNotes)); // remembered at once
            Assert.Equal("Voice typing runs on this computer. Your voice is not sent anywhere, and recordings are never kept.", Assert.Single(rig.Main.OnMain(() => Lines(view))).Text);
            rig.Elapse(window, 4.2);
            Assert.NotNull(rig.Main.OnMain(() => view.Voice.Note)); // 4.8 s in
            rig.Elapse(window, 0.4);
            Assert.Null(rig.Main.OnMain(() => view.Voice.Note)); // 5.2 s
            // the second session: no privacy line
            rig.Main.OnMain(() => Click(Slot(view).HitTarget)); // stop: no speech
            rig.Elapse(window, 0.3);
            Assert.Equal("No speech was heard", rig.Main.OnMain(() => view.Voice.Note!.Text));
            rig.Elapse(window, 2.5);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            rig.Elapse(window, 0.6);
            Assert.Equal(VoiceSessionState.Listening, view.Voice.Session.State);
            Assert.Null(rig.Main.OnMain(() => view.Voice.Note));
            rig.Close(window);
        });
    }

    [Fact] // Q-V-8 A: once per engine, not once
    [Trait("Rule", "R-TEST-10")]
    public void The_cloud_engine_has_its_own_privacy_line_shown_once_and_the_local_one_is_not_used_up_by_it()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig(typing: VoiceTypingSettings.Default with { Engine = VoiceEngine.Cloud });
            rig.QuietRoom();
            var (view, window) = rig.Open();
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            rig.Elapse(window, 0.6);
            Assert.Equal("Your recording is sent to OpenAI to be turned into text, using your key. OpenAI states it does not train on API data. Nothing is kept on this PC.",
                rig.Main.OnMain(() => view.Voice!.Note!.Text));
            Assert.Equal(new VoiceNotesSettings(false, true), rig.Main.OnMain(() => rig.App.Settings.VoiceNotes));
            rig.Main.OnMain(() => Press(view, Key.Escape));
            rig.Elapse(window, 3.0);
            // back on this PC: its own line is still due
            rig.Main.OnMain(() => rig.App.Settings.VoiceTyping = rig.App.Settings.VoiceTyping with { Engine = VoiceEngine.Local });
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            rig.Elapse(window, 0.6);
            Assert.StartsWith("Voice typing runs on this computer.", rig.Main.OnMain(() => view.Voice!.Note!.Text), StringComparison.Ordinal);
            Assert.Equal(new VoiceNotesSettings(true, true), rig.Main.OnMain(() => rig.App.Settings.VoiceNotes));
            rig.Close(window);
        });
    }

    [Fact] // V-T83 + Q-V-8 A: a later note replaces the one showing
    [Trait("Rule", "R-TEST-10")]
    public void A_later_note_takes_the_place_of_the_privacy_line()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig();
            rig.QuietRoom();
            var (view, window) = rig.Open();
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            rig.Elapse(window, 0.6);
            Assert.StartsWith("Voice typing runs on this computer.", rig.Main.OnMain(() => view.Voice!.Note!.Text), StringComparison.Ordinal);
            Assert.True(rig.Main.OnMain(() => Press(view, Key.Escape)));
            rig.Elapse(window, 0.3);
            Assert.Equal("Voice input cancelled", rig.Main.OnMain(() => view.Voice!.Note!.Text));
            rig.Elapse(window, 2.0);
            Assert.Null(rig.Main.OnMain(() => view.Voice!.Note)); // the privacy line's own timer did not bring it back
            rig.Close(window);
        });
    }
}
