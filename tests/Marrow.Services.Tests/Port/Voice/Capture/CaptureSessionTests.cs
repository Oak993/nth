using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Session;
using Marrow.Core.Port.Voice.Transcript;
using Marrow.Services.Port.Voice.Capture;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Voice.Capture;

// VT-5, V-T41 (voice-SPEC V-4.4, V-4.5): the real VoiceSession over the production capture and the scripted endpoint host. The all-zero first second is what Windows hands a blocked
// desktop app; a device lost mid-session finishes on what was captured.
public sealed class CaptureSessionTests
{
    private static readonly Func<long, float> Zero = _ => 0f;

    private static VoiceScript SwitchTheme() => new(
    [
        new ScriptedWord("switch", 0.5, 0.9),
        new ScriptedWord("the", 1.0, 1.2),
        new ScriptedWord("theme", 1.3, 1.8, FirstGuess: "team"),
        new ScriptedWord("please.", 2.0, 2.5),
    ]);

    [Theory]
    [InlineData(SampleEncoding.Float32, 48000, 2)]
    [InlineData(SampleEncoding.Pcm16, 44100, 1)]
    [InlineData(SampleEncoding.Pcm24, 48000, 8)]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T41")]
    [Trait("Decision", "ED-267")] // V-4.5: every sample of the first 1.0 s exactly 0.0 -> capture stops and Blocked shows
    public void A_first_second_of_exact_zeros_blocks_the_session_and_closes_the_device(SampleEncoding encoding, int rate, int channels)
    {
        using var rig = new CaptureSessionRig(new DeviceFormat(rate, channels, encoding));
        rig.Session.Activate();
        rig.Runtime.RunUntilIdle();
        Assert.Equal(VoiceSessionState.Listening, rig.Session.State);
        rig.Feed(Zero, 0.9);
        Assert.Equal(VoiceSessionState.Listening, rig.Session.State);
        rig.Feed(Zero, 0.2);
        Assert.Equal(VoiceSessionState.Blocked, rig.Session.State);
        Assert.Equal([VoiceNoteKind.Blocked], rig.Notes);
        Assert.True(rig.Device.IsDisposed);
        Assert.Equal(1, rig.Device.StopCount);
        Assert.Equal(0, rig.Host.WatcherCount);
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T41")]
    [Trait("Decision", "ED-267")] // V-4.5: a buffer WASAPI flags silent is zeros even if the bytes are not
    public void Buffers_flagged_silent_count_as_the_all_zero_second_whatever_their_bytes()
    {
        using var rig = new CaptureSessionRig();
        rig.Session.Activate();
        rig.Runtime.RunUntilIdle();
        rig.Feed(_ => 0.3f, 1.1, silent: true);
        Assert.Equal(VoiceSessionState.Blocked, rig.Session.State);
        Assert.Equal([VoiceNoteKind.Blocked], rig.Notes);
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T41")]
    [Trait("Decision", "ED-267")] // V-4.5: one non-zero sample in the first second is a working microphone
    public void A_single_non_zero_sample_in_the_first_second_keeps_the_session_listening()
    {
        using var rig = new CaptureSessionRig();
        rig.Session.Activate();
        rig.Runtime.RunUntilIdle();
        rig.Feed(Zero, 0.8);
        rig.Feed(ScriptedSignals.RoomNoise, 0.05);
        rig.Feed(Zero, 2.0);
        Assert.Equal(VoiceSessionState.Listening, rig.Session.State);
        Assert.Empty(rig.Notes);
        Assert.True(rig.Device.IsStarted);
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T41")]
    [Trait("Decision", "ED-267")] // V-4.4: the device is removed mid-session -> the session runs the final pass on what was captured
    public void A_device_removed_mid_session_gives_the_final_pass_on_what_was_captured()
    {
        using var rig = new CaptureSessionRig();
        var script = SwitchTheme();
        rig.Speak(script);
        rig.Session.Activate();
        rig.Runtime.RunUntilIdle();
        rig.Feed(script.Signal(48000), 3.0);
        Assert.Equal(VoiceSessionState.Listening, rig.Session.State);
        rig.Host.Remove("mic-a");
        rig.Capture.Pump();
        rig.Runtime.RunUntilIdle();
        Assert.Equal(VoiceSessionState.Processing, rig.Session.State);
        Assert.Equal(VoiceStopReason.DeviceEvent, rig.Session.LastStopReason);
        Assert.True(rig.Device.IsDisposed);
        rig.Advance(5.0);
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        Assert.Contains(VoiceSessionState.Landing, rig.Visited);
        Assert.Equal("switch the theme please.", string.Join(" ", rig.Session.Words.Select(w => w.Text)));
        var final = rig.Engine.Decodes.Last(d => d.Request.Mode == DecodeMode.Final);
        Assert.InRange(final.AudioEnd.TotalSeconds - StreamingTranscriber.FinalSilenceSeconds, 2.9, 3.0); // the end of the audio decoded (the pass adds 0.3 s of silence): the 3.0 s the microphone gave, less the resampler's lookahead
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T41")]
    [Trait("Decision", "ED-267")] // V-7.6: the second click stops the capture and the last audio is flushed into the pass (the sink is still listening)
    public void The_second_click_flushes_the_last_buffers_of_the_device_into_the_final_pass()
    {
        using var rig = new CaptureSessionRig();
        var script = SwitchTheme();
        rig.Speak(script);
        rig.Session.Activate();
        rig.Runtime.RunUntilIdle();
        rig.Feed(script.Signal(48000), 3.0);
        rig.Device.PushSignal(script.Signal(48000), TimeSpan.FromMilliseconds(30)); // in the ring, never pumped
        rig.Session.Activate();
        rig.Runtime.RunUntilIdle();
        Assert.Equal(VoiceSessionState.Processing, rig.Session.State);
        var final = rig.Engine.Decodes.Last(d => d.Request.Mode == DecodeMode.Final);
        Assert.InRange(final.AudioEnd.TotalSeconds - StreamingTranscriber.FinalSilenceSeconds, 3.0, 3.04); // 3.0 s fed, 30 ms flushed at the click, less the lookahead
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T41")]
    [Trait("Decision", "ED-267")] // V-4.4: another app holds the device exclusively: the capture-failed note, no recording
    public void A_device_taken_by_another_app_at_the_start_gives_the_capture_failed_note()
    {
        using var rig = new CaptureSessionRig();
        rig.Host.FailOpen("mic-a", CaptureDeviceFailure.InUse);
        rig.Session.Activate();
        rig.Runtime.RunUntilIdle();
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        Assert.Equal([VoiceNoteKind.CaptureFailed], rig.Notes);
        Assert.Empty(rig.Host.Devices);
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T41")]
    [Trait("Decision", "ED-267")] // V-4.4: the device is taken while listening
    public void A_device_taken_while_listening_ends_the_session_with_the_capture_failed_note()
    {
        using var rig = new CaptureSessionRig();
        rig.Session.Activate();
        rig.Runtime.RunUntilIdle();
        rig.Feed(ScriptedSignals.RoomNoise, 0.5);
        rig.Device.Fail(CaptureDeviceFailure.InUse);
        rig.Capture.Pump();
        rig.Runtime.RunUntilIdle();
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        Assert.Equal([VoiceNoteKind.CaptureFailed], rig.Notes);
        Assert.True(rig.Device.IsDisposed);
    }
}
