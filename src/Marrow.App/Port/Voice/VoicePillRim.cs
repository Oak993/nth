// PORT-ONLY, owner-ruled addition (OD-0091 fifth amendment, OD-0097; first-release voice typing, VT-12, voice-SPEC V-12.4): what the pill's glass is drawn with besides the shared material.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Marrow.Core.Port.Voice;
using Marrow.UI.Port;

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
        context.Custom(new VoiceInsetFringe(new Rect(size), Color.FromArgb(0x66, 96, 176, 255), -0.7, Color.FromArgb(0x61, 255, 96, 150), 0.7, 0.4f));
        context.Custom(new VoiceInsetRing(new Rect(size), 1, tokens.PgEdge));
        context.Custom(new VoiceInsetRing(new Rect(size), 0.5, tokens.PgRim));
        context.Custom(new VoicePillHighlights(new Rect(size), tokens.PgHi));
    }

    /// <summary>
    /// The clean-cap mask (V-12.12): <c>radial-gradient(circle at the orb's centre, transparent D/2 - 1, #000 H/2 + 1.5)</c>, transparent inside the ball's edge and
    /// opaque from the cap's radius plus 1.5, a straight ramp between. A radial brush in the control's own coordinates (not a drawing resampled to the box).
    /// </summary>
    public static IBrush OrbMask(Point centre, double pillHeight, double orbDiameter)
    {
        var inner = Math.Max(orbDiameter / 2 - 1, 0);
        var outer = Math.Max(pillHeight / 2 + 1.5, inner + 0.01);
        var at = new RelativePoint(centre, RelativeUnit.Absolute);
        return new ImmutableRadialGradientBrush(
            [new ImmutableGradientStop(0, Colors.Transparent), new ImmutableGradientStop(inner / outer, Colors.Transparent), new ImmutableGradientStop(1, Colors.Black)],
            center: at,
            gradientOrigin: at,
            radiusX: new RelativeScalar(outer, RelativeUnit.Absolute),
            radiusY: new RelativeScalar(outer, RelativeUnit.Absolute));
    }

    /// <summary>
    /// The side the orb sits on is cleared outright (OD-0121 5, V-T132): zero on the exposed side of the orb's centre and complete one cap radius toward the body. The radial
    /// <see cref="OrbMask"/> (the page's) is multiplied by it, as the page's pill, where the orb's own glow covers the band the radial mask leaves, is not in the port's frame. A direct linear brush in the control's own coordinates, like the radial one (no drawing resampled to the box; ED-569 5, ED-570).
    /// </summary>
    public static IBrush OrbFade(Point centre, double pillHeight, VoiceListeningStyle style)
    {
        var direction = style == VoiceListeningStyle.Float ? 1 : -1;
        return new ImmutableLinearGradientBrush(
            [new ImmutableGradientStop(0, Colors.Transparent), new ImmutableGradientStop(1, Colors.Black)],
            startPoint: new RelativePoint(centre, RelativeUnit.Absolute),
            endPoint: new RelativePoint(centre.X + direction * pillHeight / 2, centre.Y, RelativeUnit.Absolute));
    }
}
