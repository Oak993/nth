using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.UI.Theme;

namespace Marrow.UI.Port;

// PORT-ONLY: SwiftUI Color and AppKit NSColor as one immutable value, foundation unit F.SwiftColor (ED-092 part 1,
// ED-051, ED-062, ED-104, ED-105, ED-106; R-VIEW-11). It has no Swift twin: Color and NSColor are platform types.
// It carries the two B2 Color extensions whose C# twins would otherwise need a UI type in Core
// (Core/Models/MCP.swift:255-263 Color(rgb:), UI/Theme/AppTheme.swift:236-248 Color(hex:)).
//
// A SwiftColor is one of three things:
//   - a fixed sRGB colour with alpha (Color(red:green:blue:), Color(.sRGB, ...), NSColor(srgbRed:...),
//     Color(white:), Color(white:opacity:), NSColor(white:alpha:), and the two hex extensions);
//   - an appearance pair, NSColor(name:dynamicProvider:), resolved per the control's ActualThemeVariant;
//   - the system control accent, which on Windows is the personalization accent (ED-051).
// Opacity(x) multiplies alpha in doubles and returns a new value; bytes are produced only by Resolve, as
// component * 255 rounded to the nearest integer.

/// <summary>SwiftUI <c>Color</c> and AppKit <c>NSColor</c> (ED-092).</summary>
public sealed class SwiftColor
{
    // Live (PORT-ONLY, OD-0088, ED-250): the app's accent as it is now (Chrome.accent: the spec's accent, the user's pick
    // or the theme's, else the system's), read again whenever it is resolved, so a brush follows a later pick.
    private enum Kind { Fixed, Pair, Accent, Live }

    private readonly Kind kind;
    private readonly double red;
    private readonly double green;
    private readonly double blue;
    // Fixed: the colour's alpha. Accent and Live: the factor applied to the accent's own alpha.
    private readonly double alpha;
    private readonly SwiftColor? light;
    private readonly SwiftColor? dark;
    private readonly Func<SwiftColor>? live;
    // Web surfaces W20 (ED-NEW-web-w20-1): what the colour is for under a Windows contrast theme, and the opacity applied to it
    // since the role was given (a fade to nothing still fades there). None: drawn as it is in every mode.
    private readonly ContrastRole contrast;
    private readonly double contrastAlpha = 1;

    /// <summary>
    /// <c>Color(red:green:blue:opacity:)</c>, <c>Color(.sRGB, red:green:blue:opacity:)</c> and
    /// <c>NSColor(srgbRed:green:blue:alpha:)</c>: a fixed sRGB colour, the same in every appearance
    /// (EffortSlider.swift:876, AppTheme.swift:185).
    /// </summary>
    public SwiftColor(double red, double green, double blue, double opacity = 1)
    {
        kind = Kind.Fixed;
        this.red = red;
        this.green = green;
        this.blue = blue;
        alpha = opacity;
    }

    /// <summary>
    /// <c>Color(white:)</c>, <c>Color(white:opacity:)</c> and <c>NSColor(white:alpha:)</c>: an sRGB grey with every
    /// channel equal to <paramref name="white"/> (TerminalPanel.swift:107, TerminalStore.swift:50-51). ED-106: there is
    /// no gamma 2.2 grey; PR-TERM-045 requires the terminal background to match the panel's own sRGB fill exactly.
    /// </summary>
    public SwiftColor(double white, double opacity = 1) : this(white, white, white, opacity)
    {
    }

    private SwiftColor(Kind kind, double alpha, SwiftColor? light, SwiftColor? dark, Func<SwiftColor>? live = null, ContrastRole contrast = ContrastRole.None, double contrastAlpha = 1)
    {
        this.kind = kind;
        this.alpha = alpha;
        this.light = light;
        this.dark = dark;
        this.live = live;
        this.contrast = contrast;
        this.contrastAlpha = contrastAlpha;
    }

    /// <summary>
    /// PORT-ONLY, web surfaces W20 (DESIGN 10, ED-NEW-web-w20-1): the same colour, drawn as the system colour for
    /// <paramref name="role"/> wherever a Windows contrast theme is on (EnvironmentValues.AccessibilityHighContrast). Without
    /// one it resolves exactly as before.
    /// </summary>
    public SwiftColor InHighContrast(ContrastRole role) => new(Kind.Pair, 1, this, this, null, role);

