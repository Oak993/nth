using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.App.Port.FirstRun;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port.FirstRun;

// GR-1 adversarial review, round 4 (failing tests; the reviewer's, not the builder's).
//
// The round-3 CPU marker (warm-cpu.txt: build, "cpu", the machine) names the machine by GpuProgramCache.Machine(), the DXGI
// adapter ANGLE would make its device on, and that line is the same whether ANGLE came up or Avalonia fell back to its software
// renderer. IsWarm answers true from the CPU marker before it looks at the GPU's, so a launch that now draws on the GPU (ANGLE up
// on the same adapter and driver: a transient D3D11 failure at the first run, a driver repair that keeps its version string, the
// GLES library briefly locked) is Skipped, though no program was ever made or kept on that GPU (OD-0096: the first run happens
// again when the prepared state is invalid; the GPU's prepared state is the GPU marker, ED-252). ED-NEW-f-gr-1 (17) accepts this
// on the ground that "ED-252's idle fallback still prepares the programs at the first composer and writes the GPU marker"; with
// a registered work step (the Voice hardware check, VT-15) it does not: the idle fallback never runs a work step, and the marker
// waits for them (ED-NEW-f-gr-1 (14)), so the CPU marker stands, and every launch on the GPU is Skipped, for as long as the build
// and the driver last.
//
// And a work step is awaited with no bound and no cancellation of its own: one that never completes holds the first run (and
// GR-2's Getting ready sheet over the app, and FirstRunStartup's one run) forever, cancelled or not, where every draw step is
// bounded by StepTimeout.
[Collection("Headless")]
public sealed class FirstRunPreparationReview4Tests(HeadlessSession headless)
{
    private const string Machine = "10de:2704:00000000:a1 Test GPU 32.0.15.6094";

    [Fact] // GR-1.invalidation
    [Trait("Decision", "ED-252")]
    public void REVIEW4_a_launch_that_draws_on_the_GPU_after_a_first_run_on_the_CPU_renderer_runs_the_first_run_on_the_GPU() => headless.OnUiThread(() =>
    {
        using var cache = new GpuProgramCacheFolder();
        var source = GpuProgramCache.MachineSource;
        GpuProgramCache.MachineSource = () => Machine;
        GpuWarmup.PretendGpu = false; // launch 1: ANGLE could not be made, the app draws on the CPU renderer
        var first = new Avalonia.Controls.Window { Width = 200, Height = 100 };
        first.Show();
        Avalonia.Controls.Window? second = null;
        try
        {
            var run = new FirstRunPreparation().RunAsync(first, new FirstRunQuietScreen());
            Pump(TimeSpan.FromSeconds(60), () => run.IsCompleted);
            Assert.True(run.IsCompletedSuccessfully, run.Exception?.InnerException?.ToString());
            Assert.Equal(FirstRunOutcomeKind.Prepared, run.Result.Kind);
            Assert.True(File.Exists(Path.Combine(cache.Root, "warm-cpu.txt")));
            first.Close();

            // launch 2: ANGLE comes up on the same adapter and driver (DXGI names the same machine); the window draws on a GPU
            // context now, and no program has ever been made or kept on it (no GPU marker)
            GpuWarmup.PretendGpu = true;
            Assert.False(File.Exists(Path.Combine(cache.Root, "warm.txt")));
            second = new Avalonia.Controls.Window { Width = 200, Height = 100 };
            second.Show();
            var done = GpuWarmup.StepsDone;
            var next = new FirstRunPreparation().RunAsync(second, new FirstRunQuietScreen());
            Pump(TimeSpan.FromSeconds(60), () => next.IsCompleted);
            Assert.True(next.IsCompleted, "the second launch's first run did not end");
            Assert.True(next.Result.Kind != FirstRunOutcomeKind.Skipped,
                "the app now draws on the GPU and none of its programs was ever made there, yet the CPU renderer's marker made the first run Skipped: the programs compile at their first visible draw");
            Assert.True(GpuWarmup.StepsDone - done >= GpuWarmup.Steps, "the first run on the GPU did not draw the programs");
        }
        finally { GpuWarmup.PretendGpu = false; GpuProgramCache.MachineSource = source; first.Close(); second?.Close(); }
    });

