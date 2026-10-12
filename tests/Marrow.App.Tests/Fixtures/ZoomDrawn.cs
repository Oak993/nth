using Avalonia;
using Marrow.UI.Common;
using Xunit;

namespace Marrow.App.Tests.Fixtures;

// ED-NEW-textsize-2: a window's content is laid out in points and drawn at the app's zoom (AppZoom.BaseScale at 100 %), so its size on
// the window is its size in points times the scale, and layout rounding (whole points of the window under the zoom) can add up to a point.
public static class ZoomDrawn
{
    /// <summary>The size <paramref name="visual"/> is drawn at on <paramref name="window"/>, in the window's pixels.</summary>
    public static Size On(Visual visual, Visual window) =>
        new Rect(visual.Bounds.Size).TransformToAABB(visual.TransformToVisual(window)!.Value).Size;

    /// <summary>
    /// <paramref name="visual"/> is drawn over <paramref name="pixels"/> of <paramref name="window"/>: never less, and no more than the point of
    /// layout rounding drawn at the base scale.
    /// </summary>
    public static void Covers(Size pixels, Visual visual, Visual window)
    {
        var drawn = On(visual, window);
        Assert.InRange(drawn.Width, pixels.Width - 0.01, pixels.Width + AppZoom.BaseScale);
        Assert.InRange(drawn.Height, pixels.Height - 0.01, pixels.Height + AppZoom.BaseScale);
    }
}
