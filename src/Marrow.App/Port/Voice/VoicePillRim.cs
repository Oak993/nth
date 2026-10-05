// PORT-ONLY, owner-ruled addition (OD-0091 fifth amendment, OD-0097; first-release voice typing, VT-12, voice-SPEC V-12.4): what the pill's glass is drawn with besides the shared material.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Marrow.Core.Port.Voice;

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
        context.DrawRectangle(null, null, round, new BoxShadows(new BoxShadow { IsInset = true, OffsetY = -2, Blur = 4, Color = tokens.PgShade }));
        context.DrawRectangle(null, null, round, new BoxShadows(new BoxShadow { IsInset = true, OffsetX = -0.7, Blur = 0.8, Spread = -0.1, Color = Color.FromArgb(0x66, 96, 176, 255) }));
        context.DrawRectangle(null, null, round, new BoxShadows(new BoxShadow { IsInset = true, OffsetX = 0.7, Blur = 0.8, Spread = -0.1, Color = Color.FromArgb(0x61, 255, 96, 150) }));
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
    /// The radial mask that fades the pill's material out round the orb (V-12.12): transparent inside <c>D/2 - 1</c>, opaque from <c>H/2 + 1.5</c>, centred on the orb (the pill's right cap inline, its left
    /// cap in the float pill).
    /// </summary>
    public static IBrush OrbMask(Point centre, double orbDiameter, double pillHeight)
    {
        var outer = pillHeight / 2 + 1.5;
        var inner = Math.Clamp((orbDiameter / 2 - 1) / outer, 0, 1);
        return new ImmutableRadialGradientBrush(
            [new ImmutableGradientStop(0, Color.FromArgb(0, 0, 0, 0)), new ImmutableGradientStop(inner, Color.FromArgb(0, 0, 0, 0)), new ImmutableGradientStop(1, Colors.Black)],
            center: new RelativePoint(centre, RelativeUnit.Absolute),
            gradientOrigin: new RelativePoint(centre, RelativeUnit.Absolute),
            radiusX: new RelativeScalar(outer, RelativeUnit.Absolute),
            radiusY: new RelativeScalar(outer, RelativeUnit.Absolute));
    }
}