    [Fact] // GR-1.skip
    [Trait("Decision", "ED-252")]
    public void REVIEW4_with_a_registered_work_step_the_CPU_marker_does_not_keep_a_machine_that_draws_on_the_GPU_prepared() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        var cap = GpuWarmup.IdleCap;
        GpuWarmup.IdleCap = TimeSpan.FromMilliseconds(50);
        using var cache = new GpuProgramCacheFolder();
        var source = GpuProgramCache.MachineSource;
        GpuProgramCache.MachineSource = () => Machine;
        GpuWarmup.PretendGpu = false; // launch 1: the CPU renderer
        var ran = 0;
        var work = GpuWarmup.Register(GpuProgram.Work("review4-voice-hardware", FirstRunSteps.Voice, _ => { ran++; return Task.CompletedTask; }));
        IDisposable? entries = null;
        var first = new Avalonia.Controls.Window { Width = 200, Height = 100 };
        first.Show();
        Avalonia.Controls.Window? second = null;
        try
        {
            var run = new FirstRunPreparation().RunAsync(first, new FirstRunQuietScreen());
            Pump(TimeSpan.FromSeconds(60), () => run.IsCompleted);
            Assert.Equal(FirstRunOutcomeKind.Prepared, run.Result.Kind);
            Assert.Equal(1, ran);
            Assert.True(new FirstRunPreparation().IsPrepared);
            first.Close();

            // launch 2, a new process: nothing done in it yet (the registration made again: a disposed one's record is forgotten),
            // ANGLE up on the same adapter and driver, its program cache keeping the draws' entries as on ANGLE
            work.Dispose();
            work = GpuWarmup.Register(GpuProgram.Work("review4-voice-hardware", FirstRunSteps.Voice, _ => { ran++; return Task.CompletedTask; }));
            entries = GpuWarmup.Register(GpuProgram.Draw("review4-entries", FirstRunSteps.Strike, _ => Interlocked.Add(ref GpuProgramCache.Writes, GpuWarmup.Steps)));
            File.WriteAllText(Path.Combine(cache.Folder, "key.txt"), "ANGLE (Test GPU)\nOpenGL ES 3.0.0\n" + GpuProgramCache.Build + "\n" + Machine + "\n");
            GpuWarmup.PretendGpu = true;
            // the first run is Skipped (the CPU marker) and accepted as such by ED-NEW-f-gr-1 (17), which counts on the idle
            // fallback to prepare the programs at the first composer and write the GPU marker
            var host = new Border { Width = 40, Height = 20 };
            second = new Avalonia.Controls.Window { Width = 200, Height = 100, Content = host };
            second.Show();
            var done = GpuWarmup.StepsDone;
            GpuWarmup.WhenIdle(host);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Pump(TimeSpan.FromSeconds(60), () => GpuWarmup.StepsDone - done >= GpuWarmup.Steps);
            Assert.True(GpuWarmup.StepsDone - done >= GpuWarmup.Steps, "the idle fallback did not draw the programs on the GPU");
            Pump(TimeSpan.FromSeconds(3), () => File.Exists(Path.Combine(cache.Root, "warm.txt")));
            Assert.True(File.Exists(Path.Combine(cache.Root, "warm.txt")) || !new FirstRunPreparation().IsPrepared,
                "the app draws on the GPU and its GPU marker is never written (the idle fallback never runs the work step), yet the CPU renderer's marker keeps the machine prepared: every later launch on this GPU is Skipped while the build and the driver last");
        }
        finally
        {
            GpuWarmup.PretendGpu = false;
            GpuWarmup.IdleCap = cap;
            GpuProgramCache.MachineSource = source;
            entries?.Dispose();
            work.Dispose();
            first.Close();
            second?.Close();
        }
    });

    [Fact] // GR-1.run
    [Trait("Decision", "ED-252")]
    public void REVIEW4_a_work_step_that_never_completes_does_not_hold_the_first_run_forever() => headless.OnUiThread(() =>
    {
        using var cache = new GpuProgramCacheFolder();
        var source = GpuProgramCache.MachineSource;
        GpuProgramCache.MachineSource = () => Machine;
        var timeout = GpuWarmup.StepTimeout;
        GpuWarmup.StepTimeout = TimeSpan.FromMilliseconds(500); // a draw step's bound, shortened; a work step has none
        GpuWarmup.PretendGpu = false; // the CPU renderer: no draws, only the work step
        var entered = false;
        using var stuck = GpuWarmup.Register(GpuProgram.Work("review4-stuck-voice-hardware", FirstRunSteps.Voice,
            _ => { entered = true; return new TaskCompletionSource().Task; })); // an audio driver that never answers
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100 };
        window.Show();
        try
        {
            var run = new FirstRunPreparation().RunAsync(window, new FirstRunQuietScreen());
            Pump(TimeSpan.FromSeconds(60), () => run.IsCompleted);
            Assert.True(entered, "the work step was never started");
            Assert.True(run.IsCompleted, "a work step that never completes holds the first run (and the Getting ready sheet over the app) forever");
            Assert.Equal(FirstRunOutcomeKind.Failed, run.Result.Kind);
            Assert.False(new FirstRunPreparation().IsPrepared);
        }
        finally { GpuWarmup.StepTimeout = timeout; GpuProgramCache.MachineSource = source; window.Close(); }
    });

    [Fact] // GR-1.run
    [Trait("Decision", "ED-252")]
    public void REVIEW4_a_cancelled_first_run_ends_though_its_work_step_does_not_answer_the_cancellation() => headless.OnUiThread(() =>
    {
        using var cache = new GpuProgramCacheFolder();
        var source = GpuProgramCache.MachineSource;
        GpuProgramCache.MachineSource = () => Machine;
        GpuWarmup.PretendGpu = false;
        var entered = false;
        using var stuck = GpuWarmup.Register(GpuProgram.Work("review4-deaf-voice-hardware", FirstRunSteps.Voice,
            _ => { entered = true; return new TaskCompletionSource().Task; }));
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100 };
        window.Show();
        using var cancellation = new CancellationTokenSource();
        try
        {
            var run = new FirstRunPreparation().RunAsync(window, new FirstRunQuietScreen(), cancellation.Token);
            Pump(TimeSpan.FromSeconds(30), () => entered);
            Assert.True(entered, "the work step was never started");
            cancellation.Cancel(); // the app quits while Getting ready shows
            Pump(TimeSpan.FromSeconds(10), () => run.IsCompleted);
            Assert.True(run.IsCompleted, "the first run was cancelled and still waits for a work step that does not answer");
            Assert.Equal(FirstRunOutcomeKind.Cancelled, run.Result.Kind);
        }
        finally { GpuProgramCache.MachineSource = source; window.Close(); }
    });

    [Fact] // GR-1.invalidation
    [Trait("Decision", "ED-252")]
    public void REVIEW4_a_GPU_marker_for_one_adapter_does_not_take_away_the_CPU_marker_of_another()
    {
        // a laptop drawn on its GPU at the desk and on the CPU renderer in another session whose adapter DXGI names otherwise (a
        // remote desktop session where ANGLE cannot be made): each has its own prepared state, as the GPU folders of the
        // machine's adapters are each kept (GpuProgramCache.RemoveOthers); proving one adapter on its GPU says nothing of the other
        var root = Path.Combine(Path.GetTempPath(), "gr1-review4-" + Guid.NewGuid().ToString("N"));
        var source = GpuProgramCache.MachineSource;
        const string remote = "1414:008c:00000000:00 Microsoft Basic Render Driver 10.0.26100.1";
        var machine = remote;
        GpuProgramCache.MachineSource = () => machine;
        GpuProgramCache.FolderOverride = root;
        try
        {
            GpuProgramCache.MarkWarmOnCpu();                                  // the remote session, prepared on the CPU renderer
            Assert.True(new FirstRunPreparation().IsPrepared);
            machine = Machine;                                                // at the desk, on the GPU
            var gpu = Path.Combine(root, "0123456789abcdef01234567");
            Directory.CreateDirectory(gpu);
            File.WriteAllText(Path.Combine(gpu, "key.txt"), "ANGLE (Test GPU)\nOpenGL ES 3.0.0\n" + GpuProgramCache.Build + "\n" + Machine + "\n");
            for (var k = 0; k < GpuWarmup.Steps; k++) File.WriteAllBytes(Path.Combine(gpu, k + ".bin"), [1, 2, 3]);
            GpuProgramCache.MarkWarm(gpu);
            Assert.True(new FirstRunPreparation().IsPrepared);
            machine = remote;                                                 // the remote session again: nothing of it changed
            Assert.True(new FirstRunPreparation().IsPrepared,
                "the GPU marker of the desk's adapter took away the remote session's CPU marker: every launch that alternates between them runs the first run again");
        }
        finally
        {
            GpuProgramCache.MachineSource = source;
            GpuProgramCache.FolderOverride = null;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
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
