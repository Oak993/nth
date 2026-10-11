using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.App.Port.FirstRun;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.FirstRun;

// GR-1 (BUILD-PLAN 2.2; OD-0096 and the OD-0098 amendments: Look I, Words W1, Download D3; ED-252): the first-run preparation service.
// W1's steps in order, real progress only (a clock never advances it), the prepared-state check, skipped when prepared. GR-2's screen
// is an interface here (IFirstRunScreen), played by a recorder.
[Collection("Headless")]
public sealed class FirstRunPreparationTests(HeadlessSession headless)
{
    // ----- the steps and their names (W1) -----

    [Fact] // GR-1.order
    [Trait("Decision", "ED-252")]
    public void W1s_steps_are_named_and_ordered_as_the_picked_words_draw_them() // OD-0098: "Look I, Words W1" (gr_lib.py:28-29)
    {
        Assert.Equal(["Graphics card", "Effort slider knob", "Composer light", "Slider liquid", "Fast mode lightning", "Voice hardware"],
            FirstRunSteps.W1.Select(s => s.Name));
        Assert.Equal(["gpu", "knob", "lights", "liquid", "strike", "hw"], FirstRunSteps.W1.Select(s => s.Id));
        Assert.Equal(FirstRunSteps.W1.Select(s => s.Order).Order(), FirstRunSteps.W1.Select(s => s.Order));
        Assert.Equal(FirstRunSteps.W1.Count, FirstRunSteps.W1.Select(s => s.Order).Distinct().Count());
    }

    [Fact] // GR-1.order
    [Trait("Decision", "ED-252")]
    public void The_plan_of_this_build_lists_the_steps_that_have_work_in_W1_order_and_leaves_out_voice_hardware_until_it_is_registered()
    {
        var plan = new FirstRunPreparation().Plan();
        Assert.Equal(["Graphics card", "Effort slider knob", "Composer light", "Slider liquid", "Fast mode lightning"], plan.Select(s => s.Step.Name));
        // the adapter read; the knob's four programs; the light's three, the liquid and the strike, each in frame-sized parts
        // (ED-NEW-warmupsplit-1: 5 + 3 + 3 for the lights, 8 for the liquid, 8 legacy and 102 Mix for the strike,
        // each brand's first fresh frame in its 14 GPU draws: ED-NEW-warmupsplit-4)
        Assert.Equal([1, 4, 11, 8, 110], plan.Select(s => s.Parts));
    }

    [Fact] // GR-1.order
    [Trait("Decision", "ED-252")]
    public void A_step_registered_later_joins_the_plan_in_its_place_without_any_change_to_GpuWarmup()
    {
        var between = new FirstRunStep("between", "A step between", 25);
        using var voice = GpuWarmup.Register(GpuProgram.Work("voice-hardware", FirstRunSteps.Voice, _ => Task.CompletedTask));
        using var step = GpuWarmup.Register(GpuProgram.Draw("between-program", between, _ => { }));
        using var strike = GpuWarmup.Register(GpuProgram.Draw("strike-two", FirstRunSteps.Strike, _ => { }));
        var plan = new FirstRunPreparation().Plan();
        Assert.Equal(["Graphics card", "Effort slider knob", "A step between", "Composer light", "Slider liquid", "Fast mode lightning", "Voice hardware"],
            plan.Select(s => s.Step.Name));
        Assert.Equal([1, 4, 1, 11, 8, 111, 1], plan.Select(s => s.Parts)); // a new strike program is one more part of its step
        voice.Dispose(); step.Dispose(); strike.Dispose();
        Assert.Equal(5, new FirstRunPreparation().Plan().Count);
    }

    [Fact] // GR-1.order
    [Trait("Decision", "ED-252")]
    public void With_nothing_registered_the_plan_is_the_graphics_card_alone_and_a_program_naming_gpu_joins_it()
    {
        var log = new List<string>();
        Assert.Equal([(FirstRunSteps.Graphics, 1)], Scripted([], log).Plan().Select(s => (s.Step, s.Parts)));
        var registry = Programs(("g1", FirstRunSteps.Graphics with { Name = "Whatever a program says", Order = 11 }), ("k1", FirstRunSteps.Knob));
        var plan = Scripted(registry, log).Plan();
        Assert.Equal([(FirstRunSteps.Graphics, 2), (FirstRunSteps.Knob, 1)], plan.Select(s => (s.Step, s.Parts))); // the graphics card is W1's
    }

