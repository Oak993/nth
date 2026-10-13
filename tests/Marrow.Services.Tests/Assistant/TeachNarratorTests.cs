using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-19 (DESIGN.md rev 5, 10 step 1; ED-NEW-dextra-as19-8): the owner's narration as lines of text with the time each began, from the
// voice seams the app already binds. The audio is cut into utterances at the pauses and each is recognised by the local engine, one at a
// time; only text is kept; a line that holds a secret is dropped; a microphone that is blocked, missing or taken, or a missing local
// model, switches narration off with the reason and leaves the recording alone.
[Trait("Feature", "AS-19")]
[Trait("Decision", "ED-NEW-dextra-as19-8")]
public sealed class TeachNarratorTests
{
    private static ScriptedVad Speaking(params (double Start, double End)[] spans) => ScriptedVad.Speech(spans);

    private static async Task Hear(TeachNarratorRig rig, double seconds)
    {
        Assert.Null(rig.Narrator.Start());
        rig.Capture.PushNow(_ => 0.1f, TimeSpan.FromSeconds(seconds));
        await rig.Narrator.StopAsync();
    }

    [Fact]
    public async Task One_stretch_of_speech_becomes_one_line_with_the_time_it_began()
    {
        using var rig = new TeachNarratorRig(Speaking((1.0, 3.0)), ScriptedEngine.Always(TeachNarratorRig.Said("Send the report")));
        await Hear(rig, 6);
        var line = Assert.Single(rig.Lines);
        Assert.Equal("Send the report", line.Text);
        Assert.InRange(line.Start.TotalSeconds, 0.80, 1.0);
        Assert.InRange(line.End.TotalSeconds, 3.5, 4.0);
        var decode = Assert.Single(rig.Engine.Decodes);
        Assert.Equal((DecodeMode.Final, "en"), (decode.Request.Mode, decode.Request.Language));
        Assert.Single(rig.Engine.Loads);
        Assert.Equal("small", rig.Engine.Loads[0].Model.Id);
    }

    [Fact]
    public async Task Two_stretches_apart_are_two_lines_in_order_and_the_engine_is_never_asked_twice_at_once()
    {
        var spoken = new Queue<string>(["First thing", "Second thing"]);
        using var rig = new TeachNarratorRig(Speaking((1.0, 2.0), (5.0, 6.0)), new ScriptedEngine(_ => TeachNarratorRig.Said(spoken.Dequeue())));
        await Hear(rig, 8);
        Assert.Equal(["First thing", "Second thing"], rig.Lines.Select(l => l.Text));
        Assert.True(rig.Lines[0].Start < rig.Lines[1].Start);
        Assert.Equal(1, rig.Engine.MaxInFlight);
    }

    [Fact]
    public async Task The_time_of_a_line_is_counted_from_the_recording_time_the_microphone_opened_at()
    {
        using var rig = new TeachNarratorRig(Speaking((1.0, 2.0)), ScriptedEngine.Always(TeachNarratorRig.Said("later")));
        rig.Clock = TimeSpan.FromSeconds(100);
        await Hear(rig, 4);
        Assert.InRange(rig.Lines.Single().Start.TotalSeconds, 100.8, 101.0);
    }

    [Fact]
    public async Task Pausing_ends_what_is_being_said_and_resuming_goes_on_from_the_recording_time_then()
    {
        var spoken = new Queue<string>(["before the pause", "after the pause"]);
        using var rig = new TeachNarratorRig(Speaking((0.5, 3.0)), new ScriptedEngine(_ => TeachNarratorRig.Said(spoken.Dequeue())));
        Assert.Null(rig.Narrator.Start());
        rig.Capture.PushNow(_ => 0.1f, TimeSpan.FromSeconds(1.5));
        rig.Narrator.Pause();
        Assert.Equal(1, rig.Capture.StopCount);
        rig.Capture.PushNow(_ => 0.1f, TimeSpan.FromSeconds(5)); // nothing listens during the pause
        rig.Clock = TimeSpan.FromSeconds(1.5);
        rig.Narrator.Resume();
        Assert.Equal(2, rig.Capture.StartCount);
        rig.Capture.PushNow(_ => 0.1f, TimeSpan.FromSeconds(3));
        await rig.Narrator.StopAsync();
        Assert.Equal(["before the pause", "after the pause"], rig.Lines.Select(l => l.Text));
        Assert.True(rig.Lines[1].Start >= TimeSpan.FromSeconds(1.5));
    }

