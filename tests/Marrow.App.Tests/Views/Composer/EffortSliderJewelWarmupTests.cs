using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

// OD-0090 / ED-252 (lead's ruling 2026-10-03, the owner's zero-stutter rule): the knob's programs are primed on the render
// thread before the effort popover can be opened, after the window's first frame at idle priority, never at attach.
// (The GPU compile itself is measured natively: EffortSliderNativeFrameTests.The_knob_warm_up_*.)
[Collection("Headless")]
public sealed class EffortSliderJewelWarmupTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-252")] // the primer is added only after the window's first frame, at idle: never at attach, never in startup's path
    public void The_warm_up_waits_for_the_windows_first_frame_and_idle() => headless.OnUiThread(() =>
    {
        var host = new Border { Width = 40, Height = 20 };
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100, Content = host };
        window.Show();
        try
        {
            JewelWarmup.Schedule(host);
            Assert.True(JewelWarmup.IsScheduled(host));
            Assert.Null(ElementComposition.GetElementChildVisual(host)); // nothing at the call
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
            Assert.Null(ElementComposition.GetElementChildVisual(host)); // nor before a frame was drawn
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();             // the first frame
            Dispatcher.UIThread.RunJobs();                               // then idle
            var primer = Assert.IsType<CompositionCustomVisual>(ElementComposition.GetElementChildVisual(host));
            Assert.Equal(new Vector(1, 1), primer.Size);                 // a pixel, drawing nothing on screen
            JewelWarmup.Schedule(host);                                   // again: nothing new
            Assert.Same(primer, ElementComposition.GetElementChildVisual(host));
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-252")] // the composer's model chip schedules it (it is what opens the effort popover)
    public void The_model_chip_schedules_the_warm_up() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness(withRuntime: false);
        var model = new AppModel();
        foreach (var provider in SwiftEnum<ProviderKind>.AllCases)
            if (!provider.IsAPIKeyBased) model.Settings.SetBinaryPath(Path.Combine(harness.Scratch, "missing.exe"), provider);
        var chip = new ModelEffortButton(new ChatThread(Guid.NewGuid(), ProviderKind.Claude, "opus", null, RuntimeMode.Supervised), false);
        Assert.True(JewelWarmup.IsScheduled(chip));
    });

    [Fact]
    [Trait("Decision", "ED-252")] // a warm-up step draws nothing where the knob will be drawn later, and leaves the knob's surfaces made
    public void Warm_up_steps_build_the_knobs_own_surfaces_and_draw_nothing_visible()
    {
        var renderer = JewelKnobRenderer.For(null, 96);
        renderer.Warm(0);
        renderer.Warm(1);
        using var bitmap = new SKBitmap(new SKImageInfo(48, 48, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        var u = new JewelUniforms();
        new JewelOrb(JewelMaterial.For(EffortBrand.Claude)).Frame(0, 0, 0, Math.PI, false, 96, 2, true, u);
        var before = GC.GetAllocatedBytesForCurrentThread();
        renderer.Draw(canvas, u.Values, new SKRect(0, 0, 48, 48), 1); // the first real draw: nothing left to make
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(255, bitmap.GetPixel(24, 24).Alpha);
    }
}
