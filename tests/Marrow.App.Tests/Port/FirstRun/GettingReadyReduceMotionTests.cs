using Avalonia.VisualTree;
using Marrow.App.Port.FirstRun;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port.FirstRun;

// GR-2: reduce motion. The sheet is OD-0096's, an addition beyond the Mac, so the system's reduce-motion setting stills every
// transition it adds: each goes to its end state in one frame, nothing else changes (ED-306 on the rule of the lead's
// decision of 2026-10-04, "(3) a transition OD-0076 adds where the Mac snaps is stilled by the setting"). The hold before the
// lift still runs (it is no motion: the page keeps it too, gr-page.js: h = reduce ? (t >= handStart ? 1 : 0)), the spinner keeps
// turning (ED-231: nothing in the source stops an indeterminate ProgressView), and the work is never made longer.
[Collection("Headless")]
public sealed class GettingReadyReduceMotionTests(HeadlessSession headless)
{
    private const double Frame = 1 / GettingReadyRig.Hz;

    [Fact] // GR-2.reduce
    [Trait("Decision", "ED-084")]
    public void A_step_changing_state_is_at_its_end_in_the_same_frame() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig(reduceMotion: true);
        var row = rig.Sheet.Rows[0];
        rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[0], 0, 6, FirstRunStepState.Running, 0));
        rig.Frame(Frame);
        Assert.Equal((1.0, 1.0, 0.0, 1.0), (row.SpinnerOpacity, row.SpinnerScale, row.DotOpacity, row.LabelOpacity));
        rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[0], 0, 6, FirstRunStepState.Done, 1));
        rig.Frame(Frame);
        Assert.Equal((1.0, 1.0, 0.0), (row.CheckOpacity, row.CheckScale, row.SpinnerOpacity));
        Assert.Equal(1.0 / 6, rig.Sheet.BarFraction, 9);
    });

    [Fact] // GR-2.reduce
    [Trait("Decision", "ED-084")]
    public void Ready_replaces_the_title_in_one_frame_after_the_hold_the_sheet_is_gone_in_one_frame_and_the_run_ends() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig(reduceMotion: true);
        for (var index = 0; index < 6; index++)
        {
            rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[index], index, 6, FirstRunStepState.Running, 0));
            rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[index], index, 6, FirstRunStepState.Done, rig.Plan[index].Parts));
        }
        rig.Frame(Frame);
        var run = rig.Sheet.EndAsync(new FirstRunOutcome(FirstRunOutcomeKind.Prepared));
        var hands = new List<double>();
        double? ended = null;
        rig.Run(0.5, time =>
        {
            hands.Add(rig.Sheet.Hand);
            Assert.Equal((1.0, 1.0), (rig.Sheet.ReadyOpacity, rig.Sheet.ReadyRise)); // the word is swapped at once
            if (ended is null && run.IsCompleted) ended = time;
        });
        Assert.Equal(GettingReadyFixture.Done, rig.Sheet.TitleWord);
        Assert.All(hands, hand => Assert.True(hand is 0 or 1, $"hand {hand}: a value between the two is a transition")); // the hold, then the end: no frame between
        Assert.InRange(ended!.Value, GettingReadyLook.Hold - 0.001, GettingReadyLook.Hold + Frame + 0.001); // the 0.15 s hold, nothing more
        Assert.Equal(GettingReadyPhase.Gone, rig.Sheet.Phase);
    });

    [Fact] // GR-2.reduce
    [Trait("Decision", "ED-084")]
    public void A_run_that_did_not_finish_leaves_in_the_first_frame() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig(reduceMotion: true);
        var run = rig.Sheet.EndAsync(new FirstRunOutcome(FirstRunOutcomeKind.Failed, "x"));
        rig.Frame(Frame);
        Assert.True(run.IsCompletedSuccessfully);
        Assert.Equal(1, rig.Sheet.Hand);
    });

    [Fact] // GR-2.reduce
    [Trait("Decision", "ED-231")]
    public void The_spinner_keeps_turning_under_reduce_motion() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig(reduceMotion: true);
        rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[0], 0, 6, FirstRunStepState.Running, 0));
        rig.Frame(Frame);
        var spinner = rig.Sheet.Rows[0].Mark.GetVisualDescendants().OfType<SystemSpinner>().Single();
        var leads = new HashSet<int> { spinner.Lead };
        rig.Run(1.2, _ => leads.Add(spinner.Lead));
        Assert.Equal(SystemSpinner.Spokes, leads.Count);
    });
}
