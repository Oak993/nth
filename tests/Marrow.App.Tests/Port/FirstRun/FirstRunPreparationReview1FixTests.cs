using Avalonia.Controls;
using Marrow.App.Port.FirstRun;
using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Port.FirstRun;

// GR-1 review round 1, the fixes (the failing REVIEW1 test is in FirstRunPreparationReview1Tests). OD-0096: "real progress and
// steps (no fake timer)"; the row: "progress only from completed work". A step is ticked only for work that was done on the context
// the run ends on, and never for work that was not run.
public sealed class FirstRunPreparationReview1FixTests
{
    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.progress")]
    [Trait("Decision", "ED-252")] // a new GPU context starts the warm-up's pass again: the programs are made again on it
    public async Task A_pass_started_again_takes_back_the_ticks_of_the_programs_it_makes_again()
    {
        var log = new List<string>();
        var registry = new List<GpuProgram>
        {
            Draw("k1", FirstRunSteps.Knob), Draw("k2", FirstRunSteps.Knob), Draw("l1", FirstRunSteps.Lights), Draw("l2", FirstRunSteps.Lights),
        };
        var run = Scripted(registry, warm: (_, progress, _) =>
        {
            void Drawn(int i, string name, string step) { log.Add("drawn:" + name); progress.Report(new GpuWarmupStep(i, 4, name, 1, false, step)); }
            Drawn(0, "k1", "knob"); Drawn(1, "k2", "knob"); Drawn(2, "l1", "lights");
            // the display moved to a new GPU context: the pass starts again from its first program
            Drawn(0, "k1", "knob"); Drawn(1, "k2", "knob"); Drawn(2, "l1", "lights"); Drawn(3, "l2", "lights");
            return Task.CompletedTask;
        });
        var outcome = await run.RunAsync(null!, new Screen(log));
        Assert.Equal(FirstRunOutcomeKind.Prepared, outcome.Kind);
        Assert.Equal(
        [
            "begin:gpu,knob,lights", "gpu:Running:0/1", "gpu:Done:1/1",
            "knob:Running:0/2",
            "drawn:k1", "knob:Running:1/2", "drawn:k2", "knob:Done:2/2", "lights:Running:0/2",
            "drawn:l1", "lights:Running:1/2",
            "drawn:k1", "knob:Running:0/2", "lights:Waiting:0/2", "knob:Running:1/2",     // made again: the earlier ticks are taken back
            "drawn:k2", "knob:Done:2/2", "lights:Running:0/2",
            "drawn:l1", "lights:Running:1/2",
            "drawn:l2", "lights:Done:2/2",
            "end:Prepared",
        ], log);
    }

    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.progress")]
    public async Task A_program_reported_twice_in_one_pass_is_one_part_not_two()
    {
        var log = new List<string>();
        var registry = new List<GpuProgram> { Draw("k1", FirstRunSteps.Knob), Draw("k2", FirstRunSteps.Knob) };
        var run = Scripted(registry, warm: (_, progress, _) =>
        {
            progress.Report(new GpuWarmupStep(0, 2, "k1", 1, false, "knob"));
            progress.Report(new GpuWarmupStep(1, 2, "k2", 1, false, "knob"));
            return Task.CompletedTask;
        });
        await run.RunAsync(null!, new Screen(log));
        Assert.Equal(["begin:gpu,knob", "gpu:Running:0/1", "gpu:Done:1/1", "knob:Running:0/2", "knob:Running:1/2", "knob:Done:2/2", "end:Prepared"], log);
    }

    [Fact]
    [Trait("Feature", "GR-1")]
    [Trait("Spec", "GR-1.progress")]
    public async Task A_work_step_the_warm_up_never_ran_is_not_ticked_and_the_run_fails()
    {
        var log = new List<string>();
        var registry = new List<GpuProgram>
        {
            Draw("k1", FirstRunSteps.Knob),
            GpuProgram.Work("voice-hardware", FirstRunSteps.Voice, _ => Task.CompletedTask),
        };
        var run = Scripted(registry, warm: (_, progress, _) =>
        {
            progress.Report(new GpuWarmupStep(0, 2, "k1", 1, false, "knob")); // the work step is never reported: it did not run
            return Task.CompletedTask;
        });
        var outcome = await run.RunAsync(null!, new Screen(log));
        Assert.Equal(FirstRunOutcomeKind.Failed, outcome.Kind);
        Assert.Contains("voice-hardware", outcome.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("hw:Done:1/1", log);
        Assert.Equal(["begin:gpu,knob,hw", "gpu:Running:0/1", "gpu:Done:1/1", "knob:Running:0/1", "knob:Done:1/1", "hw:Running:0/1", "end:Failed"], log);
    }

    private static GpuProgram Draw(string name, FirstRunStep step) => GpuProgram.Draw(name, step, _ => { });

    private static FirstRunPreparation Scripted(List<GpuProgram> registry, Func<TopLevel, IProgress<GpuWarmupStep>, CancellationToken, Task> warm) =>
        new(new FirstRunPreparation.Seams(() => false, () => registry, _ => Task.CompletedTask, warm), TimeProvider.System);

    private sealed class Screen(List<string> log) : IFirstRunScreen
    {
        public void Begin(IReadOnlyList<FirstRunStepInfo> steps) => log.Add("begin:" + string.Join(",", steps.Select(s => s.Step.Id)));
        public void Report(FirstRunProgress progress) => log.Add($"{progress.Step.Id}:{progress.State}:{progress.PartsDone}/{progress.Parts}");

        public Task EndAsync(FirstRunOutcome outcome)
        {
            log.Add("end:" + outcome.Kind);
            return Task.CompletedTask;
        }
    }
}
