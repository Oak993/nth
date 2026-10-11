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
public sealed record GlassShape(GlassShapeKind Kind, double? CornerRadius, RoundedCornerStyle? Style, bool CircularInBand = false)
{
    /// <summary>
    /// <c>RoundedRectangle(cornerRadius:style:)</c>. <paramref name="circularInBand"/> is <see cref="SwiftShapes.CircularInBandProperty"/>: the one shape a
    /// reference frame measures as the circular arc where Apple's corner does not fit at the radius (the composer pill, ED-275 (2)); every other `.continuous`
    /// rectangle takes OD-0102's corner there.
    /// </summary>
    public static GlassShape RoundedRectangle(double cornerRadius, RoundedCornerStyle style, bool circularInBand = false) =>
        new(GlassShapeKind.RoundedRectangle, cornerRadius, style, circularInBand);

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

    /// <summary>
    /// PORT-ONLY (OD-0121, voice pill): the material bends nothing under it (no lens, no rim ring, no red/green/blue split); the blur, tint, rim light and
    /// shade stay. The one piece that draws the page's plain <c>backdrop-filter: blur saturate</c> glass. Off for every other consumer.
    /// </summary>
    public bool NoRefraction { get; init; }

    /// <summary>The same glass with <see cref="NoRefraction"/> set.</summary>
    public GlassEffect WithoutRefraction() => this with { NoRefraction = true };

    /// <summary>
    /// PORT-ONLY (OD-0123, voice pill): the material draws no contact line, the dark 1 px line just outside the shape (the Mac frames' line at a capsule's ends); the rim light, the
    /// inner shade and the bend are as they were. The approved page's pill has no such line outside its edge. Off for every other consumer.
    /// </summary>
    public bool NoContactLine { get; init; }

    /// <summary>The same glass with <see cref="NoContactLine"/> set.</summary>
    public GlassEffect WithoutContactLine() => this with { NoContactLine = true };

    /// <summary>
    /// PORT-ONLY (ED-NEW-glasslook-1): the surface whose look (<see cref="GlassLooks"/>) this glass takes; null lets the material tell it from the
    /// shape (<see cref="GlassLooks.SurfaceOf"/>).
    /// </summary>
    public GlassSurface? Surface { get; init; }

    /// <summary>The same glass on <paramref name="surface"/>.</summary>
    public GlassEffect OnSurface(GlassSurface surface) => this with { Surface = surface };

    /// <summary>
    /// PORT-ONLY (ED-NEW-glasslook-1): a look for this glass alone, over its surface's (a design canvas's comparison, the tests); null takes the
    /// surface's from <see cref="GlassLooks"/>.
    /// </summary>
    public GlassLook? Look { get; init; }

    /// <summary>The same glass with its own <paramref name="look"/>.</summary>
    public GlassEffect WithLook(GlassLook look) => this with { Look = look };
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

    // `.buttonStyle(.glass)` on a text button, with the window's `.tint(Chrome.accent)` above it (ED-280).
    // SOURCE FACT (the reference runtime, work/reference-video): such a button is an accent capsule with a white label,
    // not the neutral chromeGlassCapsule (Chrome.swift:162-184, which the Permissions, Theme and search pills use):
    //   regular  Reveal, S 199.5 (frames/w-1-settings-tour-198.0/stills/win_199.5.png): 64 x 22, fill (247,46,120) beside the
    //            accent (246,32,102), label (255,248,255) 13 high-ish (38 wide, 10 tall), 13 either side of the label;
    //   small    Show, S 213.0 (stills/scene-1-settings-tour-213.00.png), `.controlSize(.small)`: 48 x 20, label 27 wide, 8 tall
    //            (11 point), 10.5 either side.
    // The press and hover looks are the interactive glass's (GlassMaterial); the lit hover and the 4 % press growth the
    // catalogue lists (hover.accent-pill-instant, press.accent-pill) are not drawn here.
    public const double GlassButtonRegularHeight = 22;
    public const double GlassButtonRegularPadding = 13;
    public const double GlassButtonRegularFontSize = 13;
    public const double GlassButtonSmallHeight = 20;
    public const double GlassButtonSmallPadding = 10.5;
    public const double GlassButtonSmallFontSize = 11;

    /// <summary><c>.buttonStyle(.glass)</c>, with <c>.controlSize(.small)</c> when <paramref name="small"/>.</summary>
    public static void StyleAccentGlassButton(Button button, bool small = false)
    {
        button.Padding = default;
        var height = small ? GlassButtonSmallHeight : GlassButtonRegularHeight;
        var padding = small ? GlassButtonSmallPadding : GlassButtonRegularPadding;
        var fontSize = small ? GlassButtonSmallFontSize : GlassButtonRegularFontSize;
        button.Template = new FuncControlTemplate<Button>((source, scope) =>
        {
            var label = new ContentPresenter
            {
                Name = "PART_ContentPresenter",
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Foreground = Brushes.White,
                FontFamily = SwiftFont.System(fontSize).Resolve().FontFamily,
                FontSize = fontSize,
                [!ContentPresenter.ContentProperty] = source[!ContentControl.ContentProperty],
            }.RegisterInNameScope(scope);
            var surface = new Border
            {
                Child = label,
                Height = height,
                Padding = new Thickness(padding, 0),
            }.Capsule(RoundedCornerStyle.Continuous);
            surface.Background = Chrome.Chrome.Accent.Brush(surface);
            SetEffect(surface, GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous)).Tint(Chrome.Chrome.Accent, 1).Interactive());
            // ENGINEERING DECISION (ED-280, UNVERIFIED: no reference frame shows a disabled glass button): a disabled one (.disabled, SettingsView.swift:1134)
            // is dimmed to 40 %, the dim every port control takes when disabled, easing over Chrome.hover (OD-0076).
            var dim = new AnimatedScalar(surface, source.IsEffectivelyEnabled ? 1 : 0.4, value => surface.Opacity = value);
            source.PropertyChanged += (_, e) => { if (e.Property == Avalonia.Input.InputElement.IsEffectivelyEnabledProperty) dim.Set(source.IsEffectivelyEnabled ? 1 : 0.4, Chrome.Chrome.Hover); };
            return surface;
        });
    }
}
