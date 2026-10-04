using Avalonia.Media;

namespace Marrow.UI.Port;

/// <summary>
/// SwiftUI's <c>View.shadow(color:radius:x:y:)</c> as an Avalonia shadow (unit rec-shadow, ENGINEERING DECISION
/// ED-NEW-rec-shadow; lead ruling finding B, records UI-SIDEBAR-060 and UI-SIDEBAR-017).
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
