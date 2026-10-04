using Avalonia.Media;
using Marrow.App.Port.FirstRun;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Port.FirstRun;

// GR-2: the frame-clock transition audit (OD-0076: nothing cuts). Every state change of the sheet, with the transition it gets:
//   a step's mark (spinner <-> check <-> dot)   opacity 0.25 s ease, scale 0.35 s cubic-bezier(.32,.72,0,1)   (gr_lib.py .tr)
//   a step's label going from waiting to live   colour 0.3 s ease                                          (gr_lib.py .r)
//   the bar's fill                               0.3 s cubic-bezier(.32,.72,0,1)                            (gr_lib.py .bar i)
//   the title "Getting ready" -> "Ready"         opacity 0.28 s ease, rise 0.38 s cubic-bezier(.32,.72,0,1) (gr_lib.py .stk)
//   the lift and the pane's return               1.1 s cubic in-out after a 0.15 s hold                     (gr-page.js HAND, HOLD, ease)
// All on the frame clock: the only wake-ups are frames, spans are measured on SwiftClock (ED-117).
[Collection("Headless")]
public sealed class GettingReadyTransitionAuditTests(HeadlessSession headless)
{
    private const double Frame = 1 / GettingReadyRig.Hz;

    private static void AllDone(GettingReadyRig rig)
    {
        for (var index = 0; index < rig.Plan.Count; index++)
        {
            rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[index], index, rig.Plan.Count, FirstRunStepState.Running, 0));
            rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[index], index, rig.Plan.Count, FirstRunStepState.Done, rig.Plan[index].Parts));
        }
        rig.Run(0.6);
    }

    private static double Lift(GettingReadyRig rig) => rig.Sheet.RenderTransform is TranslateTransform move ? move.Y : 0;

    [Fact] // GR-2.audit
    [Trait("Decision", "ED-288")]
    public void The_lift_holds_015_s_then_follows_the_fixtures_frames_for_11_s() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig();
        AllDone(rig);
        var run = rig.Sheet.EndAsync(new FirstRunOutcome(FirstRunOutcomeKind.Prepared));
        var height = rig.Rect(rig.Sheet.Card).Height;
        var at = 0.0;
        var frames = GettingReadyFixture.HandFrames();
        for (var index = 0; index < frames.Count; index++)
        {
            var instant = GettingReadyLook.Hold + GettingReadyLook.HandOverSeconds * index / 20;
            if (index == 20) instant += 0.002; // the last frame lands a hair after the end, never short of it
            rig.Frame(instant - at);
            at = instant;
            var frame = frames[index];
            var hand = GettingReadyFixture.Number(frame, "h");
            var label = $"hand-over {index}/20 (t = {instant:0.000})";
            Assert.True(Math.Abs(rig.Sheet.Hand - hand) <= 0.002, $"{label}: hand {rig.Sheet.Hand:0.0000}, fixture {hand:0.0000}");
            var lift = GettingReadyFixture.TranslateY(frame.GetProperty("sheetStyle").GetProperty("transform").GetString()!);
            Assert.True(Math.Abs(Lift(rig) - lift) <= 0.75, $"{label}: lift {Lift(rig):0.00}, fixture {lift:0.00}");
            var opacity = double.Parse(frame.GetProperty("sheetStyle").GetProperty("opacity").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(Math.Abs(rig.Sheet.Opacity - opacity) <= 0.005, $"{label}: sheet opacity {rig.Sheet.Opacity:0.000}, fixture {opacity:0.000}");
        }
        Assert.True(height > 200);
        Assert.True(run.IsCompletedSuccessfully);
    });

    [Fact] // GR-2.audit
    [Trait("Decision", "ED-288")]
    public void At_60_Hz_the_lift_never_jumps_and_never_runs_backwards() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig();
        AllDone(rig);
        _ = rig.Sheet.EndAsync(new FirstRunOutcome(FirstRunOutcomeKind.Prepared));
        var travel = rig.Rect(rig.Sheet.Card).Height + GettingReadyLook.LiftExtra;
        var hands = new List<double> { rig.Sheet.Hand };
        var lifts = new List<double> { Lift(rig) };
        var opacities = new List<double> { rig.Sheet.Opacity };
        var times = new List<double> { 0 };
        rig.Run(1.5, time =>
        {
            times.Add(time);
            hands.Add(rig.Sheet.Hand);
            lifts.Add(Lift(rig));
            opacities.Add(rig.Sheet.Opacity);
        });
        for (var index = 1; index < hands.Count; index++)
        {
            Assert.True(hands[index] >= hands[index - 1], $"hand ran backwards at {times[index]:0.000}");
            Assert.True(lifts[index] <= lifts[index - 1] + 1e-9, $"the sheet came back down at {times[index]:0.000}");
            Assert.True(opacities[index] <= opacities[index - 1] + 1e-9, $"the sheet brightened at {times[index]:0.000}");
            Assert.True(hands[index] - hands[index - 1] <= 0.05, $"hand jumped {hands[index] - hands[index - 1]:0.000} at {times[index]:0.000}"); // the cubic's steepest 60 Hz step is 0.0455
            Assert.True(Math.Abs(lifts[index] - lifts[index - 1]) <= 0.05 * travel, $"the sheet jumped {lifts[index] - lifts[index - 1]:0.0} px at {times[index]:0.000}");
            if (times[index] < GettingReadyLook.Hold - 1e-6) Assert.Equal(0, hands[index]); // the hold: nothing moves
        }
        Assert.Equal(1, hands[^1]);
    });

    [Fact] // GR-2.audit
    [Trait("Decision", "ED-288")]
    public void A_prepared_run_ends_015_s_plus_11_s_after_its_last_event_and_a_failed_one_11_s_never_longer_than_the_work() => headless.OnUiThread(() =>
    {
        double Ends(FirstRunOutcome outcome)
        {
            using var rig = new GettingReadyRig();
            AllDone(rig);
            var run = rig.Sheet.EndAsync(outcome);
            double ended = -1;
            rig.Run(2, time => { if (ended < 0 && run.IsCompleted) ended = time; });
            Assert.Equal(GettingReadyPhase.Gone, rig.Sheet.Phase);
            return ended;
        }

        var prepared = Ends(new FirstRunOutcome(FirstRunOutcomeKind.Prepared));
        Assert.InRange(prepared, GettingReadyLook.Hold + GettingReadyLook.HandOverSeconds - 0.001, GettingReadyLook.Hold + GettingReadyLook.HandOverSeconds + Frame + 0.001);
        foreach (var kind in new[] { FirstRunOutcomeKind.Failed, FirstRunOutcomeKind.Cancelled, FirstRunOutcomeKind.Skipped, FirstRunOutcomeKind.NotShown })
        {
            var ended = Ends(new FirstRunOutcome(kind));
            Assert.InRange(ended, GettingReadyLook.HandOverSeconds - 0.001, GettingReadyLook.HandOverSeconds + Frame + 0.001); // it just lifts away: no hold, no "Ready"
        }
    });

    [Fact] // GR-2.audit
    [Trait("Decision", "ED-288")]
    public void Frames_are_the_only_wake_ups_time_alone_moves_nothing_and_ends_nothing() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig();
        AllDone(rig);
        var run = rig.Sheet.EndAsync(new FirstRunOutcome(FirstRunOutcomeKind.Prepared));
        rig.Time.Advance(TimeSpan.FromHours(5)); // the clock moves, no frame is delivered
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(run.IsCompleted);
        Assert.Equal(GettingReadyPhase.Ready, rig.Sheet.Phase);
        Assert.Equal(0, rig.Sheet.Hand);
        rig.Frame(0.001); // the next frame finds the span long over: the sheet is gone at once, not stuck
        Assert.True(run.IsCompletedSuccessfully);
        Assert.Equal(1, rig.Sheet.Hand);
    });

    [Fact] // GR-2.audit
    [Trait("Decision", "ED-288")]
    public void A_step_finishing_swaps_spinner_for_check_along_the_pages_curves_with_no_jump() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig();
        rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[0], 0, 6, FirstRunStepState.Running, 0));
        rig.Run(0.6);
        var row = rig.Sheet.Rows[0];
        rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[0], 0, 6, FirstRunStepState.Done, 1));
        for (var index = 1; index <= 30; index++)
        {
            rig.Frame(Frame);
            var time = index * Frame;
            var opacity = GettingReadyCurves.Ease(time / 0.25);
            var scale = 0.5 + 0.5 * GettingReadyCurves.Settle(time / 0.35);
            Assert.True(Math.Abs(row.CheckOpacity - opacity) <= 0.03, $"{time:0.000}: check opacity {row.CheckOpacity:0.000}, page {opacity:0.000}");
            Assert.True(Math.Abs(row.CheckScale - scale) <= 0.03, $"{time:0.000}: check scale {row.CheckScale:0.000}, page {scale:0.000}");
            Assert.True(Math.Abs(row.SpinnerOpacity - (1 - opacity)) <= 0.03, $"{time:0.000}: spinner opacity {row.SpinnerOpacity:0.000}");
            Assert.True(Math.Abs(row.SpinnerScale - (1 - 0.5 * GettingReadyCurves.Settle(time / 0.35))) <= 0.03, $"{time:0.000}: spinner scale");
        }
        Assert.Equal((1.0, 1.0, 0.0), (row.CheckOpacity, row.CheckScale, row.SpinnerOpacity));
    });

    [Fact] // GR-2.audit
    [Trait("Decision", "ED-288")]
    public void A_step_starting_dims_up_its_label_in_03_s_and_the_dot_gives_way_to_the_spinner() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig();
        var row = rig.Sheet.Rows[0];
        var from = row.LabelOpacity;
        var dot = row.DotOpacity;
        rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[0], 0, 6, FirstRunStepState.Running, 0));
        var last = from;
        for (var index = 1; index <= 24; index++)
        {
            rig.Frame(Frame);
            var time = index * Frame;
            var share = GettingReadyCurves.Ease(time / 0.3);
            Assert.True(Math.Abs(row.LabelOpacity - (from + (1 - from) * share)) <= 0.03, $"{time:0.000}: label {row.LabelOpacity:0.000}");
            Assert.True(row.LabelOpacity >= last - 1e-9);
            last = row.LabelOpacity;
            var gone = GettingReadyCurves.Ease(time / 0.25);
            Assert.True(Math.Abs(row.DotOpacity - dot * (1 - gone)) <= 0.03, $"{time:0.000}: dot {row.DotOpacity:0.000}");
            Assert.True(Math.Abs(row.SpinnerOpacity - gone) <= 0.03, $"{time:0.000}: spinner {row.SpinnerOpacity:0.000}");
        }
    });

    [Fact] // GR-2.audit
    [Trait("Decision", "ED-288")]
    public void The_bar_eases_to_the_new_share_in_03_s() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig();
        rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[0], 0, 6, FirstRunStepState.Done, 1));
        for (var index = 1; index <= 24; index++)
        {
            rig.Frame(Frame);
            var expected = 1.0 / 6 * GettingReadyCurves.Settle(index * Frame / 0.3);
            Assert.True(Math.Abs(rig.Sheet.BarFraction - expected) <= 0.01, $"{index * Frame:0.000}: bar {rig.Sheet.BarFraction:0.0000}, page {expected:0.0000}");
        }
        Assert.Equal(1.0 / 6, rig.Sheet.BarFraction, 9);
    });

    [Fact] // GR-2.audit
    [Trait("Decision", "ED-288")]
    public void Ready_replaces_the_title_by_a_crossfade_with_a_short_rise_never_a_cut() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig();
        AllDone(rig);
        Assert.Equal(GettingReadyFixture.Title, rig.Sheet.TitleWord);
        _ = rig.Sheet.EndAsync(new FirstRunOutcome(FirstRunOutcomeKind.Prepared));
        var opacity = new List<double>();
        var rise = new List<double>();
        for (var index = 1; index <= 30; index++)
        {
            rig.Frame(Frame);
            var time = index * Frame;
            opacity.Add(rig.Sheet.ReadyOpacity);
            rise.Add(rig.Sheet.ReadyRise);
            Assert.True(Math.Abs(rig.Sheet.ReadyOpacity - GettingReadyCurves.Ease(time / 0.28)) <= 0.03, $"{time:0.000}: fade {rig.Sheet.ReadyOpacity:0.000}");
            Assert.True(Math.Abs(rig.Sheet.ReadyRise - GettingReadyCurves.Settle(time / 0.38)) <= 0.03, $"{time:0.000}: rise {rig.Sheet.ReadyRise:0.000}");
        }
        Assert.Equal(opacity.Order().ToList(), opacity);
        Assert.Equal(rise.Order().ToList(), rise);
        Assert.True(opacity[0] < 0.5); // the first frame is a step on the way, not the end
        Assert.Equal(GettingReadyFixture.Done, rig.Sheet.TitleWord);
    });
}
