using Marrow.App.Port.Voice;
using Marrow.App.Port.Voice.Notes;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Session;
using Marrow.Testing;
namespace Marrow.App.Tests.Port.Voice.Notes;

[Trait("Decision", "ED-351")]
public sealed class VoiceReleaseNoteTests
{
    [Theory]
    [InlineData(VoiceNoteCase.NoMicrophone, "ms-settings:sound")]
    [InlineData(VoiceNoteCase.MicrophoneUnplugged, "voice:microphone")]
    [InlineData(VoiceNoteCase.MicrophoneBusy, "voice:microphone")]
    [InlineData(VoiceNoteCase.ModelStillDownloading, "voice:speechModel")]
    [InlineData(VoiceNoteCase.ModelFailedToLoad, "voice:speechModel")]
    [InlineData(VoiceNoteCase.NoAvx2, "voice:engine")]
    [InlineData(VoiceNoteCase.CloudNoKey, "voice:openAiKey")]
    [InlineData(VoiceNoteCase.CloudKeyRefused, "voice:openAiKey")]
    [InlineData(VoiceNoteCase.CloudOutOfQuota, "voice:engine")]
    [InlineData(VoiceNoteCase.CloudNoNetwork, "voice:engine")]
    [InlineData(VoiceNoteCase.EngineFailure, "voice:page")]
    [InlineData(VoiceNoteCase.Blocked, "ms-settings:privacy-microphone")]
    public void Every_picked_note_preserves_its_exact_words_and_one_destination(VoiceNoteCase noteCase, string target)
    {
        var note = Marrow.App.Port.Voice.Notes.VoiceNotes.For(noteCase);
        var content = VoiceReleaseNotes.Content(note);
        Assert.Equal(note.Before, content.Text);
        Assert.Equal(note.Link, content.LinkText);
        Assert.Equal(target, content.LinkTarget);
        Assert.Equal(TimeSpan.FromSeconds(5.2), content.Length);
        Assert.True(content.Warning);
    }

    [Fact]
    public void An_error_is_resolved_from_the_bound_processor_fact_and_announced_once()
    {
        using var runtime = new TestRuntime();
        var capture = new ScriptedCapture();
        var mic = new ScriptedMicrophone();
        mic.Current = MicrophoneAccessStatus.NoDevice;
        var session = new VoiceSession(() => new(Marrow.Core.Port.Voice.VoiceOrbSettings.Default, Marrow.Core.Port.Voice.VoiceTypingSettings.Default, "en-US", new("tiny", "fake")), _ => { },
            new VoiceSeamSet(() => capture, () => ScriptedVad.Silence(), () => ScriptedEngine.Always(Hypothesis.Empty), () => mic, () => new FailClosedStreamingResampler()), new());
        var seen = new List<VoiceNoteContent?>();
        var spoken = new List<string>();
        using var binding = new VoiceReleaseNotes(session, () => "", () => false, () => false, new ScriptedProcessorFeatures(false), seen.Add, spoken.Add);
        session.Activate();
        runtime.RunUntilIdle();
        Assert.Equal("No microphone found. Set one up in ", seen.Last()!.Text);
        Assert.Equal(new[] { "No microphone found. Set one up in System, Sound." }, spoken);
        runtime.Advance(TimeSpan.FromSeconds(5.2));
        Assert.Null(seen.Last());
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_runtime_processor_adapter_reports_the_bound_fact(bool supported)
    {
        var reads = 0;
        var processor = new RuntimeProcessorFeatures(() => { reads++; return supported; });
        Assert.Equal(supported, processor.Avx2Supported);
        Assert.Equal(1, reads);
    }

    [Fact]
    public void Draft_refresh_restores_the_same_note_without_repeating_the_announcement()
    {
        using var rig = new Marrow.App.Tests.Fixtures.VoiceNoteRig();
        rig.Microphone.Current = MicrophoneAccessStatus.NoDevice;
        var shown = new List<VoiceNoteContent?>();
        var spoken = new List<string>();
        using var notes = new VoiceReleaseNotes(rig.Session, () => rig.Draft, () => false, () => false, rig.Processor, shown.Add, spoken.Add);
        rig.Session.Activate(); rig.Settle();
        var first = shown.Single();
        notes.RefreshSurface();
        Assert.Equal(2, shown.Count);
        Assert.Same(first, shown.Last());
        rig.Draft = "existing text";
        notes.RefreshSurface();
        Assert.Same(first, shown.Last());
        Assert.Single(spoken);
    }

}
