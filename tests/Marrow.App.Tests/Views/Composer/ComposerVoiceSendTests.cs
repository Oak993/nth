using Avalonia.Controls;
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

// VT-10, V-T80 and OD-0099 Q-V-11 C (voice-SPEC V-8.8; ComposerView.swift:370-382): with "Send it as the prompt" on, the voice text is sent 0.35 s after landing
// through the composer's own Return path, whatever the thread is doing: nothing running sends it; a running turn is interrupted and the text sent; with heads at work it
// is queued (the heads run inside the lead's session, and a stop would kill them). With "Leave the text in the composer" nothing is ever sent.
[Collection("Headless")]
public sealed class ComposerVoiceSendTests(HeadlessSession headless)
{
    private static readonly VoiceOrbSettings Sending = VoiceOrbSettings.Default with { AfterVoice = VoiceAfterVoice.Send };

    private static VoiceOrbSlot Slot(Control view) => LogicalQuery.SelfAndDescendants<VoiceOrbSlot>(view).Single();

    private static VoiceComposerRig Rig(VoiceOrbSettings orb, bool workingHead = false)
    {
        var rig = new VoiceComposerRig(orb, workingHead: workingHead);
        rig.Main.OnMain(() => rig.App.Settings.VoiceNotes = new VoiceNotesSettings(Local: true, Cloud: true));
        rig.Speak(VoiceComposerRig.SwitchTheme());
        return rig;
    }

    // Click, speak for 3 s, click again, and let the session run until it lands; the instant Landing began is what the 0.35 s send counts from.
    private static ContinuousInstant ToLanding(VoiceComposerRig rig, ComposerView view, Avalonia.Controls.Window window)
    {
        ContinuousInstant? landed = null;
        rig.Main.OnMain(() =>
        {
            Click(Slot(view).HitTarget);
            view.Voice!.Session.StateChanged += (_, to) =>
            {
                if (to == VoiceSessionState.Landing) landed = SwiftClock.Now;
            };
        });
        rig.Elapse(window, 3.0);
        rig.Main.OnMain(() => Click(Slot(view).HitTarget));
        for (var step = 0; step < 600 && landed is null; step++) rig.Elapse(window, 0.01);
        Assert.NotNull(landed);
        Assert.Equal(VoiceSessionState.Landing, view.Voice!.Session.State);
        return landed.Value;
    }

    private static double Since(ContinuousInstant landed, VoiceComposerRig rig) => rig.Main.OnMain(() => (SwiftClock.Now - landed).TotalSeconds);

