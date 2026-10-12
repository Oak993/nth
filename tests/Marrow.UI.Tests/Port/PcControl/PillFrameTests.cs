using System.Globalization;
using Avalonia.Controls;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.PcControl;
using Marrow.UI.Tests.Fixtures;
using Xunit.Abstractions;

namespace Marrow.UI.Tests.Port.PcControl;

// PORT-ONLY: Tactus, the pill's frames (P30; OD-0145, ED-NEW-pcctl-30; DESIGN.md 10.3, 10.4, 10.6, 10.11 and 5.2 "Motion").
// Every gesture is read back frame by frame at 240 Hz the way the compositor plays its key frames, and held against the
// closed form: each frame is where its curve is (within the 1 ms keys' own error), a gesture mid-flight starts where the pill
// is and at its speed, a flick passes its target or its release point by at most 3 DIP, a travel from rest lands within
// 1 DIP by 400 ms, a size change grows away from the anchor and never past its size, Stop keeps full opacity on the right
// edge through a group swap, a swap turned back mid-way never jumps, the card opens on the body's edges and holds its place,
// and Reduce Motion eases a short travel and hides a long one's jump inside its fade. No input, no window but the card's.
// Each gesture also prints a "metric" line in the words scripts/pill-canvas-metrics.mjs prints for the canvas.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-pcctl-30")]
public sealed class PillFrameTests(HeadlessSession headless, ITestOutputHelper output)
{
    private const double Frame = 1.0 / 240;

    // DESIGN.md 10.11: the 1 ms keys stay within 0.15 DIP of the curve up to 2,000 DIP
    private const double KeyError = 0.15;

    private static readonly PillApp Excel = new("excel", "Excel", "Filling row 12 of 40");
    private static readonly PillAsk Ask = new("a1", PillAskKind.Approval, "Outlook", "Quarterly report", "GPT-6.1 Sol") { Head = "Head 2" };
    private static readonly PillApprovalBody Body = new("a1", 0, "Outlook", "C:\\office\\outlook.exe", Risky: false, UseAsked: true, AllowAlways: false, Who: "Head 2");

    private static PillView Working(string step = "Filling row 12 of 40") =>
        PillStates.Compute(new PillFacts { Apps = [Excel with { Step = step }] }, 10);

    private static PillView Paused() =>
        PillStates.Compute(new PillFacts { Apps = [Excel with { Phase = PillAppPhase.Paused, PhaseSince = 9 }] }, 10);

    private static PillView Asking(bool card) => PillStates.Compute(new PillFacts { Apps = [Excel], Asks = [Ask], Card = card }, 10);

    private static PillView Stopped() => PillStates.Compute(new PillFacts { Apps = [Excel with { Phase = PillAppPhase.Stopped, PhaseSince = 9 }] }, 10);

    private static (ControlPill Pill, PillFrames Frames, RecordingPillAnimator Animator, ManualTimeProvider Clock, double T0) Make(
        PillVector anchor, double side, bool reduce = false, PillView? view = null)
    {
        var animator = new RecordingPillAnimator();
        var clock = new ManualTimeProvider();
        var pill = new ControlPill(PillDriverKind.RenderThread, animator, clock) { ReduceMotion = reduce };
        var frames = new PillFrames(pill, animator);
        var t0 = pill.Now;
        frames.At(t0, () =>
        {
            pill.Show(view ?? Working(), t0);
            pill.Place(anchor, side, t0);
            pill.Present(t0);
        });
        return (pill, frames, animator, clock, t0);
    }

    private static double At(double start, int frame) => start + (frame * Frame);

    private static int FramesTo(double start, double end) => (int)Math.Ceiling((end - start) / Frame);

    // The 1 ms keys' own error at t: a line between two keys is off its curve by at most |x''| h^2 / 8 (the second
    // difference over one key spacing, with a margin for x'' changing inside it), never less than DESIGN.md's 0.15 DIP.
    private static double KeysError(PillMotion motion, double t)
    {
        const double h = PillTrack.Step;
        var bend = (motion.At(t - h).Anchor - (motion.At(t).Anchor * 2) + motion.At(t + h).Anchor).Length;
        return Math.Max(KeyError, (bend / 8 * 1.5) + 1e-3);
    }

    private static void Near(PillVector expected, PillVector actual, double tolerance, string what) =>
        Assert.True((actual - expected).Length <= tolerance, $"{what}: {actual} is {(actual - expected).Length} from {expected}");

