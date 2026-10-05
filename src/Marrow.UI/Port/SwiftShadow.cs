using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace Marrow.UI.Port;

/// <summary>
/// SwiftUI's <c>View.shadow(color:radius:x:y:)</c> as an Avalonia shadow (unit rec-shadow, ENGINEERING DECISION
/// ED-327; lead ruling finding B, records UI-SIDEBAR-060 and UI-SIDEBAR-017).
/// <para>
/// SwiftUI: <c>.shadow(radius: r)</c> draws a Gaussian of standard deviation r (the CALayer shadowRadius
/// convention). Measured from the Mac video 2-chat-use.mov (118.0 s against 126.5 s) on the radius-28 panel scrim
/// (HydraPanel.swift:120): the least-squares sigma above the card is 24-28, where the "radius = 2 sigma" reading
/// (sigma 14) fits 6x and 25x worse.
/// </para>
/// <para>
/// Avalonia.Skia 12.1.2 turns <see cref="BoxShadow.Blur"/>, <see cref="DropShadowEffect.BlurRadius"/> and
/// <see cref="BlurEffect.Radius"/> into one Skia sigma, <c>0.288675 * blur + 0.5</c> (its SkBlurRadiusToSigma, read
/// in DrawingContextImpl.BoxShadowFilter.Create and CreateEffect; the same function LoadingMark's GaussianBlur
/// inverts), in the drawing units of the pushed transform. So the blur for sigma r is <c>(r - 0.5) / 0.288675</c>.
/// Measured by SwiftShadowHeadlessTests: the raster blur draws that sigma to within its window step (a few
/// percent; r 1.5, 10 and 28 render 1.43, 9.71 and 27.20 px headless).
/// </para>
/// Every port of a Swift <c>.shadow(radius:)</c> builds its shadow here.
/// <para>
/// A shadow cast by a FILLED SHAPE (<c>shape.fill(fill).shadow(...)</c>: the panel scrims, the dragged rows' lifts) is the fill's
/// own coverage, blurred and coloured (ED-NEW-glass-g3, part A): SwiftUI draws a view's shadow from the view's rendered alpha, so the
/// shadow's strength is the shadow colour's alpha times the fill's (<see cref="Cast"/>), and it is drawn UNDER the fill, so through a
/// translucent fill it shows (<see cref="Caster"/>, <see cref="DrawCast"/>). Avalonia's BoxShadow does neither: it draws the colour's full
/// alpha and only outside the box (DrawingContextImpl.DrawRectangle clips the box out, SKClipOperation.Difference). Measured on the Mac
/// video (frames 18 and 31 against frame 01, the head card's scrim, black 0.3 casting black 1.0 at radius 28, y 10): the shadow is
/// 0.095 one point above the card, 0.13 beside it, 0.07 at 8 points above and nothing past 46, which is 0.3 x Phi(-(d + 10) / 28) to
/// 0.02; the colour's full alpha would draw 0.35 and 0.49 there.
/// </para>
/// </summary>
public static class SwiftShadow
{
    /// <summary>The Skia sigma Avalonia gives a blur value b is <c>Slope * b + Floor</c>.</summary>
    public const double Slope = 0.288675, Floor = 0.5;

    /// <summary>
    /// The Avalonia blur value (BoxShadow.Blur, DropShadowEffect.BlurRadius) whose drawn Gaussian has the standard
    /// deviation <paramref name="radius"/>, SwiftUI's <c>.shadow(radius:)</c>. A radius at or below 0.5 (under
    /// Skia's least blurred sigma) draws an unblurred shadow.
    /// </summary>
    public static double Blur(double radius) => radius <= Floor ? 0 : (radius - Floor) / Slope;

    /// <summary><c>.shadow(color: color, radius: radius, x: x, y: y)</c> as one Avalonia box shadow.</summary>
    public static BoxShadow BoxShadow(Color color, double radius, double x = 0, double y = 0) =>
        new() { Color = color, Blur = Blur(radius), OffsetX = x, OffsetY = y };

    /// <summary><c>.shadow(color: color, radius: radius, x: x, y: y)</c> as a Border's or a drawn rectangle's box shadows.</summary>
    public static BoxShadows BoxShadows(Color color, double radius, double x = 0, double y = 0) =>
        new(BoxShadow(color, radius, x, y));

