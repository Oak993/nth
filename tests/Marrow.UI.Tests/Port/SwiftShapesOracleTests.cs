using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-181: the port's own shapes against the reference runtime. ORACLE: the reference video (macOS 26, 2-chat-use.mov and
// the Settings and Hydra stretches), edge points measured per element by the build spec's tools/measure_corners.py
// (E:\Droppy\work\design\build-specs\continuous-corners\out\oracle_fits.json, the accepted elements, copied to
// ContinuousCornerOracle.json: each element's measured boundary points in the top-left frame, the Swift radius and size,
// and the rms of each candidate curve the spec's fitter found). The method is the spec's (SPEC.md 2.1): translation-only
// least squares of a model path onto the measured points, rms = distance of each point to the path.
// Swift sources of the elements: SettingsView.swift:120 RoundedRectangle(cornerRadius: 26, style: .continuous) clipShape (780 x 580);
// HydraPanel.swift:33,57,105,112 r 22 (about 400 x 280); ChromeVisualPicker.swift:92-102 and :14-16 the 82 x 54 ring r 9 + 3;
// ComposerView.swift:1298-1304 r 22 (about 818 x 46); Chrome.swift:129-131,163-166 the 28 pt glass capsule.
// What the video shows (SPEC.md 2.3): Apple's corner at r 22-26 (8 of 8 corners better than the circle), circular-like ends
// for capsules and r >= h / 2 (PaintCode's clamped continuous corner is rejected there), unresolvable at r 9.
[Collection("Headless")]
public sealed class SwiftShapesOracleTests(HeadlessSession headless)
{
    private static IEnumerable<OracleElement> Normal => OracleData.Elements.Where(e => !e.IsEnd && e.SourceRadius >= 20); // r 22-26: windows and Hydra's card
    private static IEnumerable<OracleElement> Rings => OracleData.Elements.Where(e => !e.IsEnd && e.SourceRadius < 20); // r 9
    private static IEnumerable<OracleElement> Ends => OracleData.Elements.Where(e => e.IsEnd); // composer and capsule ends

    // ---- the fit ------------------------------------------------------------------------------------------------------

