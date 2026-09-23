// PORT-ONLY: SwiftUI Font and the AppKit NSFont system-font factories have no Avalonia twin; one immutable value stands in for both (ED-091).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using System.Diagnostics;
using AvaloniaFontWeight = Avalonia.Media.FontWeight;

namespace Marrow.UI.Port;

// Decisions: ED-091 (the model), ED-059 (the size per text style, from B1:DroppyCode/UI/Common/ChatZoom.swift:44-57),
// ED-071 (one point is one DIP), OD-0009 (Inter for text, Cascadia Mono for monospaced), ED-107 (where the faces come
// from and how they are registered), PA-0013. Visual parity of the sizes, of the headline weight and of the faces stays
// UNVERIFIED under OD-0001. Zoom is never applied here: upstream scales the window, not the type
// (UI/Common/ChatFont.swift:4-5, UI/Common/AppZoom.swift).

/// <summary>
/// A SwiftUI <c>Font</c> value: a text style or a fixed point size, an optional weight, a design, and the italic and
/// monospacedDigit traits. Every modifier returns a new value and never changes the receiver (ED-091).
/// </summary>
public sealed record SwiftFont
{
    /// <summary>SwiftUI <c>Font.TextStyle</c>, the eleven cases in declaration order (ED-091).</summary>
    public enum TextStyle
    {
        LargeTitle,
        Title,
        Title2,
        Title3,
        Headline,
        Subheadline,
        Body,
        Callout,
        Footnote,
        Caption,
        Caption2,
    }

    /// <summary>SwiftUI <c>Font.Weight</c> (and AppKit <c>NSFont.Weight</c>), the nine cases in order (ED-091).</summary>
    public enum Weight
    {
        UltraLight,
        Thin,
        Light,
        Regular,
        Medium,
        Semibold,
        Bold,
        Heavy,
        Black,
    }

    /// <summary>SwiftUI <c>Font.Design</c>: only default and monospaced occur at B2; rounded and serif are not provided (ED-091).</summary>
    public enum Design
    {
        Default,
        Monospaced,
    }

    /// <summary>What a font resolves to in Avalonia. <see cref="FontFeatures"/> is null when there is none, and a new
    /// collection on every resolve when there is one, so changing it leaks into no other value or control.</summary>
    public sealed record ResolvedFont(
        FontFamily FontFamily,
        double FontSize,
        AvaloniaFontWeight FontWeight,
        FontStyle FontStyle,
        FontFeatureCollection? FontFeatures);

    // ED-107 (3): the two faces, reached through their fonts: collection keys, never by name.
    private static readonly FontFamily InterFamily = new("fonts:Inter#Inter");
    private static readonly FontFamily CascadiaMonoFamily = new("fonts:CascadiaMono#Cascadia Mono");

    private TextStyle? Style { get; init; }
    private double? Size { get; init; }
    private Weight? FontWeightValue { get; init; }
    private Design FontDesign { get; init; }
    private bool IsItalic { get; init; }
    private bool HasMonospacedDigit { get; init; }

    private SwiftFont()
    {
    }

    // ---- Factories ----

    /// <summary>SwiftUI <c>Font.system(_:design:weight:)</c>: a text style (ChatFont.swift:8; ChatView.swift:1204, :1550).</summary>
    public static SwiftFont System(TextStyle style, Design design = Design.Default, Weight? weight = null) =>
        new() { Style = style, FontDesign = design, FontWeightValue = weight };

    /// <summary>SwiftUI <c>Font.system(size:weight:design:)</c>: a fixed point size (Chrome.swift:30; SettingsView.swift:729).</summary>
    public static SwiftFont System(double size, Weight? weight = null, Design design = Design.Default) =>
        new() { Size = size, FontDesign = design, FontWeightValue = weight };

    /// <summary>AppKit <c>NSFont.systemFont(ofSize:weight:)</c> (ComposerView.swift:557; ProseRunView.swift:182).</summary>
    public static SwiftFont SystemFont(double ofSize, Weight weight = Weight.Regular) =>
        new() { Size = ofSize, FontWeightValue = weight };

    /// <summary>AppKit <c>NSFont.boldSystemFont(ofSize:)</c> (LinkParagraphView.swift:115).</summary>
    public static SwiftFont BoldSystemFont(double ofSize) =>
        new() { Size = ofSize, FontWeightValue = Weight.Bold };

    /// <summary>AppKit <c>NSFont.monospacedSystemFont(ofSize:weight:)</c> (LinkParagraphView.swift:117).</summary>
    public static SwiftFont MonospacedSystemFont(double ofSize, Weight weight) =>
        new() { Size = ofSize, FontWeightValue = weight, FontDesign = Design.Monospaced };

    // SwiftUI's static text-style fonts: Font.largeTitle ... Font.caption2 (.font(.caption) at UI/Common/Components.swift:54,
    // .font(.headline) at App/Views/Composer/RequestCards.swift:13).
    public static SwiftFont LargeTitle { get; } = System(TextStyle.LargeTitle);
    public static SwiftFont Title { get; } = System(TextStyle.Title);
    public static SwiftFont Title2 { get; } = System(TextStyle.Title2);
    public static SwiftFont Title3 { get; } = System(TextStyle.Title3);
    public static SwiftFont Headline { get; } = System(TextStyle.Headline);
    public static SwiftFont Subheadline { get; } = System(TextStyle.Subheadline);
    public static SwiftFont Body { get; } = System(TextStyle.Body);
    public static SwiftFont Callout { get; } = System(TextStyle.Callout);
    public static SwiftFont Footnote { get; } = System(TextStyle.Footnote);
    public static SwiftFont Caption { get; } = System(TextStyle.Caption);
    public static SwiftFont Caption2 { get; } = System(TextStyle.Caption2);

