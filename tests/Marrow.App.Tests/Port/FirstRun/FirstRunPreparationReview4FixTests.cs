using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Port.FirstRun;

// GR-1 review round 4, the fixes (the failing REVIEW4 tests are in FirstRunPreparationReview4Tests). ED-NEW-f-gr-1 (18) to (20).
public sealed class FirstRunPreparationReview4FixTests
{
    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.skip")]
    [Trait("Decision", "ED-252")] // the renderer is read from Avalonia's platform: the headless one has no graphics platform
    public void The_renderer_is_read_from_the_pinned_Avalonia_and_the_headless_platform_draws_on_the_CPU()
    {
        Assert.False(GpuWarmup.PretendGpu);
        var read = GpuWarmup.PlatformDrawsOnGpu();
        Assert.True(read == false, "read: " + (read?.ToString() ?? "null")); // null would be "could not be read": the reflection on the pinned Avalonia broke
        Assert.True(GpuWarmup.DrawsOnCpu);
    }
}
