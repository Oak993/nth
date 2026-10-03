// PORT-ONLY: SwiftUI's `RoundedRectangle(cornerRadius:style:)` as a view that fills and strokes its bounds, with the
// corner style Avalonia's Border has no counterpart for (ED-181, build spec SPEC.md 4.3). Avalonia's Border draws circular
// corners only and its Render is sealed, so a `.continuous` shape is its own control that draws the continuous path.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Marrow.UI.Port;

/// <summary>
/// A <see cref="Border"/> whose corners are SwiftUI's: the fill is the shape's <c>.fill</c> (and the hit shape), the border
/// its <c>.strokeBorder</c> (the shape inset by half the width, stroked, so its outer edge is the shape), the clip its
/// <c>.clipShape</c> (<see cref="Visual.ClipToBounds"/> clips the children to the shape's path). The properties are
/// Border's own (<see cref="Border.BackgroundProperty"/> and the rest are owned by both), so styles, bindings and
/// animations that name Border's properties apply. While the corners are circular (a capsule, a radius past
/// <see cref="ContinuousCorner.LargestRadius"/>, the `.circular` style) it draws what a Border draws, the same calls.
/// </summary>
public class SwiftShapeBorder : Decorator
{
    public static readonly StyledProperty<IBrush?> BackgroundProperty = Border.BackgroundProperty.AddOwner<SwiftShapeBorder>();
    public static readonly StyledProperty<IBrush?> BorderBrushProperty = Border.BorderBrushProperty.AddOwner<SwiftShapeBorder>();
    public static readonly StyledProperty<Thickness> BorderThicknessProperty = Border.BorderThicknessProperty.AddOwner<SwiftShapeBorder>();
    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty = Border.CornerRadiusProperty.AddOwner<SwiftShapeBorder>();
    public static readonly StyledProperty<BoxShadows> BoxShadowProperty = Border.BoxShadowProperty.AddOwner<SwiftShapeBorder>();

    private Geometry? appliedClip;
    private Thickness? layoutThickness;
    private double layoutScale;

    static SwiftShapeBorder()
    {
        AffectsRender<SwiftShapeBorder>(BackgroundProperty, BorderBrushProperty, BorderThicknessProperty, CornerRadiusProperty,
            BoxShadowProperty, SwiftShapes.CornerStyleProperty);
        AffectsMeasure<SwiftShapeBorder>(BorderThicknessProperty);
    }

    public IBrush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    public IBrush? BorderBrush { get => GetValue(BorderBrushProperty); set => SetValue(BorderBrushProperty, value); }
    public Thickness BorderThickness { get => GetValue(BorderThicknessProperty); set => SetValue(BorderThicknessProperty, value); }
    public CornerRadius CornerRadius { get => GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    public BoxShadows BoxShadow { get => GetValue(BoxShadowProperty); set => SetValue(BoxShadowProperty, value); }

    /// <summary>The corner style this shape was drawn with (<see cref="RoundedCornerStyle.Circular"/> until a shape method sets one).</summary>
    public RoundedCornerStyle CornerStyle => SwiftShapes.GetCornerStyle(this) ?? RoundedCornerStyle.Circular;

    /// <summary><c>RoundedRectangle(cornerRadius:style:)</c>: every corner this radius.</summary>
    public SwiftShapeBorder RoundedRectangle(double cornerRadius, RoundedCornerStyle style)
    {
        SetValue(SwiftShapes.CornerStyleProperty, style);
        CornerRadius = new CornerRadius(cornerRadius);
        return this;
    }

    /// <summary><c>UnevenRoundedRectangle</c> (left to right): a radius per corner.</summary>
    public SwiftShapeBorder UnevenRoundedRectangle(CornerRadius cornerRadii, RoundedCornerStyle style)
    {
        SetValue(SwiftShapes.CornerStyleProperty, style);
        CornerRadius = cornerRadii;
        return this;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        var p = change.Property;
        if (p == UseLayoutRoundingProperty || p == BorderThicknessProperty) layoutThickness = null;
        if (p == CornerRadiusProperty || p == ClipToBoundsProperty || p == SwiftShapes.CornerStyleProperty) UpdateClip(Bounds.Size);
    }

    private Thickness LayoutThickness
    {
        get
        {
            var scale = LayoutHelper.GetLayoutScale(this);
            if (!(Math.Abs(scale - layoutScale) < 1e-9)) { layoutScale = scale; layoutThickness = null; }
            if (layoutThickness is null)
            {
                var thickness = BorderThickness;
                layoutThickness = UseLayoutRounding ? LayoutHelper.RoundLayoutThickness(thickness, scale) : thickness;
            }
            return layoutThickness.Value;
        }
    }

    protected override Size MeasureOverride(Size availableSize) =>
        LayoutHelper.MeasureChild(Child, availableSize, Padding, BorderThickness);

    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = LayoutHelper.ArrangeChild(Child, finalSize, Padding, BorderThickness);
        UpdateClip(finalSize);
        return arranged;
    }

