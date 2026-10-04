using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Threading;
using Marrow.App.Port.FirstRun;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Testing;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

// GR-1 adversarial review, round 2 (failing tests; the reviewer's, not the builder's).
[Collection("Headless")]
public sealed class GpuWarmupReview2Tests(HeadlessSession headless)
{
    private sealed class Throwing : ICustomDrawOperation
    {
        public Rect Bounds => new(0, 0, 1, 1);
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }
        public void Render(ImmediateDrawingContext context) => throw new InvalidOperationException("the owner's draw failed on the render thread");
    }

    // The slider liquid and the strike draw through WarmTarget.Context with LiquidTrack.Draw, which records a custom operation.
    // WarmTarget.Context is Avalonia.Base's PlatformDrawingContext, whose Custom catches a throw from Render and only logs it
    // (Avalonia 12.1.2, decompiled). So a liquid or strike draw that fails returns normally, the step is reported done and the
    // marker can be written: against GpuProgram.Draw's contract ("A throw faults the warm-up (it never completes as if
    // prepared)") and ED-NEW-f-gr-1 (7) and (11).
    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.programs")]
    public void REVIEW2_a_draw_that_fails_inside_a_warm_targets_operation_faults_its_program() => headless.OnUiThread(() =>
    {
        var program = GpuProgram.Draw("review2-throws-in-operation", FirstRunSteps.Liquid, context =>
        {
            using var target = GpuWarmup.WarmTarget.Open(context.Gpu, new SKImageInfo(16, 16, SKColorType.Rgba8888, SKAlphaType.Premul), 1);
            target.Context.Custom(new Throwing()); // what LiquidTrack.Draw does with its Operation
        });
        Assert.ThrowsAny<Exception>(() => program.Drawing(new GpuWarmContext(null, 1)));
    });

    // ED-NEW-f-gr-1 (5): "A failed run leaves the marker unwritten ..., so the next launch prepares again". The idle fallback (the
    // model chip's WhenIdle) never runs a work step, yet it writes the marker when its pass of the draws completes, and IsPrepared
    // reads only the marker. A first run that failed (or was cancelled) before its Voice hardware step, followed by any composer
    // in the session, therefore makes the next launch Skipped: the registered Voice hardware step never runs again.
    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.skip")]
    public void REVIEW2_the_idle_fallback_does_not_make_the_machine_prepared_while_a_registered_work_step_never_ran() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        var cap = GpuWarmup.IdleCap;
        GpuWarmup.IdleCap = TimeSpan.FromMilliseconds(50);
        using var cache = new GpuProgramCacheFolder();
        var source = GpuProgramCache.MachineSource;
        const string machine = "10de:2704:00000000:a1 Test GPU 1.2.3.4";
        GpuProgramCache.MachineSource = () => machine;
        GpuWarmup.PretendGpu = true;
        var ran = 0;
        using var work = GpuWarmup.Register(GpuProgram.Work("review2-voice-hardware", FirstRunSteps.Voice, _ => { ran++; return Task.CompletedTask; }));
        // as on ANGLE, the pass's draws go through the program cache: the entries a warm state needs, and the folder holding them
        using var entries = GpuWarmup.Register(GpuProgram.Draw("review2-entries", FirstRunSteps.Strike, _ => Interlocked.Add(ref GpuProgramCache.Writes, GpuWarmup.Steps)));
        File.WriteAllText(Path.Combine(cache.Folder, "key.txt"), "ANGLE (Test GPU)\nOpenGL ES 3.0.0\n" + GpuProgramCache.Build + "\n" + machine + "\n");
        for (var k = 0; k < GpuWarmup.Steps; k++) File.WriteAllBytes(Path.Combine(cache.Folder, k + ".bin"), [1, 2, 3]);
        var host = new Border { Width = 40, Height = 20 };
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100, Content = host };
        window.Show();
        var done = GpuWarmup.StepsDone;
        try
        {
            Assert.False(new FirstRunPreparation().IsPrepared);
            GpuWarmup.WhenIdle(host);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            FramesFor(TimeSpan.FromSeconds(60), () => GpuWarmup.StepsDone - done >= GpuWarmup.Steps);
            FramesFor(TimeSpan.FromSeconds(3), () => File.Exists(Path.Combine(cache.Root, "warm.txt")));
            Assert.Equal(0, ran);                                     // the idle fallback never ran the Voice hardware step
            Assert.False(new FirstRunPreparation().IsPrepared, "the next launch would skip the first run: Voice hardware never ran");
        }
        finally { GpuWarmup.PretendGpu = false; GpuWarmup.IdleCap = cap; GpuProgramCache.MachineSource = source; window.Close(); }
    });

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
