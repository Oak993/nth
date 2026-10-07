using Marrow.App.Port.Voice;
using Marrow.App.Port.Voice.Notes;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Port.Voice.Notes;

[Collection("Headless")]
[Trait("Decision", "ED-351")]
public sealed class MountedVoiceProcessorTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(true, "ModelFailedToLoad", "voice:speechModel")]
    [InlineData(false, "NoAvx2", "voice:engine")]
    public void A_real_mounted_load_failure_uses_the_bound_instruction_fact(bool supported, string note, string destination)
    {
        headless.OnUiThread(() =>
        {
            using var processor = ProcessorFeatureSeams.Current.Override(new ScriptedProcessorFeatures(supported));
            using var rig = Marrow.App.Tests.Views.Composer.ComposerVoicePillGeometryTests.Quiet();
            rig.Engine.FailLoads(new DllNotFoundException("scripted load failure"));
            var (view, window) = rig.Open(900);
            try
            {
                rig.Main.OnMain(() => view.Voice!.Activate());
                rig.Elapse(window, .7);
                var line = LogicalQuery.SelfAndDescendants<VoiceNoteLine>(view).Single(value => value.Content.Id == "voice.error." + note && value.IsVisible);
                Assert.Equal(destination, line.Content.LinkTarget);
                Assert.NotNull(line.Link);
                Assert.NotNull(view.Voice!.Session.FailureCause);
            }
            finally { rig.Close(window); }
        });
    }

    [Fact]
    public void The_production_processor_fact_is_the_runtime_CPU_and_OS_support_fact() =>
        Assert.Equal(System.Runtime.Intrinsics.X86.Avx2.IsSupported, new RuntimeProcessorFeatures().Avx2Supported);
}
