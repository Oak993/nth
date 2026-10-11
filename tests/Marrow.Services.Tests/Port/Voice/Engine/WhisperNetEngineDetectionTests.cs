using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Port.Voice.Engine;

namespace Marrow.Services.Tests.Port.Voice.Engine;

// ED-NEW-voicefix-4: the engine hands the transcriber Whisper's language detection with probabilities, one call at a time with the passes (it runs the encoder), with the
// model it had (loaded again after an idle release) and the pass's thread count. Nothing in it decodes or translates.
public sealed class WhisperNetEngineDetectionTests : IDisposable
{
    private readonly EngineRig rig = new(cores: 8);

    public void Dispose() => rig.Dispose();

    private static readonly string[] Hindustani = ["hi", "ur"];

    private async Task Load() => await EngineRig.Guard(rig.Engine.LoadAsync(EngineRig.Turbo, new EngineLoadOptions(), CancellationToken.None));

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-NEW-voicefix-4")]
    public async Task The_backends_odds_are_returned_for_the_runners_up_asked_with_the_pass_threads_and_nothing_is_decoded()
    {
        await Load();
        rig.Backend.DetectScript = runnersUp => new LanguageOdds("en", 0.6f, runnersUp[0], 0.3f);
        var odds = await EngineRig.Guard(rig.Engine.DetectLanguageAsync(EngineRig.Audio(2.5), Hindustani, CancellationToken.None));
        Assert.Equal(new LanguageOdds("en", 0.6f, "hi", 0.3f), odds);
        var detection = Assert.Single(rig.Backend.Detections);
        Assert.Equal(Hindustani, detection.RunnersUp);
        Assert.Equal(40000, detection.Samples);
        Assert.Equal(ThreadCount.For(8), detection.Threads);
        Assert.Empty(rig.Backend.Decodes);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-NEW-voicefix-4")]
    public async Task A_failed_detection_is_null_and_empty_audio_is_never_detected()
    {
        await Load();
        Assert.Null(await EngineRig.Guard(rig.Engine.DetectLanguageAsync(EngineRig.Audio(1), Hindustani, CancellationToken.None)));
        Assert.Null(await EngineRig.Guard(rig.Engine.DetectLanguageAsync(ReadOnlyMemory<float>.Empty, Hindustani, CancellationToken.None)));
        Assert.Single(rig.Backend.Detections);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-NEW-voicefix-4")]
    public async Task A_detection_waits_for_a_decode_in_flight_and_never_runs_beside_it()
    {
        await Load();
        var release = rig.Backend.HoldDecodes();
        var pass = rig.Engine.TranscribeAsync(EngineRig.Audio(1), new DecodeRequest("hi", "", DecodeMode.Live, TimeSpan.Zero), CancellationToken.None);
        var detection = rig.Engine.DetectLanguageAsync(EngineRig.Audio(1), Hindustani, CancellationToken.None);
        Assert.False(detection.IsCompleted);
        Assert.Empty(rig.Backend.Detections);
        release();
        await EngineRig.Guard(pass);
        await EngineRig.Guard(detection);
        Assert.Single(rig.Backend.Detections);
        Assert.Equal(1, rig.Backend.MaxInFlight);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-NEW-voicefix-4")]
    public async Task After_the_idle_release_a_detection_reloads_the_model_it_had_and_rearms_the_release()
    {
        await Load();
        rig.Runtime.Advance(WhisperNetEngine.IdleUnloadAfter);
        Assert.False(rig.Engine.IsLoaded);
        await EngineRig.Guard(rig.Engine.DetectLanguageAsync(EngineRig.Audio(1), Hindustani, CancellationToken.None));
        Assert.Equal(2, rig.Backend.Loads.Count);
        Assert.Equal(rig.Backend.Loads[0].ModelPath, rig.Backend.Loads[1].ModelPath);
        Assert.True(rig.Engine.IsLoaded);
        rig.Runtime.Advance(WhisperNetEngine.IdleUnloadAfter);
        Assert.False(rig.Engine.IsLoaded);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-NEW-voicefix-4")]
    public async Task A_cancelled_detection_throws_and_releases_the_lock()
    {
        await Load();
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Engine.DetectLanguageAsync(EngineRig.Audio(1), Hindustani, cancel.Token));
        await EngineRig.Guard(rig.Engine.DetectLanguageAsync(EngineRig.Audio(1), Hindustani, CancellationToken.None));
        Assert.Single(rig.Backend.Detections);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-NEW-voicefix-4")]
    public void The_detection_uses_the_recorded_Whisper_net_member_and_no_translate_option()
    {
        Assert.Contains("WhisperProcessor.DetectLanguageWithProbability(ReadOnlySpan`1,ReadOnlySpan`1)", WhisperNetBackend.UsedMembers);
        Assert.DoesNotContain(WhisperNetBackend.UsedMembers, m => m.Contains("Translate", StringComparison.OrdinalIgnoreCase));
    }
}
