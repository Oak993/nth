// Source: DroppyCode/UI/Common/AppZoom.swift (B2 1fbf81e), :1-51.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

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

    /// <summary>
    /// PORT-ONLY (ED-NEW-textsize-2, owner 2026-10-11: "Try to match this text size that Claude uses"): the scale the whole
    /// app is drawn at under the zoom of Settings › Appearance, so 100 % shows the type and the controls at the size of the
    /// Claude desktop app on Windows (measured on its windows at 96 DPI: session sidebar items 17 px on a 36 px pitch, chat
    /// text 16 px, composer footer 15-16 px; nth's source sizes are 13 pt on a 28 pt pitch, 13 pt and 12.5 pt: 17 / 13 and
    /// 36 / 28 are both 1.3). The source's point sizes match the Mac, drawn at 2x on a Retina screen; a 96 DPI Windows
    /// screen showed them at the size of a dense 13 px web page. The zoom levels multiply it (125 % is 1.625).
    /// </summary>
    public const double BaseScale = 1.3;

    /// <summary>
    /// PORT-ONLY (ED-NEW-textsize-1): the scale <paramref name="visual"/> is drawn at by the app zoom: the product of the
    /// zoom layers it is inside (a window root's ZoomLayout, a popup's PopupZoom); 1 outside any.
    /// </summary>
    public static double ScaleOf(Visual? visual)
    {
        double scale = 1;
        for (var at = visual; at is not null; at = at.GetVisualParent())
        {
            if (at is ZoomLayout zoom) scale *= zoom.Scale;
            else if (at is PopupZoom popup) scale *= popup.Scale;
        }
        return scale;
    }

    /// <summary>PORT-ONLY (ED-NEW-textsize-1): the scale a window draws its root at (its ZoomLayout's); 1 for none.</summary>
    public static double ScaleOfWindow(TopLevel? window) =>
        window?.GetVisualDescendants().OfType<ZoomLayout>().FirstOrDefault() is { } zoom ? zoom.Scale : 1;

    /// <summary>
    /// PORT-ONLY (ED-NEW-textsize-1): a window of its own that is not a hosted root (an alert, a dialog, a guide panel)
    /// draws its content at <paramref name="scale"/> like the window it serves; the content itself at 100 %.
    /// </summary>
    public static Control Zoomed(Control content, double scale)
    {
        if (Math.Abs(scale - 1) < 1e-9) return content;
        return new AppZoomModifier(100) { BaseScale = scale }.Body(content);
    }

    /// <summary>PORT-ONLY (ED-NEW-textsize-1): the content <see cref="Zoomed"/> wrapped, or <paramref name="shown"/> itself.</summary>
    public static object? Unzoomed(object? shown)
    {
        if (shown is not ZoomLayout { Children.Count: 1 } zoom) return shown;
        var only = zoom.Children[0];
        return only is AppZoomModifier.ZoomScaler { Children.Count: 1 } scaler ? scaler.Children[0] : only;
    }
}

// :18-27
public sealed record AppZoomModifier(int Percent)
{
    /// <summary>PORT-ONLY (ED-NEW-textsize-2): a scale under the zoom level (<see cref="AppZoom.BaseScale"/> for a window
    /// root; 1, the source's, by default).</summary>
    public double BaseScale { get; init; } = 1;

    // :21-26
    public Control Body(Control content)
    {
        var scale = (double)AppZoom.Normalized(Percent) / 100 * BaseScale;
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
    internal sealed class ZoomScaler : Panel
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
