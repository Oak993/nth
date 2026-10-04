using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.App.Port.FirstRun;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port.FirstRun;

// GR-1 review round 4, the fixes (the failing REVIEW4 tests are in FirstRunPreparationReview4Tests, the reviewer's). ED-NEW-f-gr-1
// (18) to (20). Every wait below is a pump with a time limit.
[Collection("Headless")]
public sealed class FirstRunPreparationReview4FixTests(HeadlessSession headless)
{
    private const string Machine = "10de:2704:00000000:a1 Test GPU 32.0.15.6094";

    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.skip")]
    [Trait("Decision", "ED-252")] // the renderer is read from Avalonia's platform: the headless one has no graphics platform
    public void The_renderer_is_read_from_the_pinned_Avalonia_and_the_headless_platform_draws_on_the_CPU() => headless.OnUiThread(() =>
    {
        Assert.False(GpuWarmup.PretendGpu);
        var read = GpuWarmup.PlatformDrawsOnGpu();
        Assert.True(read == false, "the graphics platform could not be read from the pinned Avalonia (" + (read?.ToString() ?? "null") + "): the CPU marker would count for nothing");
        Assert.True(GpuWarmup.DrawsOnCpu);
        GpuWarmup.PretendGpu = true;
        try { Assert.False(GpuWarmup.DrawsOnCpu); } // a test pretending a GPU draws on one
        finally { GpuWarmup.PretendGpu = false; }
    });

    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.run")]
    [Trait("Decision", "ED-252")]
    public void A_work_step_that_answers_after_its_bound_is_not_done_and_leaves_the_machine_unprepared() => headless.OnUiThread(() =>
    {
        using var cache = new GpuProgramCacheFolder();
        var source = GpuProgramCache.MachineSource;
        GpuProgramCache.MachineSource = () => Machine;
        var timeout = GpuWarmup.StepTimeout;
        GpuWarmup.StepTimeout = TimeSpan.FromMilliseconds(150);
        GpuWarmup.PretendGpu = false;
        var late = new TaskCompletionSource();
        using var slow = GpuWarmup.Register(GpuProgram.Work("review4fix-slow-voice-hardware", FirstRunSteps.Voice, _ => late.Task));
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100 };
        window.Show();
        try
        {
            var run = new FirstRunPreparation().RunAsync(window, new Screen());
            Pump(TimeSpan.FromSeconds(30), () => run.IsCompleted);
            Assert.True(run.IsCompleted, "the first run did not end");
            Assert.Equal(FirstRunOutcomeKind.Failed, run.Result.Kind);
            Assert.Contains("review4fix-slow-voice-hardware", run.Result.Error, StringComparison.Ordinal);
            Assert.Contains("0.15 s", run.Result.Error, StringComparison.Ordinal); // the bound is told: StepTimeout
            late.SetResult();                            // it answers after the run has ended: too late to count
            Pump(TimeSpan.FromMilliseconds(300), () => false);
            Assert.False(File.Exists(Path.Combine(cache.Root, "warm-cpu.txt")), "a work step that was not done in time left a CPU marker");
            Assert.False(new FirstRunPreparation().IsPrepared);
        }
        finally { GpuWarmup.StepTimeout = timeout; GpuProgramCache.MachineSource = source; window.Close(); }
    });

    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.run")]
    [Trait("Decision", "ED-252")]
    public void A_work_step_that_throws_after_the_run_gave_up_on_it_is_not_left_unobserved() => headless.OnUiThread(() =>
    {
        using var cache = new GpuProgramCacheFolder();
        var source = GpuProgramCache.MachineSource;
        GpuProgramCache.MachineSource = () => Machine;
        var timeout = GpuWarmup.StepTimeout;
        GpuWarmup.StepTimeout = TimeSpan.FromMilliseconds(100);
        GpuWarmup.PretendGpu = false;
        var unobserved = 0;
        EventHandler<UnobservedTaskExceptionEventArgs> count = (_, _) => Interlocked.Increment(ref unobserved);
        TaskScheduler.UnobservedTaskException += count;
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100 };
        window.Show();
        try
        {
            Task<FirstRunOutcome> run;
            using (GpuWarmup.Register(GpuProgram.Work("review4fix-throwing-voice-hardware", FirstRunSteps.Voice, async _ =>
            {
                await Task.Delay(400);
                throw new InvalidOperationException("the audio driver gave up");
            })))
            {
                run = new FirstRunPreparation().RunAsync(window, new Screen());
                Pump(TimeSpan.FromSeconds(30), () => run.IsCompleted);
                Assert.Equal(FirstRunOutcomeKind.Failed, run.Result.Kind);
                Pump(TimeSpan.FromMilliseconds(800), () => false); // the step throws after the run is over
            }
            for (var i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); Pump(TimeSpan.FromMilliseconds(50), () => false); }
            Assert.Equal(0, Volatile.Read(ref unobserved));
        }
        finally { TaskScheduler.UnobservedTaskException -= count; GpuWarmup.StepTimeout = timeout; GpuProgramCache.MachineSource = source; window.Close(); }
    });

    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.skip")]
    [Trait("Decision", "ED-252")] // mutant M5: the CPU marker must wait for the registered work steps
    public void An_idle_pass_on_the_CPU_renderer_with_a_work_step_not_run_leaves_no_CPU_marker() => headless.OnUiThread(() =>
    {
        var asked = IdlePassOnTheCpu(registerWorkStep: true, markerWithin: TimeSpan.FromSeconds(1.5), out var marker);
        Assert.True(asked > 0, "the idle pass never asked for a step");
        Assert.False(marker, "an idle pass never runs the Voice hardware work step, yet it left a CPU marker: the next launch would skip the hardware check");
    });

    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.skip")]
    [Trait("Decision", "ED-252")] // the twin: with no work step to wait for the same pass does leave the marker, and soon
    public void An_idle_pass_on_the_CPU_renderer_with_no_work_step_leaves_the_CPU_marker() => headless.OnUiThread(() =>
    {
        var asked = IdlePassOnTheCpu(registerWorkStep: false, markerWithin: TimeSpan.FromSeconds(20), out var marker);
        Assert.True(asked > 0, "the idle pass never asked for a step");
        Assert.True(marker, "the idle pass on the CPU renderer wrote no CPU marker");
    });

    private static int IdlePassOnTheCpu(bool registerWorkStep, TimeSpan markerWithin, out bool marker)
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        var cap = GpuWarmup.IdleCap;
        GpuWarmup.IdleCap = TimeSpan.FromMilliseconds(50);
        using var cache = new GpuProgramCacheFolder();
        var source = GpuProgramCache.MachineSource;
        GpuProgramCache.MachineSource = () => Machine;
        GpuWarmup.PretendGpu = false;
        IDisposable? work = registerWorkStep
            ? GpuWarmup.Register(GpuProgram.Work("review4fix-idle-voice-hardware", FirstRunSteps.Voice, _ => Task.CompletedTask))
            : null;
        var host = new Border { Width = 40, Height = 20 };
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100, Content = host };
        window.Show();
        try
        {
            var asked = GpuWarmup.StepsAsked;
            GpuWarmup.WhenIdle(host);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var file = Path.Combine(cache.Root, "warm-cpu.txt");
            Pump(markerWithin, () => File.Exists(file));
            marker = File.Exists(file);
            return GpuWarmup.StepsAsked - asked;
        }
        finally { GpuWarmup.IdleCap = cap; GpuProgramCache.MachineSource = source; work?.Dispose(); window.Close(); }
    }

    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.skip")]
    [Trait("Decision", "ED-252")]
    public void The_CPU_marker_is_kept_for_each_adapter_and_a_GPU_marker_takes_away_only_its_own() => headless.OnUiThread(() =>
    {
        var root = Path.Combine(Path.GetTempPath(), "gr1-review4fix-" + Guid.NewGuid().ToString("N"));
        var source = GpuProgramCache.MachineSource;
        const string remote = "1414:008c:00000000:00 Microsoft Basic Render Driver 10.0.26100.1";
        const string third = "1002:73bf:00000000:c1 Another GPU 31.0.1.1";
        var machine = remote;
        GpuProgramCache.MachineSource = () => machine;
        GpuProgramCache.FolderOverride = root;
        try
        {
            GpuProgramCache.MarkWarmOnCpu();
            machine = third; GpuProgramCache.MarkWarmOnCpu();
            machine = Machine; GpuProgramCache.MarkWarmOnCpu();
            foreach (var m in new[] { remote, third, Machine }) { machine = m; Assert.True(new FirstRunPreparation().IsPrepared, m + " lost its CPU marker"); }

            machine = Machine;
            var gpu = Path.Combine(root, "0123456789abcdef01234567");
            Directory.CreateDirectory(gpu);
            GpuProgramCache.MarkWarm(gpu);                 // proven on its GPU: only its own CPU marker goes
            Assert.True(GpuProgramCache.WarmedOnCpu(root, remote));
            Assert.True(GpuProgramCache.WarmedOnCpu(root, third));
            Assert.False(GpuProgramCache.WarmedOnCpu(root, Machine));

            GpuProgramCache.MarkWarmOnCpu();               // written again beside the others, not over them
            Assert.True(GpuProgramCache.WarmedOnCpu(root, remote) && GpuProgramCache.WarmedOnCpu(root, third) && GpuProgramCache.WarmedOnCpu(root, Machine));

            machine = third;
            GpuProgramCache.MarkWarm(gpu);
            machine = remote;
            GpuProgramCache.MarkWarm(gpu);
            machine = Machine;
            GpuProgramCache.MarkWarm(gpu);
            Assert.False(File.Exists(Path.Combine(root, "warm-cpu.txt")), "the last CPU marker went and its file stays");
        }
        finally
        {
            GpuProgramCache.MachineSource = source;
            GpuProgramCache.FolderOverride = null;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    });

    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.skip")]
    [Trait("Decision", "ED-252")] // mutant M8: the marker is written before RunAsync completes, not after a delay
    public void The_CPU_marker_is_written_by_the_time_the_first_run_completes() => headless.OnUiThread(() =>
    {
        using var cache = new GpuProgramCacheFolder();
        var source = GpuProgramCache.MachineSource;
        GpuProgramCache.MachineSource = () => Machine;
        GpuWarmup.PretendGpu = false;
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100 };
        window.Show();
        try
        {
            var run = new FirstRunPreparation().RunAsync(window, new Screen());
            Pump(TimeSpan.FromSeconds(60), () => run.IsCompleted);
            Assert.Equal(FirstRunOutcomeKind.Prepared, run.Result.Kind);
            // no pump from here: the run has ended, so the marker is there
            Assert.True(File.Exists(Path.Combine(cache.Root, "warm-cpu.txt")), "the first run ended before its CPU marker was written");
            Assert.True(new FirstRunPreparation().IsPrepared);
        }
        finally { GpuProgramCache.MachineSource = source; window.Close(); }
    });

    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.skip")]
    [Trait("Decision", "ED-252")] // mutant M8, on the GPU
    public void The_GPU_marker_is_written_by_the_time_the_first_run_completes() => headless.OnUiThread(() =>
    {
        using var cache = new GpuProgramCacheFolder();
        var source = GpuProgramCache.MachineSource;
        GpuProgramCache.MachineSource = () => Machine;
        GpuWarmup.PretendGpu = true;
        // the cache took an entry for each program that draws (the headless renderer has no ANGLE to make them)
        using var entries = GpuWarmup.Register(GpuProgram.Draw("review4fix-entries", FirstRunSteps.Strike, _ => Interlocked.Add(ref GpuProgramCache.Writes, GpuWarmup.Steps)));
        File.WriteAllText(Path.Combine(cache.Folder, "key.txt"), "ANGLE (Test GPU)\nOpenGL ES 3.0.0\n" + GpuProgramCache.Build + "\n" + Machine + "\n");
        for (var k = 0; k < GpuWarmup.Steps; k++) File.WriteAllBytes(Path.Combine(cache.Folder, k + ".bin"), [1, 2, 3]);
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100 };
        window.Show();
        try
        {
            var run = new FirstRunPreparation().RunAsync(window, new Screen());
            Pump(TimeSpan.FromSeconds(60), () => run.IsCompleted);
            Assert.Equal(FirstRunOutcomeKind.Prepared, run.Result.Kind);
            Assert.True(File.Exists(Path.Combine(cache.Root, "warm.txt")), "the first run ended before its GPU marker was written");
            Assert.True(new FirstRunPreparation().IsPrepared);
        }
        finally { GpuWarmup.PretendGpu = false; GpuProgramCache.MachineSource = source; window.Close(); }
    });

    private sealed class Screen : IFirstRunScreen
    {
        public void Begin(IReadOnlyList<FirstRunStepInfo> steps) { }
        public void Report(FirstRunProgress progress) { }
        public Task EndAsync(FirstRunOutcome outcome) => Task.CompletedTask;
    }

    private static void Pump(TimeSpan span, Func<bool> done)
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