    [Fact] // V-T80: nothing running -> Return's send
    [Trait("Rule", "R-TEST-10")]
    public void With_send_on_and_nothing_running_the_text_is_sent_0_35_s_after_landing()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Rig(Sending);
            var (view, window) = rig.Open();
            var landed = ToLanding(rig, view, window);
            Assert.Equal(VoiceComposerRig.SwitchThemeText, rig.Runtime.Draft.Text);
            rig.Elapse(window, 0.349 - Since(landed, rig));
            Assert.Empty(rig.Runtime.SentPrompts);
            Assert.Equal(VoiceComposerRig.SwitchThemeText, rig.Runtime.Draft.Text);
            rig.Elapse(window, 0.002);
            Assert.True(rig.Runtime.Draft.IsEmpty);
            rig.Main.Loop.RunUntil(() => rig.Runtime.Phase == RuntimePhase.Idle && rig.Runtime.Turns.Count == 1, AppModelHarness.Bound);
            Assert.Equal([VoiceComposerRig.SwitchThemeText], rig.Runtime.SentPrompts);
            rig.Close(window);
        });
    }

    [Fact] // V-T80: leave -> never
    [Trait("Rule", "R-TEST-10")]
    public void With_leave_the_text_stays_in_the_composer_and_nothing_is_ever_sent()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Rig(VoiceOrbSettings.Default);
            var (view, window) = rig.Open();
            ToLanding(rig, view, window);
            rig.Elapse(window, 10);
            Assert.Equal(VoiceComposerRig.SwitchThemeText, rig.Runtime.Draft.Text);
            Assert.Empty(rig.Runtime.SentPrompts);
            Assert.Empty(rig.Runtime.Turns);
            rig.Close(window);
        });
    }

    [Fact] // Q-V-11 C: a running turn is interrupted and the text sent, as Return does
    [Trait("Rule", "R-TEST-10")]
    public void With_send_on_and_a_turn_running_the_turn_is_interrupted_and_the_text_sent_as_Return_does()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Rig(Sending);
            rig.Main.OnMain(() => rig.Runtime.RehearseTurn("working"));
            var running = rig.Runtime.Turns.Single().Id;
            var (view, window) = rig.Open();
            ToLanding(rig, view, window);
            Assert.True(rig.Runtime.IsRunning);
            rig.Elapse(window, 0.4);
            Assert.True(rig.Runtime.Draft.IsEmpty); // taken, as Return takes it
            Assert.Empty(rig.Runtime.FollowUps); // not queued: nothing else is at work
            rig.Main.Loop.RunUntil(() => rig.Runtime.Turns.Single(turn => turn.Id == running).Status != TurnStatus.Running, AppModelHarness.Bound);
            Assert.Equal(TurnStatus.Interrupted, rig.Runtime.Turns.Single(turn => turn.Id == running).Status);
            rig.Close(window);
        });
    }

    [Fact] // Q-V-11 C: heads at work -> queued, the lead is not stopped
    [Trait("Rule", "R-TEST-10")]
    public void With_send_on_and_heads_at_work_the_text_is_queued_and_the_running_lead_is_left_alone()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Rig(Sending, workingHead: true);
            rig.Main.OnMain(() => rig.Runtime.RehearseTurn("working"));
            var running = rig.Runtime.Turns.Single().Id;
            Assert.True(rig.Main.OnMain(() => rig.Runtime.HasWorkingHeads));
            var (view, window) = rig.Open();
            ToLanding(rig, view, window);
            rig.Elapse(window, 0.4);
            Assert.True(rig.Runtime.Draft.IsEmpty);
            Assert.Equal(VoiceComposerRig.SwitchThemeText, Assert.Single(rig.Runtime.FollowUps).Text);
            Assert.Equal(TurnStatus.Running, rig.Runtime.Turns.Single(turn => turn.Id == running).Status); // a stop would kill the heads
            rig.Close(window);
        });
    }

    [Fact] // V-8.8: a cancelled session sends nothing, whatever "After voice input" says
    [Trait("Rule", "R-TEST-10")]
    public void A_cancel_with_send_on_sends_nothing_and_leaves_no_text()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Rig(Sending);
            var (view, window) = rig.Open();
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            rig.Elapse(window, 3.0);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget)); // the second click: the text is being finished
            rig.Elapse(window, 0.01);
            Assert.Equal(VoiceSessionState.Processing, view.Voice!.Session.State);
            Assert.True(rig.Main.OnMain(() => Press(view, Avalonia.Input.Key.Escape)));
            rig.Elapse(window, 10);
            Assert.Equal(VoiceSessionState.Rest, view.Voice.Session.State);
            Assert.Empty(rig.Runtime.SentPrompts);
            Assert.Empty(rig.Runtime.Turns);
            Assert.True(rig.Runtime.Draft.IsEmpty);
            rig.Close(window);
        });
    }

    [Fact] // V-8.8: a session cancelled, or left with the composer, sends nothing; a new dictation before the send drops it
    [Trait("Rule", "R-TEST-10")]
    public void A_new_dictation_started_before_the_send_fires_drops_it()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Rig(Sending);
            var (view, window) = rig.Open();
            ToLanding(rig, view, window);
            rig.Elapse(window, 0.1);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget)); // the orb is clickable again during Landing
            rig.Elapse(window, 0.5);
            Assert.Empty(rig.Runtime.SentPrompts);
            Assert.Equal(VoiceSessionState.Listening, view.Voice!.Session.State);
            Assert.Equal(VoiceComposerRig.SwitchThemeText, rig.Runtime.Draft.Text);
            rig.Close(window);
        });
    }
}
