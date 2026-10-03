// PORT-ONLY: SwiftUI's `.continuous` RoundedCornerStyle (the continuous-curvature corner Apple draws for
// RoundedRectangle(cornerRadius:style: .continuous), Capsule, UnevenRoundedRectangle and CALayer.cornerCurve = .continuous).
// The curve is in no pinned source and in no Apple document (SOURCE FACT, ED-181); its constants are the ones a third
// party measured from Apple's runtime and the reference video confirms on macOS 26 (build spec
// E:\Droppy\work\design\build-specs\continuous-corners\SPEC.md sections 1 and 2, closes ED-181):
//   Liam Rosenfeld, "My Quest for the Apple Icon Shape" (2021-10-12): UIBezierPath(roundedRect:cornerRadius:).cgPath,
//   element by element, 8 decimals; the same numbers at 6 decimals in NativeScript issue #11452 (2026-09-25), which says
//   they render pixel-identical to a CALayer with cornerCurve = .continuous.
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;

[assembly: InternalsVisibleTo("Marrow.UI.Tests")]
[assembly: InternalsVisibleTo("Marrow.App.Tests")]

namespace Marrow.UI.Port;

/// <summary>
/// The continuous corner: one corner is three cubic Beziers that ease the circular corner in and out over
/// <see cref="Extent"/> radii instead of one. Coordinates of a unit corner are <c>(u, v)</c> in units of the radius:
/// <c>u</c> along the edge measured from the corner's vertex, <c>v</c> from that edge into the shape.
/// </summary>
public static class ContinuousCorner
{
    /// <summary>
    /// How far along each edge the corner starts, in radii (CALayer.cornerCurveExpansionFactor(.continuous) as quoted by
    /// NativeScript #11452). ENGINEERING DECISION: one constant for both ends; the measurement differs by 2e-7 r
    /// between them, which is numeric noise.
    /// </summary>
    public const double Extent = 1.528665;

    /// <summary>
    /// The corner from the edge toward the diagonal and on to the other edge: three cubics, each as start, control 1,
    /// control 2, end (the zero-length line UIKit emits between C1 and C2 adds no geometry and is left out, an
    /// equivalent mutant). C2 is symmetric about the diagonal; C3 is C1 with <c>u</c> and <c>v</c> swapped and traversed
    /// backwards.
    /// </summary>
    public static IReadOnlyList<UnitCubic> Cubics { get; } =
    [
        new(new Point(Extent, 0), new Point(1.08849296, 0), new Point(0.86840694, 0), new Point(0.63149379, 0.07491139)),
        new(new Point(0.63149379, 0.07491139), new Point(0.37282383, 0.16905956), new Point(0.16905956, 0.37282383), new Point(0.07491139, 0.63149379)),
        new(new Point(0.07491139, 0.63149379), new Point(0, 0.86840694), new Point(0, 1.08849296), new Point(0, Extent)),
    ];

    /// <summary>One cubic of the unit corner.</summary>
    public readonly record struct UnitCubic(Point Start, Point Control1, Point Control2, Point End);

    /// <summary>
    /// The largest radius at which the continuous corner is drawn on a shape of this size: both neighbouring corners
    /// need their run-in of <see cref="Extent"/> radii along a side, so <c>2 * Extent * r &lt;= min(width, height)</c>.
    /// ENGINEERING DECISION backed by the reference video (SPEC.md 2.3): above it (a capsule, the composer's 22 pt corner
    /// on a 46 pt bar) the video shows circular ends, not PaintCode's clamped continuous corner.
    /// </summary>
    public static double LargestRadius(Size size) => Math.Min(size.Width, size.Height) / (2 * Extent);

