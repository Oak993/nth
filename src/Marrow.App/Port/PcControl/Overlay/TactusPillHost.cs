// PORT-ONLY: Tactus pill v2, the pill on the bands (OD-0145, ED-NEW-pcctl-23, ED-NEW-pcctl-24; DESIGN.md 4.5, 10.3 "Which
// monitor", "Drag", "Auto-dodge", 10.6 "One overlay per monitor", "Hit testing", "Glass, motion and one clock"). The glue
// between the facts and the screen: the facts become a view (PillStates), the view is shown by the pill in the band of
// the monitor PillScreens chooses, with its mirror in the band's shadow window when the hit test has one. Each window
// draws the layers PillPaintPlan gives it (region test: the input window draws the body and content at rest and nothing
// while it moves; the shadow window draws the shadow at rest and everything while it moves; the order allows a doubled
// frame, never a missing one). The pill's shape goes to the band's hit test at each change and when the motion settles,
// never per frame; the blur follows the window that draws the body, once per UI frame while it moves. A monitor change
// is a leave there and an arrive here, never a flight across; a drag's release picks the monitor under the pointer and
// the nearest anchor to the pill's projected centre; a dodge offsets the resting place. Everything outside runs through
// PillOverlays, IPillBandSurfaces and IPillGlass, so the rules run on fakes; the composition root wires the real ones.
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.UI.Port.PcControl;

namespace Marrow.App.Port.PcControl.Overlay;

/// <summary>One band's drawing surfaces: the input window's panel, the shadow window's (when there is one), and the blur under each.</summary>
public sealed record PillBandSurface(Panel Input, Panel? Shadow, IPillGlass InputGlass, IPillGlass? ShadowGlass);

/// <summary>The band surfaces by monitor, once the band's windows exist.</summary>
public interface IPillBandSurfaces
{
    /// <summary>The surfaces of the band on <paramref name="monitorId"/> (null before its windows open or after they closed).</summary>
    PillBandSurface? For(string monitorId);
}

/// <summary>The real surfaces: a panel in each of PillBandWindows' windows as they open, and their glass. Never run by tests.</summary>
public sealed class PillBandWindowSurfaces : IPillBandSurfaces
{
    private readonly Dictionary<string, PillBandSurface> surfaces = new(StringComparer.OrdinalIgnoreCase);

    public PillBandWindowSurfaces(PillBandWindows windows) => windows.Opened += Opened;

    public PillBandSurface? For(string monitorId) => surfaces.GetValueOrDefault(monitorId);

    private void Opened(OverlayMonitor monitor, PillOverlayWindow input, ShadowOverlayWindow? shadow)
    {
        var inputPanel = new Canvas();
        input.Content = inputPanel;
        Canvas? shadowPanel = null;
        if (shadow is not null)
        {
            shadowPanel = new Canvas();
            shadow.Content = shadowPanel;
        }
        var surface = new PillBandSurface(inputPanel, shadowPanel, PillGlass.For(input), shadow is null ? null : PillGlass.For(shadow));
        surfaces[monitor.Id] = surface;
        input.Closed += (_, _) =>
        {
            if (!surfaces.TryGetValue(monitor.Id, out var open) || open != surface) return;
            surfaces.Remove(monitor.Id);
            surface.InputGlass.Dispose();
            surface.ShadowGlass?.Dispose();
        };
    }
}

/// <summary>Which layers each of a band's windows draws (DESIGN.md 10.6 "Hit testing").</summary>
public static class PillPaintPlan
{
    public static (PillPaint Input, PillPaint Shadow) For(OverlayHitTestKind kind, bool moving) => kind switch
    {
        OverlayHitTestKind.Region => moving ? (PillPaint.None, PillPaint.All) : (PillPaint.Body | PillPaint.Content, PillPaint.Shadow),
        OverlayHitTestKind.Pixel => (PillPaint.Body | PillPaint.Content, PillPaint.Shadow),
        _ => (PillPaint.All, PillPaint.None),
    };
}

public sealed class TactusPillHost : IDisposable
{
    /// <summary>The host looks again this often while a band is open (time-based texts, the follow delay, band closes).</summary>
    public const double TickSeconds = 0.1;

    /// <summary>A settled motion is handed back this long after its last frame (the compositor draws it first).</summary>
    public const double SettleMargin = 0.02;