    /// <summary>The role a contrast theme draws this colour as (None: it is drawn as it is).</summary>
    public ContrastRole HighContrastRole => contrast;

    /// <summary>The colour drawn in the given appearance, or the role's system colour under a contrast theme's palette.</summary>
    public Color Resolve(ThemeVariant theme, HighContrastPalette? palette) =>
        palette is not null && contrast != ContrastRole.None ? Faded(palette.For(contrast), contrastAlpha) : Resolve(theme);

    /// <summary>
    /// PORT-ONLY (OD-0088, ED-250): a colour that is whatever <paramref name="current"/> returns when it is resolved, and a
    /// brush that follows it when ThemeManager posts a change (the accent eases there over 0.32 s, SPEC 6.4).
    /// </summary>
    internal static SwiftColor Live(Func<SwiftColor> current) => new(Kind.Live, 1, null, null, current);

    /// <summary>
    /// Core/Models/MCP.swift:255-263 <c>Color(rgb:)</c>: the low 24 bits as red, green, blue, each masked with
    /// <c>&amp; 0xFF</c> after its shift; opaque, the top byte never read.
    /// </summary>
    public static SwiftColor Rgb(uint hex) => new(
        red: ((hex >> 16) & 0xFF) / 255.0, // MCP.swift:258
        green: ((hex >> 8) & 0xFF) / 255.0, // MCP.swift:259
        blue: (hex & 0xFF) / 255.0); // MCP.swift:260

    /// <summary>UI/Theme/AppTheme.swift:236-248 <c>Color(hex:)</c>: <c>"CBA6F7"</c> or <c>"#CBA6F7"</c> in sRGB.</summary>
    public static SwiftColor Hex(string hex)
    {
        // :239 hex.trimmed, Core/Support/Text.swift:132 trimmingCharacters(in: .whitespacesAndNewlines)
        var text = FoundationText.Trim(hex, includeNewlines: true);
        // :240 one leading # is dropped
        if (SwiftString.HasPrefix(text, "#")) text = SwiftString.DropFirst(text);
        // :241-242 value starts at 0; a scan that reads no digit leaves it there (O-16 for overflow)
        SwiftScanner.ScanHexInt64(text, out var value);
        // :243-247 the low 24 bits, opacity 1
        return new SwiftColor(
            red: ((value >> 16) & 0xFF) / 255.0,
            green: ((value >> 8) & 0xFF) / 255.0,
            blue: (value & 0xFF) / 255.0,
            opacity: 1);
    }

    /// <summary>
    /// <c>NSColor(name: nil, dynamicProvider:)</c> as an appearance pair (AppTheme.swift:182-187): the provider's
    /// darkAqua member in a dark appearance and its other member otherwise.
    /// </summary>
    public static SwiftColor Dynamic(SwiftColor light, SwiftColor dark) => new(Kind.Pair, 1, light, dark);

    /// <summary><c>Color.clear</c> / <c>NSColor.clear</c>: grayscale 0 with alpha 0.</summary>
    public static SwiftColor Clear { get; } = new(white: 0, opacity: 0);

    /// <summary><c>Color.white</c>: grayscale 1, opaque (EffortSlider.swift:788).</summary>
    public static SwiftColor White { get; } = new(white: 1);

    /// <summary><c>Color.black</c>: grayscale 0, opaque (HydraPanel.swift:120).</summary>
    public static SwiftColor Black { get; } = new(white: 0);

    /// <summary>
    /// <c>NSColor.labelColor</c>, Chrome.swift:76 primaryText. ED-051: rgba(0,0,0,0.85) light and
    /// rgba(255,255,255,0.85) dark, pinned literals (UNVERIFIED under OD-0001, no O-nn, per ED-051).
    /// </summary>
    public static SwiftColor LabelColor { get; } = Dynamic(
        light: new SwiftColor(red: 0, green: 0, blue: 0, opacity: 0.85),
        dark: new SwiftColor(red: 1, green: 1, blue: 1, opacity: 0.85));

    /// <summary><c>Color.primary</c> as a foreground style: the ED-051 primary text token (ED-092).</summary>
    public static SwiftColor Primary => LabelColor;

