// PORT-ONLY: the frame clock seam for TimelineView(.animation), CADisplayLink and frame-tick sleeps (ED-083, ED-098 (1), ED-117; R-VIEW-10, R-TASK-3).
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

// Frames come from TopLevel.RequestAnimationFrame behind IFrameSource and are the only wake-ups (ED-117 (1)). Spans
// are measured on SwiftClock and dates are SwiftDate.Now at the frame; the TimeSpan a frame callback receives
// decides nothing. A span is complete 1 ms early at most (ED-117 (2)). No change of the system timer resolution:
// Windows timers tick every 15.6 ms (R-VIEW-10). ED-NEW-lean-7 amends "the only wake-ups" in how a frame is asked for,
// never in what decides a draw: a schedule slower than the display lets its request sleep until 1.5 refreshes before
// its next draw is due (IWakeFrameSource; production wakes it with a one-shot high-resolution waitable timer, which
// leaves the system timer resolution alone), and the draw still happens in a frame, by the same maths.
// UNVERIFIED O-34: the cadence, dates, first tick and schedule-change rules below are the port's parked choice for
// SwiftUI and Core Animation behaviour the pinned source does not contain (ED-117 (7)).

/// <summary>PORT-ONLY. A source of display frames: one call per request at the next frame, never inside the request.</summary>
public interface IFrameSource
{
    /// <summary>Calls <paramref name="frame"/> once, on main, at the next frame, with that frame's time.</summary>
    void RequestFrame(Action<TimeSpan> frame);

    /// <summary>
    /// Withdraws a request of <paramref name="frame"/> (the same delegate) that has not run yet; nothing when there is
    /// none. A view that leaves the tree and an invalidated display link leave no request behind.
    /// </summary>
    void CancelFrame(Action<TimeSpan> frame);

    /// <summary>
    /// The frame requests waiting for the next frame: while any wait, something is animating (ED-252: the knob's GPU
    /// warm-up compiles only in frames where nothing does). A source that cannot tell answers 0.
    /// </summary>
    int Pending => 0;
}

/// <summary>
/// PORT-ONLY (ED-NEW-lean-7). A frame source that can hold a request back to an instant, so a schedule slower than the
/// display asks for no frames between its draws.
/// </summary>
public interface IWakeFrameSource : IFrameSource
{
    /// <summary>The display's refresh period when this source can hold a request back; null: it cannot.</summary>
    TimeSpan? WakePeriod { get; }

    /// <summary>
    /// Calls <paramref name="frame"/> once, on main, at the first frame at or after the SwiftClock instant
    /// <paramref name="wake"/>, never inside the request; <see cref="IFrameSource.CancelFrame"/> withdraws it.
    /// </summary>
    void RequestFrameAt(ContinuousInstant wake, Action<TimeSpan> frame);
}

/// <summary>
/// PORT-ONLY (ED-248 review 2). A frame source that can also give a view frames on its compositor's render thread, at the
/// display's refreshes, for a timeline whose drawing is a function of time alone (it changes nothing on main).
/// </summary>
public interface IRenderFrameSourceProvider
{
    /// <summary>
    /// A frame source whose frames come on the render thread of <paramref name="host"/>'s compositor, once per display
    /// refresh, invalidating the host's area when asked (<see cref="RenderFrameSource.InvalidateTicking"/>); null when the
    /// host has no compositor. The caller disposes it when the host leaves the tree.
    /// </summary>
    RenderFrameSource? RenderFrames(Control host);
}

/// <summary>
/// PORT-ONLY (ED-248 review 2). A frame source that knows the display's refreshes: <see cref="Refresh"/> numbers the refresh
/// of the frame being delivered, so a timeline can keep an interval of a whole number of refreshes exactly.
/// </summary>
public interface IRefreshFrameSource : IFrameSource
{
    /// <summary>The display's refresh period, when known.</summary>
    TimeSpan? RefreshPeriod { get; }

    /// <summary>The refresh the frame being delivered belongs to (counted from the source's first frame).</summary>
    long Refresh { get; }
}

