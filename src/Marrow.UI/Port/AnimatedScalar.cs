// PORT-ONLY: a number a view draws with (an opacity, a scale) that, set inside an animated transaction, moves to its new
// value along that animation on the frame clock (R-VIEW-4, ED-117; OD-0076: controls that appear or disappear fade).
using Avalonia.Controls;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

/// <summary>
/// A scalar that follows an <see cref="Animation"/>: <see cref="Set"/> with no animation jumps; with one it moves from
/// where it is to the target, each step on a frame, measured on SwiftClock (ED-117). A new target replaces a running one
/// from the value reached. Leaving the tree stops it. OD-0076: the system's reduce-motion setting stills every transition,
/// so under it (the owner's window environment or the Chrome platform's setting) a target is taken at once.
/// </summary>
public sealed class AnimatedScalar
{
    private readonly Control owner;
    private readonly Action<double> apply;
    private IFrameSource? source;
    private Action<TimeSpan>? pending;
    private double goal;

    public AnimatedScalar(Control owner, double initial, Action<double> apply)
    {
        Value = initial;
        this.owner = owner;
        this.apply = apply;
        apply(initial);
        owner.DetachedFromVisualTree += (_, _) => Stop();
    }

    /// <summary>PORT-ONLY (OD-0143, ED-NEW-nocta-1): a light that still crossfades under Reduce Motion (the caller passes the short fade it wants);
    /// a position or a scale never sets this, so it jumps as OD-0076 asks.</summary>
    public bool RunsUnderReduceMotion { get; init; }

    /// <summary>The value drawn now.</summary>
    public double Value { get; private set; }

    /// <summary>A run is moving the value towards <see cref="Target"/>: a frame is still wanted.</summary>
    public bool IsRunning => pending is not null;

    /// <summary>Where the value is heading: the last target set (the value itself once it has landed).</summary>
    public double Target => pending is null ? Value : goal;

    /// <summary>How fast the value moves now, per second (0 at rest), on the run's own clock.</summary>
    public double Speed
    {
        get
        {
            if (pending is null) return 0;
            var now = SwiftClock.Now;
            if (runs.Count > 0) return runs.Sum(run => run.Delta * run.Animation.Speed((now - run.Start).TotalSeconds));
            return plain is { } running ? (goal - running.From) * running.Animation.Speed((now - running.Start).TotalSeconds) : 0;
        }
    }

    /// <param name="additive">PORT-ONLY (ED-NEW-sidebarmodes-8): SwiftUI's springs are additive, so a new target set while a run is in flight
    /// adds a run of its own on top of the one already moving and the value keeps its speed: a reversal turns round smoothly instead of
    /// stopping dead and starting again from rest. Off, a new target replaces the run from the value reached, at rest.</param>
    /// <param name="keepSpeed">PORT-ONLY (ED-NEW-perf-motion-11): SwiftUI's springs keep their velocity through a retarget, so a new
    /// target set while a spring is in flight sets out from the value reached at the speed it had (the carried speed dies out by the
    /// curve's end) instead of from rest. A site whose motion must stay on the same curve as another one's (the composer's inline point
    /// and its height) uses this, as the other does.</param>
    public void Set(double target, Animation? animation, bool preserveFrameProgress = false, bool additive = false, bool keepSpeed = false)
    {
        if (additive && animation is not null && pending is not null && FrameClock.IsBound && !ReduceMotion)
        {
            Retarget(target, animation);
            return;
        }
        if (keepSpeed && animation is { KeepsSpeed: true } && pending is not null && target != Value)
            animation = animation.WithStartSpeed(Speed / (target - Value));
        Stop();
        runs.Clear();
        var from = Value;
        if (animation is null || from == target || !FrameClock.IsBound || (ReduceMotion && !RunsUnderReduceMotion))
        {
            Value = target;
            apply(target);
            return;
        }
        if (additive)
        {
            goal = target;
            runs.Add(new Run(target - from, SwiftClock.Now, animation));
            StartRuns();
            return;
        }
        goal = target;
        var start = SwiftClock.Now;
        plain = (from, start, animation);
        var previousFrame = start;
        var span = TimeSpan.FromSeconds(animation.Duration);
        var frames = FrameClock.Source.Current;
        Action<TimeSpan>? tick = null;
        tick = _ =>
        {
            var now = SwiftClock.Now;
            var gap = now - previousFrame;
            if (preserveFrameProgress && gap > TimeSpan.FromMilliseconds(50))
            {
                start += gap - TimeSpan.FromSeconds(1.0 / 60);
                plain = (from, start, animation);
            }
            previousFrame = now;
            var complete = FrameClock.IsComplete(start, now, span);
            Value = complete ? target : from + (target - from) * animation.Progress((now - start).TotalSeconds);
            apply(Value);
            if (complete) pending = null;
            else frames.RequestFrame(tick!);
        };
        source = frames;
        pending = tick;
        frames.RequestFrame(tick);
    }

    // One additive run: what it still has to add is (1 - progress) of its delta, counted back from the goal.
    private sealed record Run(double Delta, ContinuousInstant Start, Animation Animation);
    private readonly List<Run> runs = [];
    // The plain run in flight (where it set out from, when, and along what), so an additive target can carry it on.
    private (double From, ContinuousInstant Start, Animation Animation)? plain;

    private void Retarget(double target, Animation animation)
    {
        if (runs.Count == 0) // a plain (replacing) run is in flight: it carries on as an additive run, so the value keeps its speed
        {
            // ED-NEW-perf-motion-11: before, the plain run was stopped and the additive one began from the value at rest, a dead stop.
            var running = plain;
            var heading = goal;
            Stop();
            if (running is { } carried) runs.Add(new Run(heading - carried.From, carried.Start, carried.Animation));
            goal = target;
            runs.Add(new Run(target - heading, SwiftClock.Now, animation));
            StartRuns();
            return;
        }
        runs.Add(new Run(target - goal, SwiftClock.Now, animation)); // the runs in flight keep moving; this one adds the change of goal
        goal = target;
    }

    private void StartRuns()
    {
        var frames = FrameClock.Source.Current;
        Action<TimeSpan>? tick = null;
        tick = _ =>
        {
            var now = SwiftClock.Now;
            var value = goal;
            runs.RemoveAll(run => FrameClock.IsComplete(run.Start, now, TimeSpan.FromSeconds(run.Animation.Duration)));
            foreach (var run in runs) value -= run.Delta * (1 - run.Animation.Progress((now - run.Start).TotalSeconds));
            Value = value;
            apply(Value);
            if (runs.Count == 0) pending = null;
            else frames.RequestFrame(tick!);
        };
        source = frames;
        pending = tick;
        frames.RequestFrame(tick);
    }

    /// <summary>A run in progress lands on its target at once (the owner's Reduce Motion turned on while it ran); nothing is left waiting for a frame.</summary>
    public void Finish()
    {
        if (pending is null) return;
        Stop();
        Value = goal;
        apply(goal);
    }

    private bool ReduceMotion =>
        EnvironmentValues.GetAccessibilityReduceMotion(owner) || Chrome.Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion;

    private void Stop()
    {
        if (pending is { } frame) source?.CancelFrame(frame);
        pending = null;
        runs.Clear();
        plain = null;
    }
}
