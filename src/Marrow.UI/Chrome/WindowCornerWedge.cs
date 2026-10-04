using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.UI.Port;

namespace Marrow.UI.Chrome;

/// <summary>
/// PORT-ONLY. OD-0075 (owner, 2026-10-02, on the choices canvas: "Window corner: the wedge is filled with the window's scrim"; settles
/// OD-0072). A window of this app is a clear Windows window blurred by the system (Acrylic, ED-236) that draws its own 26 point shape
/// (<see cref="Chrome.WindowCornerRadius"/>) over the blur, with the scrim <see cref="WindowBackdrop"/> paints. The system's window is a
/// rectangle (its corner rounded by 8 px at most, ED-236 UNRESOLVED (a)), so between the shape and the rectangle, a wedge about 7 px deep on the
/// diagonal, the blur shows without the scrim: measured luma 126 beside 93 inside the shape. On the Mac the corner outside the shape is clear;
/// here the ruling is that the wedge wears the window's scrim, so the corners read as the window's own body.
/// <para>
/// The layer fills the rectangle of the window minus the very rounded rectangle the window's content is clipped to (the same
/// <see cref="SwiftShapes.RoundedRectangleGeometry"/> at the same radius and style, so it follows the shape wherever ED-181 takes it), with the
/// scrim the window draws at that moment (<see cref="WindowBackdrop.Scrim"/>: the setting's opacity and the theme; a solid window, from
/// 0.98 up, is solid in its corners too). It sits BELOW the content's clip, so the glass and its hairline still stop at the curve, and it
/// takes no pointer.
/// </para>
/// <para>
/// In a window that turns over (<see cref="AppearanceTurn"/>, the Settings window: OD-0088, OD-0076, ED-254) the wedge's colour and alpha ease
/// with the scrim it wears, as the backdrop's do (its brush per channel premultiplied, its opacity along the turnover's curve, both from the
/// change's own frame), so the corners never change a frame before the rest of the window.
/// </para>
/// </summary>
public sealed class WindowCornerWedge : Control
{
    private double opacity;
    private (Color Color, double Alpha)? turning; // the turnover's values while it runs (ED-254)
    private (Color Color, double Alpha)? drawn; // what the last frame drew
    private Action stopTurn = static () => { };

    /// <param name="opacity">The window's backdrop opacity setting (<c>settings.backdropOpacity</c>, RootView.swift:64, SettingsView.swift:118).</param>
    public WindowCornerWedge(double opacity)
    {
        this.opacity = opacity;
        IsHitTestVisible = false;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        ActualThemeVariantChanged += (_, _) => Turn(); // the scrim is white in the light theme and black in the dark one
    }

    // ED-254 (OD-0088, OD-0076): in a tree that turns over, the appearance change eases from what the wedge shows to the new scrim, as
    // WindowBackdrop's scrim layer does (its SwiftColor brush, SwiftColor.PremultipliedMix; its opacity, AppearanceTurn.Run); at once elsewhere.
    private void Turn()
    {
        stopTurn();
        turning = null;
        var to = Target();
        if (drawn is not { } from || from == to || !AppearanceTurn.Eases(this))
        {
            InvalidateVisual();
            return;
        }
        stopTurn = AppearanceTurn.Run(this, progress =>
        {
            turning = progress >= 1 ? null : (SwiftColor.PremultipliedMix(from.Color, to.Color, progress), from.Alpha + (to.Alpha - from.Alpha) * progress);
            InvalidateVisual();
        });
    }

    private (Color Color, double Alpha) Target() => (ScrimColor(ActualThemeVariant), Alpha(opacity, PlainControls.IsDark(ActualThemeVariant)));

    // Chrome.swift:256-257 `isDark ? Color.black : Color.white`, the colour WindowBackdrop's scrim is filled with.
    private static Color ScrimColor(Avalonia.Styling.ThemeVariant theme) => SwiftColor.Dynamic(light: SwiftColor.White, dark: SwiftColor.Black).Resolve(theme);

    /// <summary>The window's backdrop opacity setting changed; the wedge follows the backdrop it is the corner of.</summary>
    public void Update(double opacity)
    {
        if (this.opacity == opacity) return;
        this.opacity = opacity;
        stopTurn();
        turning = null;
        InvalidateVisual();
    }

    /// <summary>The alpha of the scrim the wedge is filled with in the given theme: the one <see cref="WindowBackdrop"/> draws over its glass
    /// (Chrome.swift:211-215), or the whole fill when the window is solid (:220-224, :249-250).</summary>
    public double Alpha(bool isDark) => Alpha(opacity, isDark);

    /// <summary><see cref="Alpha(bool)"/> for a window whose backdrop is at <paramref name="opacity"/>.</summary>
    public static double Alpha(double opacity, bool isDark) => WindowBackdrop.IsSolid(opacity) ? 1 : WindowBackdrop.Scrim(opacity, isDark);

    /// <summary>The wedge of a window of this size: its rectangle minus the 26 point continuous rounded rectangle its content is clipped to.</summary>
    public static Geometry WedgeGeometry(Size size)
    {
        var window = new Rect(size);
        return new GeometryGroup
        {
            FillRule = FillRule.EvenOdd,
            Children =
            [
                new RectangleGeometry(window),
                SwiftShapes.RoundedRectangleGeometry(window, Chrome.WindowCornerRadius, RoundedCornerStyle.Continuous),
            ],
        };
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var shown = turning ?? Target();
        drawn = shown;
        Paint(context, Bounds.Size, shown.Color, shown.Alpha);
    }

    /// <summary>
    /// Draws the wedge of a window of <paramref name="size"/> whose backdrop is at <paramref name="opacity"/> in the given theme. The layer's own drawing, for a
    /// view that is itself the window's whole content and so paints the wedge under what it holds (the guide panels, <c>FloatingGuidePanelView</c>).
    /// </summary>
    public static void Paint(DrawingContext context, Size size, double opacity, Avalonia.Styling.ThemeVariant theme)
    {
        Paint(context, size, ScrimColor(theme), Alpha(opacity, PlainControls.IsDark(theme)));
    }

    private static void Paint(DrawingContext context, Size size, Color color, double alpha)
    {
        if (size.Width <= 0 || size.Height <= 0) return;
        context.DrawGeometry(new SolidColorBrush(color, alpha), null, WedgeGeometry(size));
    }
}
