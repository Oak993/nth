using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Logging;
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

// GR-1 review round 2, the fixes (ED-NEW-f-gr-1 (14) and (15)). The reviewer's failing tests are GpuWarmupReview2Tests; these pin
// what the fixes must also keep: the app's own log sink is untouched, a failed draw does not leak into the next target, a work step
// done by a first run lets a later idle pass write the marker, and one that never ran does not.
[Collection("Headless")]
public sealed class GpuWarmupReview2FixTests(HeadlessSession headless)
{
    private sealed class Throwing(string message) : ICustomDrawOperation
    {
        public Rect Bounds => new(0, 0, 1, 1);
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }
        public void Render(ImmediateDrawingContext context) => throw new InvalidOperationException(message);
    }

    private sealed class Quiet : ICustomDrawOperation
    {
        public Rect Bounds => new(0, 0, 1, 1);
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }
        public void Render(ImmediateDrawingContext context) { }
    }

    private sealed class Recording : ILogSink
    {
        public List<string> Errors { get; } = [];
        public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Error;
        public void Log(LogEventLevel level, string area, object? source, string messageTemplate) => Errors.Add(area + ": " + messageTemplate);
        public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues) =>
            Errors.Add(area + ": " + messageTemplate + " " + string.Join(" ", propertyValues.Select(v => v?.GetType().Name)));
    }

    private static readonly SKImageInfo Small = new(16, 16, SKColorType.Rgba8888, SKAlphaType.Premul);

    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.programs")]
    public void A_target_throws_the_operations_own_exception_when_it_is_let_go() => headless.OnUiThread(() =>
    {
        var target = GpuWarmup.WarmTarget.Open(null, Small, 1);
        target.Context.Custom(new Throwing("the shader failed"));
        Assert.Equal("the shader failed", target.Failure?.Message);
        var error = Assert.Throws<InvalidOperationException>(target.Dispose);
        Assert.Equal("the shader failed", error.Message);
        target.Dispose(); // once: a second Dispose does nothing
    });

    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.programs")]
    public void A_target_whose_operations_drew_is_let_go_quietly() => headless.OnUiThread(() =>
    {
        using var target = GpuWarmup.WarmTarget.Open(null, Small, 1);
        target.Context.Custom(new Quiet());
        Assert.Null(target.Failure);
    });

    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.programs")]
    public void The_apps_log_sink_is_put_back_and_still_hears_the_error_while_a_target_is_open() => headless.OnUiThread(() =>
    {
        var app = new Recording();
        var before = Logger.Sink;
        Logger.Sink = app;
        try
        {
            var target = GpuWarmup.WarmTarget.Open(null, Small, 1);
            Assert.NotSame(app, Logger.Sink);                   // watching
            target.Context.Custom(new Throwing("x"));
            Assert.Throws<InvalidOperationException>(target.Dispose);
            Assert.Same(app, Logger.Sink);                      // the app's own, back
            Assert.Single(app.Errors);                          // it heard the one Avalonia logged
            Assert.StartsWith("Visual:", app.Errors[0], StringComparison.Ordinal);
        }
        finally { Logger.Sink = before; }
    });

    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.programs")]
    public void With_no_app_sink_none_is_left_behind_and_a_failed_draw_does_not_fail_the_next_target() => headless.OnUiThread(() =>
    {
        var before = Logger.Sink;
        Logger.Sink = null;
        try
        {
            var first = GpuWarmup.WarmTarget.Open(null, Small, 1);
            first.Context.Custom(new Throwing("first"));
            Assert.Throws<InvalidOperationException>(first.Dispose);
            Assert.Null(Logger.Sink);
            using var second = GpuWarmup.WarmTarget.Open(null, Small, 1);
            second.Context.Custom(new Quiet());
            Assert.Null(second.Failure);
        }
        finally { Logger.Sink = before; }
    });

    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.programs")]
    public void A_failure_belongs_to_the_target_of_its_own_thread_and_nested_targets_each_keep_theirs() => headless.OnUiThread(() =>
    {
        var before = Logger.Sink;
        Logger.Sink = null;
        try
        {
            var outer = GpuWarmup.WarmTarget.Open(null, Small, 1);
            var inner = GpuWarmup.WarmTarget.Open(null, Small, 1);
            inner.Context.Custom(new Throwing("inner"));
            Assert.Equal("inner", Assert.Throws<InvalidOperationException>(inner.Dispose).Message);
            Assert.NotNull(Logger.Sink);                        // the outer still watches
            outer.Context.Custom(new Throwing("outer"));
            Assert.Equal("outer", Assert.Throws<InvalidOperationException>(outer.Dispose).Message);
            Assert.Null(Logger.Sink);
        }
        finally { Logger.Sink = before; }
    });

    // The pass of the draws, as on ANGLE: its programs go through the program cache (the entries a warm state needs, the folder
    // holding them), the machine named as a test GPU.
    private sealed class Pass : IDisposable
    {
        private readonly GpuProgramCacheFolder cache = new();
        private readonly Func<string?> source = GpuProgramCache.MachineSource!;
        private readonly TimeSpan cap = GpuWarmup.IdleCap;
        private readonly IDisposable entries;
        public const string Machine = "10de:2704:00000000:a1 Test GPU 1.2.3.4";
        public string Root => cache.Root;

        public Pass()
        {
            GpuProgramCache.MachineSource = () => Machine;
            GpuWarmup.IdleCap = TimeSpan.FromMilliseconds(50);
            GpuWarmup.PretendGpu = true;
            entries = GpuWarmup.Register(GpuProgram.Draw("review2fix-entries", FirstRunSteps.Strike, _ => Interlocked.Add(ref GpuProgramCache.Writes, GpuWarmup.Steps)));
            File.WriteAllText(Path.Combine(cache.Folder, "key.txt"), "ANGLE (Test GPU)\nOpenGL ES 3.0.0\n" + GpuProgramCache.Build + "\n" + Machine + "\n");
            for (var k = 0; k < GpuWarmup.Steps; k++) File.WriteAllBytes(Path.Combine(cache.Folder, k + ".bin"), [1, 2, 3]);
        }

        public void Dispose()
        {
            GpuWarmup.PretendGpu = false;
            GpuWarmup.IdleCap = cap;
            GpuProgramCache.MachineSource = source;
            entries.Dispose();
            cache.Dispose();
        }
    }

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

    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.skip")]
    public void A_first_run_that_did_the_work_step_leaves_the_machine_prepared() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        using var pass = new Pass();
        var ran = 0;
        using var work = GpuWarmup.Register(GpuProgram.Work("review2fix-voice-hardware", FirstRunSteps.Voice, _ => { ran++; return Task.CompletedTask; }));
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100 };
        window.Show();
        try
        {
            Assert.False(new FirstRunPreparation().IsPrepared);
            var task = GpuWarmup.WarmAsync(window);
            FramesFor(TimeSpan.FromSeconds(30), () => task.IsCompleted);
            Assert.True(task.IsCompletedSuccessfully, task.Exception?.InnerException?.Message);
            FramesFor(TimeSpan.FromSeconds(10), () => File.Exists(Path.Combine(pass.Root, "warm.txt")));
            Assert.Equal(1, ran);
            Assert.True(new FirstRunPreparation().IsPrepared, "the first run did every step: the next launch skips it");
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.skip")]
    public void A_work_step_that_failed_in_the_first_run_leaves_the_machine_unprepared_after_a_later_idle_pass() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        using var pass = new Pass();
        using var work = GpuWarmup.Register(GpuProgram.Work("review2fix-voice-fails", FirstRunSteps.Voice, _ => throw new InvalidOperationException("no microphone check")));
        var host = new Border { Width = 40, Height = 20 };
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100, Content = host };
        window.Show();
        var done = GpuWarmup.StepsDone;
        try
        {
            var first = GpuWarmup.WarmAsync(window);
            FramesFor(TimeSpan.FromSeconds(30), () => first.IsCompleted);
            Assert.True(first.IsFaulted);                                       // the first run failed at its work step
            GpuWarmup.WhenIdle(host);                                           // a composer's model chip, later in the session
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            FramesFor(TimeSpan.FromSeconds(60), () => GpuWarmup.StepsDone - done >= GpuWarmup.Steps * 2);
            FramesFor(TimeSpan.FromSeconds(3), () => File.Exists(Path.Combine(pass.Root, "warm.txt")));
            Assert.False(File.Exists(Path.Combine(pass.Root, "warm.txt")), "the idle pass wrote the marker though the work step never succeeded");
            Assert.False(new FirstRunPreparation().IsPrepared);
        }
        finally { window.Close(); }
    });
}
