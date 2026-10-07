// PORT-ONLY: opacity remains animated under Reduce Motion; only spatial motion is replaced.
using Avalonia.Controls;
using Marrow.Core.Port;
using Marrow.UI.Port;
namespace Marrow.App.Port.MultiAccount.Shared;
internal sealed class OpacityTransition
{
    private readonly Action<double> apply;
    private readonly Action<TimeSpan> tick;
    private IFrameSource? frames;
    private bool pending;
    private double from, target;
    private Animation? curve;
    private ContinuousInstant start;
    internal double Value { get; private set; }
    internal OpacityTransition(Control owner, double initial, Action<double> apply)
    {
        this.apply = apply; Value = target = initial; apply(initial);
        tick = _ => Advance(); owner.DetachedFromVisualTree += (_, _) => Stop();
    }
    internal void Set(double next, Animation? animation)
    {
        if (pending && next == target) return;
        Stop(); from = Value; target = next; curve = animation;
        if (animation is null || !FrameClock.IsBound || from == next) { Value = next; apply(next); return; }
        start = SwiftClock.Now; frames = FrameClock.Source.Current; pending = true; frames.RequestFrame(tick);
    }
    private void Advance()
    {
        pending = false; if (curve is null) return;
        var elapsed = (SwiftClock.Now - start).TotalSeconds;
        var done = elapsed >= curve.Duration;
        Value = done ? target : from + (target - from) * curve.Progress(elapsed); apply(Value);
        if (!done) { pending = true; frames!.RequestFrame(tick); }
    }
    private void Stop() { if (pending) frames?.CancelFrame(tick); pending = false; }
}
