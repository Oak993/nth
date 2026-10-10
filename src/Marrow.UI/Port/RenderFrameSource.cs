// PORT-ONLY: frames on the compositor's render thread for a timeline that only changes what a control draws (ED-248 review 2).
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace Marrow.UI.Port;

// ED-248 review 2 (OD-0069, zero stutter). The effort track's liquid is a function of time alone, drawn by a custom draw
// operation on the render thread. Driven from main it cost every display refresh a frame on main (Avalonia's
// RequestAnimationFrame and, per ED-237, a compositor commit: about 2.2 kB of garbage a refresh, 240 a second) and every
// liquid frame a render pass on main and its batch (native run 2026-10-03: 1.28 MB a second in all, a gen1 collection of
// 21-27 ms every 20-30 s). And a frame decided on main reached the screen one refresh late whenever main ran late for the
// render thread's next tick. Here the frames come on the render thread itself: an invisible custom visual over the host,
// registered for the compositor's animation frames, delivers the requested callbacks at each tick (the display's refresh)
// under the compositor's lock, and a callback that changes what the host draws invalidates the visual's area, so the same
// tick redraws the host's retained drawing there (the custom operation reading the new time). Main is not involved at all
// while the timeline runs. Nothing is allocated per tick.

/// <summary>
/// PORT-ONLY (ED-248 review 2). A frame source whose frames come on the render thread of a control's compositor, once per
/// tick of its render loop (the display's refresh), counting the refreshes. Requests and withdrawals may come from main or
/// from inside a frame; the callbacks run outside its lock. <see cref="Dispose"/> (main) removes its visual.
/// </summary>
public sealed class RenderFrameSource : IRefreshFrameSource, IDisposable
{
    [ThreadStatic] private static RenderFrameSource? ticking;

    private readonly object sync = new();
    private readonly Control host;
    private readonly CompositionCustomVisual visual;
    private readonly Ticker ticker;
    private List<Action<TimeSpan>> pending = new(4), running = new(4);
    private readonly long started = Timestamp();
    private readonly DisplayRefreshCounter refreshCounter = new();
    private long refresh;
    private bool disposed;
    private AmbientMotionController? ambient;
    private TimeSpan decorationCarry;
    private static readonly ConditionalWeakTable<Control, RenderFrameSource> AmbientSources = new();
    /// <summary>The host's paused decoration phase, when it opted into the owner ambient policy.</summary>
    public static TimeSpan? DecorationTimeFor(Control host) => AmbientSources.TryGetValue(host, out var source) ? source.DecorationTime : null;
    public TimeSpan? DecorationTime => ambient is null ? null : decorationCarry + TimeSpan.FromSeconds(ambient.Clock.Time);
    private Func<bool>? ambientInteraction;

    internal void EnableAmbientMotion(Func<bool>? interaction, TimeSpan carry)
    {
        if (ambient is not null || AmbientWindowState.Acquire(host) is not { } window) return;
        ambient = new(window, host);
        ambientInteraction = interaction;
        decorationCarry = carry;
        AmbientSources.Remove(host); AmbientSources.Add(host, this);
        ambient.Bind(visual);
    }
    public static void WakeAmbientFor(Control host)
    {
        if (AmbientSources.TryGetValue(host, out var source)) source.visual.SendHandlerMessage(Ticker.Wake);
    }

    private RenderFrameSource(Control host, Compositor compositor)
    {
        this.host = host;
        ticker = new Ticker(this);
        visual = compositor.CreateCustomVisual(ticker);
        Resize();
        host.SizeChanged += OnSizeChanged;
        ElementComposition.SetElementChildVisual(host, visual);
        RefreshPeriod = PeriodSource();
    }

    /// <summary>The clock a tick reads (Stopwatch's; tests step it by hand).</summary>
    internal static Func<long> Timestamp { get; set; } = Stopwatch.GetTimestamp;

    /// <summary>Where a new source reads the display's refresh period (tests set a fixed one).</summary>
    internal static Func<TimeSpan?> PeriodSource { get; set; } = DisplayRefreshPeriod;

    internal static Func<long?> RefreshSource { get; set; } = DisplayRefreshNumber;

