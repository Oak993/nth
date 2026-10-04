using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Session;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Port.Voice;

// VT-3 (voice-SPEC V-7.11, V-14.7, V-15 rows "any | another orb clicked" and "any | the composer leaves"): one session at a time, and a composer
// that goes away.
public sealed class VoiceSessionHubTests
{
    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-7.11: a click on another orb first finishes the running session (as a second click), then starts the new one
    public void A_click_on_another_orb_finishes_the_running_session_first_and_then_starts_the_new_one()
    {
        using var rig = new VoiceSessionRig();
        rig.Speak(VoiceSessionRig.SwitchTheme(), TimeSpan.FromSeconds(0.2));
        var other = rig.NewSession();
        rig.Session.Activate();
        rig.Advance(3.0);
        Assert.Same(rig.Session, rig.Hub.Active);
        other.Activate();
        rig.Settle();
        Assert.Equal(VoiceSessionState.Processing, rig.Session.State);
        Assert.Equal(VoiceStopReason.AnotherOrb, rig.Session.LastStopReason);
        Assert.Equal(VoiceSessionState.Rest, other.State);
        Assert.True(other.StartQueued);
        Assert.Equal(1, rig.Capture.StartCount);
        Assert.Same(rig.Session, rig.Hub.Active);
        // The new orb waits for the whole of Processing (never starts early) and starts the moment it ends.
        for (var step = 0; step < 400 && rig.Session.State == VoiceSessionState.Processing; step++)
        {
            Assert.Equal(VoiceSessionState.Rest, other.State);
            Assert.Equal(1, rig.Capture.StartCount);
            rig.Advance(0.01);
        }
        Assert.Equal(VoiceSessionState.Landing, rig.Session.State);
        Assert.Equal(VoiceSessionState.Listening, other.State);
        Assert.False(other.StartQueued);
        Assert.Equal(2, rig.Capture.StartCount);
        Assert.Same(other, rig.Hub.Active);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-7.11: a running session with nothing in it finishes at once, so the new one starts at once
    public void A_running_session_with_nothing_said_makes_way_at_once()
    {
        using var rig = new VoiceSessionRig();
        rig.QuietRoom();
        var other = rig.NewSession();
        rig.Session.Activate();
        rig.Advance(1.0);
        other.Activate();
        rig.Settle();
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        Assert.Equal(VoiceSessionState.Listening, other.State);
        Assert.Equal([VoiceNoteKind.NoSpeech], rig.Notes);
        Assert.Equal(2, rig.Capture.StartCount);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-7.11: a click during Landing is not a second session: the new one starts, Landing carries on
    public void A_click_on_another_orb_during_Landing_starts_at_once()
    {
        using var rig = new VoiceSessionRig();
        rig.Speak(VoiceSessionRig.SwitchTheme());
        var other = rig.NewSession();
        rig.Session.Activate();
        rig.Advance(6.0);
        rig.Session.Activate();
        rig.Settle();
        rig.Advance(0.75);
        Assert.Equal(VoiceSessionState.Landing, rig.Session.State);
        Assert.Null(rig.Hub.Active);
        other.Activate();
        rig.Settle();
        Assert.Equal(VoiceSessionState.Listening, other.State);
        Assert.Equal(VoiceSessionState.Landing, rig.Session.State);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-12.11 "one question at a time": another orb clicked while a question is open closes it
    public void A_click_on_another_orb_closes_an_open_question()
    {
        using var rig = new VoiceSessionRig();
        rig.Orb = new VoiceOrbSettings { LangMode = VoiceLangMode.Ask };
        var other = rig.NewSession();
        rig.Session.Activate();
        Assert.Equal(VoiceSessionState.Asking, rig.Session.State);
        other.Activate();
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        Assert.Equal(VoiceSessionState.Asking, other.State);
        Assert.Empty(rig.Stored);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-7.11 (ED-292 (9)): a start waiting for the running session is withdrawn by Esc or by the same orb clicked again
    public void A_queued_start_is_withdrawn_by_Escape_or_a_second_click()
    {
        using var rig = new VoiceSessionRig();
        rig.Speak(VoiceSessionRig.SwitchTheme(), TimeSpan.FromSeconds(0.2));
        var escaped = rig.NewSession();
        var clicked = rig.NewSession();
        rig.Session.Activate();
        rig.Advance(3.0);
        escaped.Activate();
        Assert.True(escaped.StartQueued);
        escaped.Cancel();
        Assert.False(escaped.StartQueued);
        clicked.Activate();
        Assert.True(clicked.StartQueued);
        clicked.Activate();
        Assert.False(clicked.StartQueued);
        rig.Advance(5.0);
        Assert.Equal(VoiceSessionState.Rest, escaped.State);
        Assert.Equal(VoiceSessionState.Rest, clicked.State);
        Assert.Equal(1, rig.Capture.StartCount);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-7.11 (ED-292 (9)): only the latest click waits; an earlier waiting orb is dropped, never started and stopped at once
    public void Of_two_waiting_orbs_the_latest_click_starts()
    {
        using var rig = new VoiceSessionRig();
        rig.Speak(VoiceSessionRig.SwitchTheme(), TimeSpan.FromSeconds(0.2));
        var second = rig.NewSession();
        var third = rig.NewSession();
        rig.Session.Activate();
        rig.Advance(3.0);
        second.Activate();
        third.Activate();
        rig.Settle();
        Assert.False(second.StartQueued);
        Assert.True(third.StartQueued);
        rig.Advance(2.0);
        Assert.Equal(VoiceSessionState.Rest, second.State);
        Assert.Equal(VoiceSessionState.Listening, third.State);
        Assert.Equal(2, rig.Capture.StartCount);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-14.7: the composer leaving during Listening stops the session as a second click does; the text lands, is never sent
    public void The_composer_leaving_while_listening_stops_it_and_the_text_is_never_sent()
    {
        using var rig = new VoiceSessionRig();
        rig.Orb = new VoiceOrbSettings { AfterVoice = VoiceAfterVoice.Send };
        rig.Speak(VoiceSessionRig.SwitchTheme());
        rig.Session.Activate();
        rig.Advance(3.0);
        rig.Session.ComposerLeaving();
        rig.Settle();
        Assert.Equal(VoiceSessionState.Processing, rig.Session.State);
        Assert.Equal(VoiceStopReason.ComposerLeft, rig.Session.LastStopReason);
        rig.Advance(5.0);
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        Assert.Contains(VoiceSessionState.Landing, rig.Visited);
        Assert.Contains(rig.Updates, u => u.Kind == TranscriptUpdateKind.Landing);
        Assert.Equal("switch the theme please.", rig.FinalText);
        Assert.Empty(rig.Sends);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-14.7 during Processing: the session carries on to Landing and is never sent
    public void The_composer_leaving_during_Processing_still_lands_and_is_never_sent()
    {
        using var rig = new VoiceSessionRig();
        rig.Orb = new VoiceOrbSettings { AfterVoice = VoiceAfterVoice.Send };
        rig.Speak(VoiceSessionRig.SwitchTheme(), TimeSpan.FromSeconds(0.2));
        rig.Session.Activate();
        rig.Advance(3.0);
        rig.Session.Activate();
        rig.Settle();
        rig.Session.ComposerLeaving();
        rig.Advance(5.0);
        Assert.Contains(VoiceSessionState.Landing, rig.Visited);
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        Assert.Empty(rig.Sends);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-14.7 during Landing: a send still waiting for its 0.35 s is dropped
    public void The_composer_leaving_during_Landing_drops_a_pending_send()
    {
        using var rig = new VoiceSessionRig();
        rig.Orb = new VoiceOrbSettings { AfterVoice = VoiceAfterVoice.Send };
        rig.Speak(VoiceSessionRig.SwitchTheme());
        rig.Session.Activate();
        rig.Advance(6.0);
        rig.Session.Activate();
        rig.Settle();
        rig.Advance(0.75);
        Assert.Equal(VoiceSessionState.Landing, rig.Session.State);
        rig.Advance(0.1);
        rig.Session.ComposerLeaving();
        rig.Advance(2.0);
        Assert.Empty(rig.Sends);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-14.7 in the other states: a question closes, Hover and Blocked go to Rest, Rest stays, a queued start is withdrawn
    public void The_composer_leaving_in_the_other_states_puts_everything_away()
    {
        using var asking = new VoiceSessionRig();
        asking.Orb = new VoiceOrbSettings { LangMode = VoiceLangMode.Ask };
        asking.Session.Activate();
        asking.Session.ComposerLeaving();
        Assert.Equal(VoiceSessionState.Rest, asking.Session.State);
        using var hover = new VoiceSessionRig();
        hover.Session.PointerEnter();
        hover.Session.ComposerLeaving();
        Assert.Equal(VoiceSessionState.Rest, hover.Session.State);
        using var blocked = new VoiceSessionRig { Microphone = new ScriptedMicrophone(MicrophoneAccessStatus.Denied) };
        blocked.Session.Activate();
        blocked.Session.ComposerLeaving();
        Assert.Equal(VoiceSessionState.Rest, blocked.Session.State);
        blocked.Advance(10.0);
        Assert.Equal(VoiceSessionState.Rest, blocked.Session.State);
        using var rest = new VoiceSessionRig();
        rest.Session.ComposerLeaving();
        Assert.Empty(rest.States);
        using var queued = new VoiceSessionRig();
        queued.Speak(VoiceSessionRig.SwitchTheme(), TimeSpan.FromSeconds(0.2));
        var other = queued.NewSession();
        queued.Session.Activate();
        queued.Advance(3.0);
        other.Activate();
        other.ComposerLeaving();
        Assert.False(other.StartQueued);
        queued.Advance(5.0);
        Assert.Equal(VoiceSessionState.Rest, other.State);
    }
}
