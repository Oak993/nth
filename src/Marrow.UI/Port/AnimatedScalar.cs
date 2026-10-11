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

    /// <summary>The value drawn now.</summary>
    public double Value { get; private set; }

    /// <summary>A run is moving the value towards <see cref="Target"/>: a frame is still wanted.</summary>
    public bool IsRunning => pending is not null;

    /// <summary>Where the value is heading: the last target set (the value itself once it has landed).</summary>
    public double Target => pending is null ? Value : goal;

    /// <param name="additive">PORT-ONLY (ED-NEW-sidebarmodes-8): SwiftUI's springs are additive, so a new target set while a run is in flight
    /// adds a run of its own on top of the one already moving and the value keeps its speed: a reversal turns round smoothly instead of
    /// stopping dead and starting again from rest. Off, a new target replaces the run from the value reached, at rest.</param>
    public void Set(double target, Animation? animation, bool preserveFrameProgress = false, bool additive = false)
    {
        if (additive && animation is not null && pending is not null && FrameClock.IsBound && !ReduceMotion)
        {
            Retarget(target, animation);
            return;
        }
        Stop();
        runs.Clear();
        var from = Value;
        if (animation is null || from == target || !FrameClock.IsBound || ReduceMotion)
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
        var previousFrame = start;
        var span = TimeSpan.FromSeconds(animation.Duration);
        var frames = FrameClock.Source.Current;
        Action<TimeSpan>? tick = null;
        tick = _ =>
        {
            var now = SwiftClock.Now;
            var gap = now - previousFrame;
            if (preserveFrameProgress && gap > TimeSpan.FromMilliseconds(50))
                start += gap - TimeSpan.FromSeconds(1.0 / 60);
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

    private void Retarget(double target, Animation animation)
    {
        if (runs.Count == 0) // a plain (replacing) run is in flight: the additive one takes over from the value it reached
        {
            Stop();
            goal = target;
            runs.Add(new Run(target - Value, SwiftClock.Now, animation));
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
    }
}