    /// <summary>
    /// <c>Color.secondary</c> as a foreground style: Chrome.swift:79 secondaryText = primaryText.opacity(0.7)
    /// (ED-062, ED-092).
    /// </summary>
    public static SwiftColor Secondary { get; } = LabelColor.Opacity(0.7);

    /// <summary>
    /// <c>ShapeStyle.quaternary</c> (<c>HierarchicalShapeStyle.quaternary</c>) used as a shape <c>.fill</c>: the
    /// lightest step of SwiftUI's four-level hierarchy (AttachmentPreview.swift:206, :333; pervasive elsewhere,
    /// porting/spec/CHAT.json e.g. :2531, :2735-2827). No SwiftColor constant for it existed before this unit
    /// (UI.Common.AttachmentPreview) needed one and none of ED-104 to ED-150 covers it (checked: no "quaternary" hit
    /// in porting/engineering_decisions.md, porting/owner_decisions.md or the rulings ledgers as of this unit).
    /// ENGINEERING DECISION, pending an assigned ED number: placed at the label colour's opacity, following this
    /// file's own hierarchy step from <see cref="Secondary"/> (0.7, ED-062) inward. UNVERIFIED under OD-0001: the
    /// value SwiftUI actually resolves `.quaternary` to as a fill (distinct from the `.secondary`/`.tertiary`
    /// foreground styles) is not independently knowable without macOS access; flag for an ED number and, if
    /// possible, an oracle measurement in the verification wave.
    /// </summary>
    public static SwiftColor Quaternary { get; } = LabelColor.Opacity(0.15);

    /// <summary>
    /// <c>ShapeStyle.tertiary</c> as a foreground style (UI.Markdown.MarkdownView.swift:509: an unchecked list
    /// item's marker, <c>AnyShapeStyle(.tertiary)</c>). ED-013 commits the port to "define exactly four tokens,
    /// one per level, as opacity multipliers over the resolved foreground colour" for
    /// primary/secondary/tertiary/quaternary and to ship them UNVERIFIED under OD-0001, but only Primary (this
    /// file's LabelColor), Secondary (0.7, ED-062) and Quaternary (0.15, above) had been wired in before this
    /// unit needed the third step. ENGINEERING DECISION completing ED-013's four-token table: interpolated
    /// midway between Secondary and Quaternary, the same reasoning Quaternary's own comment used to extend the
    /// hierarchy one step further inward. UNVERIFIED under OD-0001; flag for an ED number and an oracle
    /// measurement in the verification wave, same as Quaternary.
    /// </summary>
    public static SwiftColor Tertiary { get; } = LabelColor.Opacity(0.45);

    /// <summary>
    /// <c>NSColor.controlAccentColor</c> (Chrome.swift:80, :87): the Windows personalization accent (ED-051), read
    /// from the platform's <see cref="PlatformColorValues.AccentColor1"/>.
    /// </summary>
    public static SwiftColor ControlAccentColor { get; } = new(Kind.Accent, 1, null, null);

    /// <summary><c>Color.accentColor</c>: B2 ships no AccentColor asset, so it is the system control accent.</summary>
    public static SwiftColor AccentColor => ControlAccentColor;

    /// <summary>
    /// <c>Color.red</c> (SidebarView.swift:927 <c>Text("Delete").foregroundStyle(.red)</c>). ED-092 sends a system
    /// colour with no B2 value to the macOS documented values, and ED-104 records the method for <c>Color.yellow</c>:
    /// an appearance pair, the 'Default (light)' and 'Default (dark)' columns of the system colours table, the dark
    /// member the lighter of the two. Red is the same kind of pair, light sRGB #FF383C (255, 56, 60) and dark #FF4245
    /// (255, 66, 69), opaque; the increased contrast values are not modelled (ED-104). These are the same table
    /// edition as ED-104's Yellow: the macOS 27 UI Kit (OD-0045, E:\Droppy\work\apple-kit\macos27-uikit-controls.md
    /// "System colours Dark/Light") lists Yellow at #FFCC00 / #FFD600, exactly ED-104's (255, 204, 0) and (255, 214, 0),
    /// and Red at #FF383C / #FF4245. ENGINEERING DECISION ED-258 (1); UNVERIFIED under OD-0001 on
    /// the same footing as ED-104, no O-nn.
    /// This is the system red the SwiftUI literal names. The theme's own danger fallback is a different decision:
    /// PR-THEME-031's windows_path fixes one red for the three system-hue themes and keeps it constant
    /// (AppTheme.cs FrameworkRed, AppTheme.swift:197 <c>?? .red</c>), so that literal does not follow this pair.
    /// </summary>
    public static SwiftColor Red { get; } = Dynamic(
        light: Rgb(0xFF383C),
        dark: Rgb(0xFF4245));