/// <summary>
/// PORT-ONLY. The production frame source: <c>TopLevel.RequestAnimationFrame</c>, its frame time passed on.
/// ED-NEW-lean-7: on the Windows platform it holds a request back to an instant (<see cref="IWakeFrameSource"/>), and while
/// every shown window of the app is minimised or cloaked it asks for no frames: the requests wait, and are asked for as
/// soon as a window can be seen again.
/// </summary>
public sealed class AvaloniaFrameSource(TopLevel topLevel) : IWakeFrameSource, IRenderFrameSourceProvider
{
    // ED-NEW-lean-7: how often parked requests look at the windows again, and how long a look at them is trusted.
    private static readonly TimeSpan LookEvery = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan Slack = TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond / 2);
    private static event Action? HostsChanged;
    private static int hooked;

    private readonly List<Request> waiting = [];
    private readonly Stack<Request> pool = new();
    private readonly List<(Action<TimeSpan> Frame, ContinuousInstant Wake)> sleeping = []; // held back to an instant
    private readonly List<Action<TimeSpan>> parked = [];                                    // held while no window is seen
    private bool? win32;
    private WakeTimer? timer;
    private bool timerMade;
    private ContinuousInstant? armed;
    private TimeSpan? period;
    private long periodRead, lookedAt;
    private bool unseen, hostsChanged;

    /// <summary>The requests waiting for a frame: asked for, held back to an instant, or held while no window is seen.</summary>
    public int Pending => waiting.Count + sleeping.Count + parked.Count;

    /// <summary>ED-NEW-lean-7 seam (tests): whether no window can be seen; null asks the app's windows.</summary>
    internal Func<bool>? UnseenOverride { get; set; }

    // ED-248: request objects and their delegates are reused.
    private sealed class Request
    {
        private readonly AvaloniaFrameSource owner;

        public Request(AvaloniaFrameSource owner)
        {
            this.owner = owner;
            Run = OnFrame;
        }

        public Action<TimeSpan>? Frame { get; set; }
        public Action<TimeSpan> Run { get; }

        private void OnFrame(TimeSpan time)
        {
            var frame = Frame;
            // A withdrawn request still gets Avalonia's frame, and does nothing with it.
            var live = owner.waiting.Remove(this);
            Frame = null;
            owner.pool.Push(this); // Avalonia holds it no longer: free for the next request
            if (live) frame!(time);
        }
    }

    public RenderFrameSource? RenderFrames(Control host) => RenderFrameSource.Create(host);

    /// <summary>
    /// ED-NEW-lean-7: DWM's refresh period (read again after a second) when this source can hold a request back: a window
    /// of the Windows platform with a high-resolution waitable timer; null otherwise (headless, older Windows).
    /// </summary>
    public TimeSpan? WakePeriod
    {
        get
        {
            if (!IsWin32) return null;
            var now = Stopwatch.GetTimestamp();
            if (periodRead == 0 || Stopwatch.GetElapsedTime(periodRead, now) >= TimeSpan.FromSeconds(1))
            {
                period = RenderFrameSource.DisplayRefreshPeriod();
                periodRead = now;
            }
            return period is not null && Timer() is not null ? period : null;
        }
    }

    public void RequestFrame(Action<TimeSpan> frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (Unseen())
        {
            parked.Add(frame); // ED-NEW-lean-7: nothing can be seen, so no frame is asked for
            Arm();
            return;
        }
        Ask(frame);
    }

    public void RequestFrameAt(ContinuousInstant wake, Action<TimeSpan> frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (Timer() is null || wake <= SwiftClock.Now + Slack)
        {
            RequestFrame(frame);
            return;
        }
        sleeping.Add((frame, wake));
        Arm();
    }

    public void CancelFrame(Action<TimeSpan> frame)
    {
        for (var i = 0; i < waiting.Count; i++)
        {
            if (!ReferenceEquals(waiting[i].Frame, frame)) continue;
            waiting.RemoveAt(i);
            return;
        }
        for (var i = 0; i < sleeping.Count; i++)
        {
            if (!ReferenceEquals(sleeping[i].Frame, frame)) continue;
            sleeping.RemoveAt(i); // the timer may still fire: a wake-up with nothing due asks for nothing
            return;
        }
        for (var i = 0; i < parked.Count; i++)
        {
            if (!ReferenceEquals(parked[i], frame)) continue;
            parked.RemoveAt(i);
            return;
        }
    }

    /// <summary>ED-NEW-lean-7: asks for the parked requests when a window can be seen again (also the tests' seam).</summary>
    internal void Recheck()
    {
        hostsChanged = true;
        if (parked.Count > 0 && !Unseen())
        {
            var resumed = parked.ToArray();
            parked.Clear();
            foreach (var frame in resumed) Ask(frame);
        }
        Arm();
    }

    private void Ask(Action<TimeSpan> frame)
    {
        var request = pool.Count > 0 ? pool.Pop() : new Request(this);
        request.Frame = frame;
        waiting.Add(request);
        topLevel.RequestAnimationFrame(request.Run);
        // ED-237: a frame whose callback changes nothing on screen lets Avalonia's render loop go idle, and the next
        // requested frame then waits for the idle wake-up (16-31 ms) instead of the display's next refresh; a timeline
        // drawing every n-th refresh fell to about 48 updates a second on a 240 Hz display. A commit request each
        // frame keeps the loop on the display's refresh while frames are wanted (an empty commit draws nothing).
        if (Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(topLevel)?.Compositor is { } compositor)
            _ = compositor.RequestCommitAsync();
    }

    private bool IsWin32
    {
        get
        {
            if (win32 is { } known) return known;
            win32 = OperatingSystem.IsWindows() && topLevel.TryGetPlatformHandle()?.HandleDescriptor == "HWND";
            if (win32 == true)
            {
                HostsChanged += () =>
                {
                    hostsChanged = true;
                    if (parked.Count > 0) Dispatcher.UIThread.Post(Recheck, DispatcherPriority.Render);
                };
                Hook();
            }
            return win32.Value;
        }
    }

    // A window minimised, restored, shown, hidden or activated (back from another virtual desktop) is looked at again at once.
    private static void Hook()
    {
        if (Interlocked.Exchange(ref hooked, 1) != 0) return;
        Window.WindowStateProperty.Changed.AddClassHandler<Window>((_, _) => HostsChanged?.Invoke());
        WindowBase.IsActiveProperty.Changed.AddClassHandler<WindowBase>((_, _) => HostsChanged?.Invoke());
        Visual.IsVisibleProperty.Changed.AddClassHandler<Window>((_, _) => HostsChanged?.Invoke());
    }

    private WakeTimer? Timer()
    {
        if (timerMade || !IsWin32) return timer;
        timerMade = true;
        return timer = WakeTimer.Create(OnWake);
    }

    // The timer is armed for the earliest instant anything waits for: a held request's wake-up, or the next look at the
    // windows while requests are parked. A source without a timer (headless) looks again only when asked (Recheck).
    private void Arm()
    {
        if (Timer() is not { } wakeTimer) return;
        ContinuousInstant? at = null;
        foreach (var (_, wake) in sleeping)
            if (at is not { } earliest || wake < earliest) at = wake;
        if (parked.Count > 0 && (at is not { } soonest || SwiftClock.Now + LookEvery < soonest)) at = SwiftClock.Now + LookEvery;
        if (at is not { } when || armed == when) return;
        armed = when;
        wakeTimer.Arm(when - SwiftClock.Now);
    }

    private void OnWake()
    {
        armed = null;
        var now = SwiftClock.Now + Slack;
        for (var i = 0; i < sleeping.Count;)
        {
            if (sleeping[i].Wake > now)
            {
                i++;
                continue;
            }
            var frame = sleeping[i].Frame;
            sleeping.RemoveAt(i);
            RequestFrame(frame);
        }
        if (parked.Count > 0) Recheck();
        else Arm();
    }

    private bool Unseen()
    {
        if (UnseenOverride is { } probe) return probe();
        if (!IsWin32) return false;
        var now = Stopwatch.GetTimestamp();
        if (hostsChanged || lookedAt == 0 || Stopwatch.GetElapsedTime(lookedAt, now) >= LookEvery)
        {
            unseen = NoWindowSeen();
            lookedAt = now;
            hostsChanged = false;
        }
        return unseen;
    }

    // ED-NEW-lean-7: frames serve every window (one process-wide MediaContext, Program.cs), so they stop only when every
    // shown window of the app is minimised (IsIconic) or cloaked (DWMWA_CLOAKED: another virtual desktop). No shown window,
    // or one that is not a Win32 window, counts as seen: frames go on as before.
    private static bool NoWindowSeen()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { Windows: { Count: > 0 } windows }) return false;
        var hidden = false;
        foreach (var window in windows)
        {
            if (!window.IsVisible) continue;
            if (window.TryGetPlatformHandle() is not { HandleDescriptor: "HWND" } handle || handle.Handle == 0) return false;
            if (!IsIconic(handle.Handle) && !(DwmGetWindowAttribute(handle.Handle, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0)) return false;
            hidden = true;
        }
        return hidden;
    }

    private const int DwmwaCloaked = 14;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);

    /// <summary>
    /// PORT-ONLY (ED-NEW-lean-7). A one-shot high-resolution waitable timer (CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, Windows 10
    /// 1803 and later): it fires within about a millisecond of its due time without changing the system timer resolution
    /// (R-VIEW-10). A background thread waits on it and only posts the wake-up to main.
    /// </summary>
    private sealed class WakeTimer
    {
        private const uint HighResolution = 0x2, AllAccess = 0x1F0003, Infinite = 0xFFFFFFFF;
        private readonly nint handle;
        private readonly Action fired;

        private WakeTimer(nint handle, Action fired)
        {
            this.handle = handle;
            this.fired = fired;
            new Thread(Wait, 64 * 1024) { IsBackground = true, Name = "Marrow frame wake", Priority = ThreadPriority.AboveNormal }.Start();
        }

        public static WakeTimer? Create(Action fired)
        {
            if (!OperatingSystem.IsWindows()) return null;
            try
            {
                var handle = CreateWaitableTimerExW(0, null, HighResolution, AllAccess);
                return handle == 0 ? null : new WakeTimer(handle, fired);
            }
            catch (DllNotFoundException) { return null; }
            catch (EntryPointNotFoundException) { return null; }
        }

        /// <summary>Fires once, <paramref name="delay"/> from now (replacing any earlier arming).</summary>
        public void Arm(TimeSpan delay)
        {
            var due = -Math.Max(1L, delay.Ticks); // negative: relative, in 100 ns units (TimeSpan ticks)
            SetWaitableTimer(handle, ref due, 0, 0, 0, false);
        }

        private void Wait()
        {
            while (WaitForSingleObject(handle, Infinite) == 0) Dispatcher.UIThread.Post(fired, DispatcherPriority.Render);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint CreateWaitableTimerExW(nint attributes, string? name, uint flags, uint access);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWaitableTimer(nint timer, ref long dueTime, int period, nint completion, nint argument, [MarshalAs(UnmanagedType.Bool)] bool resume);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(nint handle, uint milliseconds);
    }
}

