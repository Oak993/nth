// PORT-ONLY: Dextra (DESIGN.md rev 5, 2.5 "Asturs on Armilla"; unit AS-16; ED-NEW-dextra-as16-1, -15). No Swift twin.
// One Astur node shape drawn still, in its own tint for the theme it is on: what the profile sheet's picker shows at 30 px in a 44 px button (and what a
// team list can show beside a name). The moving Armilla marks are AS-8's; this is only the shape. It draws inside its square and never outside it.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Port;

namespace Marrow.App.Assistant.Views;

public sealed class AsturShapeGlyph : Control
{
    public static readonly StyledProperty<AsturShape> ShapeProperty = AvaloniaProperty.Register<AsturShapeGlyph, AsturShape>(nameof(Shape));

    /// <summary>How much of its tint it shows: a shape at rest is a little fainter than one that is picked (2.5: 72 % and 100 %).</summary>
    public static readonly StyledProperty<double> StrengthProperty = AvaloniaProperty.Register<AsturShapeGlyph, double>(nameof(Strength), 1.0);

    static AsturShapeGlyph()
    {
        AffectsRender<AsturShapeGlyph>(ShapeProperty, StrengthProperty);
        AffectsMeasure<AsturShapeGlyph>(WidthProperty, HeightProperty);
    }

    public AsturShapeGlyph(AsturShape shape, double size = 30)
    {
        Shape = shape;
        Width = size;
        Height = size;
        IsHitTestVisible = false;
        Focusable = false;
    }

    public AsturShape Shape
    {
        get => GetValue(ShapeProperty);
        set => SetValue(ShapeProperty, value);
    }

    public double Strength
    {
        get => GetValue(StrengthProperty);
        set => SetValue(StrengthProperty, value);
    }

    /// <summary>The tint the shape has on the dark theme or the light one.</summary>
    public static Color TintOf(AsturShape shape, bool dark) => Color.Parse(dark ? AsturShapes.Tint(shape).Dark : AsturShapes.Tint(shape).Light);

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;
        var tint = TintOf(Shape, PlainControls.IsDark(ActualThemeVariant));
        var brush = new SolidColorBrush(tint, Math.Clamp(Strength, 0, 1));
        // The shape fits a box a little inside the square, so a round join or a point never reaches its edge.
        var half = size / 2;
        var radius = half * 0.82;
        var centre = new Point(half + (Bounds.Width - size) / 2, half + (Bounds.Height - size) / 2);
        var soften = new Pen(brush, size * 0.1, lineJoin: PenLineJoin.Round);
        switch (Shape)
        {
            case AsturShape.Squircle:
                context.DrawRectangle(brush, null, new Rect(centre.X - radius, centre.Y - radius, radius * 2, radius * 2), radius * 0.62, radius * 0.62);
                break;
            case AsturShape.Hexagon:
                context.DrawGeometry(brush, soften, Polygon(centre, radius * 0.9, 6));
                break;
            case AsturShape.Diamond:
                context.DrawGeometry(brush, soften, Polygon(centre, radius * 0.95, 4));
                break;
            case AsturShape.RoundedTriangle:
                context.DrawGeometry(brush, soften, Polygon(centre, radius * 0.9, 3));
                break;
            case AsturShape.Clover:
                var lobe = radius * 0.5;
                var reach = radius * 0.5;
                foreach (var (dx, dy) in new[] { (0.0, -reach), (reach, 0.0), (0.0, reach), (-reach, 0.0) })
                    context.DrawEllipse(brush, null, new Point(centre.X + dx, centre.Y + dy), lobe, lobe);
                context.DrawEllipse(brush, null, centre, lobe, lobe);
                break;
            default:
                context.DrawGeometry(brush, soften, Star(centre, radius * 0.95, radius * 0.42));
                break;
        }
    }

    private static StreamGeometry Polygon(Point centre, double radius, int sides)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        for (var i = 0; i < sides; i++)
        {
            var angle = (-90 + 360.0 * i / sides) * Math.PI / 180;
            var point = new Point(centre.X + Math.Cos(angle) * radius, centre.Y + Math.Sin(angle) * radius);
            if (i == 0) context.BeginFigure(point, true);
            else context.LineTo(point);
        }
        context.EndFigure(true);
        return geometry;
    }

    private static StreamGeometry Star(Point centre, double outer, double inner)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        for (var i = 0; i < 10; i++)
        {
            var angle = (-90 + 36.0 * i) * Math.PI / 180;
            var radius = i % 2 == 0 ? outer : inner;
            var point = new Point(centre.X + Math.Cos(angle) * radius, centre.Y + Math.Sin(angle) * radius);
            if (i == 0) context.BeginFigure(point, true);
            else context.LineTo(point);
        }
        context.EndFigure(true);
        return geometry;
    }
}