    // `.clipShape`: the children are clipped to the shape's own path (a Border clips to its circular rounded rectangle; this
    // is the same for circular corners, and Apple's curve for continuous ones). A Clip a site set itself is never replaced.
    private void UpdateClip(Size size)
    {
        Geometry? wanted = ClipToBounds && size.Width > 0 && size.Height > 0
            ? SwiftShapes.UnevenRoundedRectangleGeometry(new Rect(size), CornerRadius, CornerStyle)
            : null;
        if (ReferenceEquals(wanted, appliedClip)) return;
        if (Clip is null || ReferenceEquals(Clip, appliedClip))
        {
            Clip = wanted;
            appliedClip = wanted;
        }
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0) return;
        var thickness = LayoutThickness.Top; // SwiftUI's strokeBorder has one width
        var radii = CornerRadius;
        var rect = new Rect(size);
        var outline = SwiftShapes.Outline(rect, radii, CornerStyle);
        var stroke = BorderBrush is { } brush && thickness > 0 ? new Pen(brush, thickness) : null;

        if (!outline.HasContinuousCorner)
        {
            // Circular corners, a capsule, a radius past the limit: what a Border draws, the same call, pixel for pixel.
            var inner = thickness != 0 ? rect.Deflate(thickness * 0.5) : rect;
            context.DrawRectangle(Background, stroke, new RoundedRect(inner, radii.TopLeft, radii.TopRight, radii.BottomRight, radii.BottomLeft), BoxShadow);
            return;
        }

        // Continuous corners. A shadow is blurred, so the circular rounded rectangle of the same radius stands for the shape
        // (ENGINEERING DECISION: the curves differ by at most 0.0138 r, which no blur shows); the fill and the border are the path.
        var outer = new List<BoxShadow>();
        var insets = new List<BoxShadow>();
        foreach (var shadow in BoxShadow) (shadow.IsInset ? insets : outer).Add(shadow);
        var circular = new RoundedRect(rect, radii.TopLeft, radii.TopRight, radii.BottomRight, radii.BottomLeft);
        if (outer.Count > 0) context.DrawRectangle(null, null, circular, new BoxShadows(outer[0], outer.Skip(1).ToArray()));
        context.DrawGeometry(Background, null, SwiftShapes.UnevenRoundedRectangleGeometry(rect, radii, CornerStyle));
        if (insets.Count > 0) context.DrawRectangle(null, null, circular, new BoxShadows(insets[0], insets.Skip(1).ToArray()));
        if (stroke is not null)
        {
            // strokeBorder: the shape inset by half the width (radius r - width / 2), stroked with the full width.
            var half = thickness / 2;
            var inset = new CornerRadius(Math.Max(0, radii.TopLeft - half), Math.Max(0, radii.TopRight - half),
                Math.Max(0, radii.BottomRight - half), Math.Max(0, radii.BottomLeft - half));
            context.DrawGeometry(null, stroke, SwiftShapes.UnevenRoundedRectangleGeometry(rect.Deflate(half), inset, CornerStyle));
        }
    }
}
