// PORT-ONLY, owner-ruled addition (OD-0091 fifth amendment, OD-0097; first-release voice typing, VT-12, voice-SPEC V-12.4): what the pill's glass is drawn with besides the shared material.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Marrow.Core.Port.Voice;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Port.Voice;

/// <summary>
/// The pill's edge and light over its glass (voice-composer.html <c>.vpill</c> and <c>.vpill::before</c>): the inner shade <c>pgShade</c> at the bottom (<c>inset 0 -2px 4px</c>), the dispersion fringe
/// (pink <c>rgba(255,96,150,.38)</c> at the left, blue <c>rgba(96,176,255,.4)</c> at the right, 0.7 px), the 1 px <c>pgEdge</c> ring and the 0.5 px <c>pgRim</c> ring, then at 0.55 opacity the soft top
/// highlight (<c>radial-gradient(70% 55% at 50% 0%, pgHi, transparent 70%)</c>) and the two refracting end bands (white 0.10, 14 px, 70 % of the height). The body, frost and tint are the shared
/// <see cref="Marrow.UI.Port.GlassMaterial"/>'s, under this. The drop shadow of V-12.4 is not drawn (see <see cref="VoicePill"/>). Everything here fades out round the orb under the pill's mask (V-12.12).
/// </summary>
internal sealed class VoicePillRim : Control
{
    public VoicePillRim()
    {
        IsHitTestVisible = false;
        Focusable = false;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0) return;
        var tokens = VoicePillTokens.For(ActualThemeVariant);
        var round = new RoundedRect(new Rect(size), size.Height / 2);
        // A CSS blur B is a Gaussian of sigma B / 2 (inset 0 -2px 4px: sigma 2); Avalonia's BoxShadow.Blur is not (its sigma is 0.288675 b + 0.5), so the blur is converted.
        context.DrawRectangle(null, null, round, new BoxShadows(new BoxShadow { IsInset = true, OffsetY = -2, Blur = SwiftShadow.Blur(2.0), Color = tokens.PgShade }));
        // The fringe's .8px blur is sigma 0.4, below the 0.5 Avalonia's box shadow cannot go under: drawn in Skia directly.
        context.Custom(new Fringe(new Rect(size), Color.FromArgb(0x66, 96, 176, 255), -0.7, Color.FromArgb(0x61, 255, 96, 150), 0.7));
        context.DrawRectangle(null, null, round, new BoxShadows(new BoxShadow { IsInset = true, Spread = 1, Color = tokens.PgEdge }));
        context.DrawRectangle(null, null, round, new BoxShadows(new BoxShadow { IsInset = true, Spread = 0.5, Color = tokens.PgRim }));
        using (context.PushClip(round))
        using (context.PushOpacity(0.55))
        {
            Ellipse(context, size.Width / 2, 0, 0.70 * size.Width, 0.55 * size.Height, tokens.PgHi, 0.70, new Rect(size));
            var white = Color.FromArgb(0x1A, 255, 255, 255);
            Ellipse(context, 0, size.Height / 2, 14, 0.70 * size.Height, white, 1, new Rect(size));
            Ellipse(context, size.Width, size.Height / 2, 14, 0.70 * size.Height, white, 1, new Rect(size));
        }
    }

    // radial-gradient(rx ry at cx cy, colour 0%, transparent stop) over the box: a circle of radius 1 drawn through a scale to the ellipse
    private static void Ellipse(DrawingContext context, double cx, double cy, double rx, double ry, Color colour, double stop, Rect box)
    {
        var brush = new ImmutableRadialGradientBrush(
            [new ImmutableGradientStop(0, colour), new ImmutableGradientStop(stop, Color.FromArgb(0, colour.R, colour.G, colour.B))],
            center: new RelativePoint(0, 0, RelativeUnit.Absolute),
            gradientOrigin: new RelativePoint(0, 0, RelativeUnit.Absolute),
            radiusX: new RelativeScalar(1, RelativeUnit.Absolute),
            radiusY: new RelativeScalar(1, RelativeUnit.Absolute));
        using (context.PushTransform(Matrix.CreateScale(rx, ry) * Matrix.CreateTranslation(cx, cy)))
        {
            var inverse = (Matrix.CreateScale(rx, ry) * Matrix.CreateTranslation(cx, cy)).Invert();
            context.DrawRectangle(brush, null, new Rect(box.TopLeft * inverse, box.BottomRight * inverse));
        }
    }

    /// <summary>
    /// The two dispersion-fringe insets, <c>inset .7px 0 .8px -.1px</c> in blue (offset -0.7) and pink (offset +0.7): the padding-box rounded rectangle minus the same
    /// rectangle grown by the spread -0.1 and moved by the offset, painted through a Gaussian of sigma 0.4 (CSS blur .8 / 2) and clipped to the padding box.
    /// Blue first, pink over it (the stack lists pink first).
    /// </summary>
    private sealed class Fringe(Rect box, Color first, double firstOffset, Color second, double secondOffset) : ICustomDrawOperation
    {
        private const float Sigma = 0.4f, Spread = -0.1f;

        public Rect Bounds => box;

        public bool HitTest(Point point) => false;

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null) return;
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            var rect = new SKRect((float)box.Left, (float)box.Top, (float)box.Right, (float)box.Bottom);
            var radius = rect.Height / 2;
            canvas.Save();
            canvas.ClipRoundRect(new SKRoundRect(rect, radius, radius), SKClipOperation.Intersect, true);
            Draw(canvas, rect, first, (float)firstOffset, (float)lease.CurrentOpacity);
            Draw(canvas, rect, second, (float)secondOffset, (float)lease.CurrentOpacity);
            canvas.Restore();
        }

        private static void Draw(SKCanvas canvas, SKRect rect, Color color, float offset, float opacity)
        {
            var hole = new SKRect(rect.Left - Spread + offset, rect.Top - Spread, rect.Right + Spread + offset, rect.Bottom + Spread);
            var holeRadius = hole.Height / 2;
            using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
            path.AddRect(SKRect.Inflate(rect, 8, 8));
            path.AddRoundRect(hole, holeRadius, holeRadius);
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Color = new SKColor(color.R, color.G, color.B, (byte)Math.Round(color.A * Math.Clamp(opacity, 0, 1))),
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Sigma),
            };
            canvas.DrawPath(path, paint);
        }
    }

    /// <summary>The clean-cap fade, zero on the exposed side and complete one cap radius toward the body.</summary>
    public static IBrush OrbMask(Point centre, double pillHeight, double width, VoiceListeningStyle style)
    {
        var direction = style == VoiceListeningStyle.Float ? 1 : -1;
        var inward = new ImmutableLinearGradientBrush(
            [new ImmutableGradientStop(0, Colors.Transparent), new ImmutableGradientStop(1, Colors.Black)],
            startPoint: new RelativePoint(centre, RelativeUnit.Absolute),
            endPoint: new RelativePoint(centre.X + direction * pillHeight / 2, centre.Y, RelativeUnit.Absolute));
        var drawing = new DrawingGroup();
        using (var context = drawing.Open()) context.DrawRectangle(inward, null, new Rect(0, 0, width, pillHeight));
        return new DrawingBrush(drawing) { Stretch = Stretch.Fill };
    }
}