/// <summary>
/// PORT-ONLY. A frame source a test drives by hand (ED-098 (1)). <see cref="Now"/> starts at zero;
/// <see cref="Advance"/> moves it, then runs once each, in request order, the callbacks requested before the call,
/// each with the new time. A callback requested during an Advance waits for the next one.
/// ED-NEW-lean-7: with a <see cref="WakePeriod"/> set, a request held back to an instant waits for the first Advance at
/// which SwiftClock has reached it; without one (the default) it waits for the next Advance as any request.
/// </summary>
public sealed class ManualFrameSource : IWakeFrameSource
{
    private readonly object gate = new();
    private readonly List<Request> waiting = [];
    private long delivered;

    private sealed class Request(Action<TimeSpan> frame, ContinuousInstant? wake = null)
    {
        public Action<TimeSpan> Frame { get; } = frame;
        public ContinuousInstant? Wake { get; } = wake;
        public bool Withdrawn { get; set; }
    }

    /// <summary>ED-NEW-lean-7: the display's refresh period a test gives it; null (the default): it holds nothing back.</summary>
    public TimeSpan? WakePeriod { get; set; }

    /// <summary>ED-NEW-lean-7: the callbacks it has run, each one a frame request answered.</summary>
    public long Delivered { get { lock (gate) return delivered; } }