    /// <summary>A source over <paramref name="host"/>, or null when it has no compositor (not in a window's tree).</summary>
    public static RenderFrameSource? Create(Control host)
    {
        ArgumentNullException.ThrowIfNull(host);
        Dispatcher.UIThread.VerifyAccess();
        return ElementComposition.GetElementVisual(host)?.Compositor is { } compositor ? new RenderFrameSource(host, compositor) : null;
    }

    /// <summary>The display's refresh period (DWM's), read when the source is made; null when DWM does not say.</summary>
    public TimeSpan? RefreshPeriod { get; }

    /// <summary>The refresh of the tick being delivered: the count from an initial display vblank, using a fixed period. Repeated vblanks add nothing.</summary>
    public long Refresh => Volatile.Read(ref refresh);

    /// <summary>Ticks delivered so far (tests: frames really came on the render thread).</summary>
    public long Ticks { get; private set; }

    /// <summary>The managed thread the last tick ran on.</summary>
    public int TickThread { get; private set; }

    /// <summary>Whether the calling code runs inside one of this source's ticks.</summary>
    public bool IsTicking => ReferenceEquals(ticking, this);

    /// <summary>
    /// Inside a tick of a render frame source (on the render thread): marks that source's host area for redrawing in this
    /// same tick and returns true; anywhere else returns false and does nothing (the caller invalidates on main).
    /// </summary>
    public static bool InvalidateTicking()
    {
        if (ticking is not { } source) return false;
        source.ticker.InvalidateArea();
        return true;
    }

    public void RequestFrame(Action<TimeSpan> frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        bool wake;
        lock (sync)
        {
            if (disposed) return;
            pending.Add(frame);
            wake = !IsTicking; // inside a tick the ticker registers for the next one itself
        }
        if (wake) visual.SendHandlerMessage(Ticker.Wake);
    }

    public void CancelFrame(Action<TimeSpan> frame)
    {
        lock (sync)
        {
            for (var i = 0; i < pending.Count; i++)
            {
                if (!ReferenceEquals(pending[i], frame)) continue;
                pending.RemoveAt(i);
                return;
            }
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            pending.Clear();
        }
        host.SizeChanged -= OnSizeChanged;
        ambient?.Dispose(); ambient = null;
        if (AmbientSources.TryGetValue(host, out var source) && ReferenceEquals(source, this)) AmbientSources.Remove(host);
        if (ReferenceEquals(ElementComposition.GetElementChildVisual(host), visual)) ElementComposition.SetElementChildVisual(host, null);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e) => Resize();

    private void Resize() => visual.Size = new Vector2((float)host.Bounds.Width, (float)host.Bounds.Height);

    // ED-248 review 2: the render thread raised once to the highest priority of a normal process (the native runs under
    // other sessions' load showed its ticks running late by 2-5 ms, and a frame drawn late reaches the screen a refresh late)
    private static int raised;

