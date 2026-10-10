using Marrow.App.Port.FirstRun;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;

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
        // the label's ink is at its end in the same frame (a still sheet moves nothing), and the status mark is told at once; its own change is the pack's calm cross-fade
        Assert.Equal((NthStatus.Working, GettingReadyRowView.InkFor(FirstRunStepState.Running).Resolve(rig.Sheet.ActualThemeVariant)), (row.Status, row.LabelColour));
        rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[0], 0, 6, FirstRunStepState.Done, 1));
        rig.Frame(Frame);
        Assert.Equal((NthStatus.Done, GettingReadyRowView.InkFor(FirstRunStepState.Done).Resolve(rig.Sheet.ActualThemeVariant)), (row.Status, row.LabelColour));
        Assert.True(row.StatusMark.Calm);
        Assert.Equal(1.0 / 6, rig.Sheet.BarTarget, 9); // the bar's calm spring is a 0.1 s ease
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
    public void The_spinner_keeps_going_under_reduce_motion_as_the_packs_calm_pulse() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig(reduceMotion: true);
        rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[0], 0, 6, FirstRunStepState.Running, 0));
        rig.Frame(Frame);
        var mark = rig.Sheet.Rows[0].StatusMark;
        Assert.Equal(NthStatus.Working, mark.Status);
        Assert.True(mark.Calm); // step 1 "Calm": the line stands still on the tile and its opacity eases 0.6 to 1 every 2.4 s, for as long as the step runs
        var frame = mark.Snapshot();
        using var painter = new NthStatusPainter();
        var now = NthFrame.Seconds(frame.Clock);
        Assert.True(painter.Moving(frame, now));
        Assert.True(painter.Moving(frame, now + 60));
    });
}