    private List<Request> running = [];

    /// <summary>Observers sample an injected frame after all its callbacks have updated their inputs.</summary>
    public event Action? FrameCompleted;

    /// <summary>The time of the last frame; zero before the first.</summary>
    public TimeSpan Now { get { lock (gate) return now; } }

    private TimeSpan now;

    /// <summary>The callbacks waiting for the next frame.</summary>
    public int Pending { get { lock (gate) return waiting.Count; } }

    public void RequestFrame(Action<TimeSpan> frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        lock (gate) waiting.Add(new Request(frame));
    }

    public void RequestFrameAt(ContinuousInstant wake, Action<TimeSpan> frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        lock (gate) waiting.Add(new Request(frame, WakePeriod is null ? null : wake));
    }

    public void CancelFrame(Action<TimeSpan> frame)
    {
        lock (gate)
        {
            var index = waiting.FindIndex(request => ReferenceEquals(request.Frame, frame));
            if (index >= 0)
            {
                waiting.RemoveAt(index);
                return;
            }
            var inFlight = running.Find(request => !request.Withdrawn && ReferenceEquals(request.Frame, frame));
            if (inFlight is not null) inFlight.Withdrawn = true;
        }
    }

    /// <summary>Moves the frame time forward by <paramref name="by"/> and delivers one frame.</summary>
    public void Advance(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);
        List<Request> due;
        TimeSpan time;
        lock (gate)
        {
            now += by;
            time = now;
            if (waiting.Exists(request => request.Wake is not null))
            {
                var clock = SwiftClock.Now;
                due = waiting.FindAll(request => request.Wake is not { } wake || wake <= clock);
                waiting.RemoveAll(request => request.Wake is not { } wake || wake <= clock);
            }
            else
            {
                due = [.. waiting];
                waiting.Clear();
            }
            running = due;
        }
        try
        {
            foreach (var request in due)
            {
                bool withdrawn;
                lock (gate)
                {
                    withdrawn = request.Withdrawn;
                    request.Withdrawn = true; // ran: a later CancelFrame no longer finds it
                    if (!withdrawn) delivered++;
                }
                if (!withdrawn) request.Frame(time);
            }
            FrameCompleted?.Invoke();
        }
        finally
        {
            lock (gate) running = [];
        }
    }
}

/// <summary>PORT-ONLY. The frame source nobody bound: Marrow.App sets the process default at start-up; tests override it.</summary>
file sealed class UnboundFrameSource : IFrameSource
{
    private const string Message = "FrameClock.Source is not bound: Marrow.App sets it at start-up; tests override it.";

    public void RequestFrame(Action<TimeSpan> frame) => throw new SwiftTrapException(Message);

    public void CancelFrame(Action<TimeSpan> frame) => throw new SwiftTrapException(Message);
}