    [Fact] // GR-1.progress
    [Trait("Decision", "ED-252")]
    public async Task A_program_under_the_graphics_step_is_a_second_part_of_it()
    {
        var log = new List<string>();
        var registry = Programs(("g1", FirstRunSteps.Graphics), ("k1", FirstRunSteps.Knob));
        var run = Scripted(registry, log, warm: (_, progress, _) =>
        {
            progress.Report(new GpuWarmupStep(0, 2, "g1", 1, false, "gpu"));
            progress.Report(new GpuWarmupStep(1, 2, "k1", 1, false, "knob"));
            return Task.CompletedTask;
        });
        await run.RunAsync(null!, new FirstRunRecordingScreen(log));
        Assert.Equal(["begin:gpu,knob", "gpu:Running:0/2", "gpu:Running:1/2", "gpu:Done:2/2", "knob:Running:0/1", "knob:Done:1/1", "end:Prepared"], log);
    }

    [Fact] // GR-1.progress
    [Trait("Decision", "ED-252")]
    public void A_steps_fraction_is_its_parts_done_over_its_parts()
    {
        var step = FirstRunSteps.Knob;
        Assert.Equal(0, new FirstRunProgress(step, 1, 5, FirstRunStepState.Running, 0, 4, TimeSpan.Zero).Fraction);
        Assert.Equal(0.75, new FirstRunProgress(step, 1, 5, FirstRunStepState.Running, 3, 4, TimeSpan.Zero).Fraction);
        Assert.Equal(1, new FirstRunProgress(step, 1, 5, FirstRunStepState.Done, 4, 4, TimeSpan.Zero).Fraction);
        Assert.Equal(1, new FirstRunProgress(step, 1, 5, FirstRunStepState.Done, 0, 0, TimeSpan.Zero).Fraction); // a step with no parts is done or it is not
        Assert.Equal(0, new FirstRunProgress(step, 1, 5, FirstRunStepState.Running, 0, 0, TimeSpan.Zero).Fraction);
    }

    // ----- real progress only -----

    [Fact] // GR-1.progress
    [Trait("Decision", "ED-252")]
    public async Task Each_event_follows_the_work_it_reports_and_the_steps_run_in_order()
    {
        var log = new List<string>();
        var screen = new FirstRunRecordingScreen(log);
        var registry = Programs(("k1", FirstRunSteps.Knob), ("k2", FirstRunSteps.Knob), ("l1", FirstRunSteps.Lights));
        var run = Scripted(registry, log, graphics: _ => { log.Add("graphics"); return Task.CompletedTask; },
            warm: (_, progress, _) =>
            {
                foreach (var (i, program) in registry.Index())
                {
                    log.Add("work:" + program.Name);
                    progress.Report(new GpuWarmupStep(i, registry.Count, program.Name, 1, false, program.Step.Id));
                }
                return Task.CompletedTask;
            });
        var outcome = await run.RunAsync(null!, screen);
        Assert.Equal(FirstRunOutcomeKind.Prepared, outcome.Kind);
        Assert.Equal(
        [
            "begin:gpu,knob,lights",
            "gpu:Running:0/1", "graphics", "gpu:Done:1/1",
            "knob:Running:0/2", "work:k1", "knob:Running:1/2", "work:k2", "knob:Done:2/2",
            "lights:Running:0/1", "work:l1", "lights:Done:1/1",
            "end:Prepared",
        ], log);
    }

    [Fact] // GR-1.progress
    [Trait("Decision", "ED-252")]
    public async Task A_clock_never_advances_the_progress_only_completed_work_does()
    {
        var log = new List<string>();
        var screen = new FirstRunRecordingScreen(log);
        var clock = new ManualTimeProvider();
        var registry = Programs(("k1", FirstRunSteps.Knob), ("l1", FirstRunSteps.Lights));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = Scripted(registry, log, clock: clock,
            warm: async (_, progress, _) =>
            {
                await gate.Task;
                progress.Report(new GpuWarmupStep(0, 2, "k1", 1, false, "knob"));
                progress.Report(new GpuWarmupStep(1, 2, "l1", 1, false, "lights"));
            });
        var task = run.RunAsync(null!, screen);
        var before = log.ToArray();
        Assert.Equal(["begin:gpu,knob,lights", "gpu:Running:0/1", "gpu:Done:1/1", "knob:Running:0/1"], before); // the work not yet done: no more
        for (var hour = 0; hour < 5; hour++) clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(before, log);                       // five hours by the clock: not one event more
        Assert.False(task.IsCompleted);
        gate.SetResult();
        Assert.Equal(FirstRunOutcomeKind.Prepared, (await task).Kind);
        Assert.Equal(["knob:Done:1/1", "lights:Running:0/1", "lights:Done:1/1", "end:Prepared"], log.Skip(before.Length));
        // the clock is read only to say how long the run has taken: the events carry it, and no event waits for it
        Assert.All(screen.Reports.Skip(before.Length - 1), r => Assert.True(r.Elapsed >= TimeSpan.FromHours(5)));
        Assert.All(screen.Reports.Take(before.Length - 1), r => Assert.Equal(TimeSpan.Zero, r.Elapsed));
    }

