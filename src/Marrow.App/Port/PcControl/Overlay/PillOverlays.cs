// PORT-ONLY: Tactus pill v2, one overlay band per monitor (OD-0145, ED-NEW-pcctl-23; DESIGN.md 10.6 "One overlay per
// monitor", "Hit testing", 10.3 "Grab in flight", "Displays"). A band is made on demand on a monitor that shows Tactus UI
// and closed 2 s after the pill hides there. Its width is the work area's full width; its height is what the state needs
// (120 DIP for the pill, 208 while dragging, the card's height plus 160), changed with one window call at a state change:
// before the motion starts when growing, after it ends when shrinking, never per frame. The shape the pill view publishes
// goes to the band's hit test (rest: input on the shape; moving: click-through) and, for the region test, to the hook's
// grab zone so a press on the moving pill grabs it. The topmost order is set again only when the pill shows, when an ask
// arrives and when another topmost window covers it. A display change moves the band to the new work area and scale; an
// unplugged monitor's band closes. Everything goes through IPillBands and IOverlayBandNative, so tests run on fakes.
using Avalonia;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Services.Port.PcControl;

namespace Marrow.App.Port.PcControl.Overlay;

/// <summary>One monitor's band windows: the input window and, when the hit test needs one, its shadow window.</summary>
public interface IPillBand
{
    /// <summary>The input window's handle (0 until it has one).</summary>
    nint Input { get; }

    /// <summary>The shadow window's handle (0 when the hit test draws everything in the input window).</summary>
    nint Shadow { get; }

    /// <summary>Puts the band's windows on top again, without activating them.</summary>
    void Reassert();

    void Close();
}

/// <summary>Opens a band's windows at a place (physical pixels), shown without activation.</summary>
public interface IPillBands
{
    IPillBand Open(OverlayMonitor monitor, IOverlayHitTest hitTest, PixelRect rect);
}

public sealed class PillOverlays : IDisposable
{
    /// <summary>A band closes this long after the pill hid on its monitor.</summary>
    public const double CloseAfterSeconds = 2;

    private readonly IPillBands bands;
    private readonly IOverlayBandNative native;
    private readonly Func<OverlayHitTestKind, IOverlayHitTest> makeHitTest;
    private readonly Func<double> seconds;
    private readonly PcGrabZone? grab;
    private readonly Func<uint> tick;
    private readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    private bool heldElsewhere;

    public PillOverlays(
        IPillBands bands,
        IOverlayBandNative native,
        OverlayHitTestKind kind,
        Func<double> seconds,
        PcGrabZone? grab = null,
        Func<uint>? tick = null,
        Func<OverlayHitTestKind, IOverlayHitTest>? makeHitTest = null)
    {
        this.bands = bands;
        this.native = native;
        Kind = kind;
        this.seconds = seconds;
        this.grab = grab;
        this.tick = tick ?? (() => unchecked((uint)Environment.TickCount));
        this.makeHitTest = makeHitTest ?? (k => OverlayHitTests.Create(k, native));
    }

    /// <summary>The hit test in force (OverlayHitTestSwitch).</summary>
    public OverlayHitTestKind Kind { get; }

    /// <summary>Every band window's handle: never a dodge reason, never a full-screen window, never listed to the model.</summary>
    public IReadOnlyCollection<nint> Handles => entries.Values.SelectMany(e => new[] { e.Band.Input, e.Band.Shadow }).Where(h => h != 0).ToList();

    /// <summary>Raised when a band opens or closes (the handles changed).</summary>
    public event Action? HandlesChanged;

    /// <summary>The monitors that have a band now.</summary>
    public IReadOnlyCollection<string> Monitors => entries.Keys.ToList();

    /// <summary>The band's height on <paramref name="monitorId"/> in DIP (0 when it has none), for the tests.</summary>
    public double Height(string monitorId) => entries.TryGetValue(monitorId, out var entry) ? entry.Height : 0;

    /// <summary>The hit test of the band on <paramref name="monitorId"/>, for the tests and the pill view.</summary>
    public IOverlayHitTest? HitTest(string monitorId) => entries.TryGetValue(monitorId, out var entry) ? entry.HitTest : null;