    private readonly PillOverlays overlays;
    private readonly IPillBandSurfaces surfaces;
    private readonly MonitorMap map;
    private readonly PillScreens screens;
    private readonly Func<OverlayMonitor, MonitorShade> shade;
    private readonly TimeProvider clock;
    private readonly IPillAnimator? animator;
    private readonly PillDriverKind? driver;
    private readonly Dictionary<string, Pair> pairs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> knownAsks = [];
    private PillFacts facts = new();
    private bool rowsOpen;
    private bool dragging;
    private bool reduceMotion;
    private bool highContrast;
    private PillDodge dodge = PillDodge.None;
    private ITimer? ticker;
    private bool disposed;

    public TactusPillHost(
        PillOverlays overlays,
        IPillBandSurfaces surfaces,
        MonitorMap map,
        PillScreens screens,
        Func<OverlayMonitor, MonitorShade>? shade = null,
        TimeProvider? clock = null,
        IPillAnimator? animator = null,
        PillDriverKind? driver = null)
    {
        this.overlays = overlays;
        this.surfaces = surfaces;
        this.map = map;
        this.screens = screens;
        this.shade = shade ?? (_ => MonitorShade.Clear);
        this.clock = clock ?? TimeProvider.System;
        this.animator = animator;
        this.driver = driver;
        overlays.HandlesChanged += Collect;
    }

    /// <summary>The anchor the pill rests at (Settings and the last drag; the caller saves it).</summary>
    public PillAnchor Anchor { get; set; } = PillAnchor.Centre;

    /// <summary>A press on the pill the engine acts on (Pause all, Stop, an app's row; a click while asks wait opens the card).</summary>
    public event Action<PillCommand>? Command;

    /// <summary>A release chose an anchor (the caller saves it).</summary>
    public event Action<PillAnchor>? AnchorChosen;

    /// <summary>A release chose another monitor (the caller saves it; PillScreens already follows it).</summary>
    public event Action<string>? MonitorDropped;

    /// <summary>The pill's monitor now (null while it shows nowhere).</summary>
    public string? MonitorId => pairs.Values.FirstOrDefault(p => p.Wanted)?.Monitor.Id;

    /// <summary>The pill pair on a monitor (tests and the card host).</summary>
    public ControlPill? Pill(string monitorId) => pairs.TryGetValue(monitorId, out var pair) ? pair.Input : null;

    /// <summary>The mirror in the shadow window on a monitor (tests).</summary>
    public ControlPill? Mirror(string monitorId) => pairs.TryGetValue(monitorId, out var pair) ? pair.Shadow : null;

    public bool ReduceMotion
    {
        get => reduceMotion;
        set
        {
            reduceMotion = value;
            foreach (var pair in pairs.Values) pair.Each(p => p.ReduceMotion = value);
        }
    }

    public bool HighContrast
    {
        get => highContrast;
        set
        {
            highContrast = value;
            foreach (var pair in pairs.Values) pair.Each(p => p.HighContrast = value);
        }
    }

    private double Now => clock.GetTimestamp() / (double)clock.TimestampFrequency;

    /// <summary>New facts from the engine: the view follows at once.</summary>
    public void Update(PillFacts next)
    {
        var fresh = next.Asks.Where(a => !knownAsks.Contains(a.Id)).ToList();
        knownAsks.Clear();
        foreach (var ask in next.Asks) knownAsks.Add(ask.Id);
        facts = next;
        Refresh();
        if (fresh.Count == 0) return;
        // a new ask: the bands go on top again and assistive technology hears it (assertive)
        overlays.Reassert();
        foreach (var pair in pairs.Values.Where(p => p.Wanted)) pair.Input.Alert(PillStates.NewAskNotice(fresh[^1]));
    }

    /// <summary>The planned dodge changed (DodgeWatch and PillDodgePlanner): the resting place moves by it.</summary>
    public void Dodge(PillDodge next)
    {
        if (next == dodge) return;
        dodge = next;
        if (dragging) return;
        foreach (var pair in pairs.Values.Where(p => p.Wanted)) pair.Input.TravelTo(RestAnchor(pair.Monitor), Factor(Anchor), at: Now);
    }

    /// <summary>The monitors changed: bands follow (PillOverlays), the pills move to the new work area and scale.</summary>
    public void Displays(MonitorChange change)
    {
        overlays.Displays(change);
        foreach (var moved in change.Changed)
        {
            if (!pairs.TryGetValue(moved.Id, out var pair)) continue;
            var oldScale = pair.Monitor.Scale;
            pair.Monitor = moved;
            var now = Now;
            pair.Each(p => p.Origin = Origin(moved));
            if (Math.Abs(oldScale - moved.Scale) > 1e-9) pair.Input.Rescaled(oldScale / moved.Scale, now);
            pair.Input.Place(RestAnchor(moved), Factor(Anchor), now);
        }
        Collect();
        Refresh();
    }

