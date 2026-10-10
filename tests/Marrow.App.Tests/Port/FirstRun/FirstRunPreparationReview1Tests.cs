using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.App.Port.FirstRun;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.FirstRun;

// REVIEW1 (adversarial review, round 1, of GR-1). OD-0096: "real progress and steps (no fake timer)"; the row: "progress only from
// completed work". GpuWarmup starts a pass again from its first program when the window is drawn on a new GPU context (ED-252; the
// unit's own test A_work_step_is_not_repeated_when_a_new_gpu_context_starts_the_run_again shows every program reported again). A
// step must not be ticked done before each of its programs has been drawn on the context the run ends on.
[Collection("Headless")]
public sealed class FirstRunPreparationReview1Tests(HeadlessSession headless)
{
    [Fact] // GR-1.progress
    [Trait("Decision", "ED-252")]
    public void REVIEW1_a_restarted_pass_does_not_tick_a_step_done_before_its_programs_are_drawn_again() => headless.OnUiThread(() =>
    {
        using var cache = new GpuProgramCacheFolder();
        var source = GpuProgramCache.MachineSource;
        GpuProgramCache.MachineSource = () => "10de:2704:00000000:a1 Test GPU 1.2.3.4";
        GpuWarmup.PretendGpu = true;
        GpuWarmup.PretendContext = 0x7E5A0001;
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100 };
        window.Show();
        var log = new List<string>();
        var seams = new FirstRunPreparation.Seams(() => false, () => GpuWarmup.Registered, _ => Task.CompletedTask,
            (w, progress, c) => GpuWarmup.WarmAsync(w, new SyncProgress<GpuWarmupStep>(s => { log.Add("drawn:" + s.Program); progress.Report(s); }), c));
        var moved = false;
        var screen = new FirstRunReactingScreen(log, p =>
        {
            // the first light program drawn: the display moves to a new GPU context before the second is drawn
            if (!moved && p.Step.Id == "lights" && p.PartsDone == 1) { moved = true; GpuWarmup.PretendContext = 0x7E5A0002; }
        });
        try
        {
            var task = new FirstRunPreparation(seams, TimeProvider.System).RunAsync(window, screen);
            Pump(TimeSpan.FromSeconds(120), () => task.IsCompleted);
            Assert.True(task.IsCompletedSuccessfully, task.Exception?.InnerException?.ToString());
            Assert.Equal(FirstRunOutcomeKind.Prepared, task.Result.Kind);
            Assert.True(moved);
            Assert.Equal(2, log.Count(l => l == "drawn:light-rotate[0]")); // the pass started again on the new context
            var lastAura = GpuWarmup.Registered.Last(p => p.Family == "light-aura").Name; // its last part (ED-NEW-warmupsplit-1)
            var auraOnNewContext = log.LastIndexOf("drawn:" + lastAura);
            var lightsDone = log.FindIndex(l => l.StartsWith("lights:Done", StringComparison.Ordinal));
            Assert.True(auraOnNewContext >= 0 && lightsDone > auraOnNewContext,
                "Composer light ticked done before " + lastAura + " was drawn on the new context: " + string.Join(" | ", log));
        }
        finally { GpuWarmup.PretendGpu = false; GpuWarmup.PretendContext = 0; GpuProgramCache.MachineSource = source; window.Close(); }
    });

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