    /// <summary>
    /// The pill shows on <paramref name="monitor"/> (or keeps showing): opens its band if it has none, cancels a pending
    /// close and puts it on top. <paramref name="state"/> sets the first height.
    /// </summary>
    public void Show(OverlayMonitor monitor, PillBandState state = PillBandState.Pill, double cardHeight = 0, double pillBottom = 0)
    {
        if (entries.TryGetValue(monitor.Id, out var entry))
        {
            entry.HiddenAt = null;
            entry.Band.Reassert();
            return;
        }
        var height = PillBand.Height(state, cardHeight, pillBottom);
        var hitTest = makeHitTest(Kind);
        var band = bands.Open(monitor, hitTest, Place(monitor, height));
        entry = new Entry(monitor, band, hitTest) { Height = height };
        entries[monitor.Id] = entry;
        if (band.Input != 0) hitTest.Attach(band.Input, monitor.Band);
        hitTest.ButtonHeldElsewhere(heldElsewhere);
        band.Reassert();
        HandlesChanged?.Invoke();
    }

    /// <summary>
    /// The pill's state on <paramref name="monitorId"/> changed: a growing band is resized now (before the motion starts),
    /// a shrinking one when <see cref="MotionEnded"/> says the motion ended. Returns which it was.
    /// </summary>
    public PillBandResize State(string monitorId, PillBandState state, double cardHeight = 0, double pillBottom = 0)
    {
        if (!entries.TryGetValue(monitorId, out var entry)) return PillBandResize.None;
        var next = PillBand.Height(state, cardHeight, pillBottom);
        var plan = PillBand.Plan(entry.PendingHeight ?? entry.Height, next);
        switch (plan)
        {
            case PillBandResize.BeforeMotion:
                entry.PendingHeight = null;
                Resize(entry, next);
                break;
            case PillBandResize.AfterMotion:
                entry.PendingHeight = next;
                break;
            default:
                if (entry.PendingHeight is not null && next == entry.Height) entry.PendingHeight = null;
                break;
        }
        return plan;
    }

    /// <summary>The motion that a shrink waited for ended (the shape is visually settled): the band shrinks now.</summary>
    public void MotionEnded(string monitorId)
    {
        if (!entries.TryGetValue(monitorId, out var entry) || entry.PendingHeight is not { } height) return;
        entry.PendingHeight = null;
        Resize(entry, height);
    }

    /// <summary>
    /// The pill view's shape on <paramref name="monitorId"/> at <paramref name="now"/>: at rest it takes input; moving it
    /// is click-through, and on the pill's own monitor (<paramref name="owner"/>) a press on it is the grab in flight.
    /// Called at a state change, at a motion's start and when it settles, never per frame.
    /// </summary>
    public void Shape(string monitorId, PillShape shape, double now, bool owner = true)
    {
        if (!entries.TryGetValue(monitorId, out var entry)) return;
        var resting = shape.AtRest || shape.Settled(now);
        if (resting) entry.HitTest.Rest(shape);
        else entry.HitTest.Moving(shape);
        if (!owner || grab is null) return;
        if (resting || !entry.HitTest.GrabsInFlight) grab.Clear();
        else grab.Publish(shape, entry.Monitor.Bounds, entry.Monitor.Scale, now, tick());
    }

    /// <summary>The pill hid on <paramref name="monitorId"/>: nothing there takes input, and the band closes 2 s later.</summary>
    public void Hide(string monitorId)
    {
        if (!entries.TryGetValue(monitorId, out var entry)) return;
        entry.HitTest.Hidden();
        entry.HiddenAt ??= seconds();
        grab?.Clear();
    }

    /// <summary>Closes bands hidden for 2 s. Returns true while any band waits to close.</summary>
    public bool Tick()
    {
        var now = seconds();
        foreach (var (id, entry) in entries.ToList())
        {
            if (entry.HiddenAt is not { } at || now - at < CloseAfterSeconds) continue;
            Close(id, entry);
        }
        return entries.Values.Any(e => e.HiddenAt is not null);
    }

    /// <summary>An ask arrived (or the pill shows again): every visible band goes on top again.</summary>
    public void Reassert()
    {
        foreach (var entry in entries.Values.Where(e => e.HiddenAt is null)) entry.Band.Reassert();
    }