    /// <summary>
    /// <c>Color.yellow</c> (EffortSlider.swift:80, :669, :673). ED-104: Apple's HIG Default values, light sRGB
    /// (255, 204, 0) and dark (255, 214, 0), opaque; the increased contrast values are not modelled.
    /// UNVERIFIED under OD-0001, no O-nn, per ED-104.
    /// </summary>
    public static SwiftColor Yellow { get; } = Dynamic(
        light: new SwiftColor(red: 1, green: 204 / 255.0, blue: 0),
        dark: new SwiftColor(red: 1, green: 214 / 255.0, blue: 0));

    /// <summary>
    /// <c>NSColor.placeholderTextColor</c> (ComposerTextView.swift:204). ED-105: pinned literals in the ED-051 family,
    /// rgba(0,0,0,0.25) light and rgba(255,255,255,0.25) dark, not derived from labelColor or a Windows brush.
    /// UNVERIFIED under OD-0001, no O-nn, per ED-105.
    /// </summary>
    public static SwiftColor PlaceholderTextColor { get; } = Dynamic(
        light: new SwiftColor(red: 0, green: 0, blue: 0, opacity: 0.25),
        dark: new SwiftColor(red: 1, green: 1, blue: 1, opacity: 0.25));

    /// <summary>
    /// <c>NSColor.selectedTextBackgroundColor</c>: NSTextView's own default of <c>selectedTextAttributes</c>
    /// (LinkParagraphView.swift:275-332 sets neither <c>isEditable</c> nor <c>selectedTextAttributes</c>, so the
    /// system default paints every selection the port's LinkTextView draws, prose included). It is a
    /// preference-driven system colour (System Settings' "Highlight color") with no literal RGB obtainable
    /// without macOS access (OD-0001; unlike Yellow/ED-104, Apple does not document a fixed HIG value for it).
    /// ENGINEERING DECISION, pending an assigned ED number: the accent at the same opacity this port already uses
    /// for the same semantic role, a text selection's background, at Marrow.App.Views.Terminal.TerminalStore.cs:109
    /// (<c>accent.Opacity(0.3)</c>), so the two selection highlights in the app read the same rather than picking
    /// an independent, equally-unverifiable number. UNVERIFIED under OD-0001; flag for an ED number and an oracle
    /// measurement in the verification wave, same as Quaternary and Tertiary above.
    /// </summary>
    public static SwiftColor SelectedTextBackgroundColor { get; } = AccentColor.Opacity(0.3);

    /// <summary>
    /// <c>Color.opacity(_:)</c>: multiplies alpha and returns a new value; the receiver is unchanged. A pair fades
    /// both members (Chrome.swift:71-73, :79); the accent keeps following the system accent, faded.
    /// </summary>
    public SwiftColor Opacity(double opacity) => kind switch
    {
        Kind.Fixed => new SwiftColor(red, green, blue, alpha * opacity),
        Kind.Pair => new SwiftColor(Kind.Pair, 1, light!.Opacity(opacity), dark!.Opacity(opacity), null, contrast, contrastAlpha * opacity),
        Kind.Live => new SwiftColor(Kind.Live, alpha * opacity, null, null, live),
        _ => new SwiftColor(Kind.Accent, alpha * opacity, null, null),
    };

    /// <summary>
    /// <c>Color.mix(with:by:)</c>: linear interpolation between this colour and another by a blend factor.
    /// Used by RenderBudget.swift:96 to mix the glass tint toward white or black.
    /// Result = self * (1 - factor) + other * factor.
    /// For dynamic colors (appearance pairs), recursively mixes both light and dark members.
    /// </summary>
    public SwiftColor Mix(SwiftColor other, double factor) => MixIn(other, factor, perceptual: false);