    // render thread, under the compositor's lock: one tick
    private bool Tick()
    {
        if (Volatile.Read(ref raised) == 0 && !Dispatcher.UIThread.CheckAccess() && Interlocked.Exchange(ref raised, 1) == 0) RaiseRenderThread(); // (never main: a render loop that runs on main, as headless, keeps it as it is)
        var now = Timestamp();
        // DWM identifies the vblank itself: a late callback and its early successor cannot
        // double-count one refresh. Without DWM, use an absolute grid, never summed rounded gaps.
        var number = RefreshSource();
        if (number is { } actual && RefreshPeriod is { } displayPeriod)
            refresh = refreshCounter.ObserveVblank(actual, (long)Math.Round(displayPeriod.TotalSeconds * Stopwatch.Frequency), now);
        else if (RefreshPeriod is { } period)
            refresh = refreshCounter.Observe((long)Math.Floor((now - started) / (double)Stopwatch.Frequency / period.TotalSeconds));
        else refresh++;
        Ticks++;
        TickThread = Environment.CurrentManagedThreadId;
        List<Action<TimeSpan>> due;
        lock (sync)
        {
            if (disposed) return false;
            due = pending;
            pending = running;
            running = due;
        }
        var time = Stopwatch.GetElapsedTime(started, now);
        var previous = ticking;
        ticking = this;
        try
        {
            foreach (var frame in due)
            {
                // an exception must not leave this tick into the compositor's clock, where it would stop every animation
                // frame of the process for good (its list of clock items is cleared only after a tick completes): it is
                // thrown again on main, and the other callbacks run
                try { frame(time); }
                catch (Exception error)
                {
                    var captured = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error);
                    Dispatcher.UIThread.Post(captured.Throw);
                }
            }
        }
        finally
        {
            ticking = previous;
            due.Clear();
        }
        lock (sync) return !disposed && pending.Count > 0;
    }

    /// <summary>The priority the render thread was given (tests).</summary>
    internal static ThreadPriority? RaisedTo { get; private set; }

    private static void RaiseRenderThread()
    {
        try
        {
            Thread.CurrentThread.Priority = ThreadPriority.Highest;
            RaisedTo = Thread.CurrentThread.Priority;
        }
        catch (ThreadStateException) { }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)] // dwmapi.h packs it (pshpack1.h)
    private struct DwmTimingInfo
    {
        public uint Size;
        public uint RefreshNumerator, RefreshDenominator;
        public ulong RefreshPeriod;
        public uint ComposeNumerator, ComposeDenominator;
        public ulong VBlank, Refresh;
        public uint DxRefresh;
        public ulong Compose, Frame;
        public uint DxPresent;
        public ulong RefreshFrame, FrameSubmitted;
        public uint DxPresentSubmitted;
        public ulong FrameConfirmed;
        public uint DxPresentConfirmed;
        public ulong RefreshConfirmed;
        public uint DxRefreshConfirmed;
        public ulong FramesLate;
        public uint FramesOutstanding;
        public ulong FrameDisplayed, QpcFrameDisplayed, RefreshFrameDisplayed, FrameComplete, QpcFrameComplete, FramePending, QpcFramePending,
            FramesDisplayed, FramesComplete, FramesPending, FramesAvailable, FramesDropped, FramesMissed, RefreshNextDisplayed,
            RefreshNextPresented, RefreshesDisplayed, RefreshesPresented, RefreshStarted, PixelsReceived, PixelsDrawn, BuffersEmpty;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetCompositionTimingInfo(nint hwnd, ref DwmTimingInfo info);

    /// <summary>DWM's refresh period for the desktop, or null (no DWM, or it does not say).</summary>
    internal static TimeSpan? DisplayRefreshPeriod()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var info = new DwmTimingInfo { Size = (uint)Marshal.SizeOf<DwmTimingInfo>() };
            if (DwmGetCompositionTimingInfo(0, ref info) != 0 || info.RefreshPeriod == 0) return null;
            var seconds = info.RefreshPeriod / (double)Stopwatch.Frequency;
            return seconds is > 0.002 and < 0.1 ? TimeSpan.FromSeconds(seconds) : null;
        }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
    }

    internal static long? DisplayRefreshNumber()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var info = new DwmTimingInfo { Size = (uint)Marshal.SizeOf<DwmTimingInfo>() };
        return DwmGetCompositionTimingInfo(0, ref info) == 0
            ? info.RefreshPeriod > 0 ? unchecked((long)info.VBlank) : null : null;
    }

    private sealed class Ticker(RenderFrameSource owner) : CompositionCustomVisualHandler
    {
        internal static readonly object Wake = new();
        private bool registered;

        private void Request()
        {
            if (registered) return;
            if (owner.ambient is { } ambient && !ambient.Request(ambient.Now, owner.ambientInteraction?.Invoke() == true)) return;
            lock (owner.sync) { if (owner.disposed || owner.pending.Count == 0) return; }
            registered = true;
            RegisterForNextAnimationFrameUpdate();
        }

        public override void OnMessage(object message)
        {
            if (ReferenceEquals(message, Wake) || ReferenceEquals(message, owner.ambient))
            {
                if (owner.ambient is { } ambient) ambient.Sample(ambient.Now, owner.ambientInteraction?.Invoke() == true);
                Request();
            }
        }

        public override void OnAnimationFrameUpdate()
        {
            registered = false;
            if (owner.ambient is { } ambient)
            {
                ambient.Sample(ambient.Now, owner.ambientInteraction?.Invoke() == true);
                if (!ambient.Available) return;
            }
            if (owner.Tick()) Request();
        }

        public override void OnRender(ImmediateDrawingContext drawingContext) { } // draws nothing: it only marks the host's area

        internal void InvalidateArea() => Invalidate();
    }
}
