using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.Core.Port;

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
    private enum Kind { Fixed, Pair, Accent }

    private readonly Kind kind;
    private readonly double red;
    private readonly double green;
    private readonly double blue;
    // Fixed: the colour's alpha. Accent: the factor applied to the accent's own alpha.
    private readonly double alpha;
    private readonly SwiftColor? light;
    private readonly SwiftColor? dark;

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

    private SwiftColor(Kind kind, double alpha, SwiftColor? light, SwiftColor? dark)
    {
        this.kind = kind;
        this.alpha = alpha;
        this.light = light;
        this.dark = dark;
    }

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
    /// <c>NSColor.controlAccentColor</c> (Chrome.swift:80, :87): the Windows personalization accent (ED-051), read
    /// from the platform's <see cref="PlatformColorValues.AccentColor1"/>.
    /// </summary>
    public static SwiftColor ControlAccentColor { get; } = new(Kind.Accent, 1, null, null);

    /// <summary><c>Color.accentColor</c>: B2 ships no AccentColor asset, so it is the system control accent.</summary>
    public static SwiftColor AccentColor => ControlAccentColor;

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
    /// <c>Color.opacity(_:)</c>: multiplies alpha and returns a new value; the receiver is unchanged. A pair fades
    /// both members (Chrome.swift:71-73, :79); the accent keeps following the system accent, faded.
    /// </summary>
    public SwiftColor Opacity(double opacity) => kind switch
    {
        Kind.Fixed => new SwiftColor(red, green, blue, alpha * opacity),
        Kind.Pair => new SwiftColor(Kind.Pair, 1, light!.Opacity(opacity), dark!.Opacity(opacity)),
        _ => new SwiftColor(Kind.Accent, alpha * opacity, null, null),
    };

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
        if (kind == Kind.Fixed)
        {
            brush.Color = Resolve(control.ActualThemeVariant, CurrentAccent);
            return brush;
        }
        _ = new BrushBinding(this, brush, control);
        return brush;
    }

    private bool UsesAccent => kind switch
    {
        Kind.Accent => true,
        Kind.Pair => light!.UsesAccent || dark!.UsesAccent,
        _ => false,
    };

    private Color Resolve(ThemeVariant theme, Func<Color> accent) => kind switch
    {
        Kind.Fixed => Color.FromArgb(Byte(alpha), Byte(red), Byte(green), Byte(blue)),
        Kind.Pair => (IsDark(theme) ? dark! : light!).Resolve(theme, accent),
        _ => Faded(accent(), alpha),
    };

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

    private static Color CurrentAccent() => PlatformSettings.GetColorValues().AccentColor1;

    /// <summary>Keeps one brush in step with its control's appearance and, when needed, the system accent.</summary>
    private sealed class BrushBinding
    {
        private readonly SwiftColor color;
        private readonly SolidColorBrush brush;
        private readonly Control control;
        private IPlatformSettings? settings;
        private Color? accent;

        public BrushBinding(SwiftColor color, SolidColorBrush brush, Control control)
        {
            this.color = color;
            this.brush = brush;
            this.control = control;
            control.ActualThemeVariantChanged += (_, _) => Update();
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
            accent = settings.GetColorValues().AccentColor1;
            settings.ColorValuesChanged += OnColorValuesChanged;
            Update();
        }

        private void Unsubscribe()
        {
            if (settings is null) return;
            settings.ColorValuesChanged -= OnColorValuesChanged;
            settings = null;
            accent = null;
        }

        // On Windows the event carries the values GetColorValues then returns; take the accent it carries.
        private void OnColorValuesChanged(object? sender, PlatformColorValues values)
        {
            accent = values.AccentColor1;
            if (Dispatcher.UIThread.CheckAccess()) Update();
            else Dispatcher.UIThread.Post(Update);
        }

        private void Update() =>
            brush.Color = color.Resolve(control.ActualThemeVariant, () => accent ?? CurrentAccent());
    }
}