/// <summary>
/// PORT-ONLY. <c>CAFrameRateRange(minimum:maximum:preferred:)</c> (StreamVeil.swift:178). Minimum and Maximum are
/// stored and not read (ED-117 (4)).
/// </summary>
public readonly record struct FrameRateRange(float Minimum, float Maximum, float? Preferred);

/// <summary>
/// PORT-ONLY. <c>.animation(minimumInterval:paused:)</c>, the schedule of a <c>TimelineView</c>
/// (HydraProgressBar.swift:142, :507, TimelineRows.swift:499, EffortSlider.swift:1050, AboutUpdateViews.swift:364,
/// :450). A null MinimumInterval updates at every frame.
/// </summary>
public sealed record AnimationTimelineSchedule(double? MinimumInterval = null, bool Paused = false, bool Ambient = false);

/// <summary>PORT-ONLY. The frame clock: the frame source seam and the frame-driven stand-ins (ED-083, ED-117).</summary>
public static class FrameClock
{
    /// <summary>The frame source. Marrow.App sets the process default (an <see cref="AvaloniaFrameSource"/>) at start-up.</summary>
    public static Ambient<IFrameSource> Source { get; } = new(new UnboundFrameSource());

    /// <summary>PORT-ONLY (ED-250): whether a frame source is bound (an app window's, or a test's), so a transition can run.</summary>
    public static bool IsBound => Source.Current is not UnboundFrameSource;

    // ED-117 (2): a span S from instant s is complete at a frame when (SwiftClock.Now - s) + 1 ms >= S, in ticks.
    internal static bool IsComplete(ContinuousInstant since, ContinuousInstant now, TimeSpan span) =>
        (now - since).Ticks + TimeSpan.TicksPerMillisecond >= span.Ticks;

    // ED-NEW-lean-7: the first instant at which IsComplete(since, now, span) holds; null for a span too long to wait for.
    internal static ContinuousInstant? DueAt(ContinuousInstant since, TimeSpan span) =>
        span <= TimeSpan.FromHours(1) ? since + span - TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond) : null;

    /// <summary>
    /// ED-NEW-lean-7: the instant a request for a draw due at <paramref name="due"/> may sleep until, 1.5 refreshes before
    /// it, when the source can hold requests back and that skips one refresh at least; null: ask for the next frame. Every
    /// frame skipped comes before the draw's due instant, so it would not have drawn: the draws are the same.
    /// </summary>
    internal static ContinuousInstant? WakeFor(IFrameSource source, ContinuousInstant? due)
    {
        if (due is not { } at || source is not IWakeFrameSource { WakePeriod: { } period } || period <= TimeSpan.Zero) return null;
        var lead = TimeSpan.FromTicks(period.Ticks * 3 / 2);
        return at - SwiftClock.Now > lead + period ? at - lead : null;
    }

    /// <summary>
    /// <c>try? await Task.sleep(for: .milliseconds(17))</c> or <c>(30)</c> standing for a frame tick
    /// (ThreadTimeline.swift:1168, :1211, :1223, HydraFusedPairIcon.swift:40; R-TASK-3, ED-117 (5)). Resumes at the
    /// first frame at which <paramref name="duration"/> since the call is complete, never on time alone. Cancellation
    /// is SwiftTask.TrySleep's: false at once, before or during the sleep; an already cancelled task asks for no frame.
    /// </summary>
    public static async Task<bool> TrySleep(TimeSpan duration)
    {
        if (SwiftTask.IsCancelled) return false;
        var source = Source.Current;
        var start = SwiftClock.Now;
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<TimeSpan>? frame = null;
        frame = _ =>
        {
            if (done.Task.IsCompleted) return; // cancelled meanwhile: the stale frame does nothing
            if (IsComplete(start, SwiftClock.Now, duration)) done.TrySetResult(true);
            else source.RequestFrame(frame!);
        };
        using var registration = SwiftTask.CancellationToken.Register(() => done.TrySetResult(false));
        if (done.Task.IsCompleted) return await done.Task;
        source.RequestFrame(frame);
        return await done.Task;
    }

    /// <summary><c>displayLink(target:selector:)</c> (StreamVeil.swift:174, :359): a link that ticks once added.</summary>
    public static DisplayLink DisplayLink(Action tick) => new(tick);

    /// <summary>
    /// <c>TimelineView(.animation(minimumInterval:paused:)) { context in ... }</c>: an <see cref="AnimationTimeline"/>
    /// that appears when the control is attached and disappears when it is detached, on every attach (R-VIEW-3).
    /// </summary>
    public static T TimelineView<T>(this T control, Func<AnimationTimelineSchedule> schedule, Action<DateTimeOffset> update)
        where T : Control => control.TimelineView(schedule, update, onRenderThread: false);

    /// <summary>
    /// The same, and with <paramref name="onRenderThread"/> a timeline whose update only changes what the control draws
    /// (a function of the time, ED-248 review 2): with the production frame source its frames come on the compositor's render
    /// thread at the display's refreshes (<see cref="RenderFrameSource"/>), so it never wakes main, and an update there
    /// invalidates the control's area through <see cref="RenderFrameSource.InvalidateTicking"/>. Appearing, the schedule and
    /// its pausing stay on main. Any other frame source (tests) delivers on main as before.
    /// </summary>
    public static T TimelineView<T>(this T control, Func<AnimationTimelineSchedule> schedule, Action<DateTimeOffset> update, bool onRenderThread, Func<bool>? ambientInteraction = null)
        where T : Control
    {
        var timeline = new AnimationTimeline(schedule, update, onRenderThread ? control : null, ambientInteraction);
        control.AttachedToVisualTree += (_, _) => timeline.Appear();
        control.DetachedFromVisualTree += (_, _) => timeline.Disappear();
        // \.accessibilityReduceMotion is an inherited Avalonia property (ED-084), which observation tracking does not see:
        // SwiftUI re-evaluates the schedule when that environment value changes, so the timeline re-reads it then too.
        control.PropertyChanged += (_, change) =>
        {
            if (change.Property == EnvironmentValues.AccessibilityReduceMotionProperty) timeline.Reschedule();
        };
        return control;
    }
}

