using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// OD-0071 / ED-246: one soft, weighted scrolling behaviour, modelled on the Mac's measured physics
// (E:/Droppy/work/review-scroll-physics/mac-physics.md); the wheel's glide is ED-NEW-softscroll-2's two critically damped
// stages (the owner, 2026-10-11: "no smooth premium weighted scrolling as I asked earlier"). The physics is checked frame
// by frame on hand-driven time; the behaviour on real ScrollViewers on the manual frame clock (ManualFrameSource) and the
// manual Swift clock.
[Collection("Headless")]
public sealed class SoftScrollTests(HeadlessSession headless)
{
    private const double Stage = ScrollPhysics.GlideStage;

    // The two stages' step response, solved by hand: x(t) = 1 - (1 + t/tau) e^(-t/tau), from rest, with no speed at t = 0.
    private static double Step(double t) => t <= 0 ? 0 : 1 - (1 + t / Stage) * Math.Exp(-t / Stage);

    // The notch response: the two stages toward the notch, aimed RestSpeed's worth (over a stage) past it, resting the
    // moment the view gets there.
    private static double Notch(double distance, double t) => Burst([(0, distance)], t);

    // Notches adding up to one target: the stages are linear, so the view is the sum of each notch's own response from its
    // own time, plus the aim's hair past the sum from the first; until it reaches the sum, where it rests.
    private static double Burst(IReadOnlyList<(double T, double Distance)> notches, double t)
    {
        var sum = notches.Sum(n => n.Distance);
        var y = Math.Sign(sum) * ScrollPhysics.RestSpeed * Stage * Step(t - notches[0].T) + notches.Sum(n => n.Distance * Step(t - n.T));
        return Math.Abs(y) >= Math.Abs(sum) ? sum : y;
    }

    // The speed is highest one stage in: the aim over (stage x e).
    private static double PeakSpeed(double distance) => (distance + ScrollPhysics.RestSpeed * Stage) / (Stage * Math.E);

    // Steps the physics at a display rate; returns (time, position) per frame, the first at `from`.
    private static List<(double T, double Y)> Frames(ScrollPhysics physics, double hz, double from, double until, Action<double>? at = null)
    {
        var frames = new List<(double, double)>();
        var dt = 1 / hz;
        for (var k = 0; from + k * dt <= until + 1e-12; k++)
        {
            var t = from + k * dt;
            at?.Invoke(t);
            physics.Step(t);
            frames.Add((t, physics.Position));
        }
        return frames;
    }

    private static ScrollPhysics Physics(double max = 100000)
    {
        var physics = new ScrollPhysics();
        physics.Reset(0, 0);
        physics.SetBounds(0, max);
        return physics;
    }

