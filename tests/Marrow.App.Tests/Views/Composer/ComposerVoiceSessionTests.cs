using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Marrow.App.Port.Voice;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Session;
using Xunit;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Views.Composer;

// VT-10, V-T82 and the session rules of V-14 (voice-SPEC V-8.1, V-8.5, V-14.4, V-14.7; V-7.11): Esc, the editor during a session, where the text lands, one session
// at a time and the composer leaving. Sessions are scripted (no microphone, no engine). The live words and their animation are VT-11's; here the text reaches
// the draft when the session lands.
[Collection("Headless")]
public sealed class ComposerVoiceSessionTests(HeadlessSession headless)
{
    private static VoiceOrbSlot Slot(Control view) => LogicalQuery.SelfAndDescendants<VoiceOrbSlot>(view).Single();

    private static VoiceComposerRig Seen(VoiceOrbSettings? orb = null, bool workingHead = false)
    {
        var rig = new VoiceComposerRig(orb, workingHead: workingHead);
        rig.Main.OnMain(() => rig.App.Settings.VoiceNotes = new VoiceNotesSettings(Local: true, Cloud: true));
        return rig;
    }

    private static void Listen(VoiceComposerRig rig, ComposerView view, Avalonia.Controls.Window window)
    {
        rig.Main.OnMain(() => Click(Slot(view).HitTarget));
        rig.Elapse(window, 0.1);
        Assert.Equal(VoiceSessionState.Listening, view.Voice!.Session.State);
    }

