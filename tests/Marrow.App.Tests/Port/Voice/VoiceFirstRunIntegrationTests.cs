using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;

namespace Marrow.App.Tests.Port.Voice;

// T23: consent and shared preparation at the actual orb entry point, entirely over scripted parts.
[Trait("Rule", "R-TEST-10")] // Scripted seams only; no model, network or hardware.
public sealed class VoiceFirstRunIntegrationTests
{
    [Fact]
    [Trait("Feature", "VT-15")]
    public async Task First_orb_click_after_no_grants_consent_and_prepares_without_starting_capture()
    {
        var rig = VoiceQuestionRig.Pc();
        var flow = new VoiceFirstRun(rig.Prep.Preparation, rig.Question, rig.Prep.Models);
        rig.Prep.Models.ThrowOnAnyTransfer = true;
        rig.Question.No();
        rig.Question.CloseWelcome();
        Assert.Empty(rig.Prep.Models.Installs);
        rig.Prep.Models.ThrowOnAnyTransfer = false;
        Assert.False(await flow.OrbAsync(CancellationToken.None)); // setup click displays setup; next click listens
        Assert.True(rig.Prep.Preparation.HasConsent);
        Assert.Equal(["large-v3-turbo-q8_0"], rig.Prep.Models.Installs);
        Assert.True(await flow.OrbAsync(CancellationToken.None));
        Assert.Equal(rig.Prep.Models.PathFor(ModelCatalog.Find(rig.Record!.Model)!), flow.Model(rig.Record.Model)!.Path);
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    public async Task Early_orb_click_joins_welcomes_download_and_replays_real_progress()
    {
        var rig = VoiceQuestionRig.Pc();
        var flow = new VoiceFirstRun(rig.Prep.Preparation, rig.Question, rig.Prep.Models);
        rig.Prep.Models.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var yes = rig.Question.YesAsync(null, CancellationToken.None);
        rig.Question.CloseWelcome();
        var orb = flow.OrbAsync(CancellationToken.None);
        Assert.False(orb.IsCompleted);
        Assert.Equal(VoicePreparation.DownloadId, flow.Progress!.Id);
        Assert.Equal(1.0, flow.Progress.Fraction);
        Assert.Single(rig.Prep.Models.Installs);
        rig.Prep.Models.Gate.SetResult();
        await yes;
        Assert.False(await orb);
        Assert.True(await flow.OrbAsync(CancellationToken.None));
    }

    [Fact]
    [Trait("Feature", "WV-1")]
    public async Task Slow_question_at_orb_blocks_capture_until_answered_and_keeps_the_answer()
    {
        var rig = VoiceQuestionRig.Slow();
        var flow = new VoiceFirstRun(rig.Prep.Preparation, rig.Question, rig.Prep.Models);
        await rig.Question.YesAsync(null, CancellationToken.None);
        rig.Question.CloseWelcome();
        var count = rig.Prep.Models.Installs.Count;
        Assert.False(await flow.OrbAsync(CancellationToken.None));
        Assert.True(rig.Question.AsksAtOrb);
        Assert.Equal(count, rig.Prep.Models.Installs.Count);
        await rig.Question.AnswerAsync(LiveWordsChoice.Accurate, CancellationToken.None);
        Assert.True(await flow.OrbAsync(CancellationToken.None));
        Assert.Equal(LiveWordsChoice.Accurate, rig.Prep.Settings.Typing.SlowChoice);
        Assert.False(rig.Record!.LiveWords);
    }

    [Fact]
    [Trait("Feature", "WV-1")]
    public async Task Restart_keeps_an_unanswered_slow_question_without_another_download_or_test()
    {
        var rig = VoiceQuestionRig.Slow();
        await rig.Question.YesAsync(null, CancellationToken.None);
        var again = new VoiceSpeedQuestion(rig.Prep.Preparation);
        var loads = rig.Prep.Engine.Loads.Count;
        await again.YesAsync(null, CancellationToken.None);
        Assert.Equal(VoiceQuestionState.Slow, again.State);
        Assert.Equal(loads, rig.Prep.Engine.Loads.Count);
    }
}