    /// <summary>
    /// <c>Color.mix(with:by:)</c> with SwiftUI's own default, <c>in: .perceptual</c>: the components are interpolated in
    /// Oklab (Apple's perceptual space), from the linear light of the sRGB components, and encoded back; the opacity is
    /// interpolated linearly. ENGINEERING DECISION, ED-286 (OD-0098: the framework's documented behaviour where
    /// the code relies on it): EffortSlider.swift:841 <c>Chrome.accent.mix(with: .white, by: 0.22)</c> and :964
    /// <c>mix(with: .white, by: 0.18)</c> name no colour space, so they take the default. <see cref="Mix"/> stays the
    /// component-wise interpolation the port's other mix sites use.
    /// </summary>
    public SwiftColor MixPerceptual(SwiftColor other, double factor) => MixIn(other, factor, perceptual: true);

    private SwiftColor MixIn(SwiftColor other, double factor, bool perceptual)
    {
        // Live (ED-250): the accent as it is now, mixed as that colour would be.
        if (kind == Kind.Live) return live!().Opacity(alpha).MixIn(other, factor, perceptual);
        if (other.kind == Kind.Live) return MixIn(other.live!().Opacity(other.alpha), factor, perceptual);

        // Handle dynamic colors (appearance pairs) by mixing both light and dark members
        if (kind == Kind.Pair)
        {
            // Mix both light and dark members separately
            return Dynamic(
                light: light!.MixIn(other, factor, perceptual),
                dark: dark!.MixIn(other, factor, perceptual));
        }

        if (other.kind == Kind.Pair)
        {
            // If only the other is dynamic, mix this with both its members
            return Dynamic(
                light: MixIn(other.light!, factor, perceptual),
                dark: MixIn(other.dark!, factor, perceptual));
        }

        // Both are fixed colors; do the interpolation
        if (kind != Kind.Fixed || other.kind != Kind.Fixed)
            throw new InvalidOperationException("Mix only supports fixed or dynamic (appearance pair) colors");

        var invFactor = 1.0 - factor;
        if (perceptual)
        {
            var (l0, a0, b0) = ToOklab(red, green, blue);
            var (l1, a1, b1) = ToOklab(other.red, other.green, other.blue);
            var (r, g, b) = FromOklab(l0 * invFactor + l1 * factor, a0 * invFactor + a1 * factor, b0 * invFactor + b1 * factor);
            return new SwiftColor(red: r, green: g, blue: b, opacity: alpha * invFactor + other.alpha * factor);
        }
        return new SwiftColor(
            red: red * invFactor + other.red * factor,
            green: green * invFactor + other.green * factor,
            blue: blue * invFactor + other.blue * factor,
            opacity: alpha * invFactor + other.alpha * factor);
    }

    // Oklab (Ottosson, 2020) from the sRGB components (0...1, not linear) and back; the components are not clamped on the way
    // in and the result is clamped to 0...1, as an sRGB colour is.
    private static double Linear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    private static double Encoded(double v) { v = Math.Clamp(v, 0, 1); return v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055; }

