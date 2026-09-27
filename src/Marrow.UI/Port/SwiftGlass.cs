// Source: DroppyCode/UI/Chrome/Chrome.swift (B3 52bc792), :153-184, :218-259.
// PORT-ONLY: SwiftUI's .glassEffect(_:in:) over the app's own pixels (OD-0005, ADR-0002 Glass (b)).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Marrow.UI.Chrome;

namespace Marrow.UI.Port;

// Source data stays attached to the original control; a composition underlay draws the frost before that control.
// Over the desktop the window's Acrylic remains the material (OD-0005, ADR-0002 Glass (a), OD-0014).

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

    // :251-259 The clear window base uses native Acrylic and the source scrim, not opaque app frost.
    public static readonly AttachedProperty<bool> UsesDesktopBackdropProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("UsesDesktopBackdrop", typeof(SwiftGlass));

    // GlassEffectContainer(spacing:) is material proximity, not the content stack's layout spacing.
    public static readonly AttachedProperty<double?> ContainerSpacingProperty =
        AvaloniaProperty.RegisterAttached<Control, double?>("ContainerSpacing", typeof(SwiftGlass));

    static SwiftGlass()
    {
        EffectProperty.Changed.AddClassHandler<Control>((control, _) => GlassMaterial.Attach(control));
        UsesDesktopBackdropProperty.Changed.AddClassHandler<Control>((control, _) => GlassMaterial.Attach(control));
        ContainerSpacingProperty.Changed.AddClassHandler<Control>((control, _) => GlassMaterial.Attach(control));
    }

    public static GlassEffect? GetEffect(Control control) => control.GetValue(EffectProperty);

    public static void SetEffect(Control control, GlassEffect? effect) => control.SetValue(EffectProperty, effect);

    public static bool GetUsesDesktopBackdrop(Control control) => control.GetValue(UsesDesktopBackdropProperty);
    public static void SetUsesDesktopBackdrop(Control control, bool value) => control.SetValue(UsesDesktopBackdropProperty, value);
    public static double? GetContainerSpacing(Control control) => control.GetValue(ContainerSpacingProperty);
    public static void SetContainerSpacing(Control control, double? spacing) => control.SetValue(ContainerSpacingProperty, spacing);

    // Native SwiftUI .buttonStyle(.glass/.glassProminent), using the port's existing capsule recipe.
    // ModelsSettingsPage.swift:334-337; HydraCookbookPanel.swift:120-127. Native metrics UNVERIFIED (OD-0001).
    public static void StyleButton(Button button, bool prominent = false)
    {
        button.Padding = default;
        button.Template = new FuncControlTemplate<Button>((source, scope) =>
        {
            var label = new ContentPresenter
            {
                Name = "PART_ContentPresenter",
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                [!ContentPresenter.ContentProperty] = source[!ContentControl.ContentProperty],
            }.RegisterInNameScope(scope);
            var surface = new Border
            {
                Child = label,
                Height = Chrome.Chrome.CapsuleContentHeight,
                Padding = new Thickness(Chrome.Chrome.CapsuleHorizontalPadding, 0),
                Background = Brushes.Transparent,
            }.Capsule(RoundedCornerStyle.Continuous);
            var glass = GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous)).Interactive();
            if (prominent) glass = glass.Tint(Chrome.Chrome.Accent, 0.3);
            SetEffect(surface, glass);
            return surface;
        });
    }
}
