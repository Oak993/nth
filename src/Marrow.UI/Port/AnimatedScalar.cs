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

    public void Set(double target, Animation? animation)
    {
        Stop();
        var from = Value;
        if (animation is null || from == target || !FrameClock.IsBound || ReduceMotion)
        {
            Value = target;
            apply(target);
            return;
        }
        goal = target;
        var start = SwiftClock.Now;
        var span = TimeSpan.FromSeconds(animation.Duration);
        var frames = FrameClock.Source.Current;
        Action<TimeSpan>? tick = null;
        tick = _ =>
        {
            var now = SwiftClock.Now;
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
    }
}
