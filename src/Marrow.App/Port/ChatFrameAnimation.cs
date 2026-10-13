// PORT-ONLY: frame-driven interpolation for ChatView's padding and transform animations (ChatView.swift:754,768,1180).
using Avalonia.Controls;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Port;

internal sealed class ChatFrameAnimation(Control owner)
{
    private IFrameSource? source;
    private Action<TimeSpan>? pending;
    private ContinuousInstant began;

    public bool IsRunning => pending is not null;
    public double Seconds => (SwiftClock.Now - began).TotalSeconds;

    public void Stop()
    {
        if (pending is { } callback) source?.CancelFrame(callback);
        pending = null;
    }

    public void Start(Animation animation, Action<double> apply)
    {
        Stop();
        var start = SwiftClock.Now;
        began = start;
        var duration = SwiftTask.Seconds(animation.Duration);
        source = FrameClock.Source.Current;
        Action<TimeSpan>? frame = null;
        frame = _ =>
        {
            if (!ReferenceEquals(pending, frame)) return;
            var elapsed = SwiftClock.Now - start;
            var complete = elapsed.Ticks + TimeSpan.TicksPerMillisecond >= duration.Ticks; // ED-117.
            if (complete) pending = null;
            apply(complete ? 1 : animation.Progress(elapsed.TotalSeconds));
            if (!complete) source.RequestFrame(frame!);
        };
        pending = frame;
        apply(0);
        source.RequestFrame(frame);
    }

    public void AttachLifetime() => owner.DetachedFromVisualTree += (_, _) => Stop();
}

// ED-NEW-motion-drawer-1: independent dimensions share a frame/start time, but retain their own physical velocity.
// An unchanged target can still have velocity; its impulse must not disappear merely because its distance is zero.
internal sealed class ChatSpringValue(double from, double to, Animation animation, double speed)
{
    private readonly double distance = to - from;
    private readonly Animation carried = animation.WithStartSpeed(to == from ? 1 : speed / (to - from));

    public double Value(double seconds, bool complete) => complete ? to
        : from + distance * animation.Progress(seconds) + speed * animation.Impulse(seconds);

    public double Speed(double seconds) => seconds <= 0 ? speed
        : distance != 0 ? distance * carried.Speed(seconds)
        : speed * (carried.Speed(seconds) - animation.Speed(seconds));
}