    // ---- The glass shader's corner distance (GlassMaterial.cs) -------------------------------------------------------
    // The glass draws one signed distance field per shape. For a continuous corner that field is the circular corner's
    // plus the offset of Apple's curve from the circle along the circle's normal: Apple's curve lies up to 0.0138 r inside
    // the circle (at (u, v) = (0.869, 0.0225) r) and up to 0.0020 r outside it (on the diagonal). The offset is a smooth
    // function of the position along the corner: on the run-in (the edge, between 1 and Extent radii from the vertex) the
    // height of C1 above the edge, a polynomial in w = (Extent - a) / (Extent - 1); on the arc, 1 - rho(phi) with rho the
    // distance from the circle's centre to the curve at polar angle phi, one polynomial for C1's part and one for C2's.
    // The coefficients are least-squares fits to the exact curve (max error 5e-7 r, 1.7e-5 r and 5.6e-7 r;
    // E:\Droppy\work\scratch\corners\derive.py), so the zero level of the field lies on the curve to 2e-5 r.

    /// <summary>The run-in height is <c>w^3 * (c0 + c1 w + c2 w^2 + c3 w^3)</c>, in radii.</summary>
    public static readonly double[] RunInCoefficients = [0.004854697399116449, 0.0026557196871704004, 0.0016271537201413195, 0.0004883276877320331];

    /// <summary>The polar angle (from the circle's centre, measured from the edge-normal direction) where C1 ends and C2 begins.</summary>
    public const double JunctionAngle = 0.3790805070308412;

    /// <summary>The offset on C1's part of the arc, <c>s = phi / JunctionAngle</c>, highest power first.</summary>
    public static readonly double[] ArcOneCoefficients = [-0.006415064682993001, 0.02258763636483788, -0.04770869965709548, 0.02615335576010935, 0.009611732796377132];

    /// <summary>The offset on C2's part of the arc, <c>s = (phi - JunctionAngle) / (pi / 4 - JunctionAngle)</c>, highest power first.</summary>
    public static readonly double[] ArcTwoCoefficients = [-0.00020707242323675273, 0.000588040091144515, 0.0056080206467140304, -0.012166649503177612, 0.004216646427064521];

    /// <summary>
    /// How far Apple's curve lies inside the circular corner of the same radius at a point <paramref name="fromEdges"/>
    /// (its distances from the shape's two nearest edges, in points), in points; negative where it lies outside. Zero
    /// from <see cref="Extent"/> radii out.
    /// </summary>
    public static double Offset(Point fromEdges, double radius)
    {
        var a = Math.Max(fromEdges.X, fromEdges.Y) / radius;
        var b = Math.Min(fromEdges.X, fromEdges.Y) / radius;
        if (a >= Extent) return 0;
        double k;
        if (a >= 1)
        {
            var w = (Extent - a) / (Extent - 1);
            k = w * w * w * (((RunInCoefficients[3] * w + RunInCoefficients[2]) * w + RunInCoefficients[1]) * w + RunInCoefficients[0]);
        }
        else
        {
            var phi = Math.Atan2(1 - a, 1 - b);
            if (phi <= JunctionAngle)
            {
                var s = phi / JunctionAngle;
                k = Horner(ArcOneCoefficients, s);
            }
            else
            {
                var s = (phi - JunctionAngle) / (Math.PI / 4 - JunctionAngle);
                k = Horner(ArcTwoCoefficients, s);
            }
        }
        return k * radius;
    }

    private static double Horner(double[] coefficients, double x)
    {
        var value = coefficients[0];
        for (var i = 1; i < coefficients.Length; i++) value = value * x + coefficients[i];
        return value;
    }

