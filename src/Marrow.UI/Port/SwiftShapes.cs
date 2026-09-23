// PORT-ONLY: SwiftUI's RoundedRectangle(cornerRadius:style:) and Capsule(style:) take a RoundedCornerStyle that Avalonia
// has no counterpart for; this is the one shared helper every `.continuous` site goes through (ED-181).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Marrow.UI.Port;

// ED-181 (orchestrator ruling on the UI.Chrome.Chrome stop #3): the corner style is a parameter of every rounded shape,
// and the shape keeps it (CornerStyleProperty) for the curve that is still to come.
// MARROW_DEFERRED ED-181 (tracked incomplete work, oracle-backed): `.continuous` corners are drawn with circular arcs at
// the exact corner radius, the same arcs as `.circular`, until the continuous-curvature corner lands. Every shape this
// helper draws, as a Border's corners or as a Geometry, goes through the arcs below, so that work changes only this file.

/// <summary>SwiftUI <c>RoundedCornerStyle</c>: <c>.circular</c> and <c>.continuous</c>.</summary>
public enum RoundedCornerStyle
{
    Circular,
    Continuous,
}

/// <summary>SwiftUI <c>RoundedRectangle</c> and <c>Capsule</c> with their corner style (ED-181).</summary>
public static class SwiftShapes
{
    /// <summary>The corner style a shape was drawn with; null on a view that is not one of these shapes.</summary>
    public static readonly AttachedProperty<RoundedCornerStyle?> CornerStyleProperty =
        AvaloniaProperty.RegisterAttached<Control, RoundedCornerStyle?>("CornerStyle", typeof(SwiftShapes));

    public static RoundedCornerStyle? GetCornerStyle(Control control) => control.GetValue(CornerStyleProperty);

    /// <summary>
    /// <c>RoundedRectangle(cornerRadius:style:)</c> as the corners of a Border that fills its bounds: its Background is
    /// the shape's <c>.fill</c>, its border the shape's <c>.strokeBorder</c> (a Border draws its border inside its
    /// bounds, the inner stroke strokeBorder draws).
    /// </summary>
    public static T RoundedRectangle<T>(this T border, double cornerRadius, RoundedCornerStyle style) where T : Border
    {
        border.SetValue(CornerStyleProperty, style);
        border.CornerRadius = new CornerRadius(cornerRadius); // MARROW_DEFERRED ED-181: circular arcs for both styles
        return border;
    }

    /// <summary><c>Capsule(style:)</c> as the corners of a Border: rounded by half its shorter side at every size.</summary>
    public static T Capsule<T>(this T border, RoundedCornerStyle style) where T : Border
    {
        border.SetValue(CornerStyleProperty, style);
        border.CornerRadius = new CornerRadius(CapsuleCornerRadius(border.Bounds.Size));
        border.SizeChanged += (_, e) => border.CornerRadius = new CornerRadius(CapsuleCornerRadius(e.NewSize)); // MARROW_DEFERRED ED-181
        return border;
    }

    /// <summary>A capsule's corner radius: half its shorter side.</summary>
    public static double CapsuleCornerRadius(Size size) => Math.Min(size.Width, size.Height) / 2;

    /// <summary><c>RoundedRectangle(cornerRadius:style:)</c> as a path in <paramref name="rect"/>.</summary>
    public static Geometry RoundedRectangleGeometry(Rect rect, double cornerRadius, RoundedCornerStyle style) =>
        Arcs(rect, cornerRadius, style);

    /// <summary><c>Capsule(style:)</c> as a path in <paramref name="rect"/>.</summary>
    public static Geometry CapsuleGeometry(Rect rect, RoundedCornerStyle style) =>
        Arcs(rect, CapsuleCornerRadius(rect.Size), style);

    // MARROW_DEFERRED ED-181: both styles take the circular arc of radius r at each corner. ENGINEERING DECISION: a radius
    // over half a side is held to half that side, so the path never folds over itself (a capsule's radius is exactly that).
    private static StreamGeometry Arcs(Rect rect, double cornerRadius, RoundedCornerStyle style)
    {
        _ = style;
        var r = Math.Max(0, Math.Min(cornerRadius, Math.Min(rect.Width, rect.Height) / 2));
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var corner = new Size(r, r);
            context.BeginFigure(new Point(rect.Left + r, rect.Top), isFilled: true);
            context.LineTo(new Point(rect.Right - r, rect.Top));
            if (r > 0) context.ArcTo(new Point(rect.Right, rect.Top + r), corner, 0, false, SweepDirection.Clockwise);
            context.LineTo(new Point(rect.Right, rect.Bottom - r));
            if (r > 0) context.ArcTo(new Point(rect.Right - r, rect.Bottom), corner, 0, false, SweepDirection.Clockwise);
            context.LineTo(new Point(rect.Left + r, rect.Bottom));
            if (r > 0) context.ArcTo(new Point(rect.Left, rect.Bottom - r), corner, 0, false, SweepDirection.Clockwise);
            context.LineTo(new Point(rect.Left, rect.Top + r));
            if (r > 0) context.ArcTo(new Point(rect.Left + r, rect.Top), corner, 0, false, SweepDirection.Clockwise);
            context.EndFigure(isClosed: true);
        }
        return geometry;
    }
}
