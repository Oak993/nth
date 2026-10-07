using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Port.Voice;

[Trait("Decision", "ED-351")]
public sealed class VoicePreparationCancellationTests
{
    [Fact]
    public async Task Cancel_during_the_probe_prevents_a_late_download_and_retry_is_fresh()
    {
        var probe = new HeldVoiceProbe();
        var models = new VoicePrepModelSource();
        var clock = new Marrow.Testing.ManualTimeProvider();
        var engine = new VoicePrepEngine(clock, VoiceQuestionRig.PcSeconds);
        var settings = new VoicePrepSettingsFake();
        var preparation = new VoicePreparation(probe, models, () => engine, settings, () => new float[160320], VoicePrepRig.App, VoicePrepRig.Runtime, clock);
        var question = new VoiceSpeedQuestion(preparation);
        var first = question.YesAsync(null, CancellationToken.None);
        Assert.True(probe.Started);
        question.Cancel();
        probe.Complete();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Empty(models.Installs);
        Assert.Empty(engine.Loads);
        Assert.Equal(0, settings.RecordWrites);
        Assert.Equal(VoiceQuestionState.Ask, question.State);
        await question.YesAsync(null, CancellationToken.None);
        Assert.Single(models.Installs);
        Assert.Equal(1, settings.RecordWrites);
        Assert.Equal(VoiceQuestionState.Ready, question.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Immediate_retry_waits_for_a_canceled_collaborator_to_release_then_starts_a_fresh_run(bool cancelQueued)
    {
        var models = new CancellationIgnoringVoiceModels();
        var clock = new Marrow.Testing.ManualTimeProvider();
        var engine = new VoicePrepEngine(clock, VoiceQuestionRig.PcSeconds);
        var settings = new VoicePrepSettingsFake();
        var preparation = new VoicePreparation(new Marrow.Services.Port.Voice.Probe.ScriptedProbe(Marrow.Services.Port.Voice.Probe.ScriptedMachines.OwnersDesktop),
            models, () => engine, settings, () => new float[160320], VoicePrepRig.App, VoicePrepRig.Runtime, clock);
        await preparation.ProbeAsync(null, CancellationToken.None);
        var question = new VoiceSpeedQuestion(preparation);
        var first = question.YesAsync(null, CancellationToken.None);
        Assert.Equal(1, models.Installs);
        question.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.True(models.FirstToken.IsCancellationRequested);
        Assert.Empty(engine.Loads);
        Assert.Equal(0, settings.RecordWrites);
        var retry = question.YesAsync(null, CancellationToken.None);
        Assert.False(retry.IsCompleted);
        Assert.Equal(1, models.Installs); // The old collaborator still owns resources; a retry must not overlap it.
        if (cancelQueued)
        {
            question.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => retry);
            Assert.Equal(0, settings.RecordWrites);
            Assert.Equal(VoiceQuestionState.Ask, question.State);
            retry = question.YesAsync(null, CancellationToken.None);
        }
        models.ReleaseFirst();
        await retry;
        Assert.Equal(2, models.Installs);
        Assert.Equal(1, models.MaxConcurrent);
        Assert.Single(engine.Loads);
        Assert.Equal(1, settings.RecordWrites);
        Assert.Equal(VoiceQuestionState.Ready, question.State);
    }
}
