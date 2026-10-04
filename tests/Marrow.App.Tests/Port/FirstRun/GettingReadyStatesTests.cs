using Marrow.App.Port.FirstRun;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Port.FirstRun;

// GR-2: the sheet's states for a scripted preparation. Each row is waiting (a soft dot), running (the spinner) or done (the
// check), driven only by the events GR-1's FirstRunPreparation reports as work completes; the bar is the real share of the
// work done; "Ready" shows when the run is prepared. No timer moves any of it (a clock moved by hours changes nothing).
[Collection("Headless")]
public sealed class GettingReadyStatesTests(HeadlessSession headless)
{
    private static void Settle(GettingReadyRig rig) => rig.Run(0.6);

    [Fact] // GR-2.states
    [Trait("Decision", "ED-288")]
    public void Before_any_event_every_step_waits_as_a_soft_dot_in_W1s_order() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig();
        Assert.Equal(GettingReadyPhase.Working, rig.Sheet.Phase);
        Assert.Equal(GettingReadyFixture.Words, rig.Sheet.Rows.Select(row => row.Label));
        foreach (var row in rig.Sheet.Rows)
        {
            Assert.Equal(FirstRunStepState.Waiting, row.State);
            Assert.Equal(0, row.SpinnerOpacity);
            Assert.Equal(0, row.CheckOpacity);
            Assert.True(row.DotOpacity > 0.5, row.Id);
            Assert.True(row.LabelOpacity < 1, row.Id); // the fixture draws a waiting step in the dimmest ink
        }
        Assert.Equal(0, rig.Sheet.BarFraction);
        Assert.Equal(0, rig.Sheet.Hand);
    });

    [Fact] // GR-2.states
    [Trait("Decision", "ED-288")]
    public void A_running_step_shows_the_spinner_and_a_done_step_the_check() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig();
        rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[0], 0, 6, FirstRunStepState.Running, 0));
        Settle(rig);
        var first = rig.Sheet.Rows[0];
        Assert.Equal(FirstRunStepState.Running, first.State);
        Assert.Equal((1.0, 1.0, 0.0), (first.SpinnerOpacity, first.SpinnerScale, first.DotOpacity));
        Assert.Equal(0, first.CheckOpacity);
        Assert.Equal(1, first.LabelOpacity);

        rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[0], 0, 6, FirstRunStepState.Done, 1));
        rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[1], 1, 6, FirstRunStepState.Running, 0));
        Settle(rig);
        Assert.Equal(FirstRunStepState.Done, first.State);
        Assert.Equal((1.0, 1.0), (first.CheckOpacity, first.CheckScale));
        Assert.Equal(0, first.SpinnerOpacity);
        Assert.Equal(FirstRunStepState.Running, rig.Sheet.Rows[1].State);
        Assert.Equal(1, rig.Sheet.Rows[1].SpinnerOpacity);
        Assert.Equal(FirstRunStepState.Waiting, rig.Sheet.Rows[2].State);
        Assert.Equal(0, rig.Sheet.Rows[2].SpinnerOpacity);
    });

    [Fact] // GR-2.states
    [Trait("Decision", "ED-288")]
    public void The_bar_is_the_real_share_of_the_work_done_never_a_guess() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig();
        var plan = rig.Plan;
        rig.Sheet.Report(GettingReadyRig.Event(plan[0], 0, 6, FirstRunStepState.Done, 1));
        rig.Sheet.Report(GettingReadyRig.Event(plan[1], 1, 6, FirstRunStepState.Running, 2)); // the knob: 2 of its 4 programs
        Settle(rig);
        Assert.Equal((1 + 0.5) / 6, rig.Sheet.BarFraction, 6);
        rig.Sheet.Report(GettingReadyRig.Event(plan[1], 1, 6, FirstRunStepState.Done, 4));
        rig.Sheet.Report(GettingReadyRig.Event(plan[2], 2, 6, FirstRunStepState.Running, 1)); // the light: 1 of 3
        Settle(rig);
        Assert.Equal((2 + 1.0 / 3) / 6, rig.Sheet.BarFraction, 6);
    });

    [Fact] // GR-2.states
    [Trait("Decision", "ED-288")]
    public void A_clock_moved_by_hours_changes_nothing_no_step_ticks_and_the_bar_does_not_creep() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig();
        rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[0], 0, 6, FirstRunStepState.Running, 0));
        Settle(rig);
        var before = rig.Sheet.Rows.Select(row => (row.State, row.SpinnerOpacity, row.CheckOpacity)).ToList();
        var bar = rig.Sheet.BarFraction;
        for (var hour = 0; hour < 5; hour++) rig.Frame(3600);
        Assert.Equal(before, rig.Sheet.Rows.Select(row => (row.State, row.SpinnerOpacity, row.CheckOpacity)).ToList());
        Assert.Equal(bar, rig.Sheet.BarFraction);
        Assert.Equal(GettingReadyPhase.Working, rig.Sheet.Phase);
        Assert.Equal(0, rig.Sheet.Hand);
    });

    [Fact] // GR-2.states
    [Trait("Decision", "ED-288")]
    public void A_prepared_run_says_Ready() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig();
        for (var index = 0; index < 6; index++)
        {
            rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[index], index, 6, FirstRunStepState.Running, 0));
            rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[index], index, 6, FirstRunStepState.Done, rig.Plan[index].Parts));
        }
        Settle(rig);
        Assert.Equal(1, rig.Sheet.BarFraction, 6);
        _ = rig.Sheet.EndAsync(new FirstRunOutcome(FirstRunOutcomeKind.Prepared));
        rig.Run(0.1);
        Assert.Equal(GettingReadyPhase.Ready, rig.Sheet.Phase);
        Assert.Equal(GettingReadyFixture.Done, rig.Sheet.TitleWord);
        rig.Run(0.4);
        Assert.Equal((1.0, 1.0), (rig.Sheet.ReadyOpacity, rig.Sheet.ReadyRise));
    });

    [Fact] // GR-2.states
    [Trait("Decision", "ED-288")]
    public void A_failed_run_ticks_nothing_it_did_not_do_and_never_says_Ready() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig();
        rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[0], 0, 6, FirstRunStepState.Done, 1));
        rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[1], 1, 6, FirstRunStepState.Running, 1));
        Settle(rig);
        _ = rig.Sheet.EndAsync(new FirstRunOutcome(FirstRunOutcomeKind.Failed, "a program did not draw"));
        rig.Run(0.3);
        Assert.Equal(GettingReadyPhase.Lifting, rig.Sheet.Phase);
        Assert.Equal(GettingReadyFixture.Title, rig.Sheet.TitleWord);
        Assert.Equal(FirstRunStepState.Running, rig.Sheet.Rows[1].State);
        Assert.Equal(0, rig.Sheet.Rows[1].CheckOpacity);
        Assert.Equal(0, rig.Sheet.ReadyOpacity);
        Assert.True(rig.Sheet.BarFraction < 0.5);
    });

    [Fact] // GR-2.states
    [Trait("Decision", "ED-288")]
    public void A_scripted_preparation_drives_the_sheet_from_its_real_events_and_ends_prepared() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig();
        var registry = new List<GpuProgram>
        {
            GpuProgram.Draw("k1", FirstRunSteps.Knob, _ => { }), GpuProgram.Draw("k2", FirstRunSteps.Knob, _ => { }),
            GpuProgram.Draw("k3", FirstRunSteps.Knob, _ => { }), GpuProgram.Draw("k4", FirstRunSteps.Knob, _ => { }),
            GpuProgram.Draw("l1", FirstRunSteps.Lights, _ => { }), GpuProgram.Draw("l2", FirstRunSteps.Lights, _ => { }), GpuProgram.Draw("l3", FirstRunSteps.Lights, _ => { }),
            GpuProgram.Draw("liquid", FirstRunSteps.Liquid, _ => { }), GpuProgram.Draw("strike", FirstRunSteps.Strike, _ => { }),
            GpuProgram.Work("voice-hardware", FirstRunSteps.Voice, _ => Task.CompletedTask),
        };
        var order = new[] { "k1", "k2", "k3", "k4", "l1", "l2", "l3", "liquid", "strike", "voice-hardware" };
        var seams = new FirstRunPreparation.Seams(() => false, () => registry, _ => Task.CompletedTask, (_, progress, _) =>
        {
            for (var index = 0; index < order.Length; index++)
                progress.Report(new GpuWarmupStep(index, order.Length, order[index], 1, false, "step"));
            return Task.CompletedTask;
        });
        var run = new FirstRunPreparation(seams, TimeProvider.System).RunAsync(null!, rig.Sheet);
        Assert.False(run.IsCompleted); // the sheet holds the run until it has lifted
        rig.Run(0.6);
        Assert.Equal(GettingReadyPhase.Ready, rig.Sheet.Phase);
        Assert.All(rig.Sheet.Rows, row => Assert.Equal(FirstRunStepState.Done, row.State));
        Assert.All(rig.Sheet.Rows, row => Assert.Equal(1, row.CheckOpacity));
        Assert.Equal(1, rig.Sheet.BarFraction, 6);
        rig.Run(1.4);
        Assert.True(run.IsCompletedSuccessfully);
        Assert.Equal(FirstRunOutcomeKind.Prepared, run.Result.Kind);
        Assert.Equal(GettingReadyPhase.Gone, rig.Sheet.Phase);
    });
}
