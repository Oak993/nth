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
            GpuWarmup.WhenIdle(host);
            Assert.True(GpuWarmup.IsScheduled(host));
            Assert.Null(ElementComposition.GetElementChildVisual(host)); // nothing at the call
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
            Assert.Null(ElementComposition.GetElementChildVisual(host)); // nor before a frame was drawn
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();             // the first frame
            Dispatcher.UIThread.RunJobs();                               // then idle
            var primer = Assert.IsType<CompositionCustomVisual>(ElementComposition.GetElementChildVisual(host));
            Assert.Equal(new Vector(1, 1), primer.Size);                 // a pixel, drawing nothing on screen
            GpuWarmup.WhenIdle(host);                                   // again: nothing new
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
        Assert.True(GpuWarmup.IsScheduled(chip));
    });

    [Fact]
    [Trait("Decision", "ED-252")] // a warm-up step draws nothing where the knob will be drawn later, and leaves the knob's surfaces made
    public void Warm_up_steps_build_the_knobs_own_surfaces_and_draw_nothing_visible()
    {
        var renderer = JewelKnobRenderer.For(null, 96);
        for (var step = 0; step < GpuWarmup.Steps; step++) renderer.Warm(step);
        using var bitmap = new SKBitmap(new SKImageInfo(48, 48, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        var u = new JewelUniforms();
        new JewelOrb(JewelMaterial.For(EffortBrand.Claude)).Frame(0, 0, 0, Math.PI, false, 96, 2, true, u);
        var before = GC.GetAllocatedBytesForCurrentThread();
        renderer.Draw(canvas, u.Values, new SKRect(0, 0, 48, 48), 1); // the first real draw: nothing left to make
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(255, bitmap.GetPixel(24, 24).Alpha);
    }

    [Fact]
    [Trait("Decision", "ED-252")] // something animates while a frame-clock request waits, or a registered activity answers true
    public void The_warm_up_sees_animation_in_waiting_frame_requests_and_registered_activities() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        Assert.False(GpuWarmup.Animating());
        frames.RequestFrame(_ => { });
        Assert.True(GpuWarmup.Animating());
        frames.Advance(TimeSpan.FromMilliseconds(16));
        Assert.False(GpuWarmup.Animating());
        var spinning = true;
        using (GpuWarmup.AddActivity(() => spinning))
        {
            Assert.True(GpuWarmup.Animating());
            spinning = false;
            Assert.False(GpuWarmup.Animating());
            spinning = true;
        }
        Assert.False(GpuWarmup.Animating()); // removed
    });

    [Fact]
    [Trait("Decision", "ED-252")] // the idle fallback asks for no step while something animates, and steps once it stops
    public void The_idle_warm_up_waits_while_something_animates() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        var cap = GpuWarmup.IdleCap;
        GpuWarmup.IdleCap = TimeSpan.FromSeconds(60); // well past this test's frames: the cap must not be what ends the wait here
        var host = new Border { Width = 40, Height = 20 };
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100, Content = host };
        window.Show();
        try
        {
            frames.RequestFrame(_ => { }); // an animation: its next frame waits
            GpuWarmup.WhenIdle(host);
            var asked = GpuWarmup.StepsAsked;
            FramesFor(TimeSpan.FromMilliseconds(600), () => false);
            Assert.True(asked == GpuWarmup.StepsAsked, $"{GpuWarmup.StepsAsked - asked} steps asked while animating"); // all animating: no step
            frames.Advance(TimeSpan.FromMilliseconds(16)); // the animation ends
            FramesFor(TimeSpan.FromSeconds(10), () => GpuWarmup.StepsAsked > asked);
            Assert.True(asked + 1 == GpuWarmup.StepsAsked, $"{GpuWarmup.StepsAsked - asked} steps asked once quiet"); // the first step (the CPU renderer then ends it: nothing to compile)
        }
        finally { GpuWarmup.IdleCap = cap; window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-252")] // an animation that never stops does not keep the popover unready: after the cap it steps anyway
    public void The_idle_warm_up_steps_anyway_after_its_cap() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        var cap = GpuWarmup.IdleCap;
        GpuWarmup.IdleCap = TimeSpan.FromSeconds(1);
        var host = new Border { Width = 40, Height = 20 };
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100, Content = host };
        window.Show();
        try
        {
            frames.RequestFrame(_ => { }); // animating throughout
            var since = DateTime.UtcNow;
            GpuWarmup.WhenIdle(host);
            var asked = GpuWarmup.StepsAsked;
            FramesFor(TimeSpan.FromSeconds(20), () => GpuWarmup.StepsAsked > asked);
            var waited = (DateTime.UtcNow - since).TotalMilliseconds;
            Assert.True(asked + 1 == GpuWarmup.StepsAsked, $"{GpuWarmup.StepsAsked - asked} steps asked at the cap");
            Assert.True(waited >= 1000, $"stepped after {waited:F0} ms, before the cap ran out");
            Assert.True(GpuWarmup.Animating()); // still animating: forced
        }
        finally { GpuWarmup.IdleCap = cap; window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-252")] // WarmAsync (OD-0096's first-run screen awaits it): completes, at once on the CPU renderer, no step reported
    public void Warm_async_completes_on_the_cpu_renderer_with_no_steps() => headless.OnUiThread(() =>
    {
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100 };
        window.Show();
        try
        {
            var reports = new List<GpuWarmupStep>();
            var asked = GpuWarmup.StepsAsked;
            var task = GpuWarmup.WarmAsync(window, new SyncProgress<GpuWarmupStep>(reports.Add));
            FramesFor(TimeSpan.FromSeconds(10), () => task.IsCompleted);
            Assert.True(task.IsCompletedSuccessfully);
            Assert.Empty(reports);                         // nothing compiled: no step to report
            Assert.Equal(asked + 1, GpuWarmup.StepsAsked); // the first step asked, answered by the CPU renderer
            var again = GpuWarmup.WarmAsync(window);       // repeatable: the same answer
            FramesFor(TimeSpan.FromSeconds(10), () => again.IsCompleted);
            Assert.True(again.IsCompletedSuccessfully);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-252")] // IsWarm: the marker names this build and a GPU folder holding programs; anything else is not warm
    public void Is_warm_only_when_the_cache_holds_programs_of_this_build()
    {
        var root = Path.Combine(Path.GetTempPath(), "jewel-warm-" + Guid.NewGuid().ToString("N"));
        var gpu = Path.Combine(root, "0123456789abcdef01234567");
        GpuProgramCache.FolderOverride = root;
        try
        {
            Assert.False(GpuWarmup.IsWarm);                  // no folder
            Directory.CreateDirectory(gpu);
            GpuProgramCache.MarkWarm(gpu);
            Assert.False(GpuWarmup.IsWarm);                  // marked, but no program in it
            File.WriteAllBytes(Path.Combine(gpu, "a.bin"), [1, 2, 3]);
            Assert.True(GpuWarmup.IsWarm);
            File.WriteAllLines(Path.Combine(root, "warm.txt"), ["another build", Path.GetFileName(gpu)]);
            Assert.False(GpuWarmup.IsWarm);                  // another build's programs
            GpuProgramCache.MarkWarm(Path.Combine(root, "fedcba9876543210fedcba98"));
            Assert.False(GpuWarmup.IsWarm);                  // another GPU's folder, not there
            GpuProgramCache.MarkWarm(gpu);
            Assert.True(GpuWarmup.IsWarm);
        }
        finally
        {
            GpuProgramCache.FolderOverride = null;
            Directory.Delete(root, recursive: true);
        }
    }

    // frames about every 10 ms (the warm-up's checks take a timer's turn between frames) until done or the time is up
    private static void FramesFor(TimeSpan span, Func<bool> done)
    {
        var since = DateTime.UtcNow;
        while (!done() && DateTime.UtcNow - since < span)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
            Dispatcher.UIThread.RunJobs();
        }
    }
}