    /// <summary>The path of a shape in the top-left frame, as a polyline with a vertex at least every <paramref name="step"/> points.</summary>
    private static List<Point> Polyline(ShapeOutline outline, double step = 0.25)
    {
        var points = new List<Point>();
        var current = new Point();
        foreach (var s in outline.Segments)
        {
            switch (s.Verb)
            {
                case PathVerb.Move:
                    points.Add(s.End);
                    break;
                case PathVerb.Line:
                {
                    var n = Math.Max(1, (int)Math.Ceiling(Distance(current, s.End) / step));
                    for (var i = 1; i <= n; i++) points.Add(current + (s.End - current) * ((double)i / n));
                    break;
                }
                case PathVerb.Cubic:
                {
                    var length = Distance(current, s.Control1) + Distance(s.Control1, s.Control2) + Distance(s.Control2, s.End);
                    var n = Math.Max(8, (int)Math.Ceiling(length / step));
                    for (var i = 1; i <= n; i++)
                    {
                        var t = (double)i / n;
                        var m = 1 - t;
                        points.Add(new Point(
                            m * m * m * current.X + 3 * m * m * t * s.Control1.X + 3 * m * t * t * s.Control2.X + t * t * t * s.End.X,
                            m * m * m * current.Y + 3 * m * m * t * s.Control1.Y + 3 * m * t * t * s.Control2.Y + t * t * t * s.End.Y));
                    }
                    break;
                }
                case PathVerb.Arc:
                {
                    var d = s.End - current;
                    var chord = Math.Sqrt(d.X * d.X + d.Y * d.Y);
                    var h = Math.Sqrt(Math.Max(0, s.ArcRadius * s.ArcRadius - chord * chord / 4));
                    var centre = new Point((current.X + s.End.X) / 2 - h * d.Y / chord, (current.Y + s.End.Y) / 2 + h * d.X / chord);
                    var a0 = Math.Atan2(current.Y - centre.Y, current.X - centre.X);
                    var a1 = Math.Atan2(s.End.Y - centre.Y, s.End.X - centre.X);
                    if (a1 < a0) a1 += 2 * Math.PI;
                    var n = Math.Max(8, (int)Math.Ceiling(s.ArcRadius * (a1 - a0) / step));
                    for (var i = 1; i <= n; i++)
                    {
                        var a = a0 + (a1 - a0) * i / n;
                        points.Add(new Point(centre.X + s.ArcRadius * Math.Cos(a), centre.Y + s.ArcRadius * Math.Sin(a)));
                    }
                    break;
                }
            }
            current = s.End;
        }
        return points;
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>The part of the model path the measured points reach (the spec's model_poly): the top-left corner and the straight runs beside it, or a whole end.</summary>
    private static List<Point> Model(OracleElement e, RoundedCornerStyle style)
    {
        var outline = SwiftShapes.Outline(new Rect(0, 0, e.W, e.H), new CornerRadius(e.Radius), style);
        var path = Polyline(outline);
        var r = e.Radius;
        if (e.IsEnd)
        {
            var keep = Math.Max(r, e.H / 2) + e.H / 2 + 12;
            return path.Where(p => p.X <= keep).ToList();
        }
        var box = 3.2 * e.SourceRadius;
        return path.Where(p => p.X <= box && p.Y <= box).ToList();
    }

    private static double DistanceToPolyline(Point p, IReadOnlyList<Point> poly, out int nearest)
    {
        var best = double.MaxValue;
        nearest = 0;
        for (var i = 0; i < poly.Count - 1; i++)
        {
            var a = poly[i];
            var b = poly[i + 1];
            var abx = b.X - a.X;
            var aby = b.Y - a.Y;
            var len2 = abx * abx + aby * aby;
            var t = len2 == 0 ? 0 : Math.Clamp(((p.X - a.X) * abx + (p.Y - a.Y) * aby) / len2, 0, 1);
            var dx = p.X - (a.X + t * abx);
            var dy = p.Y - (a.Y + t * aby);
            var d2 = dx * dx + dy * dy;
            if (d2 < best) { best = d2; nearest = i; }
        }
        return Math.Sqrt(best);
    }

    private static double MeanSquare(Point[] points, IReadOnlyList<Point> poly, Vector shift)
    {
        double sum = 0;
        foreach (var p in points)
        {
            var d = DistanceToPolyline(p - shift, poly, out _);
            sum += d * d;
        }
        return sum / points.Length;
    }

    /// <summary>Translation-only least squares (a pattern search from three starts, the spec's fit_translation), returning the rms and the shift.</summary>
    private static (double Rms, Vector Shift) Fit(Point[] points, IReadOnlyList<Point> poly)
    {
        var best = (Cost: double.MaxValue, Shift: new Vector());
        foreach (var start in new[] { new Vector(0, 0), new Vector(1, 1), new Vector(-1, -1) })
        {
            var shift = start;
            var cost = MeanSquare(points, poly, shift);
            for (var step = 0.5; step > 1e-4;)
            {
                var improved = false;
                foreach (var (dx, dy) in new[] { (1.0, 0.0), (-1.0, 0.0), (0.0, 1.0), (0.0, -1.0), (1.0, 1.0), (-1.0, -1.0), (1.0, -1.0), (-1.0, 1.0) })
                {
                    var candidate = shift + new Vector(dx * step, dy * step);
                    var c = MeanSquare(points, poly, candidate);
                    if (c < cost) { cost = c; shift = candidate; improved = true; }
                }
                if (!improved) step /= 2;
            }
            if (cost < best.Cost) best = (cost, shift);
        }
        return (Math.Sqrt(best.Cost), best.Shift);
    }

    private static double RmsOf(OracleElement e, RoundedCornerStyle style) => Fit(e.Points, Model(e, style)).Rms;

    // ---- 5.2-1 the measured edges of the normal regime ---------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-181")]
    public void The_port_corner_fits_the_measured_r22_to_26_edges_as_well_as_the_spec_s_apple_model_and_better_than_the_circle() => headless.OnUiThread(() =>
    {
        var checkedCorners = 0;
        foreach (var e in Normal)
        {
            var port = RmsOf(e, RoundedCornerStyle.Continuous);
            var circle = RmsOf(e, RoundedCornerStyle.Circular);
            // The fitter's own numbers (the spec's table): the circle fits a window corner at 0.137-0.231 px, Apple's at 0.084-0.147.
            Assert.True(Math.Abs(circle - e.Rms["circle"]) < 0.01, $"{e.Name}: the circle fits at {circle:F3}, the spec found {e.Rms["circle"]:F3}");
            Assert.True(port <= e.Rms["modern"] + 0.01, $"{e.Name}: the port path fits at {port:F3}, the spec's Apple model at {e.Rms["modern"]:F3}");
            Assert.True(port <= 0.85 * circle, $"{e.Name}: the port path {port:F3} is not 15 % better than the circle {circle:F3}");
            checkedCorners++;
        }
        Assert.Equal(8, checkedCorners); // 6 window corners (light and dark), Hydra's top-right and bottom-left
    });

    [Fact]
    [Trait("Decision", "ED-181")]
    public void At_r9_the_port_corner_is_within_a_hundredth_of_a_pixel_of_the_spec_s_model_where_the_video_cannot_tell_the_curves_apart() => headless.OnUiThread(() =>
    {
        // SPEC.md 2.3: the models differ by 0.124 px at most on the 9 pt ring and the 0.011 px edge for the circle is below
        // the edge estimator's own bias; the video neither confirms nor excludes the curve there, so the port path must simply
        // not be worse than the spec's model by more than the fit's own precision. OD-0098: the Swift source (continuous) decides.
        var count = 0;
        foreach (var e in Rings)
        {
            var port = RmsOf(e, RoundedCornerStyle.Continuous);
            Assert.True(port <= e.Rms["modern"] + 0.01, $"{e.Name}: {port:F3} against {e.Rms["modern"]:F3}");
            count++;
        }
        Assert.Equal(6, count);
    });

    // ---- 5.2-4 the large regime: circular ends -----------------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-181")]
    public void The_composer_s_end_and_the_capsule_ends_fit_the_measured_edges_at_circular_precision_not_as_a_clamped_continuous_corner() => headless.OnUiThread(() =>
    {
        var count = 0;
        foreach (var e in Ends)
        {
            var port = RmsOf(e, RoundedCornerStyle.Continuous);
            Assert.True(port <= 0.20, $"{e.Name}: the port end fits at {port:F3} px");
            Assert.True(port <= e.Rms["circle"] + 0.01, $"{e.Name}: {port:F3} against the circle's {e.Rms["circle"]:F3}");
            Assert.True(port < 0.5 * e.Rms["modern"], $"{e.Name}: the clamped continuous corner fits at {e.Rms["modern"]:F3}, the port must not be near it");
            count++;
        }
        Assert.Equal(5, count); // composer-left, choose-left/right (the system glass button), fullaccess-left/right
    });

    // ---- 5.2-2 the signature -----------------------------------------------------------------------------------------

    private const double RayStart = 181, RayEnd = 269, RayStep = 0.5; // degrees in the top-left frame, as the spec's rays

    /// <summary>The residual of a circular-arc fit (translation free) of <paramref name="points"/>, in units of r, signed by the side of the arc.</summary>
    private static double[] CircleSignature(OracleElement e, Point[] points)
    {
        var poly = Model(e, RoundedCornerStyle.Circular);
        var (_, shift) = Fit(points, poly);
        return points.Select(p =>
        {
            var q = p - shift;
            var d = DistanceToPolyline(q, poly, out var i);
            var a = poly[Math.Max(0, i - 3)];
            var b = poly[Math.Min(poly.Count - 1, i + 4)];
            var cross = (b.X - a.X) * (q.Y - poly[i].Y) - (b.Y - a.Y) * (q.X - poly[i].X);
            return Math.CopySign(d, cross) / e.SourceRadius;
        }).ToArray();
    }

    /// <summary>The model's boundary sampled on the measured rays (the spec's synthetic_points): from (2.2 r, 2.2 r), where each ray leaves the path.</summary>
    private static Point[] OnTheRays(OracleElement e, RoundedCornerStyle style)
    {
        var poly = Model(e, style);
        var c = new Point(2.2 * e.SourceRadius, 2.2 * e.SourceRadius);
        var points = new List<Point>();
        for (var deg = RayStart; deg <= RayEnd + 1e-9; deg += RayStep)
        {
            var a = deg * Math.PI / 180;
            var dir = (X: Math.Cos(a), Y: Math.Sin(a));
            var t = RayCrossing(poly, c, dir);
            Assert.False(double.IsNaN(t), $"{e.Name}: the ray at {deg} degrees misses the path");
            points.Add(new Point(c.X + t * dir.X, c.Y + t * dir.Y));
        }
        return points.ToArray();
    }

    private static double Correlation(double[] a, double[] b)
    {
        double ma = a.Average(), mb = b.Average(), sab = 0, saa = 0, sbb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            sab += (a[i] - ma) * (b[i] - mb);
            saa += (a[i] - ma) * (a[i] - ma);
            sbb += (b[i] - mb) * (b[i] - mb);
        }
        return sab / Math.Sqrt(saa * sbb);
    }

    private static double[] MeanOf(IEnumerable<double[]> series)
    {
        var list = series.ToList();
        return Enumerable.Range(0, list[0].Length).Select(i => list.Average(s => s[i])).ToArray();
    }

    [Fact]
    [Trait("Decision", "ED-181")]
    public void The_measured_corners_carry_the_port_corner_s_signature_the_circle_s_would_not() => headless.OnUiThread(() =>
    {
        // Fit the circular arc to each measured r 22-26 corner and average the signed residual by ray angle, then do the
        // same to the port's own path sampled on the same 177 rays. The spec's perfect Apple corner correlates at 0.882.
        var corners = Normal.ToList();
        Assert.All(corners, e => Assert.Equal(177, e.Points.Length));
        var measured = MeanOf(corners.Select(e => CircleSignature(e, e.Points)));
        var port = MeanOf(corners.Select(e => CircleSignature(e, OnTheRays(e, RoundedCornerStyle.Continuous))));
        var correlation = Correlation(measured, port);
        Assert.True(correlation >= 0.85, $"the measured signature correlates with the port path at {correlation:F3}");
        // A test that cannot fail is no test: a circle drawn as `.continuous` (the deferral) carries no signature at all (its
        // residual from the circle fit is the chord error of the polyline, 1e-5 r), the measured corners do.
        var circle = MeanOf(corners.Select(e => CircleSignature(e, OnTheRays(e, RoundedCornerStyle.Circular))));
        Assert.True(circle.Max() - circle.Min() < 0.001, "the circle carries a signature");
        Assert.True(measured.Max() - measured.Min() > 0.02, "the measured corners carry none");
        // The amplitude the spec found: the measured dips are about 1.4 times the perfect corner's (estimator bias or a small curve difference, SPEC.md 6.2).
        Assert.InRange(port.Max() - port.Min(), 0.004, 0.02);
    });

    // ---- 5.2-3 the rasterised edge -----------------------------------------------------------------------------------

    private const double Margin = 20, ShapeW = 780, ShapeH = 580, ShapeR = 26; // the Settings window: 780 x 580, r 26

    private static RenderedPixels Render(Control shape)
    {
        shape.Margin = new Thickness(Margin);
        shape.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        shape.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        var window = new Window { Width = ShapeW + 2 * Margin, Height = ShapeH + 2 * Margin, Background = Brushes.Black, Content = new Panel { Children = { shape } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        var pixels = RenderedPixels.Capture(window);
        window.Close();
        return pixels;
    }

    /// <summary>
    /// The distance along each of the spec's rays (from 2.2 r inside each corner toward it, 1 degree apart, 4 x 89 rays) at
    /// which the rendered brightness falls through 50 %: bilinear in pixel centres, the white shape on black.
    /// </summary>
    private static double[] RenderedCrossings(RenderedPixels pixels)
    {
        var crossings = new List<double>();
        for (var corner = 0; corner < 4; corner++)
        {
            var sx = corner is 1 or 2 ? -1 : 1; // mirror to the corner (TL, TR, BR, BL)
            var sy = corner is 2 or 3 ? -1 : 1;
            var vx = corner is 1 or 2 ? ShapeW : 0;
            var vy = corner is 2 or 3 ? ShapeH : 0;
            var c = new Point(2.2 * ShapeR, 2.2 * ShapeR);
            for (var deg = RayStart; deg <= RayEnd; deg += 1.0)
            {
                var a = deg * Math.PI / 180;
                double Level(double t)
                {
                    var px = vx + sx * (c.X + t * Math.Cos(a)) + Margin - 0.5; // top-left-frame point -> window pixel (centres at +0.5)
                    var py = vy + sy * (c.Y + t * Math.Sin(a)) + Margin - 0.5;
                    var x0 = (int)Math.Floor(px);
                    var y0 = (int)Math.Floor(py);
                    var fx = px - x0;
                    var fy = py - y0;
                    double v(int x, int y) => pixels.Level(Math.Clamp(x, 0, pixels.Width - 1), Math.Clamp(y, 0, pixels.Height - 1));
                    return v(x0, y0) * (1 - fx) * (1 - fy) + v(x0 + 1, y0) * fx * (1 - fy) + v(x0, y0 + 1) * (1 - fx) * fy + v(x0 + 1, y0 + 1) * fx * fy;
                }
                var crossing = double.NaN;
                var previous = Level(1.2 * ShapeR);
                for (var t = 1.2 * ShapeR + 0.02; t < 3.4 * ShapeR; t += 0.02)
                {
                    var now = Level(t);
                    if (previous >= 127.5 && now < 127.5) { crossing = t - 0.02 * (127.5 - now) / (previous - now); break; }
                    previous = now;
                }
                Assert.False(double.IsNaN(crossing), $"corner {corner} ray {deg}: no edge found");
                crossings.Add(crossing);
            }
        }
        return crossings.ToArray();
    }

    private static (double Rms, double Max) Difference(double[] a, double[] b)
    {
        var d = a.Zip(b, (x, y) => x - y).ToArray();
        return (Math.Sqrt(d.Average(x => x * x)), d.Max(Math.Abs));
    }

    [Fact]
    [Trait("Decision", "ED-181")]
    public void A_rendered_continuous_border_s_edge_is_the_analytic_path_s_edge_in_all_four_corners_and_the_circle_s_is_not() => headless.OnUiThread(() =>
    {
        // The reference is the analytic path drawn alone, in one pass, by the same rasteriser (a Path control filled white): it carries
        // the measurement's own bias along slanted edges (the 50 % crossing of box-filtered coverage), which the difference removes.
        // The shape drawn through SwiftShapeBorder draws the same geometry, so it must equal the reference (one anti-aliasing pass).
        var analytic = new Avalonia.Controls.Shapes.Path
        {
            Data = SwiftShapes.RoundedRectangleGeometry(new Rect(0, 0, ShapeW, ShapeH), ShapeR, RoundedCornerStyle.Continuous),
            Fill = Brushes.White,
            Width = ShapeW,
            Height = ShapeH,
        };
        var reference = RenderedCrossings(Render(analytic));

        var shape = new SwiftShapeBorder { Width = ShapeW, Height = ShapeH, Background = Brushes.White }.RoundedRectangle(ShapeR, RoundedCornerStyle.Continuous);
        var rendered = RenderedCrossings(Render(shape));
        var (rms, max) = Difference(rendered, reference);
        // And against the analytic path itself (the 50 % crossing of box-filtered coverage along a slanted edge sits a little inside it: the
        // measurement's own bias, which the reference render above carries too): the spec's 0.10 px rms and 0.25 px at worst.
        var path = Polyline(SwiftShapes.Outline(new Rect(0, 0, ShapeW, ShapeH), new CornerRadius(ShapeR), RoundedCornerStyle.Continuous), 0.1);
        var analyticCrossings = new List<double>();
        for (var corner = 0; corner < 4; corner++)
        {
            var seen = ToTopLeft(path, ShapeW, ShapeH, corner);
            for (var deg = RayStart; deg <= RayEnd; deg += 1.0)
                analyticCrossings.Add(RayCrossing(seen, new Point(2.2 * ShapeR, 2.2 * ShapeR), (Math.Cos(deg * Math.PI / 180), Math.Sin(deg * Math.PI / 180))));
        }
        var (analyticRms, analyticMax) = Difference(rendered, analyticCrossings.ToArray());
        Assert.True(analyticRms <= 0.10, $"the shape's edge is {analyticRms:F3} px rms from the analytic path");
        Assert.True(analyticMax <= 0.25, $"the shape's edge is {analyticMax:F3} px at worst from the analytic path");
        Assert.True(rms <= 0.10, $"the shape's edge is {rms:F3} px rms from the analytic path's");
        Assert.True(max <= 0.25, $"the shape's edge is {max:F3} px at worst from the analytic path's");

        // A test that cannot fail is no test: the circle (the deferral) is the same measurement 0.5 px off at 7 degrees from each edge.
        var circle = new Border { Width = ShapeW, Height = ShapeH, Background = Brushes.White }.RoundedRectangle(ShapeR, RoundedCornerStyle.Circular);
        var (circleRms, circleMax) = Difference(RenderedCrossings(Render(circle)), reference);
        Assert.True(circleMax > 0.30, $"the circle is only {circleMax:F3} px from the analytic path");
        Assert.True(rms < 0.6 * circleRms, $"the shape ({rms:F3}) is no nearer the analytic path than the circle ({circleRms:F3})");

        // A plain Border with `.continuous` corners is cut by Visual.Clip (ContinuousClip): its fill and the clip both anti-alias the edge, so
        // it lands between the circle and the exact shape (ED-275: measured 0.105 px rms against the circle's 0.135, SwiftShapeBorder 0).
        var border = new Border { Width = ShapeW, Height = ShapeH, Background = Brushes.White }.RoundedRectangle(ShapeR, RoundedCornerStyle.Continuous);
        var (borderRms, _) = Difference(RenderedCrossings(Render(border)), reference);
        Assert.NotNull(border.Clip);
        Assert.True(borderRms < circleRms, $"the clipped Border ({borderRms:F3}) is no nearer the analytic path than the circle ({circleRms:F3})");
    });

    /// <summary>The path of the whole shape as seen from <paramref name="corner"/> (mirrored into the top-left frame).</summary>
    private static List<Point> ToTopLeft(List<Point> path, double w, double h, int corner) => path.Select(p => corner switch
    {
        1 => new Point(w - p.X, p.Y),
        2 => new Point(w - p.X, h - p.Y),
        3 => new Point(p.X, h - p.Y),
        _ => p,
    }).ToList();

    /// <summary>Where a ray from <paramref name="origin"/> in <paramref name="direction"/> first crosses the polyline, as a distance along it (NaN: it does not).</summary>
    private static double RayCrossing(List<Point> poly, Point origin, (double X, double Y) direction)
    {
        var best = double.NaN;
        for (var i = 0; i < poly.Count - 1; i++)
        {
            var a = poly[i];
            var b = poly[i + 1];
            var ex = b.X - a.X;
            var ey = b.Y - a.Y;
            var den = direction.X * ey - direction.Y * ex;
            if (Math.Abs(den) < 1e-12) continue;
            var t = ((a.X - origin.X) * ey - (a.Y - origin.Y) * ex) / den;
            var s = ((a.X - origin.X) * direction.Y - (a.Y - origin.Y) * direction.X) / den;
            if (t > 0 && s >= 0 && s <= 1 && (double.IsNaN(best) || t > best)) best = t; // the farthest crossing: the boundary, not an inner fold
        }
        return best;
    }
}