    [Fact]
    [Trait("Decision", "ED-246")] // the tuning is the recorded one (the other tests read these constants, so they are pinned here)
    [Trait("Decision", "ED-NEW-softscroll-2")]
    public void The_tuning_is_the_one_ED_246_and_ED_NEW_softscroll_2_record()
    {
        // (2) the wheel (ED-NEW-softscroll-2): two critically damped stages of 0.050 s each; a notch is Windows' lines
        // setting x 33 1/3 points, 100 at the default 3 lines; "one screen at a time" (WHEEL_PAGESCROLL) reads as -1.
        Assert.Equal(0.050, ScrollPhysics.GlideStage);
        Assert.Equal(100.0 / 3, SystemWheel.LinePoints);
        Assert.Equal(3, SystemWheel.DefaultLines);
        Assert.Equal(-1, SystemWheel.PageScroll);
        Assert.Equal(100, new SystemWheel(() => SystemWheel.DefaultLines).Notch(300), 12);
        // The touchpad's stream: 50 points per WHEEL_DELTA (Avalonia's step), each report spread over the interval since
        // the one before (a stream's first over 8 ms).
        Assert.Equal(50, ScrollPhysics.WheelLine);
        Assert.Equal(0.008, ScrollPhysics.Cadence);
        Assert.Equal(0.002, ScrollPhysics.MinSpread);
        Assert.Equal(0.034, ScrollPhysics.MaxSpread);
        Assert.Equal(0.1, ScrollPhysics.StreamGap);
        // (3) the touchpad: a 4 ms lag behind its spread stream, no coast of the port's own.
        Assert.Equal(0.004, ScrollPhysics.FollowLag);
        // A touch coast's decay is UIKit's normal deceleration rate, 0.998 per ms.
        Assert.InRange(ScrollPhysics.MomentumDecay, -0.001 / Math.Log(0.998) - 1e-4, -0.001 / Math.Log(0.998) + 1e-4);
        // (4) ends and rest: brake onto an end from 12 ms out; every motion ends at 2 points a second.
        Assert.Equal(0.012, ScrollPhysics.LandingDecay);
        Assert.Equal(2, ScrollPhysics.RestSpeed);
        Assert.Equal(0.25, ScrollPhysics.RowSettle);
        // (5) keys: .smooth(duration: 0.25).
        Assert.Equal(Animation.Smooth(0.25).Duration, SoftScroll.KeyAnimation.Duration);
        foreach (var t in new[] { 0.01, 0.05, 0.1, 0.2 }) Assert.Equal(Animation.Smooth(0.25).Progress(t), SoftScroll.KeyAnimation.Progress(t), 12);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(240)]
    [Trait("Decision", "ED-NEW-softscroll-2")] // (1) a notch glides along the two-stage curve, frame by frame, at 60 and 240 Hz
    public void A_wheel_notch_glides_along_the_two_stage_curve_from_no_speed_with_no_frame_jumping_further_than_the_curve_allows(int hz)
    {
        var physics = Physics();
        physics.Notch(100, 0, instant: false);
        Assert.Equal(0, physics.Position); // nothing moves before a frame
        Assert.Equal(0, physics.Velocity); // and it starts from no speed
        var frames = Frames(physics, hz, 0, 1.0);
        var peak = PeakSpeed(100);
        for (var i = 1; i < frames.Count; i++)
        {
            var (t, y) = frames[i];
            Assert.Equal(Notch(100, t), y, 9);
            var step = y - frames[i - 1].Y;
            Assert.True(step >= 0, FormattableString.Invariant($"backwards at {t:F4}: {step}"));
            Assert.True(step <= peak / hz + 1e-9, FormattableString.Invariant($"frame at {t:F4} jumped {step:F3} > {peak / hz:F3}"));
        }
        Assert.False(physics.IsMoving);
        Assert.Equal(100, frames[^1].Y);
        var steps = frames.Zip(frames.Skip(1), (a, b) => b.Y - a.Y).ToArray();
        // The first frame shows the motion starting from rest, not the notch landing (4.5 points at 60 Hz, under 5 % of
        // it), and the frames speed up before they slow down.
        Assert.InRange(frames[1].Y, 0.01, 100.1 * Step(1.0 / hz) + 1e-9);
        if (hz == 60) Assert.InRange(frames[1].Y, 4.4, 4.5);
        Assert.True(steps[0] < steps[1] && steps[1] < steps[2], "the glide did not start from rest");
        // The speed peaks one stage (50 ms) in; half way at 83.8 ms, 90 % at 193.9 ms, at rest at 0.462 s.
        var fastest = Array.IndexOf(steps, steps.Max());
        Assert.InRange(frames[fastest].T, Stage - 1.0 / hz - 1e-9, Stage + 1e-9);
        Assert.InRange(frames.First(f => f.Y >= 50).T, 0.08383, 0.08384 + 1.0 / hz);
        Assert.InRange(frames.First(f => f.Y >= 90).T, 0.19392, 0.19393 + 1.0 / hz);
        Assert.InRange(frames.First(f => f.Y >= 99).T, 0.32648, 0.32649 + 1.0 / hz);
        Assert.InRange(frames.First(f => f.Y == 100).T, 0.46172, 0.46173 + 1.0 / hz);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-softscroll-2")] // (1) notches add up to one target, followed through the two stages
    public void A_burst_of_notches_adds_up_to_one_target_its_speed_peaking_a_stage_after_the_burst()
    {
        // A flick: five notches 12.5 ms apart.
        var physics = Physics();
        var notches = Enumerable.Range(0, 5).Select(i => (T: i * 0.0125, Distance: 100.0)).ToArray();
        var frames = Frames(physics, 800, 0, 1.0, t => { if (notches.Any(n => Math.Abs(n.T - t) < 1e-9)) physics.Notch(100, t, false); });
        // Every frame is the sum of the notches' own responses: one target, nothing lost, nothing added, no step anywhere.
        foreach (var (t, y) in frames) Assert.Equal(Burst(notches, t), y, 9);
        Assert.Equal(500, frames[^1].Y);
        Assert.False(physics.IsMoving);
        var speeds = frames.Skip(1).Select((f, i) => (f.T, V: (f.Y - frames[i].Y) * 800)).ToArray();
        var top = speeds.MaxBy(s => s.V);
        // The speed is highest about a stage after the burst's middle (81 ms), not as its last notch lands (50 ms).
        Assert.InRange(top.T, 0.0800, 0.0825);
        // Distance over peak speed: 0.144 s.
        Assert.InRange(500 / top.V, 0.143, 0.146);
        Assert.InRange(frames.First(f => f.Y == 500).T, 0.5779, 0.5779 + 1 / 800.0);
    }

    [Fact]
    [Trait("Decision", "ED-246")] // (1) the same curve whatever the display rate: the exact solution, not a per-frame filter
    public void Sixty_and_two_hundred_forty_hertz_draw_the_same_curve()
    {
        foreach (var precise in new[] { false, true })
        {
            var slow = Physics();
            var fast = Physics();
            void Input(ScrollPhysics p, double t) { if (precise) p.Precise(4, t, false); else p.Notch(100, t, false); }
            var times = precise ? Enumerable.Range(0, 20).Select(i => i * 0.0083).ToArray() : [0.0, 0.05];
            var at60 = new List<(double T, double Y)>();
            var at240 = new List<(double T, double Y)>();
            // Reports at their own times, frames on their own grids.
            foreach (var (physics, hz, trace) in new[] { (slow, 60.0, at60), (fast, 240.0, at240) })
            {
                var events = new Queue<double>(times);
                for (var k = 1; k / hz <= 0.6 + 1e-12; k++)
                {
                    while (events.Count > 0 && events.Peek() <= k / hz) Input(physics, events.Dequeue());
                    physics.Step(k / hz);
                    trace.Add((k / hz, physics.Position));
                }
            }
            foreach (var (t, y) in at60)
                Assert.Equal(y, at240.Single(f => Math.Abs(f.T - t) < 1e-9).Y, 9);
        }
    }

    [Fact]
    [Trait("Decision", "ED-246")] // (1) successive notches extend the glide; position and speed stay continuous
    public void Notches_in_quick_succession_extend_the_glide_without_a_jump_in_position_or_speed()
    {
        var physics = Physics();
        var notches = new[] { 0.0, 8 / 240.0, 16 / 240.0, 24 / 240.0 }; // on the 240 Hz frame grid, about 30 ms apart
        foreach (var t0 in notches.Skip(1))
        {
            // Just before and just after a notch lands mid-glide, the view is where it was and moving as it was.
            var probe = Physics();
            foreach (var earlier in notches.Where(n => n < t0)) { probe.Step(earlier); probe.Notch(100, earlier, false); }
            probe.Step(t0 - 1e-5);
            var before = probe.Position;
            probe.Step(t0);
            var v = probe.Velocity;
            Assert.True(v > 0);
            probe.Notch(100, t0, false);
            Assert.Equal(v, probe.Velocity, 6);
            probe.Step(t0 + 1e-5);
            Assert.Equal(before + v * 2e-5, probe.Position, 3);
        }
        var frames = Frames(physics, 240, 0, 1.0, t => { if (notches.Any(n => Math.Abs(n - t) < 1e-9)) physics.Notch(100, t, false); });
        for (var i = 1; i < frames.Count; i++) Assert.True(frames[i].Y >= frames[i - 1].Y);
        // While the notches keep coming the view never stops between them.
        for (var i = 1; i + 1 < frames.Count && frames[i].T < 0.12; i++) Assert.True(frames[i + 1].Y > frames[i].Y);
        foreach (var (t, y) in frames) Assert.Equal(Burst(notches.Select(n => (n, 100.0)).ToArray(), t), y, 9);
        Assert.Equal(400, frames[^1].Y);
    }

    [Fact]
    [Trait("Decision", "ED-246")] // (1) a notch the other way starts from where the view is
    [Trait("Decision", "ED-NEW-softscroll-2")] // from rest: the two stages' own start, back the other way
    public void A_notch_the_other_way_turns_the_glide_round_from_where_the_view_is()
    {
        var physics = Physics();
        physics.Reset(500, 0);
        physics.Notch(100, 0, false);
        physics.Step(0.04);
        var turnedAt = physics.Position;
        Assert.True(physics.Velocity > 100);
        physics.Notch(-100, 0.04, false);
        Assert.Equal(0, physics.Velocity);
        var frames = Frames(physics, 240, 0.04, 1.0);
        Assert.True(frames.Max(f => f.Y) <= turnedAt + 1e-9, "it ran on past the turn");
        foreach (var (t, y) in frames) Assert.Equal(turnedAt + Notch(-100, t - 0.04), y, 9);
        Assert.Equal(turnedAt - 100, frames[^1].Y, 6);
    }

    [Fact]
    [Trait("Decision", "ED-246")] // (1) a wheel report's source: notches, a fine wheel's quanta, a touchpad's arbitrary fractions
    public void Wheel_reports_are_told_apart_by_their_quanta()
    {
        var classifier = new WheelClassifier();
        Assert.Equal(WheelKind.Notch, classifier.Classify(-1, 0));
        Assert.True(classifier.IsFresh);
        Assert.Equal(WheelKind.Notch, classifier.Classify(-2, 0.01));
        Assert.False(classifier.IsFresh);
        // A high-resolution wheel (Logitech: 15 units, an eighth of WHEEL_DELTA), joining the same stream.
        Assert.Equal(WheelKind.FineWheel, classifier.Classify(-15 / 120.0, 0.02));
        Assert.Equal(WheelKind.FineWheel, classifier.Classify(-30 / 120.0, 0.03));
        // A touchpad: values that share no wheel quantum; the stream stays a touchpad's, even through a value that would.
        var touchpad = new WheelClassifier();
        Assert.Equal(WheelKind.FineWheel, touchpad.Classify(24 / 120.0, 1)); // a lone 24 could be either
        Assert.Equal(WheelKind.Touchpad, touchpad.Classify(17 / 120.0, 1.008));
        Assert.Equal(WheelKind.Touchpad, touchpad.Classify(120 / 120.0, 1.016));
        Assert.Equal(WheelKind.Touchpad, touchpad.Classify(9.6 / 120.0, 1.024));
        // A new stream after a pause starts afresh.
        Assert.Equal(WheelKind.Notch, touchpad.Classify(1, 1.024 + ScrollPhysics.StreamGap + 0.001));
        Assert.Equal(WheelKind.Touchpad, new WheelClassifier().Classify(7 / 120.0, 0));
    }

    [Fact]
    [Trait("Decision", "ED-246")] // (1) a stream that turns out to be a touchpad's keeps the view's position and speed
    public void A_stream_that_turns_out_to_be_a_touchpad_switches_lag_without_a_jump()
    {
        var physics = Physics();
        physics.Notch(10, 0, false); // classified a fine wheel at first
        physics.Step(0.006);
        var (before, v) = (physics.Position, physics.Velocity);
        physics.Precise(7, 0.006, false);
        Assert.Equal(v, physics.Velocity, 6);
        physics.Step(0.006 + 1e-5);
        Assert.Equal(before + v * 1e-5, physics.Position, 4);
        Frames(physics, 240, 0.006, 1.0);
        Assert.Equal(17, physics.Position, 9); // nothing lost, nothing added
    }

    [Fact]
    [Trait("Decision", "ED-NEW-softscroll-2")] // a stream's reports weighed as a wheel's and then shown a touchpad's: the excess is taken back
    public void An_amended_glide_goes_where_the_amended_sum_says_without_a_jump()
    {
        var physics = Physics();
        physics.Notch(20, 0, false); // 24/120 taken for a fine wheel: 20 points at 3 lines, 10 at a touchpad's weight
        physics.Step(0.008);
        var (before, v) = (physics.Position, physics.Velocity);
        physics.Amend(-10, 0.008);
        Assert.Equal(before, physics.Position);
        Assert.Equal(v, physics.Velocity, 9);
        physics.Precise(17 * 50 / 120.0, 0.008, false);
        Assert.Equal(v, physics.Velocity, 6);
        var frames = Frames(physics, 240, 0.008, 1.0);
        Assert.All(frames.Zip(frames.Skip(1)), pair => Assert.True(pair.Second.Y >= pair.First.Y - 1e-9, "backwards"));
        Assert.Equal(41 * 50 / 120.0, frames[^1].Y, 9); // the stream at a touchpad's weight, nothing else
        // Amending nothing under way changes nothing.
        physics.Amend(-5, 1.1);
        Assert.Equal(41 * 50 / 120.0, physics.Position, 9);
        Assert.False(physics.IsMoving);
    }

    [Fact]
    [Trait("Decision", "ED-246")] // (2) a touchpad follows its stream and stops with it: no coast of the port's own
    public void A_touchpad_stroke_follows_the_fingers_and_stops_with_its_stream()
    {
        var physics = Physics();
        var phases = new List<ScrollPhase>();
        var (frames, lastEvent) = Stroke(physics, 4, 13, 1.0, phases); // 500 points a second for 96 ms
        // Following: behind by the report interval and the lag, never ahead.
        foreach (var (t, y) in frames.Where(f => f.T <= lastEvent))
            Assert.InRange(4 * (Math.Floor(t / 0.008 + 1e-9) + 1) - y, 0, 4 + 500 * (ScrollPhysics.FollowLag + 1 / 240.0) + 0.01);
        Assert.False(physics.IsMoving);
        Assert.Equal(52, frames[^1].Y, 9);
        Assert.True(frames.First(f => f.Y == 52).T < lastEvent + 0.008 + 0.05, "it ran on after the stream");
        Assert.Equal(ScrollPhase.Interacting, phases[frames.FindIndex(f => f.T >= lastEvent)]);
        Assert.Equal(ScrollPhase.Idle, phases[^1]);
    }

    // A touchpad stroke: precise events of `step` points every 8 ms (a 125 Hz report rate), frames at 240 Hz.
    private static (List<(double T, double Y)> Frames, double LastEvent) Stroke(ScrollPhysics physics, double step, int events, double until, List<ScrollPhase>? phases = null)
    {
        var lastEvent = (events - 1) * 0.008;
        var nextEvent = 0;
        var frames = new List<(double, double)>();
        for (var k = 0; k * (1 / 240.0) <= until; k++)
        {
            var t = k / 240.0;
            while (nextEvent < events && nextEvent * 0.008 <= t + 1e-12)
            {
                physics.Precise(step, nextEvent * 0.008, instant: false);
                nextEvent++;
            }
            physics.Step(t);
            phases?.Add(physics.Phase);
            frames.Add((t, physics.Position));
        }
        return (frames, lastEvent);
    }

    [Fact]
    [Trait("Decision", "ED-246")] // (2) a single precise event, or fingers that stopped before lifting, leave no coast
    public void A_stroke_that_stopped_before_lifting_does_not_coast()
    {
        var physics = Physics();
        physics.Precise(4, 0, false);
        Frames(physics, 240, 0, 0.3);
        Assert.False(physics.IsMoving);
        Assert.Equal(4, physics.Position, 9);
    }

    // A touch drag of `step` points every 8 ms, then a release at `release`.
    private static List<(double T, double Y)> Flick(ScrollPhysics physics, double step, int moves, double until)
    {
        for (var i = 0; i < moves; i++) physics.Touch(physics.Position + step, i * 0.008);
        physics.Release((moves - 1) * 0.008);
        return Frames(physics, 240, (moves - 1) * 0.008, until);
    }

    [Fact]
    [Trait("Decision", "ED-246")] // (2) a touch flick coasts with the momentum decay and ends exactly v tau further on
    public void A_touch_flick_coasts_with_the_momentum_decay_and_runs_out_exactly()
    {
        var physics = Physics();
        var frames = Flick(physics, 4, 13, 6.0); // 500 points a second
        Assert.False(physics.IsMoving);
        // x(t) = x0 + v tau (1 - e^(-t/tau)): v tau further on, exactly (not cut off when it is slow).
        Assert.Equal(52 + 500 * ScrollPhysics.MomentumDecay, frames[^1].Y, 6);
        double SpeedAt(double t)
        {
            var i = frames.FindIndex(f => f.T >= t);
            return (frames[i + 1].Y - frames[i].Y) * 240;
        }
        var ratio = SpeedAt(0.096 + 0.7) / SpeedAt(0.096 + 0.2);
        Assert.InRange(ratio, Math.Exp(-0.5 / ScrollPhysics.MomentumDecay) * 0.97, Math.Exp(-0.5 / ScrollPhysics.MomentumDecay) * 1.03);
        Assert.InRange(SpeedAt(0.1), 480, 520); // it leaves at the release speed
    }

    [Theory]
    [InlineData(60)]
    [InlineData(240)]
    [Trait("Decision", "ED-246")] // (3) a coast that would cross the end keeps its speed, then brakes onto it within about 40 ms
    public void A_coast_towards_the_end_arrives_from_speed_and_brakes_onto_it(int hz)
    {
        var physics = Physics(max: 300);
        for (var i = 0; i < 6; i++) physics.Touch(physics.Position + 16, i * 0.008); // 2000 points a second
        physics.Release(0.04);
        var frames = Frames(physics, hz, 0.04, 1.0);
        Assert.All(frames, f => Assert.InRange(f.Y, 0, 300));
        Assert.Equal(300, frames[^1].Y);
        var speeds = frames.Zip(frames.Skip(1), (a, b) => (T: b.T, Y: b.Y, V: (b.Y - a.Y) * hz)).ToArray();
        // 2000 points a second would carry it 1000 points: until about 24 points out it is still going near that speed.
        var out24 = speeds.First(s => s.Y >= 300 - 24);
        Assert.True(out24.V >= 1500, FormattableString.Invariant($"{hz} Hz: {out24.V:F0} points a second 24 points out"));
        // Then it brakes onto the end, never passing it, and rests within a tenth of a second.
        var restAt = frames.First(f => f.Y == 300).T;
        Assert.True(restAt - out24.T < 0.1, FormattableString.Invariant($"{hz} Hz: {restAt - out24.T:F3} s over the last 24 points"));
    }

    [Theory]
    [InlineData("smooth0.35")]
    [InlineData("easeOut0.25")]
    [InlineData("smooth0.25")]
    [Trait("Decision", "ED-246")] // (4) programmatic scrolls follow the source's curves (ThreadTimeline.swift:657, :676; DiffInspector.swift:104)
    public void A_programmatic_scroll_follows_its_SwiftUI_curve_frame_by_frame(string name)
    {
        var curve = name switch { "smooth0.35" => Animation.Smooth(0.35), "easeOut0.25" => Animation.EaseOut(0.25), _ => Animation.Smooth(0.25) };
        var physics = Physics();
        physics.AnimateTo(1000, curve, 0);
        var frames = Frames(physics, 240, 0, curve.Duration * 2);
        // The curve itself (a spring run on until it is within AnimationRest, so it arrives without a snap).
        foreach (var (t, y) in frames.Where(f => f.T < curve.Duration)) Assert.InRange(y - 1000 * curve.Progress(t), -1e-6, ScrollPhysics.AnimationRest + 1e-9);
        Assert.Equal(1000, frames[^1].Y);
        Assert.False(physics.IsMoving);
        var steps = frames.Zip(frames.Skip(1), (a, b) => b.Y - a.Y).Where(s => s > 0).ToArray();
        Assert.True(steps[^1] <= steps[^2] + 1e-9, "a snap at the end: " + steps[^1] + " after " + steps[^2]);
    }

    [Fact]
    [Trait("Decision", "ED-246")] // (4) a scroll already moving carries its speed into an animation instead of stopping dead
    public void An_animation_started_while_the_view_moves_carries_its_speed_in()
    {
        var physics = Physics();
        for (var i = 0; i < 4; i++) physics.Notch(100, 0, false);
        var frames = Frames(physics, 240, 0, 0.05);
        var speed = physics.Velocity;
        physics.AnimateTo(2000, Animation.Smooth(0.35), 0.05);
        Assert.InRange(physics.Velocity - speed, -1, 1); // the speed it had, carried in (the spring itself starts from rest)
        // To a destination close by, where the curve's own first frame is a fraction of a point, the speed still carries.
        var near = Physics();
        for (var i = 0; i < 4; i++) near.Notch(100, 0, false);
        var nearFrames = Frames(near, 240, 0, 0.05);
        var nearBefore = nearFrames[^1].Y - nearFrames[^2].Y;
        near.AnimateTo(nearFrames[^1].Y + 100, Animation.Smooth(0.35), 0.05);
        near.Step(0.05 + 1 / 240.0);
        Assert.True(100 * Animation.Smooth(0.35).Progress(1 / 240.0) < 0.2 * nearBefore, "the curve alone would move it");
        Assert.True(near.Position - nearFrames[^1].Y >= 0.7 * nearBefore, FormattableString.Invariant($"stopped dead: {near.Position - nearFrames[^1].Y} after {nearBefore}"));
        // And the carried speed is gone by the end: the animation arrives exactly, with no speed left over.
        var rest = Frames(near, 240, 0.05 + 1 / 240.0, 1.5);
        Assert.Equal(nearFrames[^1].Y + 100, rest[^1].Y, 9);
    }

    [Fact]
    [Trait("Decision", "ED-246")] // (9) the terminal's rows: a stream that stops between rows runs on, forward, onto the next
    public void A_stream_that_stops_between_rows_runs_on_forward_onto_the_next_row()
    {
        foreach (var hz in new[] { 60.0, 240.0 })
        {
            var physics = Physics(max: 1000);
            physics.Quantum = 17;
            var (frames, _) = (new List<(double T, double Y)>(), 0.0);
            var events = new Queue<double>(Enumerable.Range(0, 10).Select(i => i * 0.008));
            for (var k = 1; k / hz <= 1.0 + 1e-12; k++)
            {
                while (events.Count > 0 && events.Peek() <= k / hz) physics.Precise(3, events.Dequeue(), false); // 30 points: 1.76 rows
                physics.Step(k / hz);
                frames.Add((k / hz, physics.Position));
            }
            Assert.Equal(34, frames[^1].Y, 9); // the next whole row ahead, not the nearest (17)
            var steps = frames.Zip(frames.Skip(1), (a, b) => b.Y - a.Y).ToArray();
            Assert.All(steps, s => Assert.True(s >= -1e-9, "backwards"));
            // Never a stop and then a creep: once it has stopped it stays stopped.
            var stopped = Array.FindIndex(steps, s => s == 0);
            Assert.All(steps.Skip(stopped), s => Assert.Equal(0, s));
            Assert.True(frames[stopped].Y == 34, FormattableString.Invariant($"{hz} Hz: stopped at {frames[stopped].Y:F3} before the row"));
            // A wheel's glide of 100 points rests on the next whole row ahead too (102, six rows), from speed, never backwards.
            var glide = Physics(max: 1000);
            glide.Quantum = 17;
            glide.Notch(100, 0, false);
            var rows = new List<double> { 0 };
            for (var k = 1; k / hz <= 1.0 + 1e-12; k++)
            {
                glide.Step(k / hz);
                rows.Add(glide.Position);
            }
            Assert.Equal(102, rows[^1], 9);
            var moves = rows.Zip(rows.Skip(1), (a, b) => b - a).ToArray();
            Assert.All(moves, s => Assert.True(s >= -1e-9, "backwards"));
            var still = Array.FindIndex(moves, s => s == 0);
            Assert.All(moves.Skip(still), s => Assert.Equal(0, s));
            Assert.Equal(102, rows[still], 9);
        }
    }

    [Fact]
    [Trait("Decision", "ED-246")] // reduce motion: every input steps at once, no glide and no coast
    public void With_reduce_motion_every_input_steps_at_once()
    {
        var physics = Physics();
        physics.Notch(50, 0, instant: true);
        Assert.False(physics.IsMoving);
        Assert.Equal(50, physics.Position);
        physics.Precise(4, 0.008, instant: true);
        physics.Precise(4, 0.016, instant: true);
        Assert.False(physics.IsMoving);
        Assert.Equal(58, physics.Position);
    }

    // ---- The behaviour on real ScrollViewers ----

    [Fact]
    [Trait("Decision", "ED-246")] // (1) on a real scroll view: the notch waits for the frame clock and glides along the curve
    public void A_wheel_notch_on_a_scroll_view_glides_on_the_frame_clock() => headless.OnUiThread(() =>
    {
        using var rig = new SoftScrollRig();
        var start = rig.Seconds;
        rig.Wheel(new Vector(0, -1));
        Assert.Equal(0, rig.Scroll.Offset.Y); // the presenter's instant 50-point jump did not happen
        Assert.Equal(1, rig.Frames.Pending);
        var trace = rig.Trace(1.0);
        foreach (var (t, y) in trace) Assert.Equal(Notch(100, t - start), y, 6); // 100 points: 3 lines (the test default) x 33 1/3
        Assert.Equal(100, trace[^1].Y);
        Assert.False(SoftScroll.For(rig.Scroll).IsMoving);
    });

    [Fact]
    [Trait("Decision", "ED-246")] // (2) a touchpad's fractional deltas follow on the real scroll view, then stop with the stream
    public void Touchpad_deltas_on_a_scroll_view_follow_and_stop_with_the_stream() => headless.OnUiThread(() =>
    {
        using var rig = new SoftScrollRig();
        var phases = new List<ScrollPhase>();
        rig.Scroll.AddHandler(SoftScroll.PhaseChangedEvent, (_, e) => phases.Add(e.Phase));
        for (var i = 0; i < 12; i++)
        {
            rig.Wheel(new Vector(0, -0.08)); // 4 points
            rig.Frame(250); // 4 ms
            rig.Frame(250);
        }
        var followed = rig.Scroll.Offset.Y;
        Assert.InRange(followed, 48 - 4 - 500 * (ScrollPhysics.FollowLag + 1 / 250.0), 48);
        rig.Run(1);
        Assert.Equal(48, rig.Scroll.Offset.Y, 9);
        Assert.Equal([ScrollPhase.Interacting, ScrollPhase.Idle], phases);
    });

    [Fact]
    [Trait("Decision", "ED-246")] // (2) a touch drag is the presenter's; its release coasts on with the momentum decay
    public void A_touch_flick_coasts_on_after_release() => headless.OnUiThread(() =>
    {
        using var rig = new SoftScrollRig();
        var presenter = rig.Scroll.GetVisualDescendants().OfType<ScrollContentPresenter>().Single();
        var phases = new List<ScrollPhase>();
        rig.Scroll.AddHandler(SoftScroll.PhaseChangedEvent, (_, e) => phases.Add(e.Phase));
        for (var i = 0; i < 6; i++)
        {
            presenter.RaiseEvent(new ScrollGestureEventArgs(7, new Vector(0, 10)));
            rig.Frame(125); // 8 ms: 1250 points a second
        }
        var released = rig.Scroll.Offset.Y;
        Assert.Equal(60, released, 3);
        presenter.RaiseEvent(new ScrollGestureEndedEventArgs(7));
        var coast = rig.Run(0.3);
        Assert.True(coast[0] > released, "it stopped dead on release");
        Assert.True(coast[^1] > released + 150, "the flick did not coast: " + coast[^1]);
        rig.Run(6);
        Assert.Equal([ScrollPhase.Interacting, ScrollPhase.Decelerating, ScrollPhase.Idle], phases);
    });

    [Fact]
    [Trait("Decision", "ED-246")] // (4) Page Down / End / Home animate with .smooth(0.25); presses add up
    public void Page_keys_animate_with_the_smooth_curve_and_presses_add_up() => headless.OnUiThread(() =>
    {
        using var rig = new SoftScrollRig();
        Assert.True(rig.Button.Focus());
        var start = rig.Seconds;
        rig.Press(Key.PageDown);
        Assert.Equal(0, rig.Scroll.Offset.Y);
        var page = rig.Scroll.Viewport.Height;
        var trace = rig.Trace(0.6);
        var curve = SoftScroll.KeyAnimation;
        foreach (var (t, y) in trace.Where(f => f.T - start < curve.Duration))
            Assert.InRange(y - page * curve.Progress(t - start), -1e-6, ScrollPhysics.AnimationRest + 1e-9);
        Assert.Equal(page, trace[^1].Y);
        rig.Press(Key.PageDown);
        rig.Frame();
        rig.Press(Key.PageDown);
        rig.Run(0.8);
        Assert.Equal(3 * page, rig.Scroll.Offset.Y, 6);
        rig.Press(Key.End);
        rig.Run(0.8);
        Assert.Equal(rig.Scroll.Extent.Height - rig.Scroll.Viewport.Height, rig.Scroll.Offset.Y, 6);
        rig.Press(Key.Home);
        Assert.True(rig.Scroll.Offset.Y > 0, "Home jumped");
        rig.Run(0.8);
        Assert.Equal(0, rig.Scroll.Offset.Y);
    });

    // A text field as the app builds them (PlainControls.TextField: a TextPresenter), and a multi-line editor with its
    // own scroll view (SettingsView's commit-instructions editor).
    private static TextBox Field(string text) => new()
    {
        Text = text,
        Height = 30,
        Template = new FuncControlTemplate<TextBox>((owner, scope) => new TextPresenter
        {
            Name = "PART_TextPresenter",
            [!TextPresenter.TextProperty] = owner[!TextBox.TextProperty],
            [!TextPresenter.CaretIndexProperty] = owner[!TextBox.CaretIndexProperty],
        }.RegisterInNameScope(scope)),
    };

    private static TextBox Editor(string text) => new()
    {
        Text = text,
        AcceptsReturn = true,
        Height = 60,
        Template = new FuncControlTemplate<TextBox>((owner, scope) => new ScrollViewer
        {
            Name = "PART_ScrollViewer",
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new TextPresenter
            {
                Name = "PART_TextPresenter",
                [!TextPresenter.TextProperty] = owner[!TextBox.TextProperty],
                [!TextPresenter.CaretIndexProperty] = owner[!TextBox.CaretIndexProperty],
            }.RegisterInNameScope(scope),
        }.RegisterInNameScope(scope)),
    };

    [Fact]
    [Trait("Decision", "ED-246")] // (4) a text field keeps its keys (Avalonia's TextBox takes Home, End and the page keys); an editor's page keys page its own text, softly
    public void A_text_field_keeps_its_own_keys_and_an_editor_pages_its_own_text_softly() => headless.OnUiThread(() =>
    {
        var box = Field("text");
        var lines = Editor(string.Join("\n", Enumerable.Range(0, 60).Select(i => "line " + i)));
        using var rig = new SoftScrollRig(content: new StackPanel { Children = { box, lines, new Border { Height = 2000 } } });
        var reached = new List<Key>();
        box.AddHandler(InputElement.KeyDownEvent, (_, e) => reached.Add(e.Key), RoutingStrategies.Tunnel);
        Assert.True(box.Focus());
        foreach (var key in new[] { Key.End, Key.Home, Key.PageDown, Key.PageUp })
        {
            rig.Press(key);
            rig.Run(0.5);
            Assert.Equal(0, rig.Scroll.Offset.Y); // the field's, not the page's
        }
        Assert.Equal([Key.End, Key.Home, Key.PageDown, Key.PageUp], reached);
        // The editor: its caret keys stay its own, and Page Down pages its text (TextBox -> ScrollViewer.PageDown) along
        // the keyboard's curve instead of jumping; the page around it stays.
        var inner = lines.GetVisualDescendants().OfType<ScrollViewer>().Single();
        Assert.True(inner.Extent.Height > inner.Viewport.Height);
        Assert.True(lines.Focus());
        rig.Press(Key.PageDown);
        Assert.Equal(0, inner.Offset.Y);
        rig.Run(0.8);
        Assert.Equal(inner.Viewport.Height, inner.Offset.Y, 6);
        Assert.Equal(0, rig.Scroll.Offset.Y);
    });

    [Fact]
    [Trait("Decision", "ED-246")] // (4) a programmatic scroll animates, and the reader's wheel takes over from it
    public void A_programmatic_scroll_animates_and_the_wheel_takes_over_from_it() => headless.OnUiThread(() =>
    {
        using var rig = new SoftScrollRig();
        var phases = new List<ScrollPhase>();
        rig.Scroll.AddHandler(SoftScroll.PhaseChangedEvent, (_, e) => phases.Add(e.Phase));
        var start = rig.Seconds;
        SoftScroll.ScrollTo(rig.Scroll, new Vector(0, 1000), Animation.Smooth(0.35));
        var trace = rig.Trace(0.05);
        foreach (var (t, y) in trace) Assert.InRange(y - 1000 * Animation.Smooth(0.35).Progress(t - start), -1e-6, ScrollPhysics.AnimationRest + 1e-9);
        var at = trace[^1].Y;
        rig.Wheel(new Vector(0, 1)); // a notch back up: from where the view is
        var after = rig.Run(1);
        Assert.Equal(at - 100, rig.Scroll.Offset.Y, 6);
        Assert.True(after.Max() <= at + 1e-9, "it ran on with the animation: " + after.Max());
        Assert.Equal([ScrollPhase.Animating, ScrollPhase.Idle], phases);
    });

    [Fact]
    [Trait("Decision", "ED-246")] // an instant programmatic move (content anchoring, a snap) carries a reader's glide along
    public void An_instant_programmatic_move_carries_a_running_glide_along() => headless.OnUiThread(() =>
    {
        using var rig = new SoftScrollRig();
        for (var i = 0; i < 5; i++) rig.Wheel(new Vector(0, -1));
        rig.Run(0.02);
        var moved = rig.Scroll.Offset.Y;
        Assert.InRange(moved, 1, 499);
        SoftScroll.ScrollTo(rig.Scroll, new Vector(0, 1000), null);
        Assert.Equal(1000, rig.Scroll.Offset.Y);
        rig.Run(1);
        Assert.Equal(1000 + 500 - moved, rig.Scroll.Offset.Y, 3);
    });

    [Fact]
    [Trait("Decision", "ED-246")] // reduce motion: instant steps, no frames asked for
    public void With_reduce_motion_the_wheel_and_keys_step_at_once() => headless.OnUiThread(() =>
    {
        using var rig = new SoftScrollRig(viewer => EnvironmentValues.SetAccessibilityReduceMotion(viewer, true));
        Assert.True(rig.Button.Focus()); // first: focus brings the button into view
        rig.Wheel(new Vector(0, -1));
        Assert.Equal(100, rig.Scroll.Offset.Y);
        rig.Press(Key.PageDown);
        Assert.Equal(100 + rig.Scroll.Viewport.Height, rig.Scroll.Offset.Y);
        rig.Wheel(new Vector(0, -0.08));
        Assert.Equal(104 + rig.Scroll.Viewport.Height, rig.Scroll.Offset.Y, 6);
        SoftScroll.ScrollTo(rig.Scroll, new Vector(0, 10), Animation.Smooth(0.35));
        Assert.Equal(10, rig.Scroll.Offset.Y);
        Assert.False(SoftScroll.For(rig.Scroll).IsMoving);
    });

    [Fact]
    [Trait("Decision", "ED-246")] // the innermost view that can move along the input's axis takes it; Shift turns the wheel sideways
    public void The_innermost_scroll_view_that_can_move_that_way_takes_the_wheel() => headless.OnUiThread(() =>
    {
        var strip = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new Border { Width = 2000, Height = 40 },
            Height = 40,
            VerticalAlignment = VerticalAlignment.Top,
        };
        SoftScroll.SetIsEnabled(strip, true);
        using var rig = new SoftScrollRig(content: new StackPanel { Children = { strip, new Border { Height = 2000 } } });
        rig.Wheel(new Vector(0, -1), new Point(150, 20));
        rig.Run(0.8);
        Assert.Equal(100, rig.Scroll.Offset.Y);
        Assert.Equal(0, strip.Offset.X);
        rig.Scroll.Offset = default;
        Dispatcher.UIThread.RunJobs();
        rig.Window.UpdateLayout();
        rig.Wheel(new Vector(0, -1), new Point(150, 20), RawInputModifiers.Shift);
        rig.Run(0.8);
        Assert.Equal(100, strip.Offset.X);
        Assert.Equal(0, rig.Scroll.Offset.Y);
    });

