using Marrow.Core.Port.Voice.Session;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Port.Voice;

// VT-3 (voice-SPEC V-7.12, V-15; OD-0092 the 5-minute cap, OD-0099 Q-V-6 B: the pill counts down the last 30 s, then finishes the text as if the
// orb were clicked). The countdown's look is not drawn yet (D-VN); the session carries its state: from 270 s to 300 s of listening, on the clock.
public sealed class VoiceSessionCapTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // Q-V-6 B: nothing counts down before 270 s
    public void There_is_no_countdown_before_270_s()
    {
        using var rig = new VoiceSessionRig();
        rig.QuietRoom();
        rig.Session.Activate();
        Assert.False(rig.Session.CapCountdownActive);
        Assert.Null(rig.Session.CapCountdownRemaining);
        rig.Advance(269.99);
        Assert.False(rig.Session.CapCountdownActive);
        Assert.Null(rig.Session.CapCountdownRemaining);
        Assert.Equal(0, rig.CountdownStarts);
        Assert.Equal(VoiceSessionState.Listening, rig.Session.State);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // Q-V-6 B: the countdown state runs from 270 s (30 s left) to 300 s (0 left) on the manual clock
    public void The_countdown_state_runs_from_270_s_to_300_s()
    {
        using var rig = new VoiceSessionRig();
        rig.QuietRoom();
        rig.Session.Activate();
        rig.Advance(270.0);
        Assert.True(rig.Session.CapCountdownActive);
        Assert.Equal(S(30), rig.Session.CapCountdownRemaining);
        Assert.Equal(1, rig.CountdownStarts);
        rig.Advance(15.0);
        Assert.Equal(S(15), rig.Session.CapCountdownRemaining);
        rig.Advance(14.5);
        Assert.Equal(S(0.5), rig.Session.CapCountdownRemaining);
        rig.Advance(0.49);
        Assert.Equal(S(0.01), rig.Session.CapCountdownRemaining);
        Assert.Equal(VoiceSessionState.Listening, rig.Session.State);
        Assert.Equal(1, rig.CountdownStarts);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-7.12: at 300 s of listening the session stops as if the orb were clicked (final pass, text kept)
    public void At_300_s_the_session_stops_as_a_click_would_and_keeps_the_text()
    {
        using var rig = new VoiceSessionRig();
        rig.Speak(VoiceSessionRig.SwitchTheme());
        rig.Session.Activate();
        rig.Advance(299.99);
        Assert.Equal(VoiceSessionState.Listening, rig.Session.State);
        rig.Advance(0.01);
        Assert.Equal(VoiceSessionState.Processing, rig.Session.State);
        Assert.Equal(VoiceStopReason.Cap, rig.Session.LastStopReason);
        Assert.Equal(1, rig.Capture.StopCount);
        Assert.Null(rig.Session.CapCountdownRemaining);
        rig.Advance(1.0);
        Assert.Equal(VoiceSessionState.Landing, rig.Session.State);
        Assert.Equal("switch the theme please.", rig.FinalText);
        Assert.Empty(rig.Notes);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-7.12 with nothing said: the cap ends as a click would, with the no speech note
    public void At_300_s_with_nothing_said_the_session_ends_with_the_no_speech_note()
    {
        using var rig = new VoiceSessionRig();
        rig.QuietRoom();
        rig.Session.Activate();
        rig.Advance(300.0);
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        Assert.Equal([VoiceNoteKind.NoSpeech], rig.Notes);
        Assert.Equal(VoiceStopReason.Cap, rig.Session.LastStopReason);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-7.12: leaving Listening early ends the countdown and the cap does not fire later
    public void A_click_during_the_countdown_ends_it_and_the_cap_never_fires()
    {
        using var rig = new VoiceSessionRig();
        rig.Speak(VoiceSessionRig.SwitchTheme());
        rig.Session.Activate();
        rig.Advance(280.0);
        Assert.True(rig.Session.CapCountdownActive);
        rig.Session.Activate();
        rig.Settle();
        Assert.False(rig.Session.CapCountdownActive);
        Assert.Null(rig.Session.CapCountdownRemaining);
        Assert.Equal(VoiceStopReason.Click, rig.Session.LastStopReason);
        rig.Advance(30.0);
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        Assert.Equal(VoiceStopReason.Click, rig.Session.LastStopReason);
        Assert.Equal(1, rig.Capture.StopCount);
        Assert.Equal(1, rig.CountdownStarts);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-15 Esc during the countdown: Rest, and nothing fires at 300 s
    public void Escape_during_the_countdown_ends_the_session_and_the_cap_never_fires()
    {
        using var rig = new VoiceSessionRig();
        rig.QuietRoom();
        rig.Session.Activate();
        rig.Advance(285.0);
        rig.Session.Cancel();
        rig.Settle();
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        Assert.Null(rig.Session.CapCountdownRemaining);
        var visited = rig.Visited.Count;
        rig.Advance(60.0);
        Assert.Equal(visited, rig.Visited.Count);
        Assert.Equal([VoiceNoteKind.Cancelled], rig.Notes);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-7.12: the cap counts listening time from the click that began this session, not from an earlier one
    public void A_second_session_gets_its_own_300_s()
    {
        using var rig = new VoiceSessionRig();
        rig.QuietRoom();
        rig.Session.Activate();
        rig.Advance(100.0);
        rig.Session.Activate();
        rig.Settle();
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        rig.QuietRoom();
        rig.Session.Activate();
        rig.Advance(299.0);
        Assert.Equal(VoiceSessionState.Listening, rig.Session.State);
        Assert.False(rig.Session.CapCountdownRemaining is null);
    }
}
