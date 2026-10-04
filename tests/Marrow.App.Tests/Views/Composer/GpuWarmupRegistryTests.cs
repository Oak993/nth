using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.App.Port.FirstRun;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models.ComposerLights;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// GR-1 (BUILD-PLAN 2.2; OD-0096, ED-252): GpuWarmup holds a registry of programs and steps. The jewel's four programs live in it,
// with the composer light, the slider liquid and the strike's; a program or step registered later joins the warm-up without
// GpuWarmup.cs being edited. (The 54 warm-up tests of EffortSliderJewelWarmup*Tests stay as they were.)
[Collection("Headless")]
public sealed class GpuWarmupRegistryTests(HeadlessSession headless)
{
    private static readonly FirstRunStep Later = new("later", "A step registered later", 25);

    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.registry")]
    public void The_jewels_four_programs_are_registered_first_under_the_knob_step()
    {
        var first = GpuWarmup.Registered.Take(4).ToArray();
        Assert.Equal(["noise", "fluid", "orb", "page"], first.Select(p => p.Name));
        Assert.All(first, p => { Assert.Equal(FirstRunSteps.Knob, p.Step); Assert.False(p.IsWork); });
        Assert.Equal(["noise", "fluid", "orb", "page"], GpuWarmup.Programs.Take(4));
    }

    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.registry")]
    public void The_composer_light_the_slider_liquid_and_the_strike_are_registered_after_the_knob_in_W1_order()
    {
        var after = GpuWarmup.Registered.Skip(4).ToArray();
        Assert.Equal(["light-rotate", "light-moving", "light-aura", "liquid", "strike"], after.Select(p => p.Name));
        Assert.Equal([FirstRunSteps.Lights, FirstRunSteps.Lights, FirstRunSteps.Lights, FirstRunSteps.Liquid, FirstRunSteps.Strike], after.Select(p => p.Step));
        Assert.Equal(9, GpuWarmup.Steps);
        Assert.Equal(GpuWarmup.Steps, GpuWarmup.Programs.Count);
    }

    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.registry")]
    public void Programs_are_kept_in_step_order_then_registration_order_whatever_order_they_arrive_in()
    {
        using var late = GpuWarmup.Register(GpuProgram.Draw("strike-two", FirstRunSteps.Strike, _ => { }));
        using var early = GpuWarmup.Register(GpuProgram.Draw("between", Later, _ => { }));
        using var knob = GpuWarmup.Register(GpuProgram.Draw("knob-two", FirstRunSteps.Knob, _ => { }));
        var names = GpuWarmup.Registered.Select(p => p.Name).ToArray();
        Assert.Equal(["noise", "fluid", "orb", "page", "knob-two", "between", "light-rotate", "light-moving", "light-aura", "liquid", "strike", "strike-two"], names);
    }

    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.registry")]
    public void A_name_already_registered_and_a_step_id_given_another_name_or_order_are_refused()
    {
        Assert.Throws<ArgumentException>(() => GpuWarmup.Register(GpuProgram.Draw("noise", FirstRunSteps.Knob, _ => { })));
        Assert.Throws<ArgumentException>(() => GpuWarmup.Register(GpuProgram.Draw("fresh", FirstRunSteps.Knob with { Name = "Another name" }, _ => { })));
        Assert.Throws<ArgumentException>(() => GpuWarmup.Register(GpuProgram.Draw("fresh", FirstRunSteps.Knob with { Order = 21 }, _ => { })));
        Assert.DoesNotContain(GpuWarmup.Registered, p => p.Name == "fresh");
        Assert.Throws<ArgumentException>(() => GpuProgram.Draw("", FirstRunSteps.Knob, _ => { }));
    }

    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.registry")]
    public void A_registration_ends_when_it_is_disposed_and_disposing_twice_does_nothing()
    {
        var before = GpuWarmup.Steps;
        var registration = GpuWarmup.Register(GpuProgram.Draw("temporary", FirstRunSteps.Strike, _ => { }));
        Assert.Equal(before + 1, GpuWarmup.Steps);
        registration.Dispose();
        registration.Dispose();
        Assert.Equal(before, GpuWarmup.Steps);
        using var again = GpuWarmup.Register(GpuProgram.Draw("temporary", FirstRunSteps.Strike, _ => { })); // the name is free again
        Assert.Equal(before + 1, GpuWarmup.Steps);
    }

    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.programs")]
    public void The_liquid_programs_frame_has_oil_and_bubbles_and_the_strike_programs_has_the_lightning() => headless.OnUiThread(() =>
    {
        var plain = GpuWarmup.LiquidFrame(strike: false);
        Assert.NotEmpty(LiquidTrack.Bubbles(plain));                                // the bubble program is drawn
        Assert.True(LiquidTrack.Oil(plain.Spec, plain.Size.Width, plain.Time).Oil[0] > 0.5f); // and Grok's oil
        Assert.Null(plain.Strike);
        var struck = GpuWarmup.LiquidFrame(strike: true);
        Assert.NotEmpty(struck.Strike!);                                            // the lit glass and the lightning
        Assert.Contains(struck.Strike!, op => op.Kind == StrikeOpKind.Stroke);
        Assert.Contains(struck.Strike!, op => op.Kind == StrikeOpKind.Wash);
    });

    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.programs")]
    public void The_light_programs_draw_the_rotating_beam_the_moving_beam_and_the_Uiverse_light()
    {
        var rotate = GpuWarmup.LightAnimator(GpuWarmup.LightKind.Rotate);
        Assert.False(rotate.AuraVisible);
        Assert.Equal(1, rotate.DrawnCount);
        Assert.Equal(0, rotate.Slots[rotate.Drawn[0]].Mode); // the blob bake and the rotating shader
        var moving = GpuWarmup.LightAnimator(GpuWarmup.LightKind.Moving);
        Assert.Equal(1, moving.DrawnCount);
        Assert.Equal(1, moving.Slots[moving.Drawn[0]].Mode); // Line: the moving shader
        var aura = GpuWarmup.LightAnimator(GpuWarmup.LightKind.Aura);
        Assert.True(aura.AuraVisible);                       // the Uiverse light's shader
    }

    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.programs")]
    public void Every_registered_program_draws_once_in_a_warm_up_and_reports_its_step() => headless.OnUiThread(() =>
    {
        using var cache = new GpuProgramCacheFolder();
        GpuWarmup.PretendGpu = true;
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100 };
        window.Show();
        try
        {
            var reports = new List<GpuWarmupStep>();
            var done = GpuWarmup.StepsDone;
            var task = GpuWarmup.WarmAsync(window, new SyncProgress<GpuWarmupStep>(reports.Add));
            FramesFor(TimeSpan.FromSeconds(60), () => task.IsCompleted);
            Assert.True(task.IsCompletedSuccessfully, task.Exception?.InnerException?.ToString());
            Assert.Equal(GpuWarmup.Programs, reports.Select(r => r.Program));
            Assert.Equal(GpuWarmup.Registered.Select(p => p.Step.Id), reports.Select(r => r.StepId));
            Assert.Equal(GpuWarmup.Steps, GpuWarmup.StepsDone - done);
            Assert.All(reports, r => Assert.Equal(GpuWarmup.Steps, r.Count));
        }
        finally { GpuWarmup.PretendGpu = false; window.Close(); }
    });

    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.registry")]
    public void A_program_registered_later_is_drawn_in_its_place_with_the_render_thread_context() => headless.OnUiThread(() =>
    {
        using var cache = new GpuProgramCacheFolder();
        GpuWarmup.PretendGpu = true;
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100 };
        window.Show();
        var drawn = new List<(string, int, double)>();
        using var program = GpuWarmup.Register(GpuProgram.Draw("between", Later, context => drawn.Add(("between", Environment.CurrentManagedThreadId, context.Scaling))));
        try
        {
            var reports = new List<GpuWarmupStep>();
            var task = GpuWarmup.WarmAsync(window, new SyncProgress<GpuWarmupStep>(reports.Add));
            FramesFor(TimeSpan.FromSeconds(60), () => task.IsCompleted);
            Assert.True(task.IsCompletedSuccessfully, task.Exception?.InnerException?.ToString());
            Assert.Equal(["noise", "fluid", "orb", "page", "between", "light-rotate"], reports.Select(r => r.Program).Take(6));
            var (_, thread, scaling) = Assert.Single(drawn);
            Assert.NotEqual(Environment.CurrentManagedThreadId, thread); // the render thread, not the UI thread
            Assert.Equal(window.RenderScaling, scaling);
        }
        finally { GpuWarmup.PretendGpu = false; window.Close(); }
    });

    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.registry")]
    public void A_program_that_throws_faults_the_warm_up_and_never_marks_it_prepared_and_the_render_thread_lives_on() => headless.OnUiThread(() =>
    {
        using var cache = new GpuProgramCacheFolder();
        GpuWarmup.PretendGpu = true;
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100 };
        window.Show();
        try
        {
            var marker = Path.Combine(cache.Root, "warm.txt");
            Task task;
            using (GpuWarmup.Register(GpuProgram.Draw("broken", FirstRunSteps.Strike, _ => throw new InvalidOperationException("the program did not compile"))))
            {
                task = GpuWarmup.WarmAsync(window, new SyncProgress<GpuWarmupStep>(_ => Interlocked.Increment(ref GpuProgramCache.Writes)));
                FramesFor(TimeSpan.FromSeconds(60), () => task.IsCompleted);
            }
            Assert.True(task.IsFaulted);
            Assert.Equal("the program did not compile", task.Exception!.InnerException!.Message);
            Assert.False(File.Exists(marker));
            var again = GpuWarmup.WarmAsync(window, new SyncProgress<GpuWarmupStep>(_ => Interlocked.Increment(ref GpuProgramCache.Writes)));
            FramesFor(TimeSpan.FromSeconds(60), () => again.IsCompleted);
            Assert.True(again.IsCompletedSuccessfully, again.Exception?.InnerException?.ToString()); // drawn again: the render thread was not taken down
            Assert.True(File.Exists(marker));
        }
        finally { GpuWarmup.PretendGpu = false; window.Close(); }
    });

    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.registry")]
    public void A_work_step_runs_once_in_its_place_in_a_first_run_warm_up_and_is_reported_with_its_step() => headless.OnUiThread(() =>
    {
        using var cache = new GpuProgramCacheFolder();
        GpuWarmup.PretendGpu = true;
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100 };
        window.Show();
        var ran = new List<string>();
        using var work = GpuWarmup.Register(GpuProgram.Work("voice-hardware", FirstRunSteps.Voice, cancellation =>
        {
            ran.Add("voice-hardware");
            return Task.CompletedTask;
        }));
        try
        {
            var reports = new List<GpuWarmupStep>();
            var task = GpuWarmup.WarmAsync(window, new SyncProgress<GpuWarmupStep>(reports.Add));
            FramesFor(TimeSpan.FromSeconds(60), () => task.IsCompleted);
            Assert.True(task.IsCompletedSuccessfully, task.Exception?.InnerException?.ToString());
            Assert.Equal(["voice-hardware"], ran);
            Assert.Equal("voice-hardware", reports[^1].Program);       // after the strike: the voice step is last
            Assert.Equal("hw", reports[^1].StepId);
            Assert.Equal(GpuWarmup.Steps + 1, reports.Count);
            Assert.Equal(GpuWarmup.Steps, GpuWarmup.Programs.Count);   // Programs and Steps count what draws; a work step is no program
        }
        finally { GpuWarmup.PretendGpu = false; window.Close(); }
    });

    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.registry")]
    public void The_idle_fallback_never_runs_a_work_step() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        var cap = GpuWarmup.IdleCap;
        GpuWarmup.IdleCap = TimeSpan.FromMilliseconds(50);
        GpuWarmup.PretendGpu = true;
        var ran = 0;
        using var work = GpuWarmup.Register(GpuProgram.Work("voice-hardware", FirstRunSteps.Voice, _ => { ran++; return Task.CompletedTask; }));
        var host = new Border { Width = 40, Height = 20 };
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100, Content = host };
        window.Show();
        var done = GpuWarmup.StepsDone;
        try
        {
            GpuWarmup.WhenIdle(host);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            FramesFor(TimeSpan.FromSeconds(60), () => GpuWarmup.StepsDone - done >= GpuWarmup.Steps);
            Assert.Equal(GpuWarmup.Steps, GpuWarmup.StepsDone - done); // every program, one a frame
            Assert.Equal(0, ran);
        }
        finally { GpuWarmup.PretendGpu = false; GpuWarmup.IdleCap = cap; window.Close(); }
    });

    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.registry")]
    public void On_the_CPU_renderer_the_programs_are_left_alone_and_a_work_step_still_runs() => headless.OnUiThread(() =>
    {
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100 };
        window.Show();
        var drawn = 0;
        var worked = 0;
        using var program = GpuWarmup.Register(GpuProgram.Draw("after-the-knob", FirstRunSteps.Strike, _ => drawn++));
        using var work = GpuWarmup.Register(GpuProgram.Work("voice-hardware", FirstRunSteps.Voice, _ => { worked++; return Task.CompletedTask; }));
        try
        {
            var reports = new List<GpuWarmupStep>();
            var task = GpuWarmup.WarmAsync(window, new SyncProgress<GpuWarmupStep>(reports.Add));
            FramesFor(TimeSpan.FromSeconds(30), () => task.IsCompleted);
            Assert.True(task.IsCompletedSuccessfully, task.Exception?.InnerException?.ToString());
            Assert.Equal(0, drawn);                                  // nothing to compile ahead without a GPU
            Assert.Equal(1, worked);                                 // the voice hardware check does not need one
            Assert.Equal(["voice-hardware"], reports.Select(r => r.Program));
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.registry")]
    public void A_work_step_is_not_counted_in_the_cache_entries_a_warm_state_needs()
    {
        var root = Path.Combine(Path.GetTempPath(), "gr1-warm-" + Guid.NewGuid().ToString("N"));
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
            Assert.True(GpuWarmup.IsWarm);
            using (GpuWarmup.Register(GpuProgram.Work("voice-hardware", FirstRunSteps.Voice, _ => Task.CompletedTask)))
                Assert.True(GpuWarmup.IsWarm);                       // a step that makes no program needs no entry
            using (GpuWarmup.Register(GpuProgram.Draw("one-more", FirstRunSteps.Strike, _ => { })))
                Assert.False(GpuWarmup.IsWarm);                      // a program that draws needs one
            Assert.True(GpuWarmup.IsWarm);
        }
        finally
        {
            GpuProgramCache.MachineSource = source;
            GpuProgramCache.FolderOverride = null;
            Directory.Delete(root, recursive: true);
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
}
