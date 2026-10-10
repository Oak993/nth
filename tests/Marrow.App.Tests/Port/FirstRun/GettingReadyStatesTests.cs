using Avalonia.Media;
using Marrow.App.Port.FirstRun;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;

namespace Marrow.App.Tests.Port.FirstRun;

// GR-2: the sheet's states for a scripted preparation. Each row is waiting (the nth status mark's hollow ring), running (its tile-trace spinner) or done
// (its solid disc and tick; nth motion pack step 15), driven only by the events GR-1's FirstRunPreparation reports as work completes; the bar is the real share of the
// work done; "Ready" shows when the run is prepared. No timer moves any of it (a clock moved by hours changes nothing).
[Collection("Headless")]
public sealed class GettingReadyStatesTests(HeadlessSession headless)
{
    private static void Settle(GettingReadyRig rig) => rig.Run(0.6);

    // the label's ink the sheet resolves for a step state in the rig's theme
    private static Color Ink(GettingReadyRig rig, FirstRunStepState state) => GettingReadyRowView.InkFor(state).Resolve(rig.Sheet.ActualThemeVariant);

    [Fact] // GR-2.states
    [Trait("Decision", "ED-288")]
    public void Before_any_event_every_step_waits_as_a_hollow_ring_in_W1s_order() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig();
        Assert.Equal(GettingReadyPhase.Working, rig.Sheet.Phase);
        Assert.Equal(GettingReadyFixture.Words, rig.Sheet.Rows.Select(row => row.Label));
        foreach (var row in rig.Sheet.Rows)
        {
            Assert.Equal(FirstRunStepState.Waiting, row.State);
            Assert.Equal(NthStatus.Waiting, row.Status);
            Assert.Equal(NthDoneMark.Disc, row.StatusMark.DoneMark); // step 15: done is the disc with its tick
            Assert.Equal(Ink(rig, FirstRunStepState.Waiting), row.LabelColour); // text3, the dimmest ink the sheet has
            Assert.True(row.LabelColour.A < 255, row.Id);
        }
        Assert.Equal(0, rig.Sheet.BarFraction);
        Assert.Equal("0%", rig.Sheet.PercentText);
        Assert.Equal(0, rig.Sheet.Hand);
    });

    [Fact] // GR-2.states
    [Trait("Decision", "ED-288")]
    public void A_running_step_shows_the_spinner_and_a_done_step_the_disc_with_its_tick() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig();
        rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[0], 0, 6, FirstRunStepState.Running, 0));
        Settle(rig);
        var first = rig.Sheet.Rows[0];
        Assert.Equal(FirstRunStepState.Running, first.State);
        Assert.Equal(NthStatus.Working, first.Status);
        Assert.Equal(Ink(rig, FirstRunStepState.Running), first.LabelColour); // text

        rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[0], 0, 6, FirstRunStepState.Done, 1));
        rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[1], 1, 6, FirstRunStepState.Running, 0));
        Settle(rig);
        Assert.Equal(FirstRunStepState.Done, first.State);
        Assert.Equal(NthStatus.Done, first.Status);
        Assert.Equal(Ink(rig, FirstRunStepState.Done), first.LabelColour); // text2
        Assert.Equal(FirstRunStepState.Running, rig.Sheet.Rows[1].State);
        Assert.Equal(NthStatus.Working, rig.Sheet.Rows[1].Status);
        Assert.Equal(FirstRunStepState.Waiting, rig.Sheet.Rows[2].State);
        Assert.Equal(NthStatus.Waiting, rig.Sheet.Rows[2].Status);
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
        Assert.Equal((1 + 0.5) / 6, rig.Sheet.BarTarget, 6); // the bar springs to it (the pack's step 3 spring: critically damped, stiffness 110)
        Assert.Equal((1 + 0.5) / 6, rig.Sheet.BarFraction, 2);
        Assert.Equal("25%", rig.Sheet.PercentText);
        rig.Sheet.Report(GettingReadyRig.Event(plan[1], 1, 6, FirstRunStepState.Done, 4));
        rig.Sheet.Report(GettingReadyRig.Event(plan[2], 2, 6, FirstRunStepState.Running, 1)); // the light: 1 of 3
        Settle(rig);
        Assert.Equal((2 + 1.0 / 3) / 6, rig.Sheet.BarTarget, 6);
        Assert.Equal((2 + 1.0 / 3) / 6, rig.Sheet.BarFraction, 2);
        Assert.Equal("38%", rig.Sheet.PercentText);
    });

    [Fact] // GR-2.states
    [Trait("Decision", "ED-288")]
    public void A_clock_moved_by_hours_changes_nothing_no_step_ticks_and_the_bar_does_not_creep() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig();
        rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[0], 0, 6, FirstRunStepState.Running, 0));
        Settle(rig);
        var before = rig.Sheet.Rows.Select(row => (row.State, row.Status)).ToList();
        var bar = rig.Sheet.BarFraction;
        for (var hour = 0; hour < 5; hour++) rig.Frame(3600);
        Assert.Equal(before, rig.Sheet.Rows.Select(row => (row.State, row.Status)).ToList());
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
        Assert.Equal(1, rig.Sheet.BarTarget, 6);
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
        Assert.NotEqual(NthStatus.Done, rig.Sheet.Rows[1].Status);
        Assert.Equal(0, rig.Sheet.ReadyOpacity);
        Assert.True(rig.Sheet.BarTarget < 0.5);
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
        rig.Run(0.1);
        Assert.Equal(GettingReadyPhase.Ready, rig.Sheet.Phase); // the hold
        Assert.All(rig.Sheet.Rows, row => Assert.Equal(FirstRunStepState.Done, row.State));
        rig.Run(0.5);
        Assert.All(rig.Sheet.Rows, row => Assert.Equal(NthStatus.Done, row.Status));
        Assert.Equal(1, rig.Sheet.BarTarget, 6);
        Assert.Equal(GettingReadyFixture.Done, rig.Sheet.TitleWord);
        rig.Run(1.2);
        Assert.True(run.IsCompletedSuccessfully);
        Assert.Equal(FirstRunOutcomeKind.Prepared, run.Result.Kind);
        Assert.Equal(GettingReadyPhase.Gone, rig.Sheet.Phase);
    });
}
