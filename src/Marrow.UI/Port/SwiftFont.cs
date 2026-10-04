// PORT-ONLY: SwiftUI Font and the AppKit NSFont system-font factories have no Avalonia twin; one immutable value stands in for both (ED-091).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Marrow.Core.Port;
using AvaloniaFontWeight = Avalonia.Media.FontWeight;

// ED-175: App.Views.Terminal.TerminalFont (Marrow.App) scripts the installed-font-family seam (InstalledFontFamilyNames)
// from its own tests, which live in Marrow.App.Tests rather than this assembly's own Marrow.UI.Tests.
[assembly: InternalsVisibleTo("Marrow.App.Tests")]

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
    private bool IsAppKit { get; init; }
    private bool UsesNaturalLineHeight { get; init; }

    /// <summary>NSFont.familyName (ED-175 (1)): set only by <see cref="Named"/>, for TerminalFont's nerdSibling
    /// (TerminalFont.swift :106) to build the family's "nf" sibling name. Null for every design-based font,
    /// which has no family of its own on this port (ED-107).</summary>
    public string? FamilyName { get; private init; }

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
        new() { Size = ofSize, FontWeightValue = weight, IsAppKit = true };

    /// <summary>AppKit <c>NSFont.boldSystemFont(ofSize:)</c> (LinkParagraphView.swift:115).</summary>
    public static SwiftFont BoldSystemFont(double ofSize) =>
        new() { Size = ofSize, FontWeightValue = Weight.Bold, IsAppKit = true };

    /// <summary>AppKit <c>NSFont.monospacedSystemFont(ofSize:weight:)</c> (LinkParagraphView.swift:117).</summary>
    public static SwiftFont MonospacedSystemFont(double ofSize, Weight weight) =>
        new() { Size = ofSize, FontWeightValue = weight, FontDesign = Design.Monospaced, IsAppKit = true };

    /// <summary>
    /// AppKit <c>NSFont(name:size:)</c> (TerminalFont.swift resolve(), :121-124 and :129; ED-175 (1)). Non-null
    /// only when an installed family's name equals <paramref name="name"/> case-insensitively, checked against the
    /// same seam <see cref="AvailableFontFamilies"/> reads (unfiltered, so this can still match the app's own
    /// embedded Inter or Cascadia Mono face if a genuine system font of that name is also installed): Avalonia's
    /// silent substitution of a missing family (ED-107 finding 1) is never taken as a match. PostScript names do
    /// not resolve on Windows, since Avalonia matches family names only (ED-175 (1), a named deviation).
    /// </summary>
    public static SwiftFont? Named(string name, double size)
    {
        var family = InstalledFontFamilyNames.Current.FirstOrDefault(candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase));
        return family is null ? null : new SwiftFont { FamilyName = family, Size = size };
    }

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

    /// <summary>NSFont.pointSize (ED-175 (1)): the size this value resolves to, a text style's ED-059 size when
    /// none was given directly. Used by TerminalFont's nerdSibling and firstInstalledNerdFont
    /// (TerminalFont.swift :106-119) to keep a Nerd Font match at the configured font's own size.</summary>
    public double PointSize => Size ?? StyleSize(Style!.Value);

    /// <summary>
    /// <c>fontDescriptor.symbolicTraits.contains(.bold)</c> (ProseRunView.swift:150-151, B3 52bc792): the bold weight
    /// or heavier. The fonts that reach that check are regular, bold, italic and monospaced regular faces
    /// (LinkParagraphView.swift:113-118, :219), so whether AppKit also marks semibold bold is never asked; it is
    /// not modelled.
    /// </summary>
    public bool HasBoldTrait => ResolvedWeight is Weight.Bold or Weight.Heavy or Weight.Black;

    /// <summary><c>fontDescriptor.symbolicTraits.contains(.monoSpace)</c> (ProseRunView.swift:151): the monospaced
    /// design (<c>NSFont.monospacedSystemFont</c>).</summary>
    public bool HasMonoSpaceTrait => FontDesign == Design.Monospaced;

    /// <summary>
    /// NSFont.coveredCharacterSet.contains(_:) (TerminalFont.swift hasNerdGlyphs, :94-101; ED-175 (1)): whether
    /// this font's regular (400, upright) face has a glyph for the codepoint, through Avalonia's glyph map rather
    /// than the boolean TryGetGlyphTypeface alone (ED-107 finding 1: that returns true even for a silently
    /// substituted face). Always the regular face, regardless of this value's own weight and italic trait,
    /// exactly as coveredCharacterSet does not vary with them.
    /// </summary>
    public bool Covers(int codepoint)
    {
        var family = FamilyName is { } familyName ? new FontFamily(familyName) : Resolve().FontFamily;
        var typeface = new Typeface(family, FontStyle.Normal, AvaloniaFontWeight.Normal);
        return FontManager.Current.TryGetGlyphTypeface(typeface, out var glyphTypeface)
            && glyphTypeface.SupportedUnicodeRange.IsInRange(codepoint);
    }

    /// <summary>
    /// PORT-ONLY test seam behind NSFontManager.shared.availableFontFamilies and the family search inside
    /// NSFont(name:size:) (ED-175): production reads Avalonia's installed system fonts once per process;
    /// Marrow.UI.Tests scripts this list (Oracle O-61) so a font test never depends on what is actually installed
    /// on the machine it runs on.
    /// </summary>
    internal static Ambient<IReadOnlyList<string>> InstalledFontFamilyNames { get; } =
        new(() => FontManager.Current.SystemFonts.Select(static family => family.Name).ToArray());

    /// <summary>
    /// NSFontManager.shared.availableFontFamilies (TerminalFont.swift :106-129; ED-175 (1)): the installed family
    /// names, sorted ordinal case-insensitively (Oracle O-61: macOS's own order is unspecified), with the app's
    /// embedded Inter and Cascadia Mono (ED-107) excluded so a configured "Inter" or "Cascadia Mono" never matches
    /// the port's own bundled face instead of a genuine installed one.
    /// </summary>
    public static IReadOnlyList<string> AvailableFontFamilies =>
        InstalledFontFamilyNames.Current
            .Where(static name => !IsEmbeddedFace(name))
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool IsEmbeddedFace(string name) =>
        string.Equals(name, "Inter", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "Cascadia Mono", StringComparison.OrdinalIgnoreCase);

    // ---- Resolution ----

    /// <summary>The Avalonia family, size, weight, style and features for this font (ED-091, ED-107, ED-175).
    /// A font obtained through <see cref="Named"/> resolves to its own family (its FamilyName) rather than the
    /// design-based Inter or Cascadia Mono key.</summary>
    public ResolvedFont Resolve() => new(
        FamilyName is { } familyName ? new FontFamily(familyName) : FontDesign == Design.Monospaced ? CascadiaMonoFamily : InterFamily,
        Size ?? StyleSize(Style!.Value),
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
        text.LineHeight = MacLineHeight() ?? double.NaN;
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

    /// <summary>
    /// ED-NEW-frames-1 (SOURCE FACT, 1-settings-tour.mov S 180.50-185.50, 1 px = 1 pt): a SwiftUI <c>Text</c> in the system font stands on a line box taller
    /// than the face's own (Inter's is 1.21 times the size): 16 points at 11, 17 at 12, 18 at 13. Measured as the pitch of wrapped lines (the lead card's
    /// callout lines 17 apart, the GitHub detail's subheadline lines 16 apart, "Chrome DevTools" at the body size 36 over two lines) and as the heights
    /// they give: a Connected row is 58 = 11 + 18 + 2 + 16 + 11, a catalog card 126, the lead card 94. The other sizes have no measurement yet and keep
    /// the face's own height. Not applied to AppKit fonts (their line heights are the text system's), to the monospaced design, to a named font, or where
    /// a surface draws to a board's metrics (<see cref="WithNaturalLineHeight"/>).
    /// </summary>
    internal double? MacLineHeight() => FontDesign == Design.Default && FamilyName is null && !IsAppKit && !UsesNaturalLineHeight
        ? (Size ?? StyleSize(Style!.Value)) switch { 11 => 16, 12 => 17, 13 => 18, _ => null } : null;

    /// <summary>The same font on the face's own line height: for a surface the owner's board measured (the Look card, OD-0088, ED-250).</summary>
    public SwiftFont WithNaturalLineHeight() => this with { UsesNaturalLineHeight = true };

    // ED-059: B1:DroppyCode/UI/Common/ChatZoom.swift:44-57, chatBaseSize, one point to one DIP (ED-071).
    private static double StyleSize(TextStyle style) => style switch
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
/// The folder avares://Marrow.UI/Assets/Fonts also holds the Inter release files (porting/probes/p9/INTER-FILES.md), so
/// the source names the CascadiaMono-*.ttf files only; the whole folder put those Inter faces under this key
/// (reviewer finding 1).
/// </summary>
public sealed class CascadiaMonoFontCollection : EmbeddedFontCollection
{
    public CascadiaMonoFontCollection()
        : base(new Uri("fonts:CascadiaMono", UriKind.Absolute), new Uri("avares://Marrow.UI/Assets/Fonts/CascadiaMono-*.ttf", UriKind.Absolute))
    {
    }
}

/// <summary>
/// ED-107 (4), OD-0009 and OD-0020 item 2: the Inter family under the key <c>fonts:Inter</c>. It holds the
/// Avalonia.Fonts.Inter 12.1.2 package's files (the same key and source as the package's InterFontCollection) and the
/// rsms/inter v4.1 release files the package lacks, italic at 400 to 700 and weight 800 upright and italic, shipped in
/// avares://Marrow.UI/Assets/Fonts (porting/probes/p9/INTER-FILES.md, whose line 22 gives this registration to
/// F.SwiftFont). B2 reaches those faces through the italic trait (UI/Markdown/LinkParagraphView.swift:116, :131) and
/// .heavy (UI/Common/HydraGlyph.swift:56, UI/Common/MCPGlyph.swift:49).
/// </summary>
public sealed class InterFontCollectionWithReleaseFiles : EmbeddedFontCollection
{
    private static readonly Uri ReleaseFiles = new("avares://Marrow.UI/Assets/Fonts/Inter-*.ttf", UriKind.Absolute);

    public InterFontCollectionWithReleaseFiles()
        : base(new Uri("fonts:Inter", UriKind.Absolute), new Uri("avares://Avalonia.Fonts.Inter/Assets", UriKind.Absolute))
    {
        // A missing release file must not fall to simulation unseen (PORT_CONTRACT.md:173, silent substitution).
        if (!TryAddFontSource(ReleaseFiles))
            throw new InvalidOperationException($"The Inter release files were not found at {ReleaseFiles} (INTER-FILES.md).");
    }
}

/// <summary>ED-107 (5): the one font registration point; the headless TestApp and the app shell's AppBuilder call it.</summary>
public static class SwiftFontAppBuilderExtensions
{
    // The package's WithInterFont() does only ConfigureFonts(m => m.AddFontCollection(new InterFontCollection())) in
    // 12.1.2; InterFontCollectionWithReleaseFiles takes its place under the same fonts:Inter key with the release files
    // added (OD-0020 item 2), so the key is registered once.
    public static AppBuilder WithSwiftFonts(this AppBuilder builder) =>
        builder.ConfigureFonts(m =>
        {
            m.AddFontCollection(new InterFontCollectionWithReleaseFiles());
            m.AddFontCollection(new CascadiaMonoFontCollection());
        });
}
