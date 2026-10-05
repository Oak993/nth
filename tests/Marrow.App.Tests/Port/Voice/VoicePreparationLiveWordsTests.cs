using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Probe;

namespace Marrow.App.Tests.Port.Voice;

// VT-15, V-T103 (V-7.7, V-10.5; Q-V-1 C, lead rulings R1 to R8): LiveWords is read from the stored record, and the record's liveWords is what the timing and the stored answer decide; both paths.
public sealed class VoicePreparationLiveWordsTests
{
    private static readonly Dictionary<string, double> Slow = new() { ["large-v3-turbo-q5_0"] = 3.0, ["medium-q8_0"] = 1.0 };

    private static async Task<string> HardwarePartAsync(ScriptedMachine machine) =>
        HardwareKey.ComputeHardwarePart(await new ScriptedProbe(machine).RunAsync(null, CancellationToken.None));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Feature", "VT-15")]
    [Trait("Spec", "V-T103")]
    [Trait("Decision", "ED-289")]
    public void LiveWords_is_the_stored_records_switch(bool live) =>
        Assert.Equal(live, VoicePreparation.LiveWordsOf(new VoiceHardwareRecord { LiveWords = live }));

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Spec", "V-T103")]
    [Trait("Decision", "ED-289")]
    public void With_no_stored_record_LiveWords_is_on_the_live_default_of_OD_0091() =>
        Assert.True(VoicePreparation.LiveWordsOf(null));

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Spec", "V-T103")]
    [Trait("Decision", "ED-289")]
    public async Task The_switch_read_back_through_IsPrepared_is_what_the_first_run_stored_for_a_fast_processor()
    {
        var rig = new VoicePrepRig(ScriptedMachines.CpuOnlyLaptop, new Dictionary<string, double> { ["large-v3-turbo-q5_0"] = 1.0 });
        await rig.FirstRunAsync();
        Assert.True(rig.Preparation.IsPrepared(out var record));
        Assert.True(VoicePreparation.LiveWordsOf(record));
        Assert.True(record!.LiveWords);
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Spec", "V-T103")]
    [Trait("Decision", "ED-289")]
    public async Task A_slow_processor_with_no_answer_keeps_the_accurate_model_with_text_after_the_stop_and_asks()
    {
        var rig = new VoicePrepRig(ScriptedMachines.CpuOnlyLaptop, Slow);
        var (_, _, result) = await rig.FirstRunAsync();
        Assert.Equal("large-v3-turbo-q5_0", result.Record!.Model);
        Assert.False(VoicePreparation.LiveWordsOf(result.Record));
        Assert.True(result.AskSlowQuestion);
        Assert.Single(rig.Engine.Decodes);
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Spec", "V-T103")]
    [Trait("Decision", "ED-289")]
    public async Task A_stored_live_answer_takes_the_next_smaller_rung_with_live_words_on_and_installs_it_without_timing_again()
    {
        var rig = new VoicePrepRig(ScriptedMachines.CpuOnlyLaptop, Slow);
        rig.Settings.Typing = VoiceTypingSettings.Default with { SlowChoice = LiveWordsChoice.Live, SlowChoiceKey = await HardwarePartAsync(ScriptedMachines.CpuOnlyLaptop) };
        var (_, _, result) = await rig.FirstRunAsync();
        Assert.Equal("large-v3-turbo-q5_0", result.Record!.Candidate);
        Assert.Equal("medium-q8_0", result.Record.Model);
        Assert.True(VoicePreparation.LiveWordsOf(result.Record));
        Assert.False(result.AskSlowQuestion);
        Assert.Equal(["large-v3-turbo-q5_0", "medium-q8_0"], rig.Models.Installs);
        Assert.Single(rig.Engine.Decodes);
        Assert.Equal("medium-q8_0", rig.Settings.Typing.Model);
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Spec", "V-T103")]
    [Trait("Decision", "ED-289")]
    public async Task A_stored_accurate_answer_keeps_the_model_with_live_words_off_and_asks_nothing()
    {
        var rig = new VoicePrepRig(ScriptedMachines.CpuOnlyLaptop, Slow);
        rig.Settings.Typing = VoiceTypingSettings.Default with { SlowChoice = LiveWordsChoice.Accurate, SlowChoiceKey = await HardwarePartAsync(ScriptedMachines.CpuOnlyLaptop) };
        var (_, _, result) = await rig.FirstRunAsync();
        Assert.Equal("large-v3-turbo-q5_0", result.Record!.Model);
        Assert.False(result.Record.LiveWords);
        Assert.False(result.AskSlowQuestion);
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Spec", "V-T103")]
    [Trait("Decision", "ED-289")]
    public async Task An_answer_given_for_other_hardware_decides_nothing_here()
    {
        var rig = new VoicePrepRig(ScriptedMachines.CpuOnlyLaptop, Slow);
        rig.Settings.Typing = VoiceTypingSettings.Default with { SlowChoice = LiveWordsChoice.Live, SlowChoiceKey = await HardwarePartAsync(ScriptedMachines.FourCoreMachine) };
        var (_, _, result) = await rig.FirstRunAsync();
        Assert.Equal("large-v3-turbo-q5_0", result.Record!.Model);
        Assert.False(result.Record.LiveWords);
        Assert.True(result.AskSlowQuestion);
    }
}
