// PORT-ONLY: Tactus pill v2, the blur under the pill's glass (OD-0145, ED-NEW-pcctl-24; DESIGN.md 10.6 "Glass, motion and
// one clock": "Glass is the PopoverGlass mechanism (ED-357) ... One clipped layer follows the pill-or-card shape ... If the
// glass cannot attach, the opaque panel fallback applies (ED-238)"). This wraps PopoverGlass for an overlay band: one
// rounded layer, no tip, in the band window's physical pixels, with the dark cap (so white text stays legible over a
// bright desktop; the canvas's own blur has no cap). The shape is set from the UI thread: at rest, at a state change, on
// each drag frame, and while a render-thread motion runs, once per UI frame from the same curves (the one part of the
// pill on the UI thread's clock; the tint, rim and shadow drawn over it are on the compositor's, so the eye reads the edge
// from them; NP-16 measures the gap). When the glass cannot attach (headless, another composition mode, a failed call)
// `Available` is false and the body draws the dense panel instead.
using Avalonia;
using Avalonia.Controls;
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.UI.Port.PcControl;

/// <summary>The blur under one window's pill (the seam the overlay host and its tests use).</summary>
public interface IPillGlass : IDisposable
{
    /// <summary>True until the glass fails to attach or a composition call fails.</summary>
    bool Available { get; }

    string? Failure { get; }

    /// <summary>Shows the blur under <paramref name="drawn"/> (band DIP, every scale applied); false when the glass is not available.</summary>
    bool Show(PillRect drawn, double radius);

    /// <summary>Hides the blur (the pill gone, or this window not drawing the body now).</summary>
    void Hide();
}

public sealed class PillGlass : IPillGlass
{
    private readonly PopoverGlass glass;
    private readonly TopLevel window;
    private PopoverGlass.Shape? last;

    private PillGlass(TopLevel window)
    {
        this.window = window;
        glass = PopoverGlass.For(window, dark: true);
    }

    public static PillGlass For(TopLevel window) => new(window);

    /// <summary>True until the glass fails to attach or a composition call fails.</summary>
    public bool Available => glass.Failure is null;

    public string? Failure => glass.Failure;

    /// <summary>Shows the blur under <paramref name="drawn"/> (band DIP, every scale applied); false when the glass is not available.</summary>
    public bool Show(PillRect drawn, double radius)
    {
        var scale = window.RenderScaling;
        var shape = new PopoverGlass.Shape(
            new Rect(drawn.X * scale, drawn.Y * scale, Math.Max(0, drawn.Width * scale), Math.Max(0, drawn.Height * scale)),
            Math.Min(radius, Math.Min(drawn.Width, drawn.Height) / 2) * scale, default, 0, 0, 1);
        if (last == shape) return Available;
        last = shape;
        return glass.Update(shape);
    }

    /// <summary>Hides the blur (the pill gone, or this window not drawing the body now).</summary>
    public void Hide()
    {
        if (last is { Opacity: 0 }) return;
        var shape = (last ?? default) with { Opacity = 0 };
        last = shape;
        glass.Update(shape);
    }

    public void Dispose() => glass.Dispose();
}
