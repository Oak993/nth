// Source: DroppyCode/UI/Common/AppZoom.swift (B2 1fbf81e), :1-51.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Marrow.UI.Common;

/// <summary>
/// The app-wide zoom: the window's content is laid out in the space the window has divided
/// by the scale, then drawn back up, so a bigger zoom shows less at once instead of clipping.
/// </summary>
// :5-16
public static class AppZoom
{
    // :6
    public static IReadOnlyList<int> Levels { get; } = [75, 90, 100, 110, 125, 150, 175, 200];

    // :8-10
    public static int Normalized(int percent) => Levels.Contains(percent) ? percent : 100;

    // :12-15
    public static int Next(int percent, bool increasing)
    {
        var current = Normalized(percent);
        if (increasing)
        {
            foreach (var level in Levels)
            {
                if (level > current)
                {
                    return level;
                }
            }

            return current;
        }

        for (var i = Levels.Count - 1; i >= 0; i--)
        {
            if (Levels[i] < current)
            {
                return Levels[i];
            }
        }

        return current;
    }
}

// :18-27
public sealed record AppZoomModifier(int Percent)
{
    // :21-26
    public Control Body(Control content)
    {
        var scale = (double)AppZoom.Normalized(Percent) / 100;
        // The body evaluated again for a new percent (the setting changed, WindowManager.swift:175): SwiftUI keeps the
        // same ZoomLayout and content (structural identity) and only the scale changes, so the content already in a
        // ZoomLayout keeps it and is never detached and attached again.
        if (content.Parent is ZoomLayout zoom)
        {
            Scale(content, scale);
            zoom.Scale = scale;
            return zoom;
        }
        if (content.Parent is ZoomScaler hostedScaler && hostedScaler.Parent is ZoomLayout hostedZoom)
        {
            Scale(hostedScaler, scale);
            hostedZoom.Scale = scale;
            return hostedZoom;
        }
        // :24 scaleEffect is an outer effect, so a transform the content already carries (and its origin) stays
        // in force: the scale goes on a wrapper around such content instead of replacing its transform.
        if (content.RenderTransform is { } own && !own.Value.IsIdentity)
        {
            var scaler = new ZoomScaler { Children = { content } };
            Scale(scaler, scale);
            return new ZoomLayout(scale) { Children = { scaler } };
        }
        Scale(content, scale);
        return new ZoomLayout(scale) { Children = { content } };
    }

    private static void Scale(Control target, double scale)
    {
        target.RenderTransform = new ScaleTransform(scale, scale);
        target.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
    }

    // Carries the zoom scale for content that already has a render transform of its own (:24).
    private sealed class ZoomScaler : Panel
    {
    }
}

/// <summary>
/// Proposes the unscaled viewport so forms reflow and keep their scrollbars inside the
/// window; a bare scale transform would clip the enlarged content.
/// </summary>
// :29-51
public sealed class ZoomLayout : Panel
{
    private double scale;

    public ZoomLayout(double scale)
    {
        this.scale = scale;
    }

    // :30 let scale, set again when AppZoomModifier's body is evaluated for a new percent (:21-26).
    internal double Scale
    {
        get => scale;
        set
        {
            scale = value;
            InvalidateMeasure();
        }
    }

    // :34-38
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0)
        {
            return new Size(0, 0); // :35
        }

        var content = Children[0];
        content.Measure(Unscaled(availableSize)); // :36, :48-50
        // DEVIATION ED-150: Avalonia clamps the child's and this panel's DesiredSize to the constraint; AppZoom.swift:36-37 does not clamp.
        return new Size(content.DesiredSize.Width * scale, content.DesiredSize.Height * scale); // :37
    }

    // :40-46
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0)
        {
            return finalSize;
        }

        var content = Children[0];
        content.Arrange(new Rect(0, 0, finalSize.Width / scale, finalSize.Height / scale)); // :41-45, :48-50
        return finalSize;
    }

    // :48-50
    private Size Unscaled(Size proposal) =>
        new(proposal.Width / scale, proposal.Height / scale);
}