/// <summary>
/// PORT-ONLY. <c>CADisplayLink</c> (StreamVeil.swift:174-179, :359-361, :382-383, :395-396; ED-117 (4)). Ticks on
/// main, outside any task. The first tick comes at the first frame after <see cref="Add"/>; without a preferred rate
/// it then ticks at every frame; with preferred rate p it ticks at a frame when 1/p since its last tick is complete.
/// <see cref="Invalidate"/> stops it, also inside its own tick, and withdraws its frame request.
/// </summary>
public sealed class DisplayLink
{
    private readonly Action tick;
    private readonly Action<TimeSpan> frame;
    private IFrameSource? source;
    private bool added;
    private bool invalidated;
    private bool outstanding;
    private bool asleep; // ED-NEW-lean-7: the outstanding request is held back to an instant
    private ContinuousInstant? lastTick;
    private FrameRateRange? preferredFrameRateRange;

    internal DisplayLink(Action tick)
    {
        this.tick = tick;
        frame = OnFrame;
    }

    /// <summary><c>preferredFrameRateRange</c> (StreamVeil.swift:178); only Preferred is read.</summary>
    public FrameRateRange? PreferredFrameRateRange
    {
        get => preferredFrameRateRange;
        set
        {
            preferredFrameRateRange = value;
            if (!outstanding || !asleep) return;
            // ED-NEW-lean-7: a request held back for the old rate is asked for at the next frame, as every request was.
            outstanding = false;
            source!.CancelFrame(frame);
            Request();
        }
    }

    /// <summary><c>add(to: .main, forMode: .common)</c> (StreamVeil.swift:179, :360).</summary>
    public void Add()
    {
        if (invalidated || added) return;
        added = true;
        source = FrameClock.Source.Current;
        Request();
    }

    /// <summary><c>invalidate()</c> (StreamVeil.swift:190, :382, :395).</summary>
    public void Invalidate()
    {
        if (invalidated) return;
        invalidated = true;
        added = false;
        if (!outstanding) return;
        outstanding = false;
        source!.CancelFrame(frame);
    }

    private void Request(ContinuousInstant? due = null)
    {
        if (outstanding) return; // ED-117 (7): one request at most
        outstanding = true;
        if (FrameClock.WakeFor(source!, due) is { } wake)
        {
            asleep = true;
            ((IWakeFrameSource)source!).RequestFrameAt(wake, frame);
        }
        else
        {
            asleep = false;
            source!.RequestFrame(frame);
        }
    }

    private void OnFrame(TimeSpan _)
    {
        outstanding = false;
        asleep = false;
        if (!added || invalidated) return;
        var now = SwiftClock.Now;
        var due = lastTick is not { } last
            || PreferredFrameRateRange?.Preferred is not { } preferred
            || FrameClock.IsComplete(last, now, SwiftTask.Seconds(1.0 / preferred));
        if (due)
        {
            lastTick = now;
            tick();
        }
        if (added && !invalidated)
            Request(lastTick is { } at && PreferredFrameRateRange?.Preferred is { } rate && rate > 0 ? FrameClock.DueAt(at, SwiftTask.Seconds(1.0 / rate)) : null);
    }
}

