using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-181: SwiftShapeBorder, the control a `.continuous` shape that strokes, or clips a window, is (SPEC.md 4.3). Its fill is the
// shape's `.fill` (and the hit shape), its border the shape's `.strokeBorder` (the shape inset by half the width, stroked, so
// the outer edge is the shape: SwiftUI's own definition, Apple's InsettableShape.strokeBorder documentation), its clip the
// shape's `.clipShape`. Where the corners are circular it draws what an Avalonia Border draws, pixel for pixel.
// Swift sources: ChromeVisualPicker.swift:96-102 (the 82 x 54 ring, r 9, width 2), Chrome.swift:263-271 (the window hairline),
// ChatView.swift:1686-1691 (the outline overlay), SettingsView.swift:118-122 (the window clip).
[Collection("Headless")]
public sealed class SwiftShapeBorderTests(HeadlessSession headless)
{
    private static (Window Window, RenderedPixels Pixels) Render(Control content, double width, double height)
    {
        var window = new Window { Width = width, Height = height, Background = Brushes.Black, Content = new Panel { Children = { content } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return (window, RenderedPixels.Capture(window));
    }

    private static double Level(RenderedPixels pixels, double x, double y)
    {
        var px = x - 0.5;
        var py = y - 0.5;
        var x0 = (int)Math.Floor(px);
        var y0 = (int)Math.Floor(py);
        var fx = px - x0;
        var fy = py - y0;
        double v(int xx, int yy) => pixels.Level(Math.Clamp(xx, 0, pixels.Width - 1), Math.Clamp(yy, 0, pixels.Height - 1));
        return v(x0, y0) * (1 - fx) * (1 - fy) + v(x0 + 1, y0) * fx * (1 - fy) + v(x0, y0 + 1) * (1 - fx) * fy + v(x0 + 1, y0 + 1) * fx * fy;
    }

    /// <summary>Points of an outline with the outward normal there (the figure runs clockwise on screen, so outward is to the left of travel).</summary>
    private static List<(Point Point, Vector Normal)> WithNormals(ShapeOutline outline)
    {
        var result = new List<(Point, Vector)>();
        var current = new Point();
        foreach (var s in outline.Segments)
        {
            if (s.Verb == PathVerb.Cubic)
            {
                for (var i = 1; i < 40; i++)
                {
                    var t = i / 40.0;
                    var m = 1 - t;
                    var p = new Point(
                        m * m * m * current.X + 3 * m * m * t * s.Control1.X + 3 * m * t * t * s.Control2.X + t * t * t * s.End.X,
                        m * m * m * current.Y + 3 * m * m * t * s.Control1.Y + 3 * m * t * t * s.Control2.Y + t * t * t * s.End.Y);
                    var dx = 3 * m * m * (s.Control1.X - current.X) + 6 * m * t * (s.Control2.X - s.Control1.X) + 3 * t * t * (s.End.X - s.Control2.X);
                    var dy = 3 * m * m * (s.Control1.Y - current.Y) + 6 * m * t * (s.Control2.Y - s.Control1.Y) + 3 * t * t * (s.End.Y - s.Control2.Y);
                    var len = Math.Sqrt(dx * dx + dy * dy);
                    if (len > 1e-9) result.Add((p, new Vector(dy / len, -dx / len)));
                }
            }
            current = s.End;
        }
        return result;
    }

    // ---- layout is a Border's ----------------------------------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-181")]
    public void A_shape_measures_and_arranges_its_child_exactly_as_a_Border_does() => headless.OnUiThread(() =>
    {
        var childA = new Border { Width = 40, Height = 20 };
        var childB = new Border { Width = 40, Height = 20 };
        var border = new Border { Child = childA, Padding = new Thickness(8, 12, 6, 4), BorderThickness = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var shape = new SwiftShapeBorder { Child = childB, Padding = new Thickness(8, 12, 6, 4), BorderThickness = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var window = new Window { Width = 300, Height = 200, Content = new StackPanel { Children = { border, shape } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(border.DesiredSize, shape.DesiredSize);
        Assert.Equal(new Size(40 + 8 + 6 + 4, 20 + 12 + 4 + 4), shape.DesiredSize);
        Assert.Equal(childA.Bounds, childB.Bounds);
        Assert.Equal(new Rect(10, 14, 40, 20), childB.Bounds);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-181")]
    public void A_shape_shares_Border_s_properties_so_styles_and_bindings_that_name_them_apply() => headless.OnUiThread(() =>
    {
        var shape = new SwiftShapeBorder();
        shape.SetValue(Border.BackgroundProperty, Brushes.Red);
        shape.SetValue(Border.BorderBrushProperty, Brushes.Blue);
        shape.SetValue(Border.BorderThicknessProperty, new Thickness(3));
        shape.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        Assert.Same(Brushes.Red, shape.Background);
        Assert.Same(Brushes.Blue, shape.BorderBrush);
        Assert.Equal(new Thickness(3), shape.BorderThickness);
        Assert.Equal(new CornerRadius(9), shape.CornerRadius);
        shape.Background = Brushes.Green;
        Assert.Same(Brushes.Green, shape.GetValue(Border.BackgroundProperty));

        var styled = new SwiftShapeBorder { Classes = { "lit" } };
        var window = new Window { Width = 100, Height = 100, Content = styled };
        window.Styles.Add(new Style(x => x.OfType<SwiftShapeBorder>().Class("lit")) { Setters = { new Setter(Border.BackgroundProperty, Brushes.Orange) } });
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Same(Brushes.Orange, styled.Background);
        window.Close();

        var rounded = new SwiftShapeBorder().RoundedRectangle(26, RoundedCornerStyle.Continuous);
        Assert.Equal(new CornerRadius(26), rounded.CornerRadius);
        Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(rounded));
        Assert.Equal(RoundedCornerStyle.Circular, new SwiftShapeBorder().CornerStyle); // until a shape method names one
        var uneven = new SwiftShapeBorder().UnevenRoundedRectangle(new CornerRadius(12, 12, 0, 0), RoundedCornerStyle.Continuous);
        Assert.Equal(new CornerRadius(12, 12, 0, 0), uneven.CornerRadius);
    });

    // ---- circular corners are a Border's, pixel for pixel ------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-181")]
    public void Where_the_corners_are_circular_the_shape_draws_what_a_Border_draws_pixel_for_pixel() => headless.OnUiThread(() =>
    {
        // A 129 x 28 capsule (Chrome.swift:164-165 Capsule(style: .continuous), radius 14: past the continuous limit) with a hairline,
        // and a `.circular` rounded rectangle with a 2 pt stroke: the same call as Border makes (DrawRectangle on the inset rrect).
        foreach (var (w, h, r, style, thickness) in new (double, double, double, RoundedCornerStyle, double)[]
                 { (129, 28, 14, RoundedCornerStyle.Continuous, 1), (82, 54, 9, RoundedCornerStyle.Circular, 2), (400, 46, 22, RoundedCornerStyle.Continuous, 0) })
        {
            Control Make(bool useShape) => useShape
                ? new SwiftShapeBorder { Width = w, Height = h, Background = Brushes.DarkSlateBlue, BorderBrush = Brushes.Orange, BorderThickness = new Thickness(thickness), Margin = new Thickness(10.5, 7.5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }.RoundedRectangle(r, style)
                : new Border { Width = w, Height = h, Background = Brushes.DarkSlateBlue, BorderBrush = Brushes.Orange, BorderThickness = new Thickness(thickness), CornerRadius = new CornerRadius(r), Margin = new Thickness(10.5, 7.5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            var (shapeWindow, shape) = Render(Make(true), w + 40, h + 30);
            var (borderWindow, border) = Render(Make(false), w + 40, h + 30);
            for (var y = 0; y < shape.Height; y++)
                for (var x = 0; x < shape.Width; x++)
                    Assert.Equal(border.At(x, y), shape.At(x, y));
            shapeWindow.Close();
            borderWindow.Close();
        }
    });

    // ---- the stroke ---------------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-181")]
    public void A_continuous_strokeBorder_has_its_outer_edge_on_the_shape_and_its_inner_edge_a_width_inside() => headless.OnUiThread(() =>
    {
        // ChromeVisualPicker.swift:96-102: RoundedRectangle(cornerRadius: 9, style: .continuous).strokeBorder(accent, lineWidth: 2), 82 x 54.
        const double w = 82, h = 54, r = 9, t = 2;
        var ring = new SwiftShapeBorder { Width = w, Height = h, BorderBrush = Brushes.White, BorderThickness = new Thickness(t), Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }
            .RoundedRectangle(r, RoundedCornerStyle.Continuous);
        var (window, pixels) = Render(ring, w + 20, h + 20);
        var outer = WithNormals(SwiftShapes.Outline(new Rect(0, 0, w, h), new CornerRadius(r), RoundedCornerStyle.Continuous));
        var corner = outer.Where(p => p.Point.X < 20 && p.Point.Y < 20).ToList();
        Assert.True(corner.Count > 60);
        foreach (var (p, n) in corner)
        {
            var at = new Point(10 + p.X, 10 + p.Y); // the ring sits at (10, 10) in the window
            // just inside the outer edge (0.75 pt in) the stroke is drawn; just outside it nothing is
            Assert.True(Level(pixels, at.X - 0.75 * n.X, at.Y - 0.75 * n.Y) >= 175, $"the stroke is missing 0.75 pt inside the outer edge at {p}");
            Assert.True(Level(pixels, at.X + 0.75 * n.X, at.Y + 0.75 * n.Y) <= 80, $"the stroke spills 0.75 pt outside the outer edge at {p}");
            // and 2.75 pt in (a width and three quarters), inside the ring's own hole, nothing is drawn: the inner edge is about a width inside
            Assert.True(Level(pixels, at.X - 2.75 * n.X, at.Y - 2.75 * n.Y) <= 80, $"the stroke is wider than {t} pt at {p}");
        }
        // The straight sides: the stroke covers the outer 2 pt and not the third.
        Assert.True(Level(pixels, 10 + w / 2, 10.5) >= 250 && Level(pixels, 10 + w / 2, 11.5) >= 250);
        Assert.True(Level(pixels, 10 + w / 2, 12.5) <= 5);
        Assert.True(Level(pixels, 10 + w / 2, 9.5) <= 5);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-181")]
    public void The_stroke_of_a_circular_shape_stays_a_Borders_centre_line_radius_and_a_continuous_one_is_SwiftUI_s_inset_shape() => headless.OnUiThread(() =>
    {
        // Avalonia's Border strokes the centre line of radius r (outer radius r + width / 2); SwiftUI's strokeBorder strokes the shape inset by
        // half the width (outer radius r). SwiftShapeBorder draws the Border's way where it draws a Border's shape (SPEC.md 4.3) and
        // SwiftUI's where it draws the continuous path: at the 45 degree point of an r 9, width 2 corner the continuous ring's outer edge is
        // the radius-9 curve, nearer the vertex than a Border's radius-10 arc by 0.29 pt.
        const double w = 82, h = 54, t = 2;
        SwiftShapeBorder Ring(RoundedCornerStyle style) => new SwiftShapeBorder { Width = w, Height = h, BorderBrush = Brushes.White, BorderThickness = new Thickness(t), Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }.RoundedRectangle(9, style);
        var (continuousWindow, continuous) = Render(Ring(RoundedCornerStyle.Continuous), w + 20, h + 20);
        var (circularWindow, circular) = Render(Ring(RoundedCornerStyle.Circular), w + 20, h + 20);
        // Along the diagonal from the corner vertex (10, 10), going in: the continuous ring's outer edge crosses at 0.29151 r * 1.414 = 3.71 pt,
        // a Border's (centre-line radius 9 centred on (11, 11), so outer radius 10) at (11 - 10 / 1.414) * 1.414 = 5.56 - ... = 4.14 pt.
        static double Crossing(RenderedPixels p)
        {
            for (var d = 2.5; d < 7; d += 0.02)
                if (Level(p, 10 + d / Math.Sqrt(2), 10 + d / Math.Sqrt(2)) >= 127.5) return d;
            return double.NaN;
        }
        var continuousEdge = Crossing(continuous);
        var circularEdge = Crossing(circular);
        Assert.InRange(continuousEdge, 3.71 - 0.25, 3.71 + 0.25);
        Assert.InRange(circularEdge, 4.14 - 0.25, 4.14 + 0.25);
        Assert.True(circularEdge - continuousEdge >= 0.3, $"the continuous ring's outer edge ({continuousEdge:F2}) is not nearer the vertex than the Border's ({circularEdge:F2})");
        continuousWindow.Close();
        circularWindow.Close();
    });

    // ---- the fill, the hit shape and the clip ------------------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-181")]
    public void The_fill_is_drawn_inside_the_continuous_path_and_a_transparent_fill_takes_the_hits_inside_it_only() => headless.OnUiThread(() =>
    {
        var shape = new SwiftShapeBorder { Width = 400, Height = 300, Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }
            .RoundedRectangle(26, RoundedCornerStyle.Continuous);
        var (window, _) = Render(shape, 420, 320);
        // `.contentShape(RoundedRectangle(cornerRadius: 26, style: .continuous))`: (22.6, 0.40) is inside the circle, outside Apple's curve.
        Assert.NotSame(shape, window.InputHitTest(new Point(22.6, 0.40)));
        Assert.Same(shape, window.InputHitTest(new Point(22.6, 0.75)));
        Assert.Same(shape, window.InputHitTest(new Point(200, 150)));
        Assert.NotSame(shape, window.InputHitTest(new Point(1, 1)));
        shape.Width = 360; // and after a resize
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Assert.NotSame(shape, window.InputHitTest(new Point(360 - 22.6, 0.40)));
        Assert.Same(shape, window.InputHitTest(new Point(360 - 22.6, 0.75)));
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-181")]
    public void ClipToBounds_clips_the_children_to_the_continuous_path_and_follows_a_resize_and_a_style_change() => headless.OnUiThread(() =>
    {
        var child = new Border { Background = Brushes.Red, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        var shape = new SwiftShapeBorder { Width = 400, Height = 300, Child = child, ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }
            .RoundedRectangle(26, RoundedCornerStyle.Continuous);
        var (window, pixels) = Render(shape, 420, 320);
        var clip = Assert.IsAssignableFrom<Geometry>(shape.Clip);
        Assert.Same(clip, SwiftShapes.RoundedRectangleGeometry(new Rect(0, 0, 400, 300), 26, RoundedCornerStyle.Continuous)); // the shared cached path
        // 1.5 pt inside Apple's curve at the diagonal the red child is drawn, 1.5 pt outside it nothing is.
        var diagonal = 0.29151 * 26; // the curve's 45 degree point, per axis
        Assert.True(pixels.At((int)Math.Round(diagonal + 1.5 / Math.Sqrt(2)), (int)Math.Round(diagonal + 1.5 / Math.Sqrt(2))).Red >= 200);
        Assert.True(pixels.At((int)Math.Round(diagonal - 1.5 / Math.Sqrt(2) - 0.5), (int)Math.Round(diagonal - 1.5 / Math.Sqrt(2) - 0.5)).Red <= 40);

        shape.Width = 360;
        Dispatcher.UIThread.RunJobs();
        var resized = Assert.IsAssignableFrom<Geometry>(shape.Clip);
        Assert.NotSame(clip, resized);
        Assert.Same(resized, SwiftShapes.RoundedRectangleGeometry(new Rect(0, 0, 360, 300), 26, RoundedCornerStyle.Continuous));

        shape.CornerRadius = new CornerRadius(30);
        Assert.Same(SwiftShapes.RoundedRectangleGeometry(new Rect(0, 0, 360, 300), 30, RoundedCornerStyle.Continuous), shape.Clip);
        shape.ClipToBounds = false;
        Assert.Null(shape.Clip); // nothing of its own to keep
        shape.ClipToBounds = true;
        var own = new RectangleGeometry(new Rect(0, 0, 5, 5));
        shape.Clip = own;
        shape.CornerRadius = new CornerRadius(20);
        Assert.Same(own, shape.Clip); // a clip a site set itself is never replaced
        window.Close();
    });

    // ---- shadows ------------------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-181")]
    public void A_continuous_shape_with_a_box_shadow_casts_it_beside_the_fill_and_an_inset_shadow_inside() => headless.OnUiThread(() =>
    {
        var outer = new SwiftShapeBorder { Width = 200, Height = 120, Background = Brushes.White, Margin = new Thickness(40), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 16, OffsetY = 8, Color = Colors.Red }) }.RoundedRectangle(26, RoundedCornerStyle.Continuous);
        var (window, pixels) = Render(outer, 280, 200);
        var below = pixels.At(40 + 100, 40 + 120 + 6); // under the shape's bottom edge, inside the shadow's reach
        Assert.True(below.Red > 60 && below.Green < 30, $"no red shadow under the shape: {below}");
        var centre = pixels.At(40 + 100, 40 + 60);
        Assert.True(centre.Red >= 250 && centre.Green >= 250, "the fill is drawn over its shadow");
        window.Close();

        var inset = new SwiftShapeBorder { Width = 200, Height = 120, Background = Brushes.White, Margin = new Thickness(40), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 8, Spread = 0, IsInset = true, Color = Colors.Blue }) }.RoundedRectangle(26, RoundedCornerStyle.Continuous);
        (window, pixels) = Render(inset, 280, 200);
        var edge = pixels.At(40 + 100, 40 + 3);
        Assert.True(edge.Blue >= edge.Red + 15, $"no inset shadow at the top edge: {edge}");
        var middle = pixels.At(40 + 100, 40 + 60);
        Assert.True(middle.Red >= 250 && middle.Blue >= 250, $"the inset shadow reaches the middle: {middle}");
        window.Close();
    });
}
