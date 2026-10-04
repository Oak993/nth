using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Session;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Port.Voice;

// VT-3 (voice-SPEC V-8.8; OD-0099 Q-V-11 C): with "Send it as the prompt" on, voice text does what Return does (interrupt the turn and send, queue
// when heads are at work: ComposerView.swift:370-381), 0.35 s after landing. The session reports the intent; the composer's own Return path decides
// what that means for the thread, so the session needs no notion of a running turn. ED-NEW-f-vt-3 (10): 0.35 s counts from the start of Landing.
public sealed class VoiceSessionSendTests
{
    private static VoiceSessionRig Sending()
    {
        var rig = new VoiceSessionRig();
        rig.Orb = new VoiceOrbSettings { AfterVoice = VoiceAfterVoice.Send };
        rig.Speak(VoiceSessionRig.SwitchTheme());
        return rig;
    }

    private static void ToLanding(VoiceSessionRig rig)
    {
        rig.Session.Activate();
        rig.Advance(6.0);
        rig.Session.Activate();
        rig.Settle();
        rig.Advance(0.75);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T80")]
    [Trait("Decision", "ED-267")] // V-8.8: send 0.35 s after landing, the intent being what Return does (Q-V-11 C)
    public void With_send_on_the_text_is_sent_as_Return_does_0_35_s_after_landing()
    {
        using var rig = Sending();
        ToLanding(rig);
        Assert.Equal(VoiceSessionState.Landing, rig.Session.State);
        Assert.Empty(rig.Sends);
        rig.Advance(0.349);
        Assert.Empty(rig.Sends);
        rig.Advance(0.001);
        var intent = Assert.Single(rig.Sends);
        Assert.Equal(new SendIntent("switch the theme please.", SendIntentKind.AsReturn), intent);
        rig.Advance(10.0);
        Assert.Single(rig.Sends);
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T80")]
    [Trait("Decision", "ED-267")] // V-8.8: with the default (leave) nothing is ever sent
    public void With_leave_nothing_is_sent()
    {
        using var rig = Sending();
        rig.Orb = VoiceOrbSettings.Default;
        ToLanding(rig);
        rig.Advance(10.0);
        Assert.Empty(rig.Sends);
        Assert.Equal("switch the theme please.", rig.FinalText);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T80")]
    [Trait("Decision", "ED-267")] // V-8.8: a cancelled session sends nothing
    public void A_cancelled_session_sends_nothing()
    {
        using var rig = Sending();
        rig.Session.Activate();
        rig.Advance(3.0);
        rig.Session.Cancel();
        rig.Advance(10.0);
        Assert.Empty(rig.Sends);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T80")]
    [Trait("Decision", "ED-267")] // V-8.8 (ED-NEW-f-vt-3 (10)): a new dictation begun before the send fires drops it; the new one sends for itself
    public void A_new_session_started_during_Landing_drops_the_pending_send()
    {
        using var rig = Sending();
        ToLanding(rig);
        rig.Advance(0.1);
        rig.Session.Activate();
        rig.Advance(0.5);
        Assert.Empty(rig.Sends);
        Assert.Equal(VoiceSessionState.Listening, rig.Session.State);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T80")]
    [Trait("Decision", "ED-267")] // Q-V-11 C: the intent is the same however the session was stopped (a click, the cap, a device event)
    public void The_send_intent_does_not_depend_on_how_the_session_was_stopped()
    {
        using var rig = Sending();
        rig.Session.Activate();
        rig.Advance(3.0);
        rig.Capture.RaiseDeviceEvent(Marrow.Core.Port.Voice.Seams.CaptureDeviceEventKind.DeviceRemoved);
        rig.Settle();
        rig.Advance(5.0);
        Assert.Equal(SendIntentKind.AsReturn, Assert.Single(rig.Sends).Kind);
    }
}
