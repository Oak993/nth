using Avalonia.Controls;
using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Probe;
using Marrow.Testing;
using static Marrow.App.Tests.Fixtures.VoicePillProbe;

namespace Marrow.App.Tests.Port.Voice;

[Collection("Headless")]
[Trait("Decision", "ED-351")]
public sealed class MountedVoiceStepDownTests(HeadlessSession headless)
{
    [Fact]
    public void Coalesced_speed_progress_keeps_the_actual_smaller_model_and_only_the_slow_question_opens_a_popover()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig();
            var models = new VoicePrepModelSource();
            models.Installed.Add("large-v3-turbo-q5_0");
            models.Installed.Add("medium-q8_0");
            var clock = new ManualTimeProvider();
            var engine = new HeldVoiceSpeedEngine(clock);
            var preparation = new VoicePreparation(new ScriptedProbe(ScriptedMachines.CpuOnlyLaptop), models, () => engine,
                new AppSettingsVoiceAccess(rig.App.Settings), () => new float[160320], VoicePrepRig.App, VoicePrepRig.Runtime, clock);
            var question = new VoiceSpeedQuestion(preparation);
            question.No(); question.CloseWelcome();
            VoiceFirstRun.Bind(rig.App.Settings, new VoiceFirstRun(preparation, question, models));
            var (view, window) = rig.Open(1000);
            try
            {
                rig.Main.OnMain(() => view.Voice!.Activate());
                rig.Main.Loop.RunUntil(() => view.Voice!.PreparationProgress is { Id: VoicePreparation.SelfTestId, ModelId: "large-v3-turbo-q5_0" });
                rig.PumpWindow(window); rig.Elapse(window, .8);
                Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(Pill(view)), text => text.Text == "Testing speed…");
                rig.Main.OnMain(engine.Complete);
                rig.Main.Loop.RunUntil(() => engine.Model == "medium-q8_0");
                rig.Main.RunUntilIdle(); rig.PumpWindow(window);
                Assert.Equal("medium-q8_0", view.Voice!.PreparationProgress!.ModelId);
                var total = (long)Math.Round(ModelCatalog.Find("medium-q8_0")!.Bytes / 1_000_000.0);
                var words = total.ToString(System.Globalization.CultureInfo.InvariantCulture) + " of " + total.ToString(System.Globalization.CultureInfo.InvariantCulture) + " MB";
                Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(Pill(view)), text => text.Text == words);
                Assert.False(view.Voice.SetupOpen);
                rig.Main.OnMain(engine.Complete);
                rig.Main.Loop.RunUntil(() => !view.Voice.Preparing);
                rig.PumpWindow(window); rig.Elapse(window, .8);
                Assert.True(view.Voice.SetupOpen);
                Assert.Equal(VoiceQuestionState.Slow, question.State);
                Assert.False(Pill(view).Open);
                Assert.Equal(0, rig.Capture.StartCount);
                Assert.Equal("medium-q8_0", rig.App.Settings.VoiceHardware!.Model);
                Assert.Empty(models.Installs);
            }
            finally { rig.Close(window); }
        });
    }
}