    [Fact] // V-T82, V-14.4
    [Trait("Rule", "R-TEST-10")]
    public void Esc_while_listening_cancels_and_is_consumed_so_it_does_not_take_back_a_running_turn()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            rig.Speak(VoiceComposerRig.SwitchTheme());
            rig.Main.OnMain(() => rig.Runtime.RehearseTurn("working"));
            var (view, window) = rig.Open();
            Listen(rig, view, window);
            Assert.True(rig.Main.OnMain(() => Press(view, Key.Escape))); // consumed
            rig.Elapse(window, 0.2);
            Assert.Equal(VoiceSessionState.Rest, view.Voice!.Session.State);
            Assert.True(rig.Runtime.IsRunning); // Esc's ordinary meaning (take the running turn back) did not run
            Assert.Equal(TurnStatus.Running, rig.Runtime.Turns.Single().Status);
            rig.Close(window);
        });
    }

    [Fact] // V-T82, V-14.4: Esc also cancels while the text is being finished
    [Trait("Rule", "R-TEST-10")]
    public void Esc_while_the_text_is_being_finished_cancels_it_too_and_nothing_lands_in_the_draft()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            rig.Speak(VoiceComposerRig.SwitchTheme());
            var (view, window) = rig.Open();
            Listen(rig, view, window);
            rig.Elapse(window, 3.0);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            rig.Elapse(window, 0.01);
            Assert.Equal(VoiceSessionState.Processing, view.Voice!.Session.State);
            Assert.True(rig.Main.OnMain(() => Press(view, Key.Escape)));
            rig.Elapse(window, 10);
            Assert.Equal(VoiceSessionState.Rest, view.Voice.Session.State);
            Assert.Equal("", rig.Runtime.Draft.Text);
            rig.Close(window);
        });
    }

    [Fact] // V-14.4: a sheet opened after the session went live takes Esc first (an NSWindow's sheet is the key window); once it is gone Esc cancels the session again
    [Trait("Rule", "R-TEST-10")]
    public void A_sheet_opened_after_the_session_went_live_takes_Esc_first_and_the_session_goes_on()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            rig.Speak(VoiceComposerRig.SwitchTheme());
            var (view, window) = rig.Open();
            Listen(rig, view, window);
            var dismissed = 0;
            var release = rig.Main.OnMain(() => Marrow.App.Port.SheetKeyboard.Take(window, new Border(), _ => false, () => dismissed++, blockUnder: false));
            Assert.True(rig.Main.OnMain(() => Press(view, Key.Escape))); // the sheet took it
            Assert.Equal(1, dismissed);
            Assert.Equal(VoiceSessionState.Listening, view.Voice!.Session.State);
            rig.Main.OnMain(release); // the sheet has closed
            Assert.True(rig.Main.OnMain(() => Press(view, Key.Escape)));
            rig.Elapse(window, 0.2);
            Assert.Equal(1, dismissed);
            Assert.Equal(VoiceSessionState.Rest, view.Voice.Session.State);
            rig.Close(window);
        });
    }

    [Fact] // V-14.4: the other order: a sheet that was shown and closed before the session leaves Esc to the session
    [Trait("Rule", "R-TEST-10")]
    public void A_sheet_closed_before_the_session_starts_leaves_Esc_to_the_session()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            rig.Speak(VoiceComposerRig.SwitchTheme());
            var (view, window) = rig.Open();
            var release = rig.Main.OnMain(() => Marrow.App.Port.SheetKeyboard.Take(window, new Border(), _ => false, () => { }, blockUnder: false));
            rig.Main.OnMain(release);
            Listen(rig, view, window);
            Assert.True(rig.Main.OnMain(() => Press(view, Key.Escape)));
            rig.Elapse(window, 0.2);
            Assert.Equal(VoiceSessionState.Rest, view.Voice!.Session.State);
            rig.Close(window);
        });
    }

    [Fact] // V-14.4: a popover or menu open over the window is the keyboard's first as well
    [Trait("Rule", "R-TEST-10")]
    public void A_popover_open_over_the_window_takes_Esc_first_and_the_session_goes_on_until_it_closes()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            rig.Speak(VoiceComposerRig.SwitchTheme());
            var (view, window) = rig.Open();
            Listen(rig, view, window);
            var key = new object();
            var dismissed = 0;
            rig.Main.OnMain(() => Marrow.UI.Port.WindowTransients.Show(key, view, () => dismissed++));
            Assert.False(rig.Main.OnMain(() => Press(view, Key.Escape))); // this session leaves it to the popover's own handler
            Assert.Equal(VoiceSessionState.Listening, view.Voice!.Session.State);
            rig.Main.OnMain(() => Marrow.UI.Port.WindowTransients.Hide(key));
            Assert.True(rig.Main.OnMain(() => Press(view, Key.Escape)));
            rig.Elapse(window, 0.2);
            Assert.Equal(VoiceSessionState.Rest, view.Voice.Session.State);
            Assert.Equal(0, dismissed);
            rig.Close(window);
        });
    }

    [Fact] // V-14.4: Esc with a modifier held is not the cancel
    [Trait("Rule", "R-TEST-10")]
    public void Esc_with_a_modifier_held_does_not_cancel_the_session()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            rig.Speak(VoiceComposerRig.SwitchTheme());
            var (view, window) = rig.Open();
            Listen(rig, view, window);
            foreach (var modifiers in new[] { KeyModifiers.Shift, KeyModifiers.Control, KeyModifiers.Alt })
            {
                rig.Main.OnMain(() => Press(view, Key.Escape, modifiers));
                rig.Elapse(window, 0.05);
                Assert.Equal(VoiceSessionState.Listening, view.Voice!.Session.State);
            }
            Assert.True(rig.Main.OnMain(() => Press(view, Key.Escape)));
            rig.Elapse(window, 0.2);
            Assert.Equal(VoiceSessionState.Rest, view.Voice!.Session.State);
            rig.Close(window);
        });
    }

    [Fact] // V-14.4: otherwise Esc keeps its existing meaning
    [Trait("Rule", "R-TEST-10")]
    public void With_no_session_running_Esc_keeps_its_ordinary_meaning()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            var (view, window) = rig.Open();
            Assert.False(rig.Main.OnMain(() => Press(view, Key.Escape))); // nothing to cancel, nothing to take back
            rig.Main.OnMain(() => rig.Runtime.RehearseTurn("working"));
            Assert.True(rig.Main.OnMain(() => Press(view, Key.Escape))); // the running turn is taken back, as before
            rig.Close(window);
        });
    }

    [Fact] // V-T82, V-8.5
    [Trait("Rule", "R-TEST-10")]
    public void The_editor_is_read_only_during_a_session_and_free_again_after_it()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            rig.Speak(VoiceComposerRig.SwitchTheme());
            rig.Main.OnMain(() => rig.Runtime.Draft = new ComposerDraft { Text = "keep" });
            var (view, window) = rig.Open();
            var box = rig.Main.OnMain(() => One<ComposerNSTextView>(view));
            Assert.False(rig.Main.OnMain(() => box.IsReadOnly));
            Listen(rig, view, window);
            Assert.True(rig.Main.OnMain(() => box.IsReadOnly));
            rig.Main.OnMain(() =>
            {
                box.Focus();
                window.KeyTextInput("typed");
                Drain();
            });
            Assert.Equal("keep", rig.Runtime.Draft.Text); // typing is ignored
            Assert.True(rig.Main.OnMain(() => Press(view, Key.Enter))); // so is Return: nothing is sent
            rig.Elapse(window, 0.2);
            Assert.Equal("keep", rig.Runtime.Draft.Text);
            Assert.Empty(rig.Runtime.SentPrompts);
            Assert.Equal(VoiceSessionState.Listening, view.Voice!.Session.State);
            rig.Main.OnMain(() => Press(view, Key.Escape));
            rig.Elapse(window, 0.3);
            Assert.False(rig.Main.OnMain(() => box.IsReadOnly));
            Assert.Equal("keep", rig.Runtime.Draft.Text);
            rig.Close(window);
        });
    }

    [Fact] // V-8.1 (interim: the text lands when the session does; VT-11 writes it live)
    [Trait("Rule", "R-TEST-10")]
    public void The_text_lands_in_the_draft_after_the_caret_with_a_space_and_the_caret_ends_after_it()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            rig.Speak(VoiceComposerRig.SwitchTheme());
            rig.Main.OnMain(() => rig.Runtime.Draft = new ComposerDraft { Text = "Hello" });
            var (view, window) = rig.Open();
            var box = rig.Main.OnMain(() => One<ComposerNSTextView>(view));
            rig.Main.OnMain(() => box.SelectionStart = box.SelectionEnd = 5);
            Listen(rig, view, window);
            rig.Elapse(window, 3.0);
            Assert.Equal("Hello", rig.Runtime.Draft.Text); // nothing before it lands
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            rig.Elapse(window, 10);
            Assert.Equal("Hello switch the theme please.", rig.Runtime.Draft.Text);
            Assert.Equal("Hello switch the theme please.".Length, rig.Main.OnMain(() => box.SelectionStart));
            Assert.Empty(rig.Runtime.SentPrompts); // "Leave the text in the composer" is the default
            rig.Close(window);
        });
    }

    [Fact] // V-8.1: a selection is replaced
    [Trait("Rule", "R-TEST-10")]
    public void A_selection_is_replaced_by_the_words()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            rig.Speak(VoiceComposerRig.SwitchTheme());
            rig.Main.OnMain(() => rig.Runtime.Draft = new ComposerDraft { Text = "one OLD two" });
            var (view, window) = rig.Open();
            var box = rig.Main.OnMain(() => One<ComposerNSTextView>(view));
            rig.Main.OnMain(() =>
            {
                box.SelectionStart = 4;
                box.SelectionEnd = 7;
            });
            Listen(rig, view, window);
            rig.Elapse(window, 3.0);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            rig.Elapse(window, 10);
            Assert.Equal("one switch the theme please. two", rig.Runtime.Draft.Text);
            rig.Close(window);
        });
    }

    [Fact] // V-14.7
    [Trait("Rule", "R-TEST-10")]
    public void The_composer_leaving_stops_the_session_as_a_second_click_does_and_the_text_lands_and_is_never_sent()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen(VoiceOrbSettings.Default with { AfterVoice = VoiceAfterVoice.Send });
            rig.Speak(VoiceComposerRig.SwitchTheme());
            var (view, window) = rig.Open();
            Listen(rig, view, window);
            rig.Elapse(window, 3.0);
            rig.Main.OnMain(() => ((Border)Host(window)).Child = null); // a thread switch takes the composer off the screen
            rig.Elapse(window, 0.01);
            var session = view.Voice!.Session;
            Assert.Equal(VoiceSessionState.Processing, session.State);
            Assert.Equal(VoiceStopReason.ComposerLeft, session.LastStopReason);
            rig.Elapse(window, 10);
            Assert.Equal(VoiceSessionState.Rest, session.State);
            Assert.Equal(VoiceComposerRig.SwitchThemeText, rig.Runtime.Draft.Text); // in that thread's draft
            Assert.Empty(rig.Runtime.SentPrompts); // and never auto-sent, whatever "After voice input" says
            Assert.False(rig.Capture.IsRunning);
            rig.Close(window);
        });
    }

    [Fact] // V-14.7: the window closing is the composer leaving too
    [Trait("Rule", "R-TEST-10")]
    public void Closing_the_window_stops_the_capture()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            rig.Speak(VoiceComposerRig.SwitchTheme());
            var (view, window) = rig.Open();
            Listen(rig, view, window);
            Assert.True(rig.Capture.IsRunning);
            rig.Close(window);
            rig.Main.Advance(TimeSpan.FromSeconds(0.1));
            Assert.False(rig.Capture.IsRunning);
        });
    }

    [Fact] // V-7.11: one session at a time
    [Trait("Rule", "R-TEST-10")]
    public void A_second_orb_clicked_finishes_the_first_session_and_then_starts()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            rig.Speak(VoiceComposerRig.SwitchTheme());
            var (first, firstWindow) = rig.Open();
            var (second, secondWindow) = rig.Open();
            Listen(rig, first, firstWindow);
            rig.Elapse(firstWindow, 3.0);
            rig.Main.OnMain(() => Click(Slot(second).HitTarget));
            rig.Elapse(firstWindow, 0.01);
            Assert.Equal(VoiceSessionState.Processing, first.Voice!.Session.State); // the first finishes as a second click would make it
            Assert.True(second.Voice!.Session.StartQueued);
            Assert.NotEqual(VoiceSessionState.Listening, second.Voice.Session.State);
            Assert.Same(first.Voice.Session, rig.Hub.Active);
            rig.Elapse(firstWindow, 10);
            Assert.Equal(VoiceSessionState.Listening, second.Voice.Session.State);
            Assert.Same(second.Voice.Session, rig.Hub.Active);
            Assert.Equal(VoiceComposerRig.SwitchThemeText, rig.Runtime.Draft.Text); // the first one's text landed
            rig.Close(secondWindow);
            rig.Close(firstWindow);
        });
    }
}