    [Fact]
    public async Task A_line_with_a_secret_in_it_is_dropped_and_said_once()
    {
        var spoken = new Queue<string>(["my password is hunter2", "the api key is sk_abc123def456"]);
        using var rig = new TeachNarratorRig(Speaking((0.5, 1.5), (4.0, 5.0)), new ScriptedEngine(_ => TeachNarratorRig.Said(spoken.Dequeue())));
        await Hear(rig, 7);
        Assert.Empty(rig.Lines);
        Assert.Single(rig.Notes);
        Assert.Contains("password or a key", rig.Notes[0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[BLANK_AUDIO]")]
    [InlineData("(music)")]
    [InlineData("*cough*")]
    public async Task A_tag_for_silence_or_background_sound_is_not_a_line(string tag)
    {
        using var rig = new TeachNarratorRig(Speaking((0.5, 1.5)), ScriptedEngine.Always(new Hypothesis([new HypothesisWord(tag, 0, 1)])));
        await Hear(rig, 3);
        Assert.Empty(rig.Lines);
    }

    [Fact]
    public async Task A_stretch_of_speech_over_thirty_seconds_is_cut_and_goes_on()
    {
        using var rig = new TeachNarratorRig(Speaking((0.5, 70.0)), ScriptedEngine.Always(TeachNarratorRig.Said("still talking")));
        await Hear(rig, 72);
        Assert.InRange(rig.Lines.Count, 3, 4);
        Assert.All(rig.Lines, l => Assert.True((l.End - l.Start).TotalSeconds <= TeachNarratorMaxSeconds + 0.1));
    }

    private const double TeachNarratorMaxSeconds = 30;

    [Fact]
    public async Task A_line_the_engine_cannot_recognise_is_said_and_the_next_one_still_is()
    {
        using var rig = new TeachNarratorRig(Speaking((0.5, 1.5), (4.0, 5.0)), ScriptedEngine.Always(TeachNarratorRig.Said("fine")));
        rig.Engine.FailNextDecode(new InvalidOperationException("the model went away"));
        await Hear(rig, 7);
        Assert.Equal(["fine"], rig.Lines.Select(l => l.Text));
        Assert.Single(rig.Notes);
        Assert.StartsWith("A line you said could not be recognised", rig.Notes[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Capture_in_stereo_at_another_rate_is_mixed_down_and_resampled()
    {
        using var rig = new TeachNarratorRig(Speaking((0.5, 1.5)), ScriptedEngine.Always(TeachNarratorRig.Said("stereo")), new CaptureFormat(48000, 2), resample: true);
        await Hear(rig, 3);
        Assert.Equal("stereo", Assert.Single(rig.Lines).Text);
        Assert.All(rig.Engine.Decodes, d => Assert.True(d.AudioLength.TotalSeconds > 1));
    }

    [Fact]
    public void A_microphone_that_is_blocked_missing_or_taken_and_a_missing_model_switch_narration_off_with_the_reason()
    {
        using var blocked = new TeachNarratorRig(Speaking(), ScriptedEngine.Always(Hypothesis.Empty), microphone: MicrophoneAccessStatus.Denied);
        Assert.StartsWith("The microphone is blocked in Windows", blocked.Narrator.Start(), StringComparison.Ordinal);
        Assert.Equal(0, blocked.Capture.StartCount);
        using var none = new TeachNarratorRig(Speaking(), ScriptedEngine.Always(Hypothesis.Empty), microphone: MicrophoneAccessStatus.NoDevice);
        Assert.StartsWith("There is no microphone", none.Narrator.Start(), StringComparison.Ordinal);
        using var taken = new TeachNarratorRig(Speaking(), ScriptedEngine.Always(Hypothesis.Empty));
        taken.Capture.FailStart = new IOException("in use");
        Assert.StartsWith("nth could not open the microphone", taken.Narrator.Start(), StringComparison.Ordinal);
        using var noModel = new TeachNarratorRig(Speaking(), ScriptedEngine.Always(Hypothesis.Empty));
        noModel.ModelNow = null;
        Assert.StartsWith("No speech model on this PC is set up", noModel.Narrator.Start(), StringComparison.Ordinal);
        Assert.Equal(0, noModel.Capture.StartCount);
    }

    [Fact]
    public async Task Stopping_twice_and_starting_twice_are_harmless_and_the_microphone_is_closed()
    {
        using var rig = new TeachNarratorRig(Speaking(), ScriptedEngine.Always(Hypothesis.Empty));
        Assert.Null(rig.Narrator.Start());
        Assert.Null(rig.Narrator.Start());
        Assert.Equal(1, rig.Capture.StartCount);
        await rig.Narrator.StopAsync();
        await rig.Narrator.StopAsync();
        Assert.False(rig.Capture.IsRunning);
    }

    [Fact]
    public async Task The_microphone_being_taken_by_another_app_stops_narration_and_says_so()
    {
        using var rig = new TeachNarratorRig(Speaking(), ScriptedEngine.Always(Hypothesis.Empty));
        Assert.Null(rig.Narrator.Start());
        rig.Narrator.OnDeviceEvent(CaptureDeviceEventKind.InUse);
        await rig.Narrator.StopAsync();
        Assert.Contains("The microphone is in use by another app, so narration stopped.", rig.Notes);
    }
}
