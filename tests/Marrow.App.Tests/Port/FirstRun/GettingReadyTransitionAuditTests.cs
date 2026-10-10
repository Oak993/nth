using Avalonia.Media;
using Marrow.App.Port.FirstRun;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Port.Nth;

namespace Marrow.App.Tests.Port.FirstRun;

// GR-2: the frame-clock transition audit (OD-0076: nothing cuts). Every state change of the sheet, with the transition it gets:
//   a step's mark (ring <-> spinner <-> disc)   the nth status mark's cross-fade, 0.2 s                    (nth motion pack step 15)
//   a step's label changing ink                  colour 0.25 s ease                                         (nth-motion-review.html .grl li)
//   the bar's fill                               the pack's step 3 spring (critically damped, stiffness 110)
//   the title "Getting ready" -> "Ready"         opacity 0.25 s ease, rise 5 px over 0.3 s cubic-bezier(.2,.8,.2,1) (nth-motion-review.html .gr .gt)
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
            // the fixture's sheet is 224.25 high, the app's a line taller where its font runs the sentence to two lines: the same share of its own
            // height and 30 px (--hi) is the same lift
            var share = -GettingReadyFixture.TranslateY(frame.GetProperty("sheetStyle").GetProperty("transform").GetString()!) / (224.25 + GettingReadyLook.LiftExtra);
            var lift = -share * (height + GettingReadyLook.LiftExtra);
            Assert.True(Math.Abs(Lift(rig) - lift) <= 0.75, $"{label}: lift {Lift(rig):0.00}, fixture's share {share:0.0000} of the sheet gives {lift:0.00}");
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
            // --hi = h / .56 carries the sheet: the page's steepest 60 Hz step of it is 8.1 % of its travel (the cubic's 4.5 % of the hand-over over .56)
            Assert.True(hands[index] - hands[index - 1] <= 0.05, $"hand jumped {hands[index] - hands[index - 1]:0.000} at {times[index]:0.000}"); // the cubic's steepest 60 Hz step is 0.0455
            Assert.True(Math.Abs(lifts[index] - lifts[index - 1]) <= 0.09 * travel, $"the sheet jumped {lifts[index] - lifts[index - 1]:0.0} px at {times[index]:0.000}");
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
    public void A_step_finishing_hands_the_slot_to_the_nth_mark_and_moves_the_label_to_its_done_ink_in_025_s() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig();
        rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[0], 0, 6, FirstRunStepState.Running, 0));
        rig.Run(0.6);
        var row = rig.Sheet.Rows[0];
        var from = row.LabelColour;
        var to = GettingReadyRowView.InkFor(FirstRunStepState.Done).Resolve(rig.Sheet.ActualThemeVariant);
        rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[0], 0, 6, FirstRunStepState.Done, 1));
        Assert.Equal(NthStatus.Done, row.Status); // the mark is told in the step's own frame; it cross-fades itself on the render thread
        var last = 0.0;
        for (var index = 1; index <= 24; index++)
        {
            rig.Frame(Frame);
            var time = index * Frame;
            var share = GettingReadyCurves.Ease(time / 0.25);
            // the ink moves along the page's ease from the working ink to the done ink (alpha is the channel that differs: text -> text2 is 100 % -> 70 % of the label colour)
            var expected = from.A + (to.A - from.A) * share;
            Assert.True(Math.Abs(row.LabelColour.A - expected) <= 8, $"{time:0.000}: ink alpha {row.LabelColour.A}, expected {expected:0}");
            Assert.True(row.LabelColour.A <= last || last == 0, $"{time:0.000}: the ink only moves one way");
            last = row.LabelColour.A;
        }
        Assert.Equal(to, row.LabelColour);
    });

    [Fact] // GR-2.audit
    [Trait("Decision", "ED-288")]
    public void A_step_starting_moves_its_label_ink_up_in_025_s_and_its_mark_becomes_the_spinner() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig();
        var row = rig.Sheet.Rows[0];
        var from = row.LabelColour;
        var to = GettingReadyRowView.InkFor(FirstRunStepState.Running).Resolve(rig.Sheet.ActualThemeVariant);
        Assert.Equal(NthStatus.Waiting, row.Status);
        rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[0], 0, 6, FirstRunStepState.Running, 0));
        Assert.Equal(NthStatus.Working, row.Status);
        var last = (double)from.A;
        for (var index = 1; index <= 24; index++)
        {
            rig.Frame(Frame);
            var time = index * Frame;
            var share = GettingReadyCurves.Ease(time / 0.25);
            Assert.True(Math.Abs(row.LabelColour.A - (from.A + (to.A - from.A) * share)) <= 8, $"{time:0.000}: label ink alpha {row.LabelColour.A}");
            Assert.True(row.LabelColour.A >= last - 1e-9);
            last = row.LabelColour.A;
        }
        Assert.Equal(to, row.LabelColour);
    });

    [Fact] // GR-2.audit
    [Trait("Decision", "ED-288")]
    public void The_bar_springs_to_the_new_share_on_the_packs_step_3_spring() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig();
        rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[0], 0, 6, FirstRunStepState.Done, 1));
        for (var index = 1; index <= 60; index++)
        {
            rig.Frame(Frame);
            // NthSpring: critically damped, stiffness 110, from rest: v(t) = target * (1 - (1 + w t) e^(-w t))
            var t = index * Frame;
            var expected = 1.0 / 6 * (1 - (1 + NthSpring.Omega * t) * Math.Exp(-NthSpring.Omega * t));
            Assert.True(Math.Abs(rig.Sheet.BarFraction - expected) <= 0.002, $"{t:0.000}: bar {rig.Sheet.BarFraction:0.0000}, spring {expected:0.0000}");
        }
        Assert.Equal(1.0 / 6, rig.Sheet.BarTarget, 9);
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
            Assert.True(Math.Abs(rig.Sheet.ReadyOpacity - GettingReadyCurves.Ease(time / 0.25)) <= 0.03, $"{time:0.000}: fade {rig.Sheet.ReadyOpacity:0.000}");
            Assert.True(Math.Abs(rig.Sheet.ReadyRise - GettingReadyCurves.CubicBezier(0.2, 0.8, 0.2, 1, time / 0.3)) <= 0.03, $"{time:0.000}: rise {rig.Sheet.ReadyRise:0.000}");
        }
        Assert.Equal(opacity.Order().ToList(), opacity);
        Assert.Equal(rise.Order().ToList(), rise);
        Assert.True(opacity[0] < 0.5); // the first frame is a step on the way, not the end
        Assert.Equal(GettingReadyFixture.Done, rig.Sheet.TitleWord);
    });
}