    [Fact] // GR-1.progress
    [Trait("Decision", "ED-252")] // the CPU renderer has nothing to compile ahead: the run ends, and the steps it had no work for are done
    public async Task Steps_the_warm_up_had_no_work_for_are_done_when_it_completes()
    {
        var log = new List<string>();
        var registry = Programs(("k1", FirstRunSteps.Knob), ("l1", FirstRunSteps.Lights));
        var run = Scripted(registry, log, warm: (_, _, _) => Task.CompletedTask); // reports nothing: no GPU to compile on
        var outcome = await run.RunAsync(null!, new FirstRunRecordingScreen(log));
        Assert.Equal(FirstRunOutcomeKind.Prepared, outcome.Kind);
        Assert.Equal(["begin:gpu,knob,lights", "gpu:Running:0/1", "gpu:Done:1/1", "knob:Running:0/1", "knob:Done:1/1", "lights:Running:0/1", "lights:Done:1/1", "end:Prepared"], log);
    }

    [Fact] // GR-1.progress
    [Trait("Decision", "ED-252")]
    public async Task A_report_of_a_program_the_plan_does_not_name_is_ignored()
    {
        var log = new List<string>();
        var registry = Programs(("k1", FirstRunSteps.Knob));
        var run = Scripted(registry, log, warm: (_, progress, _) =>
        {
            progress.Report(new GpuWarmupStep(0, 2, "registered-after-the-plan", 1, false, "knob"));
            progress.Report(new GpuWarmupStep(1, 2, "k1", 1, false, "knob"));
            return Task.CompletedTask;
        });
        await run.RunAsync(null!, new FirstRunRecordingScreen(log));
        Assert.Equal(["begin:gpu,knob", "gpu:Running:0/1", "gpu:Done:1/1", "knob:Running:0/1", "knob:Done:1/1", "end:Prepared"], log);
    }

    // ----- skipped, or not shown -----

    [Fact] // GR-1.skip
    [Trait("Decision", "ED-252")]
    public async Task A_prepared_machine_skips_the_run_and_the_screen_is_never_told()
    {
        var log = new List<string>();
        var registry = Programs(("k1", FirstRunSteps.Knob));
        var run = Scripted(registry, log, prepared: true,
            graphics: _ => throw new InvalidOperationException("the graphics were read"),
            warm: (_, _, _) => throw new InvalidOperationException("the warm-up ran"));
        var outcome = await run.RunAsync(null!, new FirstRunRecordingScreen(log));
        Assert.Equal(new FirstRunOutcome(FirstRunOutcomeKind.Skipped), outcome);
        Assert.Empty(log);
    }

    [Fact] // GR-1.skip
    [Trait("Decision", "ED-252")]
    public async Task With_no_screen_nothing_runs_and_startup_is_unchanged()
    {
        var log = new List<string>();
        var run = Scripted(Programs(("k1", FirstRunSteps.Knob)), log, prepared: false,
            graphics: _ => throw new InvalidOperationException("the graphics were read"),
            warm: (_, _, _) => throw new InvalidOperationException("the warm-up ran"));
        Assert.Equal(new FirstRunOutcome(FirstRunOutcomeKind.NotShown), await run.RunAsync(null!, null));
        // the startup entry, until GR-2 sets a screen: no screen, no work (the idle fallback of ED-252 still prepares the knob)
        Assert.Null(FirstRunStartup.Screen);
        Assert.Equal(FirstRunOutcomeKind.NotShown, (await FirstRunStartup.RunAsync(null!)).Kind);
    }

