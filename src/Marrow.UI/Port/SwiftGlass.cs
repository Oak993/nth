// PORT-ONLY: SwiftUI's .glassEffect(_:in:) (macOS Liquid Glass) has no Avalonia counterpart. ED-180 records each glass
// site's source-defined material as data on the layer the site draws, over the backdrop the port already draws.
using Avalonia;
using Avalonia.Controls;

namespace Marrow.UI.Port;

// ED-180 (orchestrator ruling on the UI.Chrome.Chrome stop #2): a glass site keeps its shape and corner radius, its tint
// colour and opacity and its .interactive() flag as a GlassEffect on the layer it draws; no compositor draws it yet.
// Over the desktop the window's Acrylic is the material under the tint (OD-0005, ADR-0002 Glass (a), OD-0014).
// MARROW_DEFERRED ED-180 (tracked incomplete work): the app-drawn glass material over a wallpaper or the solid base and
// the .interactive() press response (OD-0005, ADR-0002 Glass (b)); the sites carry `// MARROW_DEFERRED ED-180`.

/// <summary>The shape a glass effect is drawn in (<c>.glassEffect(_:in:)</c>'s <c>in:</c>).</summary>
public enum GlassShapeKind
{
    RoundedRectangle,
    Capsule,
    Circle,
}

/// <summary>
/// A glass effect's shape: a rounded rectangle with its corner radius and style, a capsule with its style (its radius is
/// half its shorter side, SwiftShapes.CapsuleCornerRadius) or a circle.
/// </summary>
public sealed record GlassShape(GlassShapeKind Kind, double? CornerRadius, RoundedCornerStyle? Style)
{
    /// <summary><c>RoundedRectangle(cornerRadius:style:)</c>.</summary>
    public static GlassShape RoundedRectangle(double cornerRadius, RoundedCornerStyle style) =>
        new(GlassShapeKind.RoundedRectangle, cornerRadius, style);

    /// <summary><c>Capsule(style:)</c>.</summary>
    public static GlassShape Capsule(RoundedCornerStyle style) => new(GlassShapeKind.Capsule, null, style);

    /// <summary><c>Circle()</c>.</summary>
    public static GlassShape Circle { get; } = new(GlassShapeKind.Circle, null, null);
}

/// <summary>
/// SwiftUI <c>Glass</c> as a site applies it: <c>.regular</c>, optionally <c>.tint(color.opacity(o))</c> (the colour and
/// the opacity kept apart) and <c>.interactive()</c>, in a shape.
/// </summary>
public sealed record GlassEffect(GlassShape Shape, SwiftColor? TintColor, double? TintOpacity, bool IsInteractive)
{
    /// <summary><c>.glassEffect(in: shape)</c>: the regular glass, no tint, no press response.</summary>
    public static GlassEffect Regular(GlassShape shape) => new(shape, null, null, false);

    /// <summary><c>.tint(color.opacity(opacity))</c>.</summary>
    public GlassEffect Tint(SwiftColor color, double opacity) => this with { TintColor = color, TintOpacity = opacity };

    /// <summary><c>.interactive()</c>.</summary>
    public GlassEffect Interactive() => this with { IsInteractive = true };
}

/// <summary>The glass effect recorded on a layer (ED-180).</summary>
public static class SwiftGlass
{
    public static readonly AttachedProperty<GlassEffect?> EffectProperty =
        AvaloniaProperty.RegisterAttached<Control, GlassEffect?>("Effect", typeof(SwiftGlass));

    public static GlassEffect? GetEffect(Control control) => control.GetValue(EffectProperty);

    public static void SetEffect(Control control, GlassEffect? effect) => control.SetValue(EffectProperty, effect);
}
