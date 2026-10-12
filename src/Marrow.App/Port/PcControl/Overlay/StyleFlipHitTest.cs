// PORT-ONLY: Tactus pill v2, the style-flip hit test, the last fallback (OD-0145, ED-NEW-pcctl-23, ED-NEW-pcctl-22 item 5;
// DESIGN.md 10.6 "Hit testing" 3). One layered band that is click-through (WS_EX_TRANSPARENT) except while a physical
// pointer record is inside the shape, tested against the closed-form shape at that record's time (inflated 2 DIP, as the
// region would be). The input reader hands each pointer record over; the flips run on a thread that does nothing else
// (setting another thread's window style is allowed), so a busy UI thread never delays them. Only the newest record
// matters, so the hand-over is one slot, not a queue. No flip while a mouse button is held in another app (it has the
// capture); hidden means click-through.
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.App.Port.PcControl.Overlay;

public sealed class StyleFlipHitTest : IOverlayHitTest
{
    private readonly IOverlayBandNative native;
    private readonly Thread? thread;
    private readonly AutoResetEvent wake = new(false);
    private readonly object slotGate = new();
    private readonly object flipGate = new();
    private volatile bool running = true;
    private volatile PillShape? shape;
    private volatile bool held;
    private OverlayBandGeometry band;
    private nint window;
    private (int X, int Y, double Seconds)? slot;
    private bool? takesInput;

    /// <param name="native">The band's Win32 calls.</param>
    /// <param name="runThread">False in tests: <see cref="Process"/> runs the flip on the caller's thread.</param>
    public StyleFlipHitTest(IOverlayBandNative native, bool runThread = true)
    {
        this.native = native;
        if (!runThread) return;
        thread = new Thread(Run) { IsBackground = true, Name = "Tactus overlay flips", Priority = ThreadPriority.AboveNormal };
        thread.Start();
    }

    public OverlayHitTestKind Kind => OverlayHitTestKind.StyleFlip;

    public uint InputStyles => OverlayStyles.Extended(OverlayRole.Input) | OverlayStyles.Layered | OverlayStyles.Transparent;

    public bool SeparateShadow => false;

    public bool GrabsInFlight => false;

    /// <summary>Whether the band takes input now (null before the first flip), for the tests.</summary>
    public bool? TakesInput => takesInput;

    /// <summary>Flips made so far, for the tests and the probe's p95.</summary>
    public int Flips { get; private set; }

    public void Attach(nint handle, OverlayBandGeometry geometry)
    {
        window = handle;
        band = geometry;
        takesInput = null;
        Flip(false);
    }

    public void Moved(OverlayBandGeometry geometry) => band = geometry;

    public void Rest(PillShape value) => shape = value;

    public void Moving(PillShape value) => shape = value;

    public void Hidden()
    {
        shape = null;
        lock (slotGate) slot = null;
        Flip(false);
    }

    public void ButtonHeldElsewhere(bool isHeld) => held = isHeld;

    public void Pointer(int x, int y, double seconds)
    {
        lock (slotGate) slot = (x, y, seconds);
        if (thread is null) return;
        wake.Set();
    }

    /// <summary>Tests the newest record against the shape at its time and flips when the answer changed.</summary>
    public void Process()
    {
        (int X, int Y, double Seconds)? record;
        lock (slotGate)
        {
            record = slot;
            slot = null;
        }
        if (record is not { } r || held || window == 0) return;
        var now = shape;
        var inside = now is not null && now.Contains(band.ToDip(r.X, r.Y), r.Seconds, PillShape.HitInflate);
        Flip(inside);
    }

    public void Dispose()
    {
        running = false;
        wake.Set();
        thread?.Join(TimeSpan.FromSeconds(1));
        wake.Dispose();
    }

    private void Flip(bool inside)
    {
        lock (flipGate)
        {
            if (window == 0 || takesInput == inside) return;
            takesInput = inside;
            Flips++;
            native.SetClickThrough(window, !inside);
        }
    }

    private void Run()
    {
        while (running)
        {
            wake.WaitOne();
            if (!running) return;
            Process();
        }
    }
}
