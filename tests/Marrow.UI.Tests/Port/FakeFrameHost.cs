using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Port;

// ED-NEW-lean-7 test seam: the platform AvaloniaFrameSource asks (Win32FrameHost in production). The source's own logic
// (holding back, arming, re-arming, cancelling, the wake-up, parking, the 100 ms look cache, the window hooks and their
// posted re-look) runs unchanged; only the answers of Windows and the kernel timer are given here.
internal sealed class FakeFrameHost : IFrameHost
{
    public bool Win32 { get; set; } = true;
    public TimeSpan? Period { get; set; }
    public bool HasPreciseTimer { get; set; } = true;
    public Func<bool> Probe { get; set; } = () => false;
    public int Looks { get; private set; }
    public int PeriodReads { get; private set; }
    public FakeWakeTimer? Precise { get; private set; }
    public FakeWakeTimer? Coarse { get; private set; }

    public bool IsWin32(TopLevel topLevel) => Win32;

    public TimeSpan? RefreshPeriod()
    {
        PeriodReads++;
        return Period;
    }

    public IWakeTimer? PreciseTimer(Action fired) => HasPreciseTimer ? Precise = new FakeWakeTimer(fired) : null;

    public IWakeTimer? CoarseTimer(Action fired) => Coarse = new FakeWakeTimer(fired);

    public bool NoWindowSeen()
    {
        Looks++;
        return Probe();
    }

    // As production: on main, at Normal (ahead of a queued render job).
    public void Post(Action job) => Dispatcher.UIThread.Post(job, DispatcherPriority.Normal);

    // The process-wide hooks are set once, by the first Win32 source of the test run; a test calls
    // AvaloniaFrameSource.WindowsChanged itself, as the production cloak watch does.
    public Action? CloakWatch { get; private set; }

    public void WatchCloaking(Action changed) => CloakWatch = changed;
}

// A one-shot timer on SwiftClock (a test's ManualTimeProvider): armed for an instant, fired by the test when it is due, as
// the waiting thread posts the wake-up once the kernel timer has fired.
internal sealed class FakeWakeTimer(Action fired) : IWakeTimer
{
    public List<TimeSpan> Arms { get; } = [];
    public ContinuousInstant? Due { get; private set; }

    public void Arm(TimeSpan delay)
    {
        Arms.Add(delay);
        Due = SwiftClock.Now + (delay > TimeSpan.Zero ? delay : TimeSpan.FromTicks(1));
    }

    /// <summary>Fires now whether due or not (a wake-up that comes early).</summary>
    public void Fire()
    {
        Due = null;
        fired();
    }

    /// <summary>Fires when its instant plus <paramref name="lateness"/> has come; again if it was re-armed for a past instant.</summary>
    public int FireIfDue(TimeSpan lateness = default)
    {
        var count = 0;
        while (count < 8 && Due is { } due && due + lateness <= SwiftClock.Now)
        {
            Fire();
            count++;
        }
        return count;
    }
}

// Counts the frame callbacks the loops get (each one a RequestAnimationFrame and a commit in production).
internal sealed class CountingLoopSource(AvaloniaFrameSource inner) : IWakeFrameSource
{
    private readonly Dictionary<Action<TimeSpan>, Action<TimeSpan>> wrapped = [];
    public long Answered { get; private set; }
    public TimeSpan? WakePeriod => inner.WakePeriod;
    public int Pending => inner.Pending;

    private Action<TimeSpan> Wrap(Action<TimeSpan> frame)
    {
        if (!wrapped.TryGetValue(frame, out var counted)) wrapped[frame] = counted = time => { Answered++; frame(time); };
        return counted;
    }

    public void RequestFrame(Action<TimeSpan> frame) => inner.RequestFrame(Wrap(frame));
    public void RequestFrameAt(ContinuousInstant? wake, Action<TimeSpan> frame) => inner.RequestFrameAt(wake, Wrap(frame));
    public void CancelFrame(Action<TimeSpan> frame) => inner.CancelFrame(Wrap(frame));
}
