using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// ED-249 (OD-0074): a stand-in DWM cloak that records the Settings window's open, with the manual frame clock and
/// SwiftClock it is driven by. Install it inside the test's async flow (it overrides both clocks).
/// </summary>
public sealed class SettingsRiseRig : IDisposable
{
    private readonly IDisposable frameScope;
    private readonly IDisposable clockScope;
    private readonly HashSet<AvaloniaWindow> watched = [];
    private readonly TracingFrameSource tracing;

    /// <summary>The frame requests <paramref name="requester"/> (the object whose method asked) has outstanding.</summary>
    public int PendingFor(object? requester) => requester is null ? 0 : tracing.PendingFor(requester);

    public SettingsRiseRig()
    {
        tracing = new TracingFrameSource(Frames);
        frameScope = FrameClock.Source.Override(tracing);
        clockScope = SwiftClock.Provider.Override(Time);
    }

    public ManualFrameSource Frames { get; } = new();

    public ManualTimeProvider Time { get; } = new();

    /// <summary>"cloak", "shown", "uncloak", "activated", "hidden", in order.</summary>
    public List<string> Events { get; } = [];

    /// <summary>Whether the window takes the cloak (false: DWM refused or no Win32 window).</summary>
    public bool Accepts { get; set; } = true;

    /// <summary>Is the window cloaked now.</summary>
    public bool Cloaked { get; private set; }

    /// <summary>When the window was last uncloaked: glass-ready, G.</summary>
    public ContinuousInstant? Reveal { get; private set; }

    /// <summary>Runs at the uncloak, before it returns: what the first displayed frame holds.</summary>
    public Action? AtUncloak { get; set; }

    public bool Cloak(AvaloniaWindow window, bool cloaked)
    {
        Watch(window);
        Events.Add(cloaked ? "cloak" : "uncloak");
        if (!Accepts) return false;
        Cloaked = cloaked;
        if (!cloaked)
        {
            Reveal = SwiftClock.Now;
            AtUncloak?.Invoke();
        }
        return true;
    }

    public void Watch(AvaloniaWindow window)
    {
        if (!watched.Add(window)) return;
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.IsVisibleProperty) Events.Add(window.IsVisible ? "shown" : "hidden");
        };
        window.Activated += (_, _) => Events.Add("activated");
    }

    /// <summary>One frame <paramref name="seconds"/> after the last.</summary>
    public void Frame(double seconds) => Step(TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond)));

    /// <summary>One frame at G + <paramref name="seconds"/> (G = the last uncloak).</summary>
    public void At(double seconds)
    {
        var target = Reveal!.Value + TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));
        Step(target - SwiftClock.Now);
    }

    private void Step(TimeSpan by)
    {
        Time.Advance(by);
        Frames.Advance(by);
        Dispatcher.UIThread.RunJobs();
    }

    public void Dispose()
    {
        clockScope.Dispose();
        frameScope.Dispose();
    }
}

/// <summary>
/// ED-249: the spec's curve, transcribed from E:/Droppy/work/design/settings-rise/SPEC.md (sections 2 and 3), solved
/// here by bisection, independently of the port's easing.
/// </summary>
public static class SpecRise
{
    public const double GlassFade = 0.080, ItemDuration = 0.140, Stagger = 0.012, Offset = 10, OpenDone = 0.260;

    /// <summary>cubic-bezier(0.2, 0.8, 0.2, 1) at progress <paramref name="x"/>.</summary>
    public static double Ease(double x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        const double x1 = 0.2, y1 = 0.8, x2 = 0.2, y2 = 1;
        static double Cubic(double u, double a, double b) => 3 * a * u * (1 - u) * (1 - u) + 3 * b * u * u * (1 - u) + u * u * u;
        double low = 0, high = 1;
        for (var i = 0; i < 80; i++)
        {
            var mid = (low + high) / 2;
            if (Cubic(mid, x1, x2) < x) low = mid; else high = mid;
        }
        return Cubic((low + high) / 2, y1, y2);
    }

    public static double Glass(double t) => Ease(t / GlassFade);

    public static double Item(int index, double t) => Ease((t - Stagger * index) / ItemDuration);
}

/// <summary>A frame source that remembers who asked for each outstanding frame (ED-249 tests).</summary>
public sealed class TracingFrameSource(ManualFrameSource inner) : IFrameSource
{
    private readonly List<(Action<TimeSpan> Frame, Action<TimeSpan> Wrapper)> waiting = [];

    public void RequestFrame(Action<TimeSpan> frame)
    {
        Action<TimeSpan>? wrapper = null;
        wrapper = time =>
        {
            waiting.RemoveAll(entry => ReferenceEquals(entry.Wrapper, wrapper));
            frame(time);
        };
        waiting.Add((frame, wrapper));
        inner.RequestFrame(wrapper);
    }

    public void CancelFrame(Action<TimeSpan> frame)
    {
        var index = waiting.FindIndex(entry => ReferenceEquals(entry.Frame, frame));
        if (index < 0) return;
        inner.CancelFrame(waiting[index].Wrapper);
        waiting.RemoveAt(index);
    }

    public int PendingFor(object requester) => waiting.Count(entry => ReferenceEquals(entry.Frame.Target, requester));
}
