using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Dsp;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Session;
using Marrow.Services.Port.Voice.Capture;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.Voice;

// VT-5, V-T42 (voice-SPEC V-4.5, RESEARCH 3.3): the privacy gate before any device is opened. The Windows answer (WinRT DeviceAccessInformation) is a scripted source here; the real
// reader is checked by the native test. A denied microphone opens no device.
public sealed class MicrophoneAccessTests
{
    private static ScriptedCaptureHost OneMicrophone() => new(("mic", "Microphone"));

    [Theory]
    [InlineData(MicrophonePrivacy.DeniedByUser)]
    [InlineData(MicrophonePrivacy.DeniedBySystem)]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")] // V-4.5: denied (by the user or by the system / policy) -> Denied
    public void Either_denial_is_Denied(MicrophonePrivacy answer)
    {
        var access = new MicrophoneAccess(new ScriptedPrivacySource(answer), OneMicrophone());
        Assert.Equal(MicrophoneAccessStatus.Denied, access.Status());
    }

    [Theory]
    [InlineData(MicrophonePrivacy.Allowed)]
    [InlineData(MicrophonePrivacy.Unspecified)]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")] // a desktop app declares no capability: Windows says Unspecified; the all-zero second (V-4.5) catches a block that says nothing
    public void Allowed_and_unspecified_are_Allowed(MicrophonePrivacy answer)
    {
        var access = new MicrophoneAccess(new ScriptedPrivacySource(answer), OneMicrophone());
        Assert.Equal(MicrophoneAccessStatus.Allowed, access.Status());
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")] // no endpoint is NoDevice, whatever the privacy switch says: the note must name the real problem
    public void A_PC_with_no_capture_endpoint_is_NoDevice_even_when_privacy_is_denied()
    {
        var access = new MicrophoneAccess(new ScriptedPrivacySource(MicrophonePrivacy.DeniedByUser), new ScriptedCaptureHost());
        Assert.Equal(MicrophoneAccessStatus.NoDevice, access.Status());
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")] // a Windows that cannot answer is not a reason to refuse: the all-zero check backs it up
    public void A_privacy_reader_that_throws_means_Unspecified()
    {
        var privacy = new ScriptedPrivacySource { Failure = new InvalidOperationException("no packaged identity") };
        var access = new MicrophoneAccess(privacy, OneMicrophone());
        Assert.Equal(MicrophoneAccessStatus.Allowed, access.Status());
        Assert.Equal(1, privacy.Asked);
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")] // an audio service that cannot list endpoints leaves no microphone to use; an unbound seam still fails closed
    public void An_endpoint_list_that_throws_is_NoDevice_but_an_unbound_seam_is_not_swallowed()
    {
        var access = new MicrophoneAccess(new ScriptedPrivacySource(), new ThrowingHost(new System.Runtime.InteropServices.COMException("audio service")));
        Assert.Equal(MicrophoneAccessStatus.NoDevice, access.Status());
        var failClosed = new MicrophoneAccess(new ScriptedPrivacySource(), new FailClosedCaptureHost());
        Assert.Throws<SeamNotBoundException>(() => failClosed.Status());
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")] // V-4.5: "denied -> the Blocked state (V-15) without opening the device"
    public void A_denied_microphone_opens_no_device_in_a_real_session()
    {
        using var runtime = new TestRuntime();
        var host = OneMicrophone();
        var capture = new WasapiCapture(host, () => null, runThread: false);
        var access = new MicrophoneAccess(new ScriptedPrivacySource(MicrophonePrivacy.DeniedByUser), host);
        var seams = VoiceSeamSet.Of(capture, ScriptedVad.Silence(), ScriptedEngine.Always(Hypothesis.Empty), access, new PolyphaseResampler());
        var session = new VoiceSession(() => new VoiceSessionSettings(VoiceOrbSettings.Default, VoiceTypingSettings.Default, "en-US", new SpeechModel("tiny-q5_1", "m.bin")), _ => { }, seams, new VoiceSessionHub());
        var notes = new List<VoiceNoteKind>();
        session.NoteRaised += notes.Add;
        session.Activate();
        runtime.RunUntilIdle();
        Assert.Equal(VoiceSessionState.Blocked, session.State);
        Assert.Equal([VoiceNoteKind.Blocked], notes);
        Assert.Empty(host.OpenRequests);
        Assert.Empty(host.Devices);
        Assert.Equal(0, host.WatcherCount);
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")] // the same session with the microphone allowed opens exactly one device (the control for the test above)
    public void An_allowed_microphone_opens_one_device_in_a_real_session()
    {
        using var runtime = new TestRuntime();
        var host = OneMicrophone();
        var capture = new WasapiCapture(host, () => null, runThread: false);
        var access = new MicrophoneAccess(new ScriptedPrivacySource(MicrophonePrivacy.Allowed), host);
        var seams = VoiceSeamSet.Of(capture, ScriptedVad.Silence(), ScriptedEngine.Always(Hypothesis.Empty), access, new PolyphaseResampler());
        var session = new VoiceSession(() => new VoiceSessionSettings(VoiceOrbSettings.Default, VoiceTypingSettings.Default, "en-US", new SpeechModel("tiny-q5_1", "m.bin")), _ => { }, seams, new VoiceSessionHub());
        session.Activate();
        runtime.RunUntilIdle();
        Assert.Equal(VoiceSessionState.Listening, session.State);
        Assert.Single(host.Devices);
        capture.Stop();
    }
}