    [Fact]
    [Trait("Decision", "ED-246")] // the behaviour is not the template's: a scroll view with a template of its own glides too
    public void A_scroll_view_with_its_own_template_glides_too() => headless.OnUiThread(() =>
    {
        var own = new Avalonia.Controls.Templates.FuncControlTemplate<ScrollViewer>((viewer, scope) =>
            new ScrollContentPresenter
            {
                Name = "PART_ContentPresenter",
                Background = Avalonia.Media.Brushes.Transparent,
                [!ContentPresenter.ContentProperty] = viewer[!ContentControl.ContentProperty],
            }.RegisterInNameScope(scope));
        using var rig = new SoftScrollRig(viewer => viewer.Template = own);
        rig.Wheel(new Vector(0, -1));
        Assert.Equal(0, rig.Scroll.Offset.Y);
        var offsets = rig.Run(1.0);
        Assert.Equal(Notch(100, 1 / 240.0), offsets[0], 3);
        Assert.Equal(100, offsets[^1]);
    });

    // ---- Traces for the review plots (E:/Droppy/work/review-scroll-physics/port-vs-mac-*.png) ----

    // Writes the port's per-frame curves as JSON when MARROW_SCROLL_TRACES names a file; otherwise it only checks that
    // every scenario comes to rest. The plots are drawn from this file, not from a copy of the physics.
    [Fact]
    [Trait("Decision", "ED-246")]
    public void Every_traced_scenario_comes_to_rest_and_the_traces_are_written_when_asked() => headless.OnUiThread(() =>
    {
        var traces = new Dictionary<string, object>();
        void Add(string name, List<(double T, double Y)> frames, object? meta = null)
        {
            Assert.True(frames.Count > 1);
            traces[name] = new { t = frames.Select(f => f.T).ToArray(), y = frames.Select(f => f.Y).ToArray(), meta };
        }
        // Reports at their own times, frames on their own grid (as on a display).
        List<(double T, double Y)> Play(ScrollPhysics physics, double hz, double until, IEnumerable<(double T, Action<double> Input)> events)
        {
            var queue = new Queue<(double T, Action<double> Input)>(events.OrderBy(e => e.T));
            var frames = new List<(double, double)> { (0, physics.Position) };
            for (var k = 1; k / hz <= until + 1e-12; k++)
            {
                while (queue.Count > 0 && queue.Peek().T <= k / hz + 1e-12) { var e = queue.Dequeue(); e.Input(e.T); }
                physics.Step(k / hz);
                frames.Add((k / hz, physics.Position));
            }
            Assert.False(physics.IsMoving);
            return frames;
        }
        foreach (var hz in new[] { 60, 240 })
        {
            var single = Physics();
            Add("notch" + hz, Play(single, hz, 1.0, [(0.0, t => single.Notch(100, t, false))]));
        }
        foreach (var n in new[] { 2, 3, 4, 5, 6, 8, 10, 12, 13, 20, 40, 80, 160, 300 })
        {
            var burst = Physics();
            Add("burst" + n, Play(burst, 240, 1.5, Enumerable.Range(0, n).Select(k => (n > 1 ? k * 0.05 / (n - 1) : 0, (Action<double>)(t => burst.Notch(100, t, false))))), new { notches = n, spread = 0.05 });
        }
        foreach (var hz in new[] { 60, 240 })
        {
            var end = Physics(max: 276);
            Add("end276@" + hz, Play(end, hz, 1.0, Enumerable.Range(0, 8).Select(k => (k * 0.0125, (Action<double>)(t => end.Notch(100, t, false))))), new { max = 276, notches = 8 });
        }
        var fine = Physics();
        var fineTimes = new[] { 0, 0.011, 0.019, 0.031, 0.040, 0.054, 0.063, 0.079 };
        Add("fineWheel", Play(fine, 240, 1.2, fineTimes.Select(t0 => (t0, (Action<double>)(t => fine.Notch(12.5, t, false))))), new { reports = fineTimes, points = 12.5 });
        foreach (var hz in new[] { 60, 240 })
        {
            var stroke = Physics();
            Add("touchStart@" + hz, Play(stroke, hz, 0.4, Enumerable.Range(0, 20).Select(i => (i * 0.008, (Action<double>)(t => stroke.Precise(4, t, false))))), new { step = 4, cadence = 0.008, events = 20 });
        }
        var hold = Physics();
        Add("touchHold", Play(hold, 240, 0.6, new[] { 7, 11, 13, 13, 14, 13, 14, 13, 13, 14, 13, 14, 13 }.Select((u, i) => (i * 0.008, (Action<double>)(t => hold.Precise(u * 50 / 120.0, t, false))))));
        var inertia = Physics();
        var tail = Enumerable.Repeat(25.0, 8).ToList();
        for (var u = 25.0 * 0.93; u >= 1; u *= 0.93) tail.Add(Math.Round(u));
        Add("touchInertia", Play(inertia, 240, 2.0, tail.Select((u, i) => (i * 0.008, (Action<double>)(t => inertia.Precise(u * 50 / 120.0, t, false))))), new { units = tail });
        foreach (var (name, step, max) in new[] { ("flick500", 4.0, 100000.0), ("flick2000", 16.0, 100000.0), ("flickEnd", 16.0, 300.0) })
        {
            var flick = Physics(max);
            Add(name, Flick(flick, step, 13, 6.0), new { step, moves = 13, release = 12 * 0.008, max });
            Assert.False(flick.IsMoving);
        }
        foreach (var (name, curve) in new[] { ("smooth0.35", Animation.Smooth(0.35)), ("easeOut0.25", Animation.EaseOut(0.25)), ("smooth0.25", Animation.Smooth(0.25)) })
            foreach (var hz in new[] { 60, 240 })
            {
                var animated = Physics();
                animated.AnimateTo(1000, curve, 0);
                Add(name + "@" + hz, Play(animated, hz, 0.8, []), new { distance = 1000 });
            }
        foreach (var hz in new[] { 60, 240 })
        {
            var rows = Physics(max: 1000);
            rows.Quantum = 17;
            Add("rows@" + hz, Play(rows, hz, 1.0, Enumerable.Range(0, 10).Select(i => (i * 0.008, (Action<double>)(t => rows.Precise(3, t, false))))), new { quantum = 17 });
        }
        if (Environment.GetEnvironmentVariable("MARROW_SCROLL_TRACES") is { Length: > 0 } path)
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(traces));
    });
}
