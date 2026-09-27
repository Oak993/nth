// PORT-ONLY: frame-driven interpolation for ChatView's padding and transform animations (ChatView.swift:754,768,1180).
using Avalonia.Controls;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Port;

internal sealed class ChatFrameAnimation(Control owner)
{
    private IFrameSource? source;
    private Action<TimeSpan>? pending;

    public void Stop()
    {
        if (pending is { } callback) source?.CancelFrame(callback);
        pending = null;
    }

    public void Start(Animation animation, Action<double> apply)
    {
        Stop();
        var start = SwiftClock.Now;
        var duration = SwiftTask.Seconds(animation.Duration);
        source = FrameClock.Source.Current;
        Action<TimeSpan>? frame = null;
        frame = _ =>
        {
            if (!ReferenceEquals(pending, frame)) return;
            var elapsed = SwiftClock.Now - start;
            var complete = elapsed.Ticks + TimeSpan.TicksPerMillisecond >= duration.Ticks; // ED-117.
            apply(complete ? 1 : animation.Progress(elapsed.TotalSeconds));
            if (complete) pending = null;
            else source.RequestFrame(frame!);
        };
        pending = frame;
        apply(0);
        source.RequestFrame(frame);
    }

    public void AttachLifetime() => owner.DetachedFromVisualTree += (_, _) => Stop();
}