/// <summary>
/// PORT-ONLY. The clock of a <c>TimelineView(.animation(minimumInterval:paused:))</c> (ED-117 (3), (6), (8)).
/// <see cref="Appear"/> draws at once with SwiftDate.Now, paused or not. While appeared and not paused it asks for
/// every frame, and at a frame it updates when MinimumInterval is null or MinimumInterval since its last draw is
/// complete, passing SwiftDate.Now; intervals missed without a frame collapse into one update. The schedule is
/// re-read after observable state it read changes (as Tracked), and a change never draws by itself.
/// ED-248 review 2: a timeline made for a render host takes its frames on the compositor's render thread when the frame
/// source offers them (<see cref="IRenderFrameSourceProvider"/>); its frames there and its calls on main are serialised by a
/// lock, and its update runs on whichever thread drew. On a source that numbers the display's refreshes
/// (<see cref="IRefreshFrameSource"/>) an interval that is a whole number of refreshes is kept in refreshes.
/// </summary>
public sealed class AnimationTimeline
{
    private readonly Func<AnimationTimelineSchedule> schedule;
    private readonly Action<DateTimeOffset> update;
    private readonly Action<TimeSpan> frame;
    private readonly Control? renderHost;
    private readonly Func<bool>? ambientInteraction;
    private readonly object gate = new();
    private Tracked<AnimationTimelineSchedule>? tracked;
    private AnimationTimelineSchedule current = new();
    private IFrameSource? source;
    private RenderFrameSource? renderFrames; // the render-thread source this appearance made, disposed when it ends
    private bool appeared;
    private bool outstanding;
    private bool asleep; // ED-NEW-lean-7: the outstanding request is held back to an instant
    private ContinuousInstant lastDraw;
    private ContinuousInstant due; // ED-232: when the last due draw was due
    private long? refreshDue;      // ED-248 review 2: the refresh the next draw is due at, on a refresh-counting source

    public AnimationTimeline(Func<AnimationTimelineSchedule> schedule, Action<DateTimeOffset> update) : this(schedule, update, null) { }

    /// <summary>A timeline whose frames may come on <paramref name="renderHost"/>'s render thread (see the class note).</summary>
    public AnimationTimeline(Func<AnimationTimelineSchedule> schedule, Action<DateTimeOffset> update, Control? renderHost, Func<bool>? ambientInteraction = null)
    {
        this.schedule = schedule;
        this.update = update;
        this.renderHost = renderHost;
        this.ambientInteraction = ambientInteraction;
        frame = OnFrame;
    }

    /// <summary>The date of the last draw; null before the first <see cref="Appear"/>.</summary>
    public DateTimeOffset? Date { get { lock (gate) return date; } private set => date = value; }

    private DateTimeOffset? date;
    private DateTimeOffset dateOrigin;
    private TimeSpan decorationCarry;

    /// <summary>The frame source of the current appearance (tests: whether it is the render thread's).</summary>
    public IFrameSource? Source { get { lock (gate) return source; } }

    private bool Running => appeared && !current.Paused;

    /// <summary><c>onAppear</c>: draws at once with SwiftDate.Now, then follows the schedule.</summary>
    public void Appear()
    {
        lock (gate)
        {
            if (appeared) return;
            appeared = true;
            if (dateOrigin == default) dateOrigin = SwiftDate.Now;
            source = FrameClock.Source.Current;
            if (renderHost is not null && source is IRenderFrameSourceProvider provider && provider.RenderFrames(renderHost) is { } frames)
                source = renderFrames = frames;
            current = schedule();
            if (current.Ambient) renderFrames?.EnableAmbientMotion(ambientInteraction, decorationCarry);
            Draw();
            due = lastDraw; // ED-232: the first due time is the appearance
            refreshDue = null;
            tracked = new Tracked<AnimationTimelineSchedule>(schedule, Apply);
        }
    }

    /// <summary><c>onDisappear</c>: no more updates, and its frame request is withdrawn.</summary>
    public void Disappear()
    {
        lock (gate)
        {
            if (!appeared) return;
            appeared = false;
            tracked?.Dispose();
            tracked = null;
            if (outstanding)
            {
                outstanding = false;
                source!.CancelFrame(frame);
            }
            if (renderFrames?.DecorationTime is not null && date is { } shown) decorationCarry = shown - dateOrigin;
            renderFrames?.Dispose();
            renderFrames = null;
        }
    }

    /// <summary>
    /// A redraw caused by the parent: update at once with <see cref="Date"/>, never SwiftDate.Now. The last-draw instant
    /// and the frame requests stay as they are (ED-117 (8)).
    /// </summary>
    public void Redraw()
    {
        lock (gate)
        {
            if (date is { } at) update(at);
        }
    }

    /// <summary>Re-reads the schedule now (an environment value it read changed outside observation tracking).</summary>
    public void Reschedule()
    {
        lock (gate)
        {
            if (appeared) Apply(schedule());
        }
    }