    private static void Near(double expected, double actual, double tolerance, string what) =>
        Assert.True(Math.Abs(actual - expected) <= tolerance, $"{what}: {actual} is {Math.Abs(actual - expected)} from {expected}");

    // The steepest slope of the slides' ease, cubic-bezier(0.32, 0.72, 0, 1), sampled finely.
    private static double SteepestSlide()
    {
        var steepest = 0.0;
        for (var k = 0; k < 2000; k++)
            steepest = Math.Max(steepest, (PillEase.CubicBezier(0.32, 0.72, 0, 1, (k + 1) / 2000.0) - PillEase.CubicBezier(0.32, 0.72, 0, 1, k / 2000.0)) * 2000);
        return steepest;
    }

    // One gesture's metric line: the largest frame step and the overshoot past the end, when it is within 1 DIP of its end
    // for good, the width's span, largest step and overshoot, and Stop's least opacity (the canvas script's words).
    private void Metric(string label, IReadOnlyList<(double Ms, double X, double Width, double Stop)> frames)
    {
        var first = frames[0];
        var last = frames[^1];
        var step = 0.0;
        var widthStep = 0.0;
        for (var i = 1; i < frames.Count; i++)
        {
            step = Math.Max(step, Math.Abs(frames[i].X - frames[i - 1].X));
            widthStep = Math.Max(widthStep, Math.Abs(frames[i].Width - frames[i - 1].Width));
        }
        var direction = Math.Sign(last.X - first.X);
        var overshoot = direction == 0 ? 0 : Math.Max(0, frames.Max(f => (f.X - last.X) * direction));
        var widthDirection = last.Width >= first.Width ? 1 : -1;
        var widthOvershoot = Math.Max(0, frames.Max(f => (f.Width - last.Width) * widthDirection));
        var landed = "n/a";
        for (var i = frames.Count - 1; i >= 0 && Math.Abs(frames[i].X - last.X) <= 1; i--) landed = frames[i].Ms.ToString("F2", CultureInfo.InvariantCulture);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"metric {label}: frames {frames.Count}, largest step {step:F2} DIP, overshoot {overshoot:F2} DIP, within 1 DIP from {landed} ms, width {first.Width:F2} to {last.Width:F2} DIP (largest step {widthStep:F2}, overshoot {widthOvershoot:F2}), Stop opacity min {frames.Min(f => f.Stop):F2}"));
    }

    [Fact]
    public void A_travel_retargeted_and_flicked_mid_flight_is_on_its_curve_in_every_frame_and_never_jumps() => headless.OnUiThread(() =>
    {
        var (pill, frames, _, _, t0) = Make(new PillVector(450, 8), 0.5);
        var retarget = t0 + 0.13;
        var flick = t0 + 0.29;
        var thrown = new PillVector(-3000, 0);
        frames.At(t0, () => pill.TravelTo(new PillVector(1700, 8), 0.5, at: t0));
        var first = pill.Motion;
        frames.At(retarget, () => pill.TravelTo(new PillVector(300, 300), 0.5, at: retarget));
        var second = pill.Motion;
        frames.At(flick, () => pill.TravelTo(new PillVector(900, 8), 0.5, thrown, flick));
        var third = pill.Motion;
        // the glass draws the very curve the content's key frames sample (the render-thread surfaces get the motion itself)
        Assert.Same(third, pill.Body.Last!.Motion);
        Assert.Same(third, pill.StopSurface.Last!.Motion);

        // each new curve starts where the pill is; a retarget keeps the speed it had, a flick takes the throw's
        Assert.True(first.Velocity(retarget).Length > 500, "it was moving when it was retargeted");
        Near(first.At(retarget).Anchor, second.At(retarget).Anchor, 1e-6, "retarget place");
        Near(first.Velocity(retarget), second.Velocity(retarget), 1e-6, "retarget speed");
        Near(second.At(flick).Anchor, third.At(flick).Anchor, 1e-6, "flick place");
        Near(thrown, third.Velocity(flick), 1e-6, "flick speed");

        // every frame is where its curve is, and no frame steps further than the curve moves in it
        var count = FramesTo(t0, third.DoneAt() + 0.1);
        var shown = new List<(double, double, double, double)>();
        for (var i = 0; i <= count; i++)
        {
            var t = At(t0, i);
            var pose = frames.Expected(t);
            var drawn = frames.Anchor(t);
            shown.Add(((t - t0) * 1000, drawn.X, pose.Width, frames.Opacity(pill.StopSlot, t)));
            Near(pose.Anchor + pose.ScaleOffset, drawn, KeysError(frames.MotionAt(t), t), "frame " + i);
            if (i == 0) continue;
            var before = At(t0, i - 1);
            var fastest = Math.Max(frames.MotionAt(before).Velocity(before).Length, frames.MotionAt(t).Velocity(t).Length);
            fastest = Math.Max(fastest, frames.MotionAt(t).Velocity((before + t) / 2).Length);
            var step = (drawn - frames.Anchor(before)).Length;
            Assert.True(step <= (fastest * Frame * 1.05) + (2 * KeysError(frames.MotionAt(t), t)) + PillSpring.EndPixels,
                $"frame {i}: a step of {step} DIP at {fastest} DIP/s");
        }
        Near(new PillVector(900, 8), frames.Anchor(At(t0, count)), 1e-9, "landed");
        Metric("retarget and flick", shown);
    });

    [Fact]
    public void From_rest_every_distance_lands_within_a_DIP_by_400_ms_and_never_passes_its_target() => headless.OnUiThread(() =>
    {
        var from = new PillVector(100, 100);
        var (pill, frames, _, _, t0) = Make(from, 0.5);
        var start = t0;
        var travels = new (double Distance, PillVector Direction)[]
        {
            (10, new PillVector(1, 0)), (60, new PillVector(-1, 0)), (200, new PillVector(1, 0)), (728, new PillVector(0.6, 0.8)),
            (2000, new PillVector(1, 0)), (4000, new PillVector(-1, 0)),
        };
        foreach (var (distance, direction) in travels)
        {
            var target = from + (direction * distance);
            var at = start;
            frames.At(at, () => pill.TravelTo(target, 0.5, at: at));
            var motion = pill.Motion;
            var shown = new List<(double, double, double, double)>();
            for (var i = 0; i <= 360; i++)
            {
                var t = At(at, i);
                var drawn = frames.Anchor(t);
                shown.Add(((t - at) * 1000, ((drawn - from).X * direction.X) + ((drawn - from).Y * direction.Y), motion.At(t).Width, 1));
                var what = distance + " DIP, frame " + i;
                Near(motion.At(t).Anchor, drawn, KeysError(motion, t), what);
                var moved = drawn - from;
                var along = (moved.X * direction.X) + (moved.Y * direction.Y);
                var across = (moved.X * direction.Y) - (moved.Y * direction.X);
                Assert.True(along <= distance + 1e-6, $"{what}: {along - distance} DIP past its target");
                Assert.True(Math.Abs(across) <= 1e-6, $"{what}: {across} DIP off its line");
                if (t - at >= PillSpring.LandingSeconds - 1e-9)
                    Assert.True((drawn - target).Length <= PillSpring.LandingDistance + 1e-3, $"{what}: {(drawn - target).Length} DIP from its target");
            }
            Near(target, frames.Anchor(At(at, 360)), 1e-9, distance + " DIP landed");
            Metric(string.Create(CultureInfo.InvariantCulture, $"travel {distance} DIP"), shown);
            pill.Settled(At(at, 360));
            Assert.True(pill.Motion.AtRest);
            from = target;
            start = at + 2;
        }
    });

    [Theory]
    [InlineData(400, 500, 3000, 0)]
    [InlineData(500, 400, 1500, 0)]
    [InlineData(400, 500, 0, 1500)]
    public void A_flick_passes_its_target_or_its_release_point_by_at_most_3_DIP_in_every_frame(double fromX, double toX, double throwX, double throwY) =>
        headless.OnUiThread(() =>
        {
            // thrown at its target, thrown away from it, thrown across its line
            var (pill, frames, _, _, t0) = Make(new PillVector(fromX, 8), 0.5);
            frames.At(t0, () => pill.TravelTo(new PillVector(toX, 8), 0.5, new PillVector(throwX, throwY), t0));
            var motion = pill.Motion;
            var low = Math.Min(fromX, toX) - PillSpring.MaxExcursion - 1e-6;
            var high = Math.Max(fromX, toX) + PillSpring.MaxExcursion + 1e-6;
            var farthest = 0.0;
            var count = FramesTo(t0, motion.DoneAt() + 0.1);
            for (var i = 0; i <= count; i++)
            {
                var t = At(t0, i);
                var drawn = frames.Anchor(t);
                Near(motion.At(t).Anchor, drawn, KeysError(motion, t), "frame " + i);
                Assert.True(drawn.X >= low && drawn.X <= high, $"frame {i}: x {drawn.X} outside {low}..{high}");
                Assert.True(Math.Abs(drawn.Y - 8) <= PillSpring.MaxExcursion + 1e-6, $"frame {i}: y {drawn.Y}");
                farthest = Math.Max(farthest, throwY != 0 ? Math.Abs(drawn.Y - 8) : throwX > 0 ? drawn.X - Math.Max(fromX, toX) : 0);
            }
            // the throw did carry it past (so the 3 DIP cap is what held it), and it landed
            Assert.True(farthest > 1, $"the throw carried it only {farthest} DIP past");
            Near(new PillVector(toX, 8), frames.Anchor(At(t0, count)), 1e-9, "landed");
        });

    [Theory]
    [InlineData(0.0, true)]
    [InlineData(0.5, true)]
    [InlineData(1.0, true)]
    [InlineData(0.0, false)]
    [InlineData(0.5, false)]
    [InlineData(1.0, false)]
    public void A_width_change_moves_only_the_edge_away_from_the_anchor_in_every_frame_and_never_passes_its_width(double side, bool grows) =>
        headless.OnUiThread(() =>
    {
        // a new run after a stop grows the pill (214 to 440 DIP); an ask narrows it and folds Stop's key cap (440 to 399)
        var (pill, frames, _, _, t0) = Make(new PillVector(600, 8), side, view: grows ? Stopped() : Working());
        var before = pill.Motion.Final.Width;
        frames.At(t0, () => pill.Show(grows ? Working() : Asking(card: false), t0));
        var motion = pill.Motion;
        var width = motion.Final.Width;
        Assert.True(grows ? width > before + 40 : width < before - 20, $"the pill went from {before} to {width} DIP");
        var parts = pill.Parts;
        var laid = frames.Laid(t0);
        var lastText = PillPlaces.Of(motion.At(t0), parts).TextClip;
        // the text column follows the width and Stop's cap together: it moves one way only, to its end
        var textToward = Math.Sign(laid.TextClip - lastText);
        var count = FramesTo(t0, motion.DoneAt()) + 1;
        var shown = new List<(double, double, double, double)>();
        for (var i = 0; i <= count; i++)
        {
            var t = At(t0, i);
            var pose = motion.At(t);
            var want = PillPlaces.Of(pose, parts);
            shown.Add(((t - t0) * 1000, frames.Anchor(t).X, pose.Width, frames.Opacity(pill.StopSlot, t)));
            var what = (grows ? "grows" : "narrows") + ", side " + side + ", frame " + i;
            // the anchor holds still; the parts are where the curve puts them
            Near(PillVector.Zero, frames.Offset(pill.Stage, t), 1e-9, what + " stage");
            var row = laid.RowLeft + frames.Offset(pill.Row, t).X;
            var group = laid.GroupLeft + frames.Offset(pill.Group, t).X;
            var stopLeft = laid.StopLeft + frames.Offset(pill.StopSlot, t).X;
            var stopRight = stopLeft + frames.Size(pill.StopClip, t).X + PillMotionDriver.StopClipInset;
            var text = frames.Size(pill.TextClip, t).X;
            Near(want.RowLeft, row, KeyError, what + " row");
            Near(want.GroupLeft, group, KeyError, what + " group");
            Near(want.StopLeft, stopLeft, KeyError, what + " Stop");
            Near(want.TextClip, text, KeyError, what + " text clip");
            Near(want.BodyRight - PillLayout.Gap, stopRight, 2 * KeyError, what + " Stop's right edge");
            // it grows away from its anchor: the left edge holds on a left anchor, the right edge on a right one, both
            // edges move alike about a centre one
            if (side == 0) Near(8, row, 1e-6, what + " left edge");
            if (side == 1) Near(-PillLayout.Gap, stopRight, 1e-6, what + " right edge");
            if (side == 0.5) Near(0, row - 8 + stopRight + PillLayout.Gap, 3 * KeyError, what + " centre");
            // a size change from rest never passes its size and never turns back
            var toward = grows ? 1 : -1;
            Assert.True((width - pose.Width) * toward >= -1e-6 && (pose.Width - before) * toward >= -1e-6, $"{what}: width {pose.Width} outside {before}..{width}");
            Assert.True((laid.TextClip - text) * textToward >= -1e-6 && (text - lastText) * textToward >= -1e-6, $"{what}: text clip {text} after {lastText}");
            lastText = text;
        }
        Metric(string.Create(CultureInfo.InvariantCulture, $"width {(grows ? "grows" : "narrows")}, side {side}"), shown);
    });

    [Fact]
    public void Through_a_group_swap_turned_back_mid_way_Stop_keeps_full_opacity_on_the_right_edge_and_nothing_jumps() => headless.OnUiThread(() =>
    {
        var (pill, frames, _, clock, t0) = Make(new PillVector(600, 8), 0.5);
        frames.At(t0, () => pill.Show(Paused(), t0));
        frames.Pass(clock, 0.08);
        var back = pill.Now;
        frames.At(back, () => pill.Show(Working(), back));
        frames.Pass(clock, 0.6);
        var end = pill.Now;
        var run = pill.GroupOf(PillGroupKind.Run)!;
        var paused = pill.GroupOf(PillGroupKind.Paused)!;

        // no fade anywhere, and Stop never scales or dims
        Assert.DoesNotContain(frames.All, c => c.Kind == "Fade");
        foreach (var stop in new Avalonia.Visual[] { pill.StopSlot, pill.StopClip, pill.StopSurface })
        {
            Assert.DoesNotContain(frames.On(stop), c => c.Kind == "Scale");
            Assert.All(frames.On(stop).Where(c => c.Kind is "SetOpacity" or "SetScale"), c => Assert.Equal(1, c.A, 9));
        }

        // a slide moves at most this far in a frame: twice its height (a turn back from the far side) at the ease's steepest
        var steepest = SteepestSlide();
        double Slide(double height) => (2 * height * steepest / ControlPill.TextSwapSeconds * Frame * 1.1) + 0.1;
        var blocks = frames.Targets().OfType<TextBlock>().ToList();
        Assert.NotEmpty(blocks);
        var heights = blocks.ToDictionary(b => b, b => frames.On(b).Where(c => c.Kind == "Translate").SelectMany(c => c.Y!.Values).Select(v => Math.Abs(v)).DefaultIfEmpty(0).Max());
        var scaled = frames.Targets().Where(v => frames.On(v).Any(c => c.Kind == "Scale")).ToList();
        Assert.NotEmpty(scaled);

        var count = FramesTo(t0, end);
        var shown = new List<(double, double, double, double)>();
        for (var i = 0; i <= count; i++)
        {
            var t = At(t0, i);
            var what = "frame " + i;
            var pose = frames.Expected(t);
            var laid = frames.Laid(t);
            shown.Add(((t - t0) * 1000, frames.Anchor(t).X, pose.Width, frames.Opacity(pill.StopSlot, t)));
            Near(1, frames.Opacity(pill.StopSlot, t), 1e-9, what + " Stop opacity");
            Near(1, frames.Scale(pill.StopSlot, t), 1e-9, what + " Stop scale");
            var stopRight = laid.StopLeft + frames.Offset(pill.StopSlot, t).X + frames.Size(pill.StopClip, t).X + PillMotionDriver.StopClipInset;
            Near(PillPlaces.Of(pose, pill.Parts).BodyRight - PillLayout.Gap, stopRight, 2 * KeyError, what + " Stop's right edge");
            Near(PillVector.Zero, frames.Offset(pill.Stage, t), 1e-9, what + " stage");
            if (i == 0) continue;
            var before = At(t0, i - 1);
            // the groups slide under their clip: a step larger than a slide's is a jump, unless both frames are out of sight
            foreach (var group in new[] { run, paused })
            {
                var y0 = frames.Offset(group, before).Y;
                var y1 = frames.Offset(group, t).Y;
                var hidden = Math.Abs(y0) >= PillLayout.Height - 1e-6 && Math.Abs(y1) >= PillLayout.Height - 1e-6;
                Assert.True(hidden || Math.Abs(y1 - y0) <= Slide(PillLayout.Height), $"{what}: a group stepped from {y0} to {y1}");
            }
            foreach (var block in blocks)
            {
                var y0 = frames.Offset(block, before).Y;
                var y1 = frames.Offset(block, t).Y;
                var h = heights[block];
                var hidden = Math.Abs(y0) >= h - 1e-6 && Math.Abs(y1) >= h - 1e-6;
                // a line that leaves at one edge and comes in at the other within the frame changes sides out of sight: what
                // shows of that step is its way out to the one edge and in from the other
                var through = y0 * y1 < 0 ? (h - Math.Abs(y0)) + (h - Math.Abs(y1)) : double.PositiveInfinity;
                Assert.True(hidden || Math.Min(Math.Abs(y1 - y0), through) <= Slide(h), $"{what}: \"{block.Text}\" stepped from {y0} to {y1}");
            }
            // a glyph swapped back mid-way turns from the scale it has
            foreach (var part in scaled)
            {
                var s0 = frames.Scale(part, before);
                var s1 = frames.Scale(part, t);
                Assert.True(Math.Abs(s1 - s0) <= 0.1, $"{what}: a scale stepped from {s0} to {s1}");
            }
        }

        // where it ends: the run group and the working line, Stop where it was
        Assert.True(run.IsVisible);
        Assert.False(paused.IsVisible);
        Assert.Equal(Working().Line, pill.LineSlot.Text);
        Assert.Equal(PillGroupKind.Run, pill.ShownGroup);
        Metric("group swap", shown);
    });

    [Fact]
    public void The_card_opens_on_the_body_s_edges_in_every_frame_holds_its_place_and_folds_back_from_where_it_is() => headless.OnUiThread(() =>
    {
        using var source = FrameClock.Source.Override(new ManualFrameSource());
        var animator = new RecordingPillAnimator();
        var clock = new ManualTimeProvider();
        var pill = new ControlPill(PillDriverKind.RenderThread, animator, clock);
        var window = new Window { Width = 900, Height = 600, Content = pill };
        window.Show();
        try
        {
            var frames = new PillFrames(pill, animator);
            frames.At(pill.Now, () =>
            {
                pill.Show(Asking(card: false));
                pill.Place(new PillVector(450, 8), 0.5);
                pill.Arrive();
            });
            frames.Pass(clock, 2);
            pill.Settled();
            var t0 = pill.Now;
            var fold = t0 + 0.12;
            frames.At(t0, () =>
            {
                pill.ShowCard(Body);
                pill.Show(Asking(card: true), t0);
            });
            var open = pill.Motion;
            var height = open.Final.Height;
            Assert.True(height > PillLayout.Height + 20, $"the card's body is {height - PillLayout.Height} DIP");
            Assert.Equal(PillMorph.CardWidth, open.Final.Width, 9);
            frames.At(fold, () => pill.Show(Asking(card: false), fold));
            var closing = pill.Motion;
            Near(open.At(fold).Height, closing.At(fold).Height, 1e-6, "the fold starts at the height it has");
            Near(open.At(fold).Width, closing.At(fold).Width, 1e-6, "the fold starts at the width it has");

            Assert.DoesNotContain(frames.All, c => c.Kind == "Fade" && (c.Target == pill.Card || c.Target == pill.CardClip || c.Target == pill.Content));
            var lastHeight = double.NegativeInfinity;
            var lastRise = double.PositiveInfinity;
            var count = FramesTo(t0, closing.DoneAt());
            for (var i = 0; i <= count; i++)
            {
                var t = At(t0, i);
                var what = "frame " + i;
                var pose = frames.Expected(t);
                var clip = frames.Size(pill.CardClip!, t);
                Near(pose.Width, clip.X, KeyError, what + " clip width");
                Near(Math.Max(0, pose.Height - PillLayout.Height), clip.Y, KeyError, what + " clip height");
                // the clip's edge moves with the body's and the card counter-moves: it holds its place under the anchor
                Near(0, frames.Offset(pill.CardClip!, t).X + frames.Offset(pill.Card, t).X, 1e-6, what + " card place");
                Near(1, frames.Opacity(pill.Card, t), 1e-9, what + " card opacity");
                Near(1, frames.Opacity(pill.Content, t), 1e-9, what + " content opacity");
                if (t < fold)
                {
                    // opening: the height grows to the card's and never past it, and the card rises its last 6 DIP once
                    var rise = frames.Offset(pill.Card, t).Y;
                    Assert.True(pose.Height <= height + 1e-6 && pose.Height >= lastHeight - 1e-6, $"{what}: height {pose.Height} after {lastHeight}");
                    Assert.True(rise >= -1e-9 && rise <= PillMorph.BodyRise + 1e-9 && rise <= lastRise + 1e-9, $"{what}: rise {rise} after {lastRise}");
                    lastHeight = pose.Height;
                    lastRise = rise;
                }
                if (i == 0) continue;
                var before = At(t0, i - 1);
                var was = frames.Size(pill.CardClip!, before);
                var fastest = Math.Max(Math.Abs(frames.MotionAt(t).At(t).Height - frames.MotionAt(before).At(before).Height), Math.Abs(frames.MotionAt(t).At(t).Width - frames.MotionAt(before).At(before).Width));
                Assert.True(Math.Abs(clip.Y - was.Y) <= fastest + (2 * KeyError) && Math.Abs(clip.X - was.X) <= fastest + (2 * KeyError),
                    $"{what}: the clip stepped from {was} to {clip}");
            }
            Near(PillLayout.Height, closing.Final.Height, 1e-9, "folded");
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void With_Reduce_Motion_a_short_travel_eases_in_150_ms_and_a_long_one_jumps_only_while_faded_out() => headless.OnUiThread(() =>
    {
        var (pill, frames, animator, _, t0) = Make(new PillVector(400, 8), 0.5, reduce: true);
        var near = new PillVector(400 + PillCurve.ReducedEaseDistance, 8);
        frames.At(t0, () => pill.TravelTo(near, 0.5, at: t0));
        var previous = 400.0;
        var eased = new List<(double, double, double, double)>();
        for (var i = 0; i <= 60; i++)
        {
            var t = At(t0, i);
            var x = frames.Anchor(t).X;
            eased.Add(((t - t0) * 1000, x, frames.Expected(t).Width, frames.Opacity(pill.StopSlot, t)));
            var what = "ease, frame " + i;
            Assert.True(x >= previous - 1e-9 && x <= near.X + 1e-9, $"{what}: {x} after {previous}");
            Near(1, frames.Opacity(pill.Content, t), 1e-9, what + " opacity");
            if (t - t0 >= PillCurve.ReducedEase) Near(near.X, x, 1e-6, what + " landed");
            previous = x;
        }

        Metric("travel 200 DIP, Reduce Motion", eased);
        var start = t0 + 1;
        pill.Settled(start);
        var far = new PillVector(near.X + 900, 8);
        frames.At(start, () => pill.TravelTo(far, 0.5, at: start));
        Assert.Contains(frames.On(pill.Content), c => c.Kind == "Fade");
        // a frame next to the jump is at most two frames from the fade's middle: that little opacity, no more
        var dim = (2 * Frame / PillCurve.ReducedJump) + 1e-3;
        var jumps = 0;
        var lastX = near.X;
        var lastOpacity = 1.0;
        var faded = new List<(double, double, double, double)>();
        for (var i = 0; i <= 60; i++)
        {
            var t = At(start, i);
            var x = frames.Anchor(t).X;
            var opacity = frames.Opacity(pill.Content, t);
            faded.Add(((t - start) * 1000, x, frames.Expected(t).Width, frames.Opacity(pill.StopSlot, t)));
            var what = "fade through, frame " + i;
            var atAnEnd = Math.Abs(x - near.X) <= 1e-6 || Math.Abs(x - far.X) <= 1e-6;
            Assert.True(atAnEnd || opacity <= dim, $"{what}: drawn between its ends at {x} with opacity {opacity}");
            if (Math.Abs(x - lastX) > 1e-6)
            {
                jumps++;
                Assert.True(Math.Max(opacity, lastOpacity) <= dim, $"{what}: the jump showed at opacity {lastOpacity} then {opacity}");
            }
            Near(frames.Expected(t).Opacity, opacity, 0.01, what + " opacity on its curve");
            if (t - start >= PillCurve.ReducedJump) Near(far.X, x, 1e-6, what + " landed");
            lastX = x;
            lastOpacity = opacity;
        }
        // one jump (two when a frame falls inside the keys' own 1 ms step), each while faded out
        Assert.InRange(jumps, 1, 2);
        Metric("travel 900 DIP, Reduce Motion", faded);
        // and nothing breathes while Reduce Motion is on
        Assert.DoesNotContain(animator.Calls, c => c.Kind == "Breathe");
    });
}