    /// <summary>
    /// The signed distance (negative inside) from <paramref name="point"/> (relative to the shape's centre) to a rounded
    /// rectangle of half extents <paramref name="half"/> and corner radius <paramref name="radius"/> whose corners are
    /// continuous where <see cref="LargestRadius"/> allows and circular otherwise. The C# mirror of the glass shader's
    /// <c>shapeDistance</c>; the tests evaluate it against the path and against the shader itself.
    /// </summary>
    public static double Distance(Point point, Size half, double radius, bool continuous)
    {
        radius = Math.Min(radius, Math.Min(half.Width, half.Height));
        var qx = Math.Abs(point.X) - half.Width + radius;
        var qy = Math.Abs(point.Y) - half.Height + radius;
        var outside = Math.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) + Math.Max(qy, 0) * Math.Max(qy, 0));
        var circle = outside + Math.Min(Math.Max(qx, qy), 0) - radius;
        if (!continuous || radius <= 0 || radius > Math.Min(half.Width, half.Height) / Extent) return circle;
        var offset = Offset(new Point(half.Width - Math.Abs(point.X), half.Height - Math.Abs(point.Y)), radius);
        return circle + offset * FadeWeight(circle, radius);
    }

    /// <summary>The offset is applied in full near the outline and fades to nothing inside a corner's centre, where the polar angle is undefined.</summary>
    public static double FadeWeight(double circleDistance, double radius)
    {
        var t = Math.Clamp((Math.Abs(circleDistance) - 0.15 * radius) / (0.45 * radius), 0, 1);
        return 1 - t * t * (3 - 2 * t);
    }

    // ---- The glass shader's text -------------------------------------------------------------------------------------

    /// <summary>
    /// SkSL for <c>float continuousOffset(float2 d, float radius)</c>, <c>float continuousFade(float cd, float radius)</c> and
    /// <c>float continuousCorner(float cd, float2 c, float2 p, float radius)</c> (the circular distance <c>cd</c> to a corner with half extents <c>c</c> at <c>p</c> from the centre, made Apple's),
    /// generated from the constants above so the shader and <see cref="Offset"/> cannot drift apart.
    /// </summary>
    internal static string ShaderSource()
    {
        static string F(double value)
        {
            var text = value.ToString("R", CultureInfo.InvariantCulture).Replace('E', 'e');
            return text.Contains('.', StringComparison.Ordinal) || text.Contains('e', StringComparison.Ordinal) ? text : text + ".0";
        }
        static string Poly(double[] c, string x)
        {
            var text = F(c[0]);
            for (var i = 1; i < c.Length; i++) text = $"({text} * {x} + {F(c[i])})";
            return text;
        }
        var run = RunInCoefficients;
        var runIn = $"w * w * w * ((({F(run[3])} * w + {F(run[2])}) * w + {F(run[1])}) * w + {F(run[0])})";
        return $$"""
            float continuousOffset(float2 d, float radius) {
                float a = max(d.x, d.y) / radius;
                float b = min(d.x, d.y) / radius;
                if (a >= {{F(Extent)}}) return 0.0;
                float k;
                if (a >= 1.0) {
                    float w = ({{F(Extent)}} - a) / {{F(Extent - 1)}};
                    k = {{runIn}};
                } else {
                    float phi = atan(1.0 - a, 1.0 - b);
                    if (phi <= {{F(JunctionAngle)}}) {
                        float s = phi / {{F(JunctionAngle)}};
                        k = {{Poly(ArcOneCoefficients, "s")}};
                    } else {
                        float s = (phi - {{F(JunctionAngle)}}) / {{F(Math.PI / 4 - JunctionAngle)}};
                        k = {{Poly(ArcTwoCoefficients, "s")}};
                    }
                }
                return k * radius;
            }
            float continuousFade(float cd, float radius) {
                float t = clamp((abs(cd) - 0.15 * radius) / (0.45 * radius), 0.0, 1.0);
                return 1.0 - t * t * (3.0 - 2.0 * t);
            }
            float continuousCorner(float cd, float2 c, float2 p, float radius) {
                if (radius <= 0.0 || radius > min(c.x, c.y) / {{F(Extent)}}) return cd;
                return cd + continuousOffset(c - abs(p), radius) * continuousFade(cd, radius);
            }
            """;
    }
}