    /// <summary>
    /// The shadow a filled shape casts: <c>shape.fill(f).shadow(color: color, ...)</c> draws <paramref name="color"/> at its alpha
    /// times the fill's alpha <paramref name="fillAlpha"/> (0 to 1), because SwiftUI's shadow is the view's own rendered alpha
    /// (ED-NEW-glass-g3). The scrim's black 0.3 casting black 1.0 draws a shadow of 0.3; the lifts' <c>Chrome.overlay(0.12)</c> (the
    /// label colour's 0.85 at 0.12: 0.102) casting black 0.28 draws 0.029.
    /// </summary>
    public static Color Cast(Color color, double fillAlpha) =>
        Color.FromArgb((byte)Math.Round(color.A * Math.Clamp(fillAlpha, 0, 1)), color.R, color.G, color.B);

    /// <summary>The alpha (0 to 1) a fill brush draws with: a solid brush's colour alpha times the brush's opacity; no brush draws nothing.</summary>
    public static double FillAlpha(IBrush? fill) => fill switch
    {
        null => 0,
        ISolidColorBrush solid => solid.Color.A / 255.0 * solid.Opacity,
        _ => fill.Opacity,
    };

    /// <summary>
    /// Draws the shadow of a filled <paramref name="shape"/> (local coordinates of the context) UNDER where the fill goes: the shape's
    /// coverage blurred by a Gaussian of standard deviation <paramref name="radius"/> (SwiftUI's <c>.shadow(radius:)</c>, ED-327), moved by
    /// (<paramref name="x"/>, <paramref name="y"/>) and painted <paramref name="color"/>, whole (not cut out of the shape). Pass the cast colour
    /// (<see cref="Cast"/>). Drawn by Skia's blurred rounded rectangle (a mask filter; on the GPU the analytic blur), in drawing units, so the
    /// sigma follows the transform as the radius does in points. Then draw the fill over it.
    /// </summary>
    public static void DrawCast(DrawingContext context, RoundedRect shape, Color color, double radius, double x = 0, double y = 0)
    {
        if (color.A == 0 || shape.Rect.Width <= 0 || shape.Rect.Height <= 0) return;
        context.Custom(new CastOperation(shape, color, Math.Max(0, radius), new Vector(x, y)));
    }