    private static (double L, double A, double B) ToOklab(double red, double green, double blue)
    {
        double r = Linear(red), g = Linear(green), b = Linear(blue);
        var l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        var m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        var s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        return (0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
            0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
    }

    private static (double R, double G, double B) FromOklab(double lightness, double a, double b)
    {
        var l = Math.Pow(lightness + 0.3963377774 * a + 0.2158037573 * b, 3);
        var m = Math.Pow(lightness - 0.1055613458 * a - 0.0638541728 * b, 3);
        var s = Math.Pow(lightness - 0.0894841775 * a - 1.2914855480 * b, 3);
        return (Encoded(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s),
            Encoded(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s),
            Encoded(-0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s));
    }

    /// <summary>The colour drawn in the given appearance.</summary>
    public Color Resolve(ThemeVariant theme) => Resolve(theme, CurrentAccent);

    /// <summary>
    /// A solid brush that follows the control: it re-resolves when the control's ActualThemeVariant changes and,
    /// for a colour that uses the system accent, when the platform colours change (Chrome.swift:75 "Dynamic system
    /// colors resolve at draw time").
    /// </summary>
    public IBrush Brush(Control control)
    {
        var brush = new SolidColorBrush();
        if (kind == Kind.Fixed) // a role is carried by a pair, so a fixed colour never needs the contrast theme
        {
            brush.Color = Resolve(control.ActualThemeVariant, CurrentAccent);
            return brush;
        }
        _ = new BrushBinding(this, brush, control);
        return brush;
    }

    private bool UsesAccent => kind switch
    {
        Kind.Accent or Kind.Live => true,
        Kind.Pair => light!.UsesAccent || dark!.UsesAccent,
        _ => false,
    };

    internal Color Resolve(ThemeVariant theme, Func<Color> accent) => kind switch
    {
        Kind.Fixed => Color.FromArgb(Byte(alpha), Byte(red), Byte(green), Byte(blue)),
        Kind.Pair => (IsDark(theme) ? dark! : light!).Resolve(theme, accent),
        Kind.Live => Faded(live!().Resolve(theme, accent), alpha),
        _ => Faded(AccentPalette.Lift(accent(), IsDark(theme)), alpha),
    };

    // ED-238 (3), amended by ED-250 (OD-0088): the Windows accent is drawn as the accent picker's lift makes it
    // (AccentPalette.Lift, SPEC 3.2): kept when it has 3.1:1 on the Settings card and 3:1 on the appearance's window
    // background (macOS 27 UI Kit, Window backgrounds: Dark #1E1E1E, Light #FFFFFF; OD-0045), else its OKLCH lightness
    // moves at fixed hue to the first colour that has both. ED-238's mix toward white or black is replaced; its floor
    // (3:1 on the window) still holds for every accent. An opaque black accent reads as #9C9C9C on Dark and stays black
    // on Light; the headless #0078D7 becomes #409FFF on Dark and #0073CF on Light. A theme's own accent (AppTheme.Accent)
    // and a palette pick do not go through the system accent and are drawn as given.

    private static Color Faded(Color accent, double factor) =>
        Color.FromArgb(Byte(accent.A / 255.0 * factor), accent.R, accent.G, accent.B);

    private static byte Byte(double component) =>
        (byte)Math.Round(Math.Clamp(component, 0, 1) * 255, MidpointRounding.AwayFromZero);

    // AppTheme.swift:183 appearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua: dark only for a dark
    // appearance (a custom variant counts through the variant it inherits), light for everything else.
    private static bool IsDark(ThemeVariant? theme)
    {
        for (var variant = theme; variant is not null; variant = variant.InheritVariant)
        {
            if (variant == ThemeVariant.Dark) return true;
        }
        return false;
    }

    private static IPlatformSettings PlatformSettings =>
        Application.Current?.PlatformSettings
        ?? throw new InvalidOperationException("The system accent needs a running Avalonia application.");

    private static Color CurrentAccent() => AccentSource.Current();

    /// <summary>Where the system accent is read from: the platform's personalization accent (ED-051). A test overrides it
    /// to stand for a PC whose accent is a given colour (headless always reports #0078D7).</summary>
    internal static Ambient<Func<Color>> AccentSource { get; } = new((Func<Color>)(() => PlatformSettings.GetColorValues().AccentColor1));

    /// <summary>Keeps one brush in step with its control's appearance and, when needed, the system accent.</summary>
    private sealed class BrushBinding
    {
        private readonly SwiftColor color;
        private readonly SolidColorBrush brush;
        private readonly Control control;
        private IPlatformSettings? settings;
        private Color? accent;
        private bool followsTheme;
        private IFrameSource? frames;
        private Action<TimeSpan>? pending;

        public BrushBinding(SwiftColor color, SolidColorBrush brush, Control control)
        {
            this.color = color;
            this.brush = brush;
            this.control = control;
            // ED-254 (OD-0076, SPEC 6.5): inside a tree that turns over (AppearanceTurn), an appearance change eases.
            control.ActualThemeVariantChanged += (_, _) => { if (AppearanceTurn.Eases(control)) Ease(); else Update(); };
            // W20: a contrast theme turning on or off (or its colours changing) redraws a colour that has a role at once.
            if (color.contrast != ContrastRole.None)
                control.PropertyChanged += (_, e) =>
                {
                    if (e.Property == EnvironmentValues.AccessibilityHighContrastProperty || e.Property == EnvironmentValues.AccessibilityContrastSurfaceProperty) Update();
                };
            if (color.UsesAccent)
            {
                // The platform settings live as long as the application; subscribe only while the control is in a
                // visual tree so a detached control is not kept alive by them.
                control.AttachedToVisualTree += (_, _) => Subscribe();
                control.DetachedFromVisualTree += (_, _) => Unsubscribe();
                if (TopLevel.GetTopLevel(control) is not null) Subscribe();
            }
            Update();
        }

        private void Subscribe()
        {
            if (settings is not null) return;
            settings = PlatformSettings;
            accent = CurrentAccent();
            settings.ColorValuesChanged += OnColorValuesChanged;
            if (color.UsesLive && !followsTheme)
            {
                followsTheme = true;
                ThemeManager.DidChange += OnThemeChanged;
            }
            Update();
        }

        private void Unsubscribe()
        {
            if (followsTheme)
            {
                followsTheme = false;
                ThemeManager.DidChange -= OnThemeChanged;
            }
            Stop();
            if (settings is null) return;
            settings.ColorValuesChanged -= OnColorValuesChanged;
            settings = null;
            accent = null;
        }

        // ED-250 (SPEC 6.4): the app's accent changed (a pick or a theme): the brush eases to it over 0.32 s, CSS `ease`,
        // on the frame clock; at once under Reduce Motion or with no frame clock.
        private void OnThemeChanged()
        {
            // The control's own thread: a change posted from elsewhere (ThemeManager is set from any actor) waits for it.
            if (control.CheckAccess()) Ease();
            else control.Dispatcher.Post(() => { if (followsTheme) Ease(); });
        }

        private static readonly Animation AccentChange = Animation.TimingCurve(0.25, 0.1, 0.25, 1, duration: 0.32);

        private void Ease()
        {
            Stop();
            var to = Resolved();
            if (brush.Color == to || !FrameClock.IsBound || EnvironmentValues.GetAccessibilityReduceMotion(control))
            {
                brush.Color = to;
                return;
            }
            var from = brush.Color;
            var start = SwiftClock.Now;
            var span = TimeSpan.FromSeconds(AccentChange.Duration);
            var source = FrameClock.Source.Current;
            Action<TimeSpan>? tick = null;
            tick = _ =>
            {
                var now = SwiftClock.Now;
                var done = FrameClock.IsComplete(start, now, span);
                var target = Resolved();
                brush.Color = done ? target : PremultipliedMix(from, target, AccentChange.Progress((now - start).TotalSeconds));
                if (done) pending = null;
                else source.RequestFrame(tick!);
            };
            frames = source;
            pending = tick;
            source.RequestFrame(tick);
        }

        private void Stop()
        {
            if (pending is { } frame) frames?.CancelFrame(frame);
            pending = null;
            frames = null;
        }

        // On Windows the event carries the values GetColorValues then returns; take the accent it carries.
        private void OnColorValuesChanged(object? sender, PlatformColorValues values)
        {
            accent = values.AccentColor1;
            if (Dispatcher.UIThread.CheckAccess()) Update();
            else Dispatcher.UIThread.Post(Update);
        }

        private Color Resolved() =>
            color.contrast != ContrastRole.None && EnvironmentValues.ContrastPaletteFor(control) is { } palette
                ? Faded(palette.For(color.contrast), color.contrastAlpha)
                : color.Resolve(control.ActualThemeVariant, () => accent ?? CurrentAccent());

        private void Update()
        {
            Stop();
            brush.Color = Resolved();
        }
    }

    private bool UsesLive => kind switch
    {
        Kind.Live => true,
        Kind.Pair => light!.UsesLive || dark!.UsesLive,
        _ => false,
    };

    /// <summary>Two colours mixed per channel in sRGB, premultiplied with alpha (as a browser interpolates a colour).</summary>
    public static Color PremultipliedMix(Color from, Color to, double progress)
    {
        double a0 = from.A / 255.0, a1 = to.A / 255.0;
        var a = a0 + (a1 - a0) * progress;
        byte Channel(byte c0, byte c1)
        {
            if (a <= 0) return 0;
            var premultiplied = c0 / 255.0 * a0 + (c1 / 255.0 * a1 - c0 / 255.0 * a0) * progress;
            return (byte)Math.Clamp(Math.Round(premultiplied / a * 255, MidpointRounding.AwayFromZero), 0, 255);
        }
        return Color.FromArgb((byte)Math.Clamp(Math.Round(a * 255, MidpointRounding.AwayFromZero), 0, 255), Channel(from.R, to.R), Channel(from.G, to.G), Channel(from.B, to.B));
    }
}