    [Fact] // GR-1.skip
    [Trait("Decision", "ED-252")]
    public async Task The_startup_entry_runs_one_preparation_at_a_time_for_whoever_asks()
    {
        var log = new List<string>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registry = Programs(("k1", FirstRunSteps.Knob));
        var calls = 0;
        var run = Scripted(registry, log, warm: async (_, _, _) => { calls++; await gate.Task; });
        var screen = new FirstRunRecordingScreen(log);
        var first = FirstRunStartup.RunAsync(null!, run, screen);
        var second = FirstRunStartup.RunAsync(null!, run, screen);
        Assert.Same(first, second);
        gate.SetResult();
        Assert.Equal(FirstRunOutcomeKind.Prepared, (await first).Kind);
        Assert.Equal(1, calls);
        var third = FirstRunStartup.RunAsync(null!, run, screen);   // once it ended, the next ask is a new run (here: it runs again)
        Assert.NotSame(first, third);
        await third;
        Assert.Equal(2, calls);
    }

    // ----- a run that does not finish -----

    [Fact] // GR-1.progress
    [Trait("Decision", "ED-252")] // the warm-up fails (never completes as if prepared) when a step is not drawn: the run ends failed, the screen is told
    public async Task A_failed_warm_up_ends_the_run_failed_and_tells_the_screen_without_ticking_what_was_not_done()
    {
        var log = new List<string>();
        var registry = Programs(("k1", FirstRunSteps.Knob), ("l1", FirstRunSteps.Lights));
        var run = Scripted(registry, log, warm: (_, progress, _) =>
        {
            progress.Report(new GpuWarmupStep(0, 2, "k1", 1, false, "knob"));
            throw new TimeoutException("GPU warm-up: the l1 step was not drawn within 10 s");
        });
        var outcome = await run.RunAsync(null!, new FirstRunRecordingScreen(log));
        Assert.Equal(new FirstRunOutcome(FirstRunOutcomeKind.Failed, "GPU warm-up: the l1 step was not drawn within 10 s"), outcome);
        Assert.Equal(["begin:gpu,knob,lights", "gpu:Running:0/1", "gpu:Done:1/1", "knob:Running:0/1", "knob:Done:1/1", "lights:Running:0/1",
            "end:Failed"], log);
    }

    [Fact] // GR-1.progress
    [Trait("Decision", "ED-252")]
    public async Task A_cancelled_run_ends_cancelled_and_tells_the_screen()
    {
        var log = new List<string>();
        using var cancel = new CancellationTokenSource();
        var run = Scripted(Programs(("k1", FirstRunSteps.Knob)), log, warm: (_, _, token) =>
        {
            cancel.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        });
        var outcome = await run.RunAsync(null!, new FirstRunRecordingScreen(log), cancel.Token);
        Assert.Equal(FirstRunOutcomeKind.Cancelled, outcome.Kind);
        Assert.Equal("end:Cancelled", log[^1]);
    }

    // ----- the prepared state: build, adapter and driver (ED-252's marker) -----