    // The shadow of a filled rounded rectangle, drawn whole: Skia's blur mask filter of the shape, offset. Bounds reach 3.5 sigma past it.
    private sealed class CastOperation(RoundedRect shape, Color color, double sigma, Vector offset) : ICustomDrawOperation
    {
        public Rect Bounds { get; } = shape.Rect.Translate(offset).Inflate(3.5 * sigma + 1);
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) =>
            other is CastOperation o && o.Shape == Shape && o.Colour == Colour && o.Sigma == Sigma && o.Offset == Offset;
        public void Dispose() { }
        private RoundedRect Shape => shape;
        private Color Colour => color;
        private double Sigma => sigma;
        private Vector Offset => offset;

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            var r = shape.Rect;
            using var rrect = new SKRoundRect();
            rrect.SetRectRadii(new SKRect((float)r.Left, (float)r.Top, (float)r.Right, (float)r.Bottom),
            [
                new SKPoint((float)shape.RadiiTopLeft.X, (float)shape.RadiiTopLeft.Y), new SKPoint((float)shape.RadiiTopRight.X, (float)shape.RadiiTopRight.Y),
                new SKPoint((float)shape.RadiiBottomRight.X, (float)shape.RadiiBottomRight.Y), new SKPoint((float)shape.RadiiBottomLeft.X, (float)shape.RadiiBottomLeft.Y),
            ]);
            using var blur = sigma > 0 ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)sigma) : null;
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Color = new SKColor(color.R, color.G, color.B, (byte)Math.Round(color.A * Math.Clamp(lease.CurrentOpacity, 0, 1))),
                MaskFilter = blur,
            };
            canvas.Save();
            canvas.Translate((float)offset.X, (float)offset.Y);
            canvas.DrawRoundRect(rrect, paint);
            canvas.Restore();
        }
    }

    /// <summary>
    /// <c>shape.fill(f).shadow(color:radius:x:y:)</c> for a fill drawn by a Border (<see cref="Source"/>): put the caster just BEFORE the
    /// Border among the same parent's children (the shadow goes under the fill). It draws the Border's shape (its bounds and corner radii,
    /// wherever the Border is drawn, its own render transform included) casting <see cref="Color"/> at the Border's fill alpha
    /// (<see cref="FillAlpha(IBrush?)"/> of its Background, times its Opacity; nothing while it has no Background or is hidden), whole, so a
    /// translucent fill shows it through (ED-NEW-glass-g3). The Border itself carries no BoxShadow.
    /// </summary>
    public sealed class Caster : Control
    {
        private Color color;
        private double radius, x, y;

        public Caster(Border source, Color color, double radius, double x = 0, double y = 0)
        {
            Source = source;
            (this.color, this.radius, this.x, this.y) = (color, radius, x, y);
            IsHitTestVisible = false;
            source.PropertyChanged += (_, e) =>
            {
                if (e.Property == Border.BackgroundProperty || e.Property == OpacityProperty || e.Property == Border.CornerRadiusProperty ||
                    e.Property == BoundsProperty || e.Property == IsVisibleProperty || e.Property == RenderTransformProperty ||
                    e.Property == RenderTransformOriginProperty)
                    InvalidateVisual();
            };
        }

        /// <summary>The Border whose fill casts the shadow.</summary>
        public Border Source { get; }

        /// <summary>The shadow colour as the Swift states it (<c>.shadow(color:)</c>), before the fill's alpha.</summary>
        public Color Color => color;

        /// <summary>The Swift <c>.shadow(radius:)</c>: the Gaussian's standard deviation in points (ED-327).</summary>
        public double Radius => radius;

        /// <summary>The Swift <c>.shadow(x:)</c>.</summary>
        public double X => x;

        /// <summary>The Swift <c>.shadow(y:)</c>.</summary>
        public double Y => y;

        /// <summary>The alpha the source fills with: its Background's (<see cref="FillAlpha(IBrush?)"/>) times its Opacity, 0 while hidden.</summary>
        public double FillAlpha => Source.IsVisible ? SwiftShadow.FillAlpha(Source.Background) * Source.Opacity : 0;

        /// <summary>The colour drawn: <see cref="Color"/> cast by the fill (<see cref="Cast"/>).</summary>
        public Color DrawnColor => Cast(color, FillAlpha);

        /// <summary>A new <c>.shadow(color:radius:x:y:)</c> (the Swift reads <c>isDark</c> in <c>body</c>: the colour follows the theme).</summary>
        public void Set(Color color, double radius, double x = 0, double y = 0)
        {
            if ((this.color, this.radius, this.x, this.y) == (color, radius, x, y)) return;
            (this.color, this.radius, this.x, this.y) = (color, radius, x, y);
            InvalidateVisual();
        }

        public override void Render(DrawingContext context) => DrawShadowOf(context, this, Source, color, radius, x, y);
    }

    /// <summary>
    /// What a <see cref="Caster"/> draws, for a view that draws it itself in its own Render (before its children, so under them):
    /// <paramref name="source"/>'s fill casting <paramref name="color"/> at the fill's alpha, in <paramref name="target"/>'s coordinates.
    /// </summary>
    public static void DrawShadowOf(DrawingContext context, Visual target, Border source, Color color, double radius, double x = 0, double y = 0)
    {
        var drawn = Cast(color, source.IsVisible ? FillAlpha(source.Background) * source.Opacity : 0);
        var size = source.Bounds.Size;
        if (drawn.A == 0 || size.Width <= 0 || size.Height <= 0 || source.TransformToVisual(target) is not { } toTarget) return;
        using (context.PushTransform(toTarget))
            DrawCast(context, new RoundedRect(new Rect(size), source.CornerRadius), drawn, radius, x, y);
    }

    /// <summary>
    /// <c>.shadow(color: color.opacity(opacity), radius: radius, x: x, y: y)</c> on a view whose drawn content (not
    /// its frame) casts the shadow, as an effect.
    /// </summary>
    public static DropShadowEffect DropShadowEffect(Color color, double opacity, double radius, double x = 0, double y = 0) => new()
    {
        Color = color,
        Opacity = opacity,
        BlurRadius = Blur(radius),
        OffsetX = x,
        OffsetY = y,
    };
}
