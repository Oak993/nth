// PORT-ONLY: Tactus pill v2, the auto-dodge loop (OD-0145, ED-NEW-pcctl-36; DESIGN.md 10.3 "Auto-dodge", "Dodge rules",
// 10.6 topmost re-assert). Window events (the hooks' thread) are stamped into DodgeWatch on the UI thread; when a read is
// due, what lies under the pill's resting place (CaptionButtons: the topmost window's caption buttons, a showing top
// taskbar) is read off the UI thread, planned (PillDodgePlanner.Plan) and fed to the planner's guards (no dodge while the
// pill is dragged, travelling or hovered, none within 500 ms of the last, a return only once the reason has been clear for
// 300 ms), and the dodge it gives goes to the pill host, which travels the pill there keeping its velocity. A plan the
// guards hold back is tried again every 100 ms until it is shown; nothing runs per frame. A reorder puts the band on top
// again; a foreground or display change also makes the full-screen watch read again. The pill's resting place changing (a
// width change, an anchor, a monitor) reads again at once. Everything outside runs through interfaces; tests use fakes.
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.App.Port.PcControl.Overlay;

public sealed class TactusDodge : IDisposable
{
    /// <summary>A plan the guards hold back is tried again this often (the debounce's 100 ms; never per frame).</summary>
    public const double RetrySeconds = DodgeWatch.DebounceSeconds;

    private readonly IDodgeEventSource events;
    private readonly CaptionButtons captions;
    private readonly TactusPillHost host;
    private readonly PillOverlays overlays;
    private readonly Func<double> now;
    private readonly Action<Action> post;
    private readonly Action<double, Action> after;
    private readonly Func<Func<DodgeInputs>, Task<DodgeInputs>> offUi;
    private readonly Action? invalidate;
    private readonly DodgeWatch watch = new();
    private readonly PillDodgePlanner planner = new();
    private readonly Lock readGate = new();
    private PillDodge planned = PillDodge.None;
    private (string Monitor, PillRect Place)? readFor;
    private bool checking;
    private bool retrying;
    private bool reading;
    private bool again;
    private bool disposed;

    /// <param name="events">The window events (EVENT_SYSTEM_FOREGROUND, LOCATIONCHANGE, REORDER, display changes).</param>
    /// <param name="captions">The reads under the pill.</param>
    /// <param name="host">The pill host: its resting place, whether it is dragged, travelling or hovered, and the dodge it shows.</param>
    /// <param name="overlays">The bands: their windows are never a reason, and a reorder puts them on top again.</param>
    /// <param name="now">The pill clock's seconds.</param>
    /// <param name="post">Runs on the UI thread.</param>
    /// <param name="after">Runs on the UI thread after so many seconds.</param>
    /// <param name="offUi">Runs a read off the UI thread (the default: the thread pool).</param>
    /// <param name="invalidate">A foreground or display change: the full-screen watch reads again.</param>
    public TactusDodge(IDodgeEventSource events, CaptionButtons captions, TactusPillHost host, PillOverlays overlays, Func<double> now,
        Action<Action> post, Action<double, Action> after, Func<Func<DodgeInputs>, Task<DodgeInputs>>? offUi = null, Action? invalidate = null)
    {
        this.events = events;
        this.captions = captions;
        this.host = host;
        this.overlays = overlays;
        this.now = now;
        this.post = post;
        this.after = after;
        this.offUi = offUi ?? (read => Task.Run(read));
        this.invalidate = invalidate;
        events.Raised += OnRaised;
        host.Refreshed += OnRefreshed;
    }

    /// <summary>The dodge the planner gives now (what the host shows).</summary>
    public PillDodge Current => planner.Current;

    /// <summary>The latest plan from a read (what the pill would show with no guard in the way).</summary>
    public PillDodge Planned => planned;

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        events.Raised -= OnRaised;
        host.Refreshed -= OnRefreshed;
    }

    // The hooks' thread: only stamp the kind and hand it to the UI thread.
    private void OnRaised(DodgeEventKind kind) => post(() => Event(kind));

    private void Event(DodgeEventKind kind)
    {
        if (disposed) return;
        watch.Event(kind, now());
        if (kind is DodgeEventKind.Foreground or DodgeEventKind.Display)
        {
            invalidate?.Invoke();
            // the full-screen shade may have changed: the pill looks again at once (another monitor, or the dot)
            host.Refresh();
        }
        Check();
    }

    // The resting place changed (the pill's width, its anchor or monitor, a card): read under the new place at once.
    private void OnRefreshed()
    {
        if (disposed || reading) return;
        var area = Area();
        if (area is null)
        {
            readFor = null;
            return;
        }
        if (readFor is { } last && Same(last, area.Value)) return;
        watch.Event(DodgeEventKind.Display, now());
        Check();
    }

    // A read when one is due; while a debounce runs, one check waits for its end.
    private void Check()
    {
        if (disposed) return;
        if (!watch.Due(now(), out var covered))
        {
            if (watch.Pending && !checking)
            {
                checking = true;
                after(DodgeWatch.DebounceSeconds, () =>
                {
                    checking = false;
                    Check();
                });
            }
            return;
        }
        if (covered && host.MonitorId is { } monitor) overlays.Covered(monitor);
        Read();
    }

    private void Read()
    {
        if (reading)
        {
            again = true;
            return;
        }
        if (host.DodgeArea() is not { } area)
        {
            readFor = null;
            planned = PillDodge.None;
            Apply();
            return;
        }
        reading = true;
        readFor = (area.Monitor.Id, area.Place);
        var own = overlays.Handles.ToList();
        var task = offUi(() =>
        {
            lock (readGate)
            {
                captions.Ignore.Clear();
                captions.Ignore.UnionWith(own);
                return captions.Read(area.Monitor, area.Place);
            }
        });
        task.ContinueWith(done => post(() => Read(done, area.Monitor, area.Place)), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void Read(Task<DodgeInputs> done, OverlayMonitor monitor, PillRect rest)
    {
        reading = false;
        if (disposed) return;
        // a read that failed (a hung window, a gone one) is no reason to dodge
        var inputs = done.IsCompletedSuccessfully ? done.Result : DodgeInputs.None;
        planned = CaptionButtons.Plan(rest, host.Anchor, monitor, inputs);
        Apply();
        if (again || (Area() is { } area && !Same((monitor.Id, rest), area)))
        {
            again = false;
            Read();
        }
    }

    // The plan through the guards; what they hold back is tried again shortly, never per frame.
    private void Apply()
    {
        if (disposed) return;
        var shown = planner.Update(now() * 1000, planned, host.Dragging || host.Travelling, host.Hovered);
        host.Dodge(shown);
        if (shown == planned || retrying) return;
        retrying = true;
        after(RetrySeconds, () =>
        {
            retrying = false;
            Apply();
        });
    }

    private (string Monitor, PillRect Place)? Area() => host.DodgeArea() is { } area ? (area.Monitor.Id, area.Place) : null;

    private static bool Same((string Monitor, PillRect Place) a, (string Monitor, PillRect Place) b) =>
        string.Equals(a.Monitor, b.Monitor, StringComparison.OrdinalIgnoreCase)
        && Math.Abs(a.Place.X - b.Place.X) < 0.5 && Math.Abs(a.Place.Y - b.Place.Y) < 0.5
        && Math.Abs(a.Place.Width - b.Place.Width) < 0.5 && Math.Abs(a.Place.Height - b.Place.Height) < 0.5;
}