    [Fact] // GR-1.invalidation
    [Trait("Decision", "ED-252")]
    public void The_prepared_state_is_invalidated_by_a_new_build_adapter_or_driver_and_by_a_missing_program()
    {
        var root = Path.Combine(Path.GetTempPath(), "gr1-prepared-" + Guid.NewGuid().ToString("N"));
        var gpu = Path.Combine(root, "0123456789abcdef01234567");
        var source = GpuProgramCache.MachineSource;
        var machine = "10de:2704:00000000:a1 Test GPU 32.0.15.6094";
        GpuProgramCache.MachineSource = () => machine;
        GpuProgramCache.FolderOverride = root;
        void Key(string build, string made) =>
            File.WriteAllText(Path.Combine(gpu, "key.txt"), "ANGLE (Test GPU)\nOpenGL ES 3.0.0\n" + build + "\n" + made + "\n");
        try
        {
            var preparation = new FirstRunPreparation();
            Assert.False(preparation.IsPrepared);                    // nothing kept: the first run
            Directory.CreateDirectory(gpu);
            Key(GpuProgramCache.Build, machine);
            // an entry for each family the warm-up draws (ED-NEW-warmupsplit-1: a family's parts make its programs)
            for (var k = 0; k < GpuWarmup.Families; k++) File.WriteAllBytes(Path.Combine(gpu, k + ".bin"), [1, 2, 3]);
            Assert.False(preparation.IsPrepared);                    // programs kept, but no warm-up completed on them
            GpuProgramCache.MarkWarm(gpu);
            Assert.True(preparation.IsPrepared);                     // the marker: build, adapter and driver as they are now

            machine = "10de:2704:00000000:a1 Test GPU 32.0.15.7000"; // a driver update
            Assert.False(preparation.IsPrepared);
            machine = "1002:73bf:00000000:c1 Another GPU 31.0.1.1"; // another adapter drawn with
            Assert.False(preparation.IsPrepared);
            machine = "10de:2704:00000000:a1 Test GPU 32.0.15.6094";
            Assert.True(preparation.IsPrepared);                     // back as it was

            File.WriteAllLines(Path.Combine(root, "warm.txt"), ["another build", Path.GetFileName(gpu), machine]);
            Assert.False(preparation.IsPrepared);                    // a new build
            GpuProgramCache.MarkWarm(gpu);
            Assert.True(preparation.IsPrepared);

            using (GpuWarmup.Register(GpuProgram.Draw("one-more", FirstRunSteps.Strike, _ => { })))
                Assert.False(preparation.IsPrepared);                // a program the kept ones do not cover
            Assert.True(preparation.IsPrepared);
        }
        finally
        {
            GpuProgramCache.MachineSource = source;
            GpuProgramCache.FolderOverride = null;
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact] // GR-1.invalidation
    [Trait("Decision", "ED-252")]
    public void The_marker_names_the_adapter_and_driver_itself_not_only_through_the_folders_key()
    {
        // the key of the programs' folder names this machine as it is now, so GpuProgramCache.MadeHere agrees; only the marker's own
        // third line (the machine it was written for) says otherwise, and the prepared state must not hold
        var root = Path.Combine(Path.GetTempPath(), "gr1-marker-" + Guid.NewGuid().ToString("N"));
        var gpu = Path.Combine(root, "0123456789abcdef01234567");
        var source = GpuProgramCache.MachineSource;
        var machine = "10de:2704:00000000:a1 Test GPU 32.0.15.6094";
        GpuProgramCache.MachineSource = () => machine;
        GpuProgramCache.FolderOverride = root;
        try
        {
            Directory.CreateDirectory(gpu);
            File.WriteAllText(Path.Combine(gpu, "key.txt"), "ANGLE (Test GPU)\nOpenGL ES 3.0.0\n" + GpuProgramCache.Build + "\n" + machine + "\n");
            for (var k = 0; k < GpuWarmup.Steps; k++) File.WriteAllBytes(Path.Combine(gpu, k + ".bin"), [1, 2, 3]);
            GpuProgramCache.MarkWarm(gpu);
            Assert.True(new FirstRunPreparation().IsPrepared);
            File.WriteAllLines(Path.Combine(root, "warm.txt"), [GpuProgramCache.Build, Path.GetFileName(gpu), "1002:73bf:00000000:c1 Another GPU 31.0.1.1"]);
            Assert.False(new FirstRunPreparation().IsPrepared);      // the marker was written for another adapter
        }
        finally
        {
            GpuProgramCache.MachineSource = source;
            GpuProgramCache.FolderOverride = null;
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact] // GR-1.invalidation
    [Trait("Decision", "ED-252")]
    public void The_CPU_renderers_prepared_state_is_kept_for_this_build_adapter_and_driver_only()
    {
        // GR-1 review round 3: the CPU renderer keeps no programs, so its prepared state is its own marker, and it is invalid for
        // a new build, adapter or driver, or a machine that cannot be read, exactly as the GPU's is
        var root = Path.Combine(Path.GetTempPath(), "gr1-cpu-" + Guid.NewGuid().ToString("N"));
        var source = GpuProgramCache.MachineSource;
        string? machine = "10de:2704:00000000:a1 Test GPU 32.0.15.6094";
        GpuProgramCache.MachineSource = () => machine;
        GpuProgramCache.FolderOverride = root;
        try
        {
            var preparation = new FirstRunPreparation();
            Assert.False(preparation.IsPrepared);
            GpuProgramCache.MarkWarmOnCpu();                          // the root does not exist yet: the CPU's marker makes it
            Assert.True(preparation.IsPrepared);
            GpuProgramCache.MarkWarmOnCpu();                          // again: the same
            Assert.True(preparation.IsPrepared);

            machine = "10de:2704:00000000:a1 Test GPU 32.0.15.7000";  // a driver update
            Assert.False(preparation.IsPrepared);
            machine = "1002:73bf:00000000:c1 Another GPU 31.0.1.1";   // another adapter
            Assert.False(preparation.IsPrepared);
            machine = "10de:2704:00000000:a1 Test GPU 32.0.15.6094";
            Assert.True(preparation.IsPrepared);
            machine = null;                                           // not readable: not proven, and nothing is written for it
            Assert.False(preparation.IsPrepared);
            machine = "";
            Assert.False(preparation.IsPrepared);
            machine = "10de:2704:00000000:a1 Test GPU 32.0.15.6094";

            File.WriteAllLines(Path.Combine(root, "warm-cpu.txt"), ["another build", "cpu", machine]);
            Assert.False(preparation.IsPrepared);                     // a new build
            File.WriteAllLines(Path.Combine(root, "warm-cpu.txt"), [GpuProgramCache.Build, "cpu"]);
            Assert.False(preparation.IsPrepared);                     // a short marker (read, not thrown)
            File.WriteAllLines(Path.Combine(root, "warm-cpu.txt"), [GpuProgramCache.Build, "gpu", machine]);
            Assert.False(preparation.IsPrepared);                     // not a CPU marker
            GpuProgramCache.MarkWarmOnCpu();
            Assert.True(preparation.IsPrepared);                      // rewritten for this build and machine

            // a completed warm-up on a GPU proves the machine on its GPU: the CPU's marker goes
            var gpu = Path.Combine(root, "0123456789abcdef01234567");
            Directory.CreateDirectory(gpu);
            GpuProgramCache.MarkWarm(gpu);
            Assert.False(File.Exists(Path.Combine(root, "warm-cpu.txt")));
            Assert.False(preparation.IsPrepared);                     // no programs in the GPU's folder: not prepared on the GPU
        }
        finally
        {
            GpuProgramCache.MachineSource = source;
            GpuProgramCache.FolderOverride = null;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // ----- the real preparation, on the headless renderer pretending a GPU -----

    // a step's reports, one a part drawn, then done
    private static IEnumerable<string> Parts(string step, int parts) =>
        Enumerable.Range(0, parts).Select(k => $"{step}:Running:{k}/{parts}").Append($"{step}:Done:{parts}/{parts}");

    [Fact] // GR-1.progress
    [Trait("Decision", "ED-252")]
    public void The_real_preparation_draws_every_program_and_runs_a_registered_step_once_in_order() => headless.OnUiThread(() =>
    {
        using var cache = new GpuProgramCacheFolder();
        var source = GpuProgramCache.MachineSource;
        GpuProgramCache.MachineSource = () => "10de:2704:00000000:a1 Test GPU 1.2.3.4";
        GpuWarmup.PretendGpu = true;
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100 };
        window.Show();
        var log = new List<string>();
        using var voice = GpuWarmup.Register(GpuProgram.Work("voice-hardware", FirstRunSteps.Voice, _ => { log.Add("voice-hardware"); return Task.CompletedTask; }));
        try
        {
            var done = GpuWarmup.StepsDone;
            var task = new FirstRunPreparation().RunAsync(window, new FirstRunRecordingScreen(log));
            Pump(TimeSpan.FromSeconds(120), () => task.IsCompleted);
            Assert.True(task.IsCompletedSuccessfully, task.Exception?.InnerException?.ToString());
            Assert.Equal(FirstRunOutcomeKind.Prepared, task.Result.Kind);
            Assert.Equal(GpuWarmup.Steps, GpuWarmup.StepsDone - done);
            Assert.Equal(
            [
                "begin:gpu,knob,lights,liquid,strike,hw",
                "gpu:Running:0/1", "gpu:Done:1/1",
                .. Parts("knob", 4),
                .. Parts("lights", 11), // ED-NEW-warmupsplit-1: each family in frame-sized parts (5 + 3 + 3, 8, 8 + 63)
                .. Parts("liquid", 8),
                .. Parts("strike", 110),
                "hw:Running:0/1", "voice-hardware", "hw:Done:1/1",
                "end:Prepared",
            ], log);
        }
        finally { GpuWarmup.PretendGpu = false; GpuProgramCache.MachineSource = source; window.Close(); }
    });

    // ----- helpers -----

    private static List<GpuProgram> Programs(params (string Name, FirstRunStep Step)[] programs) =>
        programs.Select(p => GpuProgram.Draw(p.Name, p.Step, _ => { })).ToList();

    private static FirstRunPreparation Scripted(List<GpuProgram> registry, List<string> log, bool prepared = false, TimeProvider? clock = null,
        Func<CancellationToken, Task>? graphics = null, Func<TopLevel, IProgress<GpuWarmupStep>, CancellationToken, Task>? warm = null) =>
        new(new FirstRunPreparation.Seams(() => prepared, () => registry, graphics ?? (_ => Task.CompletedTask),
            warm ?? ((_, _, _) => Task.CompletedTask)), clock ?? TimeProvider.System);

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
