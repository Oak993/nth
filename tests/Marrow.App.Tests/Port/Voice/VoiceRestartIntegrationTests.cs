using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Probe;

namespace Marrow.App.Tests.Port.Voice;

[Trait("Rule", "R-TEST-10")] // Scripted seams only; no model, network or hardware.
public sealed class VoiceRestartIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Feature", "VT-15")]
    [Trait("Feature", "WV-1")]
    public async Task Fresh_preparation_validates_a_saved_record_without_retiming_and_restores_an_open_question(bool slow)
    {
        var rig = slow ? VoiceQuestionRig.Slow() : VoiceQuestionRig.Pc();
        await rig.Question.YesAsync(null, CancellationToken.None);
        var record = rig.Record!;
        var part = rig.Prep.Preparation.HardwarePart;
        rig.Prep.Models.ThrowOnAnyTransfer = true;
        var machine = slow ? ScriptedMachines.CpuOnlyLaptop : ScriptedMachines.OwnersDesktop;
        var restarted = new VoicePreparation(new ScriptedProbe(machine), rig.Prep.Models, () => throw new InvalidOperationException("must not retime"),
            rig.Prep.Settings, () => throw new InvalidOperationException("must not read a clip"), VoicePrepRig.App, VoicePrepRig.Runtime,
            currentKey: () => record.Key, currentPart: () => part);
        var question = new VoiceSpeedQuestion(restarted);
        question.CloseWelcome();
        var flow = new VoiceFirstRun(restarted, question, rig.Prep.Models);
        Assert.Equal(!slow, await flow.OrbAsync(CancellationToken.None));
        Assert.Equal(slow, question.AsksAtOrb);
        if (slow)
        {
            await question.AnswerAsync(LiveWordsChoice.Accurate, CancellationToken.None);
            Assert.True(await flow.OrbAsync(CancellationToken.None));
        }
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    public async Task If_startup_key_was_unavailable_the_first_probe_reuses_the_current_saved_record()
    {
        var rig = VoiceQuestionRig.Pc();
        await rig.Question.YesAsync(null, CancellationToken.None);
        rig.Prep.Models.ThrowOnAnyTransfer = true;
        var restarted = new VoicePreparation(new ScriptedProbe(ScriptedMachines.OwnersDesktop), rig.Prep.Models,
            () => throw new InvalidOperationException("must not retime"), rig.Prep.Settings,
            () => throw new InvalidOperationException("must not read clip"), VoicePrepRig.App, VoicePrepRig.Runtime);
        restarted.GrantConsent();
        var result = await restarted.PrepareAsync(null, CancellationToken.None);
        Assert.Equal(VoicePreparationStatus.Prepared, result.Status);
        Assert.Same(rig.Record, result.Record);
    }
}