    /// <summary>Another topmost window covered the band on <paramref name="monitorId"/> (EVENT_OBJECT_REORDER).</summary>
    public void Covered(string monitorId)
    {
        if (entries.TryGetValue(monitorId, out var entry) && entry.HiddenAt is null) entry.Band.Reassert();
    }

    /// <summary>A mouse button is held (or released) in another app: no hit-test change while it is held.</summary>
    public void ButtonHeldElsewhere(bool held)
    {
        heldElsewhere = held;
        foreach (var entry in entries.Values) entry.HitTest.ButtonHeldElsewhere(held);
    }

    /// <summary>A physical pointer record (the input reader's thread), for the style-flip test.</summary>
    public void Pointer(int x, int y, double atSeconds)
    {
        if (Kind != OverlayHitTestKind.StyleFlip) return;
        foreach (var entry in entries.Values.ToArray()) entry.HitTest.Pointer(x, y, atSeconds);
    }

    /// <summary>The monitors changed: bands follow their monitor's new work area and scale; an unplugged monitor's band closes.</summary>
    public void Displays(MonitorChange change)
    {
        foreach (var gone in change.Removed)
            if (entries.TryGetValue(gone.Id, out var entry)) Close(gone.Id, entry);
        foreach (var moved in change.Changed)
        {
            if (!entries.TryGetValue(moved.Id, out var entry)) continue;
            entry.Monitor = moved;
            entry.HitTest.Moved(moved.Band);
            if (entry.Band.Input != 0) native.SetBounds(entry.Band.Input, Place(moved, entry.Height));
            if (entry.Band.Shadow != 0) native.SetBounds(entry.Band.Shadow, Place(moved, entry.Height));
        }
    }

    public void Dispose()
    {
        foreach (var (id, entry) in entries.ToList()) Close(id, entry);
    }

    private static PixelRect Place(OverlayMonitor monitor, double height) => monitor.ToScreen(PillBand.Rect(monitor.WorkDip, height));

    private void Resize(Entry entry, double height)
    {
        if (height == entry.Height) return;
        entry.Height = height;
        var rect = Place(entry.Monitor, height);
        if (entry.Band.Input != 0) native.SetBounds(entry.Band.Input, rect);
        if (entry.Band.Shadow != 0) native.SetBounds(entry.Band.Shadow, rect);
    }

    private void Close(string id, Entry entry)
    {
        entries.Remove(id);
        entry.HitTest.Hidden();
        entry.Band.Close();
        entry.HitTest.Dispose();
        HandlesChanged?.Invoke();
    }

    private sealed class Entry(OverlayMonitor monitor, IPillBand band, IOverlayHitTest hitTest)
    {
        public OverlayMonitor Monitor { get; set; } = monitor;
        public IPillBand Band { get; } = band;
        public IOverlayHitTest HitTest { get; } = hitTest;
        public double Height { get; set; }
        public double? PendingHeight { get; set; }
        public double? HiddenAt { get; set; }
    }
}

/// <summary>The real bands: a PillOverlayWindow and, when the hit test wants one, a ShadowOverlayWindow. Never run by tests.</summary>
public sealed class PillBandWindows(IOverlayNative? native = null) : IPillBands
{
    /// <summary>Raised with each new band, so the pill view puts its content in it.</summary>
    public event Action<OverlayMonitor, PillOverlayWindow, ShadowOverlayWindow?>? Opened;

    public IPillBand Open(OverlayMonitor monitor, IOverlayHitTest hitTest, PixelRect rect)
    {
        var input = new PillOverlayWindow(hitTest, native);
        var shadow = hitTest.SeparateShadow ? new ShadowOverlayWindow(native) : null;
        Opened?.Invoke(monitor, input, shadow);
        foreach (var window in new TactusOverlayWindow?[] { shadow, input })
        {
            if (window is null) continue;
            window.Position = rect.Position;
            window.Width = rect.Width / monitor.Scale;
            window.Height = rect.Height / monitor.Scale;
            window.Show();
        }
        return new Band(input, shadow);
    }

    private sealed class Band(PillOverlayWindow input, ShadowOverlayWindow? shadow) : IPillBand
    {
        public nint Input => input.Handle;

        public nint Shadow => shadow?.Handle ?? 0;

        public void Reassert()
        {
            shadow?.Reassert();
            input.Reassert();
        }

        public void Close()
        {
            input.Close();
            shadow?.Close();
        }
    }
}