    // ---- Modifiers (each returns a new value) ----

    /// <summary>SwiftUI <c>Font.weight(_:)</c> (R-NAME-3: named after its owner, Swift: weight). The later weight wins
    /// (ChatFont.swift:9; DiffInspector.swift:197).</summary>
    public SwiftFont FontWeight(Weight weight) => this with { FontWeightValue = weight };

    /// <summary>SwiftUI <c>Font.bold()</c>: the bold weight.</summary>
    public SwiftFont Bold() => this with { FontWeightValue = Weight.Bold };

    /// <summary>SwiftUI <c>Font.italic()</c>; also the NSFont italic trait (LinkParagraphView.swift:116).</summary>
    public SwiftFont Italic() => this with { IsItalic = true };

    /// <summary>SwiftUI <c>Font.monospacedDigit()</c>: the tnum feature (ED-091; HydraButton.swift:630).</summary>
    public SwiftFont MonospacedDigit() => this with { HasMonospacedDigit = true };

    // ---- Resolution ----

    /// <summary>The Avalonia family, size, weight, style and features for this font (ED-091, ED-107).</summary>
    public ResolvedFont Resolve() => new(
        FontDesign == Design.Monospaced ? CascadiaMonoFamily : InterFamily,
        Size ?? PointSize(Style!.Value),
        (AvaloniaFontWeight)WeightClass(ResolvedWeight),
        IsItalic ? FontStyle.Italic : FontStyle.Normal,
        HasMonospacedDigit ? new FontFeatureCollection { new FontFeature { Tag = "tnum", Value = 1 } } : null);

    /// <summary>Sets the whole font on a TextBlock, replacing every font property a previous font set.</summary>
    public void Apply(TextBlock text)
    {
        var resolved = Resolve();
        text.FontFamily = resolved.FontFamily;
        text.FontSize = resolved.FontSize;
        text.FontWeight = resolved.FontWeight;
        text.FontStyle = resolved.FontStyle;
        text.FontFeatures = resolved.FontFeatures;
    }

    /// <summary>Sets the whole font on a TemplatedControl (Button, TextBox), replacing every font property a previous font set.</summary>
    public void Apply(TemplatedControl control)
    {
        var resolved = Resolve();
        control.FontFamily = resolved.FontFamily;
        control.FontSize = resolved.FontSize;
        control.FontWeight = resolved.FontWeight;
        control.FontStyle = resolved.FontStyle;
        control.FontFeatures = resolved.FontFeatures;
    }

    // ED-091: a text style with no weight is regular, except headline, which macOS sets bold (platform knowledge,
    // UNVERIFIED under OD-0001). An explicit weight, regular included, is always kept (ChatFont.swift:7-9).
    private Weight ResolvedWeight =>
        FontWeightValue ?? (Style == TextStyle.Headline ? Weight.Bold : Weight.Regular);

    // ED-059: B1:DroppyCode/UI/Common/ChatZoom.swift:44-57, chatBaseSize, one point to one DIP (ED-071).
    private static double PointSize(TextStyle style) => style switch
    {
        TextStyle.LargeTitle => 26, // B1:DroppyCode/UI/Common/ChatZoom.swift:47
        TextStyle.Title => 22, // B1:DroppyCode/UI/Common/ChatZoom.swift:48
        TextStyle.Title2 => 17, // B1:DroppyCode/UI/Common/ChatZoom.swift:49
        TextStyle.Title3 => 15, // B1:DroppyCode/UI/Common/ChatZoom.swift:50
        TextStyle.Headline or TextStyle.Body => 13, // B1:DroppyCode/UI/Common/ChatZoom.swift:51
        TextStyle.Callout => 12, // B1:DroppyCode/UI/Common/ChatZoom.swift:52
        TextStyle.Subheadline => 11, // B1:DroppyCode/UI/Common/ChatZoom.swift:53
        TextStyle.Footnote or TextStyle.Caption or TextStyle.Caption2 => 10, // B1:DroppyCode/UI/Common/ChatZoom.swift:54
        _ => throw new UnreachableException(),
    };

    // ED-091: the OpenType weight classes 100 to 900, in Font.Weight order.
    private static int WeightClass(Weight weight) => weight switch
    {
        Weight.UltraLight => 100,
        Weight.Thin => 200,
        Weight.Light => 300,
        Weight.Regular => 400,
        Weight.Medium => 500,
        Weight.Semibold => 600,
        Weight.Bold => 700,
        Weight.Heavy => 800,
        Weight.Black => 900,
        _ => throw new UnreachableException(),
    };
}

/// <summary>
/// ED-107 (3): the bundled Cascadia Mono files (porting/probes/p9/CASCADIA.md), an embedded collection like the
/// Avalonia.Fonts.Inter package's InterFontCollection, reached as <c>fonts:CascadiaMono#Cascadia Mono</c>.
/// </summary>
public sealed class CascadiaMonoFontCollection : EmbeddedFontCollection
{
    public CascadiaMonoFontCollection()
        : base(new Uri("fonts:CascadiaMono", UriKind.Absolute), new Uri("avares://Marrow.UI/Assets/Fonts", UriKind.Absolute))
    {
    }
}

/// <summary>ED-107 (5): the one font registration point; the headless TestApp and the app shell's AppBuilder call it.</summary>
public static class SwiftFontAppBuilderExtensions
{
    public static AppBuilder WithSwiftFonts(this AppBuilder builder) =>
        builder.WithInterFont().ConfigureFonts(m => m.AddFontCollection(new CascadiaMonoFontCollection()));
}