    private void Apply(AnimationTimelineSchedule next)
    {
        lock (gate)
        {
            var changed = next != current;
            current = next;
            if (next.Ambient) renderFrames?.EnableAmbientMotion(ambientInteraction, decorationCarry);
            if (changed && outstanding && asleep)
            {
                // ED-NEW-lean-7: a request held back for the old schedule is withdrawn; running, it is asked for at the
                // next frame, as every request was before.
                outstanding = false;
                asleep = false;
                source!.CancelFrame(frame);
            }
            if (Running) Request(); // ED-117 (6): a resume asks at once, reusing a stale request
        }
    }

    private void Request(ContinuousInstant? due = null)
    {
        if (outstanding) return; // ED-117 (7): one request at most
        outstanding = true;
        if (FrameClock.WakeFor(source!, due) is { } wake)
        {
            asleep = true;
            ((IWakeFrameSource)source!).RequestFrameAt(wake, frame);
        }
        else
        {
            asleep = false;
            source!.RequestFrame(frame);
        }
    }

    private void Draw()
    {
        lastDraw = SwiftClock.Now;
        var at = renderFrames?.DecorationTime is { } decoration ? dateOrigin + decoration : SwiftDate.Now;
        Date = at;
        update(at);
    }

    /// <summary>
    /// ED-248 review 2: the whole number of refreshes <paramref name="interval"/> is (within 0.5 %), or null. A 1/60 s
    /// interval is 4 refreshes at 240 Hz (16.669 ms: 0.02 % longer), 2 at 120 Hz, 1 at 60 Hz; not a whole number at 144 Hz.
    /// </summary>
    internal static int? RefreshesPer(double interval, TimeSpan period)
    {
        var seconds = period.TotalSeconds;
        if (seconds <= 0) return null;
        var n = Math.Round(interval / seconds);
        return n >= 1 && Math.Abs(n * seconds - interval) <= 0.005 * interval ? (int)n : null;
    }

    /// <summary>
    /// ED-NEW-frame-pacing: an ambient render source delivers its frames only at the 60 Hz slots of the display's refreshes
    /// (AmbientRefreshPacer), the same refreshes for every ambient timeline; an interval of 1/60 s or less draws at each of
    /// them. Counting or timing the interval again here skipped slots (at 144 Hz a slot is 2 or 3 refreshes, never 1/60 s).
    /// </summary>
    private static bool PacedBySource(double interval) => interval <= AmbientMotionClock.FrameSeconds * 1.005;

    private void OnFrame(TimeSpan _)
    {
        lock (gate)
        {
            outstanding = false;
            asleep = false;
            if (!Running) return; // paused or gone: the stale request does nothing
            ContinuousInstant? next = null; // ED-NEW-lean-7: when the next draw is due, where the maths below says it
            if (current.MinimumInterval is not { } interval || current.Ambient && renderFrames?.DecorationTime is not null && (ambientInteraction?.Invoke() == true || PacedBySource(interval)))
            {
                Draw();
            }
            else if (source is IRefreshFrameSource { RefreshPeriod: { } period } refreshes && RefreshesPer(interval, period) is { } n)
            {
                // ED-248 review 2 (OD-0069): on a display whose refresh divides the interval, draws are due a whole number of
                // refreshes apart, counted, not timed. Timed against a 60 Hz grid a 240 Hz display (59.99 x 4) drifts by a
                // refresh every 25 s, and while the grid's due time sat within a frame's timing jitter of a refresh, frames
                // landed 3 and 5 refreshes apart (native run 2026-10-02: 5-15 % of intervals). The first draw of a run is
                // decided by time (ED-232), and the count starts there; more than one interval behind starts again.
                var refresh = refreshes.Refresh;
                if (refreshDue is not { } dueAt)
                {
                    var span = SwiftTask.Seconds(interval);
                    var now = SwiftClock.Now;
                    if (FrameClock.IsComplete(due, now, span))
                    {
                        Draw();
                        due = now;
                        refreshDue = refresh + n;
                    }
                }
                else if (refresh >= dueAt)
                {
                    Draw();
                    due = lastDraw;
                    refreshDue = refresh - dueAt >= n ? refresh + n : dueAt + n;
                }
            }
            else
            {
                // ED-232 (OD-0059, amends ED-117 (2)-(4)): draws are due one interval apart from the previous due time, not
                // from the moment the previous frame happened to run. A frame callback's timing jitters, and measuring from
                // it skipped every other frame of a ~60 Hz frame clock for a 1/60 interval (native run 2026-10-02: 33 updates
                // a second). Never faster than the interval on average; a timeline more than two intervals behind (a pause,
                // a slow frame) takes the frame it is at as its new start instead of catching up.
                var span = SwiftTask.Seconds(interval);
                var now = SwiftClock.Now;
                if (FrameClock.IsComplete(due, now, span))
                {
                    Draw();
                    due = now - due >= span + span ? now : due + span;
                }
                next = FrameClock.DueAt(due, span);
            }
            if (Running) Request(next);
        }
    }
}