    /// <summary>Looks again now (the ticker calls it; tests may).</summary>
    public void Refresh()
    {
        if (disposed) return;
        var now = Now;
        var shown = Visible(facts, now);
        var card = facts.Card;
        var held = dragging || card || pairs.Values.Any(p => p.Wanted && p.Input.IsPointerOver);
        var screen = shown ? screens.Choose(map, shade, now, held) : PillScreen.Nowhere;
        var target = screen.Monitor;
        foreach (var pair in pairs.Values.ToList())
            if (pair.Wanted && !string.Equals(pair.Monitor.Id, target?.Id, StringComparison.OrdinalIgnoreCase)) Leave(pair, now);
        if (target is not null)
        {
            var view = PillStates.Compute(Merged(), now);
            if (PairFor(target, view, now) is { } pair)
            {
                pair.Show(view, now);
                if (!pair.Wanted) Arrive(pair, now);
            }
        }
        Tick();
    }

    // ---- the pairs ----

    private Pair? PairFor(OverlayMonitor monitor, PillView view, double now)
    {
        if (pairs.TryGetValue(monitor.Id, out var existing)) return existing;
        overlays.Show(monitor);
        if (surfaces.For(monitor.Id) is not { } surface) return null;
        var input = new ControlPill(driver, animator, clock) { ReduceMotion = reduceMotion, HighContrast = highContrast, Origin = Origin(monitor) };
        var shadow = surface.Shadow is null ? null : new ControlPill(driver, animator, clock, follower: true) { ReduceMotion = reduceMotion, HighContrast = highContrast, Origin = Origin(monitor) };
        var pair = new Pair(monitor, surface, input, shadow);
        pairs[monitor.Id] = pair;
        if (shadow is not null) surface.Shadow!.Children.Add(shadow);
        surface.Input.Children.Add(input);
        input.Moving += motion => Moved(pair, motion, held: false);
        input.Dragged += motion => Moved(pair, motion, held: true);
        input.Command += command => Pressed(command);
        input.DragStarted += () => DragStarted(pair);
        input.DragEnded += (at, anchor, speed) => Released(pair, at, anchor, speed);
        Paint(pair, moving: false);
        // the first frames: the content drawn once off screen and once invisibly in the band (DESIGN.md 10.6)
        var warm = new ControlPill(driver, animator, clock) { Origin = Origin(monitor) };
        warm.Show(view, now);
        warm.Place(RestAnchor(monitor), Factor(Anchor), now);
        warm.Present(now);
        PillWarmup.Draw(surface.Input, warm);
        return pair;
    }

    // The pill shows on its band: a pending close is cancelled and the band goes on top (only here, at an ask, when covered).
    private void Arrive(Pair pair, double now)
    {
        pair.Wanted = true;
        pair.Leaving = false;
        overlays.Show(pair.Monitor);
        pair.Input.Place(RestAnchor(pair.Monitor), Factor(Anchor), now);
        pair.Input.Arrive(now);
    }

    private void Leave(Pair pair, double now)
    {
        pair.Wanted = false;
        pair.Leaving = true;
        if (pair.Input.Motion.Presence == PillPresence.Gone)
        {
            overlays.Hide(pair.Monitor.Id);
            return;
        }
        pair.Input.Leave(now);
    }

