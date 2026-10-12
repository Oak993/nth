// PORT-ONLY: Tactus pill v2, which part of a band takes input (OD-0145, ED-NEW-pcctl-23, ED-NEW-pcctl-22 item 5; DESIGN.md
// 10.6 "Hit testing"). Only the visible pill and card take input; everything else in the band is the app beneath. Windows
// does the hit testing wherever it can, so mouse, pen and touch all work without the hooks. One interface, three
// implementations behind ONE switch (OverlayHitTestSwitch), so a failed native probe (NP-15) only flips it:
//   Region (the default): the input window is not layered; its window region is the resting shape inflated 2 DIP, set
//     only at rest and at a state change; empty while the shape moves (the grab in flight takes presses then). A region
//     also clips drawing, so the shadow and the band's rest are drawn by a click-through shadow window beside it.
//   Pixel: a layered, not click-through input window that hit-tests by alpha (if Avalonia's composited window honours it):
//     input exactly where it draws, no region, no hook path; the shadow stays in its own window so it never takes a click.
//   StyleFlip (the last fallback): one layered window that is click-through except while a physical pointer record is
//     inside the shape at that record's time; the flips run on a thread that does nothing else.
// In every mode no change is made while a mouse button is held in another app (that app has the capture); the change
// waits for the release. The Win32 calls sit behind IOverlayBandNative so every rule runs on a fake.
using Avalonia;
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.App.Port.PcControl.Overlay;

public enum OverlayHitTestKind
{
    Region,
    Pixel,
    StyleFlip,
}

/// <summary>The one switch that picks the hit test (ED-NEW-pcctl-22 item 5): Region unless NP-15 says otherwise.</summary>
public static class OverlayHitTestSwitch
{
    /// <summary>The environment variable that flips it for a probe or a field fix: region, pixel or flip.</summary>
    public const string Variable = "MARROW_TACTUS_HIT_TEST";

    /// <summary>The default until NP-15 runs (DESIGN.md 10.6: the region hit test).</summary>
    public const OverlayHitTestKind Default = OverlayHitTestKind.Region;

    public static OverlayHitTestKind Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "pixel" => OverlayHitTestKind.Pixel,
        "flip" or "styleflip" or "style-flip" => OverlayHitTestKind.StyleFlip,
        _ => Default,
    };

    public static OverlayHitTestKind Current() => Parse(Environment.GetEnvironmentVariable(Variable));
}

/// <summary>Where a band sits: its top-left on screen (physical pixels), its origin in the monitor's DIP, and the scale.</summary>
public readonly record struct OverlayBandGeometry(PixelPoint ScreenOrigin, Point DipOrigin, double Scale)
{
    /// <summary>A rect in the monitor's DIP as the band's client pixels, rounded outward.</summary>
    public PixelRect ToClient(PillRect rect)
    {
        var left = (int)Math.Floor((rect.X - DipOrigin.X) * Scale);
        var top = (int)Math.Floor((rect.Y - DipOrigin.Y) * Scale);
        var right = (int)Math.Ceiling((rect.Right - DipOrigin.X) * Scale);
        var bottom = (int)Math.Ceiling((rect.Bottom - DipOrigin.Y) * Scale);
        return new PixelRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    /// <summary>A screen point (physical pixels) in the monitor's DIP.</summary>
    public PillVector ToDip(int x, int y) => new(((x - ScreenOrigin.X) / Scale) + DipOrigin.X, ((y - ScreenOrigin.Y) / Scale) + DipOrigin.Y);
}

/// <summary>The Win32 calls a band makes for its hit test. Implementations other than fakes never run in tests.</summary>
public interface IOverlayBandNative
{
    /// <summary>Sets the window region to a round rect (client pixels, corner diameter <paramref name="corner"/>), or empty when null.</summary>
    void SetRegion(nint window, PixelRect? roundRect, int corner);

    /// <summary>Clears or sets WS_EX_TRANSPARENT; callable from any thread.</summary>
    void SetClickThrough(nint window, bool clickThrough);

    /// <summary>Moves and sizes the window (physical pixels) without activating it or changing its Z order.</summary>
    void SetBounds(nint window, PixelRect rect);
}

/// <summary>How one band takes input. Called on the UI thread except <see cref="Pointer"/> (the input reader's thread).</summary>
public interface IOverlayHitTest : IDisposable
{
    OverlayHitTestKind Kind { get; }

    /// <summary>The extended styles the input window adds in this mode (the role's base styles included).</summary>
    uint InputStyles { get; }

    /// <summary>Whether the shadow and the band's rest are drawn by a separate click-through window.</summary>
    bool SeparateShadow { get; }

    /// <summary>Whether a press on the moving shape needs the hook's grab in flight (only the region mode).</summary>
    bool GrabsInFlight { get; }

    /// <summary>The input window exists: <paramref name="window"/> is its handle, <paramref name="band"/> its place.</summary>
    void Attach(nint window, OverlayBandGeometry band);

    /// <summary>The band moved or its scale changed (a display change).</summary>
    void Moved(OverlayBandGeometry band);

    /// <summary>The shape rests (visually settled) or changed state: it takes input from now.</summary>
    void Rest(PillShape shape);

    /// <summary>The shape travels, morphs or dodges: the drawn shape is click-through (the region mode) or tracked.</summary>
    void Moving(PillShape shape);

    /// <summary>Nothing in the band takes input (the pill hides there).</summary>
    void Hidden();

    /// <summary>A mouse button went down or up in another app (that app has the capture): changes wait while one is held.</summary>
    void ButtonHeldElsewhere(bool held);

    /// <summary>A physical pointer record at <paramref name="x"/>, <paramref name="y"/> screen pixels, at pill-clock <paramref name="seconds"/>.</summary>
    void Pointer(int x, int y, double seconds);
}

/// <summary>Makes the hit test the switch names.</summary>
public static class OverlayHitTests
{
    public static IOverlayHitTest Create(OverlayHitTestKind kind, IOverlayBandNative native) => kind switch
    {
        OverlayHitTestKind.Pixel => new PixelHitTest(native),
        OverlayHitTestKind.StyleFlip => new StyleFlipHitTest(native),
        _ => new RegionHitTest(native),
    };
}
