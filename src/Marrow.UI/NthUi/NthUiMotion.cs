// PORT-ONLY: web surfaces W16a (DESIGN.md 7.4 "A new node fades in (opacity 0 → 1, 6 DIP rise, 180 ms) and its container
// height springs open"; 10 "Reduce Motion: springs become short fades"; owner bar: interruptible springs, transform and
// opacity only for motion; ED-NEW-web-w16a-1). No Swift counterpart. One frame loop per live view: every slot, panel and
// control that moves registers here and is stepped at each display frame (FrameClock, ED-117) with the time read from
// SwiftClock, so tests drive it with a manual frame source and clock. Springs are NthSpring (the nth motion pack's critically
// damped value spring, closed form, retargeted from the value and speed reached), so every motion is interruptible.
using Marrow.Core.Port;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;

namespace Marrow.UI.NthUi;

/// <summary>Something that moves: stepped once a frame until it says it has settled.</summary>
internal interface INthUiAnimated
{
    /// <summary>Moves to <paramref name="now"/> (seconds); false once it is at rest.</summary>
    bool Step(double now);
}

/// <summary>The frame loop of one live view.</summary>
internal sealed class NthUiMotion
{
    private readonly List<INthUiAnimated> active = [];
    private readonly List<INthUiAnimated> stepping = [];
    private readonly Action<TimeSpan> tick;
    private IFrameSource? source;
    private bool requested;

    public NthUiMotion()
    {
        tick = OnFrame;
    }

    /// <summary>The clock every spring reads, in seconds.</summary>
    public static double Now => SwiftClock.Now.Ticks / (double)TimeSpan.TicksPerSecond;

    /// <summary>Whether anything can move: a frame source is bound and the view animates.</summary>
    public bool CanAnimate => Enabled && FrameClock.IsBound;

    /// <summary>False for a view shown whole (a saved answer reopened): everything takes its end state at once.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Whether anything is still moving (tests and the idle check read it).</summary>
    public bool IsAnimating => active.Count > 0;

    /// <summary>Frames delivered so far (tests).</summary>
    public int Frames { get; private set; }

    public void Start(INthUiAnimated item)
    {
        if (!active.Contains(item)) active.Add(item);
        Request();
    }

    public void Remove(INthUiAnimated item) => active.Remove(item);

    private void Request()
    {
        if (requested || active.Count == 0 || !FrameClock.IsBound) return;
        source = FrameClock.Source.Current;
        requested = true;
        source.RequestFrame(tick);
    }

    private void OnFrame(TimeSpan _)
    {
        requested = false;
        Frames++;
        var now = Now;
        stepping.Clear();
        stepping.AddRange(active);
        foreach (var item in stepping)
        {
            if (!item.Step(now)) active.Remove(item);
        }
        stepping.Clear();
        Request();
    }

    /// <summary>Withdraws the frame request (the view left the tree); items stay registered for the next start.</summary>
    public void Pause()
    {
        if (requested) source?.CancelFrame(tick);
        requested = false;
    }

    public void Resume() => Request();
}

/// <summary>A tween of a value over a fixed time with an nth curve (fades).</summary>
internal struct NthUiTween
{
    private double from;
    private double to;
    private double start;
    private double seconds;
    private NthCurve curve;

    public static NthUiTween At(double value) => new() { from = value, to = value, start = double.NegativeInfinity, seconds = 0, curve = NthEase.Out };

    public readonly double Target => to;

    public readonly double Value(double now)
    {
        if (seconds <= 0 || double.IsNegativeInfinity(start)) return to;
        var p = (now - start) / seconds;
        if (p <= 0) return from;
        if (p >= 1) return to;
        return from + (to - from) * curve.Ease(p);
    }

    public readonly bool Settled(double now) => seconds <= 0 || double.IsNegativeInfinity(start) || now - start >= seconds;

    /// <summary>Goes to <paramref name="target"/> from the value reached at <paramref name="now"/>.</summary>
    public NthUiTween Retarget(double target, double now, double duration, NthCurve ease)
    {
        if (target == to && Settled(now)) return this;
        return new NthUiTween { from = Value(now), to = target, start = now, seconds = duration, curve = ease };
    }
}

/// <summary>Reduce Motion for a control: the window's environment value or the system setting (as AnimatedScalar reads it).</summary>
internal static class NthUiCalm
{
    public static bool For(Avalonia.Controls.Control control) =>
        EnvironmentValues.GetAccessibilityReduceMotion(control) || Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion;
}