    // A band closed (its pill hid there 2 s ago, or its monitor went): its pills go with it.
    private void Collect()
    {
        var open = overlays.Monitors.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, pair) in pairs.ToList())
        {
            if (open.Contains(id) && surfaces.For(id) == pair.Surface) continue;
            pairs.Remove(id);
            pair.Surface.Input.Children.Remove(pair.Input);
            if (pair.Shadow is not null) pair.Surface.Shadow?.Children.Remove(pair.Shadow);
            pair.Settle?.Dispose();
        }
    }

    private void Tick()
    {
        overlays.Tick();
        if (pairs.Count == 0)
        {
            ticker?.Dispose();
            ticker = null;
            return;
        }
        ticker ??= clock.CreateTimer(_ => Dispatcher.UIThread.Post(Refresh), null, TimeSpan.FromSeconds(TickSeconds), TimeSpan.FromSeconds(TickSeconds));
    }

    // ---- motion, layers, shape and glass ----

    private void Moved(Pair pair, PillMotion motion, bool held)
    {
        var now = Now;
        pair.Shadow?.Follow(motion, held, now);
        var moving = held || (!motion.AtRest && !motion.Done(now));
        Paint(pair, moving);
        Glass(pair, motion, now);
        if (held) return;
        overlays.Shape(pair.Monitor.Id, PillShape.Of(motion), now);
        pair.Settle?.Dispose();
        pair.Settle = null;
        if (!moving)
        {
            Settled(pair, motion);
            return;
        }
        var due = Math.Max(0, motion.DoneAt() - now) + SettleMargin;
        pair.Settle = clock.CreateTimer(_ => Dispatcher.UIThread.Post(() =>
        {
            if (pair.Input.Motion == motion) Settled(pair, motion);
        }), null, TimeSpan.FromSeconds(due), Timeout.InfiniteTimeSpan);
        if (pair.Frames) return;
        pair.Frames = true;
        Frame(pair);
    }

    // The motion's end: the input window takes the pill again, the hit test rests on the shape, a shrink waits no more.
    private void Settled(Pair pair, PillMotion motion)
    {
        var now = Now;
        pair.Settle?.Dispose();
        pair.Settle = null;
        // while held the pill stays moving (click-through, the drag's capture), however still the pointer is
        if (dragging) return;
        pair.Each(p => p.Settled(now));
        Paint(pair, moving: false);
        Glass(pair, motion, now);
        overlays.Shape(pair.Monitor.Id, PillShape.Of(PillMotion.Still(motion.Final, now)), now);
        overlays.MotionEnded(pair.Monitor.Id);
        if (pair.Leaving && motion.Final.Presence <= 0) overlays.Hide(pair.Monitor.Id);
    }

    // The blur follows the moving pill once per UI frame (the one part on the UI thread's clock, DESIGN.md 10.6).
    private void Frame(Pair pair)
    {
        if (TopLevel.GetTopLevel(pair.Surface.Input) is not { } top)
        {
            pair.Frames = false;
            return;
        }
        top.RequestAnimationFrame(_ =>
        {
            var now = Now;
            var motion = pair.Input.Motion;
            if (disposed || !pairs.ContainsKey(pair.Monitor.Id) || motion.AtRest || motion.Done(now))
            {
                pair.Frames = false;
                return;
            }
            Glass(pair, motion, now);
            Frame(pair);
        });
    }

    private void Paint(Pair pair, bool moving)
    {
        var (input, shadow) = PillPaintPlan.For(overlays.Kind, moving);
        if (pair.Shadow is null) input = PillPaint.All;
        // the window that takes over draws first: a doubled frame, never a missing one
        if (moving)
        {
            if (pair.Shadow is not null) pair.Shadow.Paint = shadow;
            pair.Input.Paint = input;
        }
        else
        {
            pair.Input.Paint = input;
            if (pair.Shadow is not null) pair.Shadow.Paint = shadow;
        }
    }

    private void Glass(Pair pair, PillMotion motion, double now)
    {
        var pose = motion.At(now);
        var shown = pose.Presence > 0 && pose.Opacity > 0;
        var drawn = pose.Drawn;
        var origin = Origin(pair.Monitor);
        var rect = new PillRect(drawn.X - origin.X, drawn.Y - origin.Y, drawn.Width, drawn.Height);
        var radius = Math.Min(PillLayout.Radius * pose.Scale, drawn.Height / 2);
        var inputDraws = pair.Input.Paint.HasFlag(PillPaint.Body);
        var withBody = inputDraws ? pair.Surface.InputGlass : pair.Surface.ShadowGlass;
        var other = inputDraws ? pair.Surface.ShadowGlass : pair.Surface.InputGlass;
        other?.Hide();
        if (withBody is null) return;
        if (!shown)
        {
            withBody.Hide();
            return;
        }
        withBody.Show(rect, radius);
        // without the blur the body draws the dense panel (ED-238)
        pair.Each(p => p.Glass = withBody.Available);
    }

    // ---- the press and the drag ----

    internal void Pressed(PillCommand command)
    {
        if (command.Kind == PillCommandKind.Click && facts.Asks.Count == 0)
        {
            rowsOpen = !rowsOpen;
            Refresh();
            return;
        }
        Command?.Invoke(command);
    }

    private void DragStarted(Pair pair)
    {
        dragging = true;
        rowsOpen = false;
        overlays.State(pair.Monitor.Id, PillBandState.Dragging);
        Refresh();
    }

    /// <summary>
    /// A release at <paramref name="at"/> (the monitor's DIP) with the pill's anchor there and its velocity: the monitor
    /// under the pointer, then the anchor nearest the projected centre on it (DESIGN.md 10.3 "Drag").
    /// </summary>
    internal void Released(Pair pair, PillVector at, PillVector anchor, PillVector speed)
    {
        dragging = false;
        overlays.State(pair.Monitor.Id, PillBandState.Pill);
        var from = pair.Monitor;
        var screen = ToScreen(from, at);
        var monitors = map.Monitors;
        if (monitors.Count == 0) return;
        var index = PillAnchors.MonitorAt(monitors.Select(m => new PillArea(m.WorkArea.X, m.WorkArea.Y, m.WorkArea.Width, m.WorkArea.Height)).ToList(), screen);
        var target = monitors[index];
        var drawn = pair.Input.Motion.At(Now).Drawn;
        var centre = ToScreen(from, new PillVector(drawn.X + (drawn.Width / 2), anchor.Y));
        var local = target.Band.ToDip((int)Math.Round(screen.X), (int)Math.Round(screen.Y));
        var centreLocal = target.Band.ToDip((int)Math.Round(centre.X), (int)Math.Round(centre.Y));
        var velocityX = speed.X * from.Scale / target.Scale;
        var width = pair.Input.Motion.Final.Width;
        var (_, chosen) = PillAnchors.Choose([target.WorkDip], local, centreLocal.X, velocityX, width);
        if (chosen != Anchor)
        {
            Anchor = chosen;
            AnchorChosen?.Invoke(chosen);
        }
        if (string.Equals(target.Id, from.Id, StringComparison.OrdinalIgnoreCase))
        {
            pair.Input.TravelTo(RestAnchor(target), Factor(chosen), speed, Now);
            Refresh();
            return;
        }
        // another monitor: it leaves here and arrives there (never a flight across)
        screens.Dropped(target.Id);
        MonitorDropped?.Invoke(target.Id);
        Refresh();
    }

    internal Pair? PairOn(string monitorId) => pairs.GetValueOrDefault(monitorId);

    // ---- places ----

    private PillVector RestAnchor(OverlayMonitor monitor)
    {
        var work = monitor.WorkDip;
        var position = PillAnchors.Position(Anchor, work, 0);
        return new PillVector(position.X + dodge.Dx, position.Y + dodge.Dy);
    }

    private static double Factor(PillAnchor anchor) => anchor switch
    {
        PillAnchor.Left => 0,
        PillAnchor.Right => 1,
        _ => 0.5,
    };

    private static PillVector Origin(OverlayMonitor monitor) => new(monitor.WorkDip.Left, monitor.WorkDip.Top);

    private static PillVector ToScreen(OverlayMonitor monitor, PillVector dip)
    {
        var band = monitor.Band;
        return new PillVector(band.ScreenOrigin.X + ((dip.X - band.DipOrigin.X) * band.Scale), band.ScreenOrigin.Y + ((dip.Y - band.DipOrigin.Y) * band.Scale));
    }

    private PillFacts Merged() => facts with { RowsOpen = rowsOpen, Dragging = dragging, ReduceMotion = reduceMotion };

    // Something to show: an app still working or paused, an ask, a stop in its 2 s, or a notice.
    private static bool Visible(PillFacts facts, double now) =>
        facts.Apps.Any(a => a.Phase != PillAppPhase.Stopped)
        || facts.Asks.Count > 0
        || (facts.Stopped is { } stop && now < stop.Since + PillStop.Seconds)
        || facts.Notices.Any(n => n.Live(now));

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        overlays.HandlesChanged -= Collect;
        ticker?.Dispose();
        foreach (var pair in pairs.Values) pair.Settle?.Dispose();
    }

    internal sealed class Pair(OverlayMonitor monitor, PillBandSurface surface, ControlPill input, ControlPill? shadow)
    {
        public OverlayMonitor Monitor { get; set; } = monitor;

        public PillBandSurface Surface { get; } = surface;

        public ControlPill Input { get; } = input;

        public ControlPill? Shadow { get; } = shadow;

        /// <summary>The pill is meant to show here (arrived or arriving; false once it leaves).</summary>
        public bool Wanted { get; set; }

        /// <summary>It left (or is leaving): its band hides once the leave is over.</summary>
        public bool Leaving { get; set; }

        public ITimer? Settle { get; set; }

        public bool Frames { get; set; }

        /// <summary>The mirror first, so its parts are measured before the leader's motion reaches it.</summary>
        public void Show(PillView view, double now)
        {
            Shadow?.Show(view, now);
            Input.Show(view, now);
        }

        public void Each(Action<ControlPill> action)
        {
            if (Shadow is not null) action(Shadow);
            action(Input);
        }
    }
}
