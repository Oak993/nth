// PORT-ONLY CZ-1: frame-clock CSS transitions, DESIGN gen.py:189-299 (same lifecycle as ED-250 LookMotion).
using Avalonia.Controls;
using Marrow.Core.Port;

namespace Marrow.UI.Port.Settings;

public sealed class CzMotion
{
    private readonly Control owner;
    private readonly List<CzNumber> moving = [];
    private IFrameSource? source;
    private bool waiting;
    private readonly Action<TimeSpan> frame;
    public CzMotion(Control owner)
    {
        this.owner = owner;
        frame = Tick;
        owner.DetachedFromVisualTree += (_, _) => Settle();
    }
    public bool Still => !FrameClock.IsBound || EnvironmentValues.GetAccessibilityReduceMotion(owner);
    public static Animation Ez(double seconds) => Animation.TimingCurve(.32, .72, 0, 1, seconds);
    public static Animation Ease(double seconds) => Animation.TimingCurve(.25, .1, .25, 1, seconds);
    public static Animation Pop(double seconds) => Animation.TimingCurve(.2, .9, .25, 1, seconds);
    internal void Add(CzNumber number)
    {
        if (!moving.Contains(number)) moving.Add(number);
        if (waiting) return;
        source = FrameClock.Source.Current; waiting = true; source.RequestFrame(frame);
    }
    internal void Remove(CzNumber number) => moving.Remove(number);
    private void Tick(TimeSpan _)
    {
        waiting = false;
        // OD-0076: Reduce Motion turned on while a transition runs stills it at the next frame
        if (Still) { Settle(); return; }
        foreach (var number in moving.ToArray()) if (number.Step()) moving.Remove(number);
        if (moving.Count == 0) return;
        waiting = true; source!.RequestFrame(frame);
    }
    public void Settle()
    {
        foreach (var number in moving.ToArray()) number.Jump(number.Target);
        moving.Clear();
        if (waiting) source?.CancelFrame(frame);
        waiting = false;
    }
}
