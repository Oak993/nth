// PORT-ONLY: web surfaces W16a (DESIGN.md 7.5 "Calm dark cards in nth glass (no glass on glass: controls inside a card are
// tinted fills), pill chips, simple steppers and sliders, monochrome line art with the accent only for the current value and
// focus, tabular numerals, generous spacing, the same type ramp as the thread. Light and dark themes come free"; 10 "text at
// least 4.5:1"; ED-NEW-web-w16a-1). No Swift counterpart. The look of a live answer, one place: the dark values are the
// approved canvas's (index.html section 5: .uic, .stp, .sld, .utab, .ubtn, .seg, .fchips), the light values their mirror on
// nth's light window. Text tokens are chosen so every text colour keeps 4.5:1 on the card in both themes (NthUiThemeTests).
using Avalonia;
using Avalonia.Media;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.UI.NthUi;

/// <summary>The colours, sizes and fonts of a live answer.</summary>
public static class NthUiTokens
{
    private static SwiftColor Ink(double opacity) => new(red: 0, green: 0, blue: 0, opacity: opacity);

    private static SwiftColor Paper(double opacity) => new(red: 1, green: 1, blue: 1, opacity: opacity);

    // canvas --t1..--t3 are #ECECEE at 1, .68 and .48; .48 falls under 4.5:1 on the card and on a field's fill, so the third
    // step is .62.
    private static SwiftColor Mist(double opacity) => new(red: 236 / 255.0, green: 236 / 255.0, blue: 238 / 255.0, opacity: opacity);

    /// <summary>Primary text: the thread's own label colour.</summary>
    public static SwiftColor Text1 { get; } = SwiftColor.Dynamic(light: Ink(.86), dark: Mist(.94));

    /// <summary>Secondary text: labels above controls, column headers, details (canvas --t2).</summary>
    public static SwiftColor Text2 { get; } = SwiftColor.Dynamic(light: Ink(.62), dark: Mist(.68));

    /// <summary>Tertiary text: units, counters, captions; still 4.5:1 on the card.</summary>
    public static SwiftColor Text3 { get; } = SwiftColor.Dynamic(light: Ink(.56), dark: Mist(.62));

    /// <summary>A card's tint over the thread (canvas .uic background).</summary>
    public static SwiftColor CardFill { get; } = SwiftColor.Dynamic(light: Ink(.032), dark: Paper(.045));

    /// <summary>A card's hairline rim (canvas .uic inset .5px).</summary>
    public static SwiftColor CardRim { get; } = SwiftColor.Dynamic(light: Ink(.085), dark: Paper(.10));

    /// <summary>A card's soft drop (canvas 0 10px 30px -14px).</summary>
    public static SwiftColor CardShadow { get; } = SwiftColor.Dynamic(light: Ink(.12), dark: Ink(.6));

    /// <summary>A control's tinted fill inside a card (canvas .stp, .ubtn).</summary>
    public static SwiftColor ControlFill { get; } = SwiftColor.Dynamic(light: Ink(.05), dark: Paper(.075));

    /// <summary>A control under the pointer (canvas .ubtn:hover).</summary>
    public static SwiftColor ControlHover { get; } = SwiftColor.Dynamic(light: Ink(.085), dark: Paper(.13));

    /// <summary>A control's inset rim (canvas .stp .12, --line2 .14).</summary>
    public static SwiftColor ControlRim { get; } = SwiftColor.Dynamic(light: Ink(.10), dark: Paper(.13));

    /// <summary>The chosen segment, tab or chip (canvas .seg button[aria-pressed=true]).</summary>
    public static SwiftColor Chosen { get; } = SwiftColor.Dynamic(light: Paper(1), dark: Paper(.13));

    /// <summary>The chosen segment's drop in light (a raised white key) and its top light in dark.</summary>
    public static SwiftColor ChosenEdge { get; } = SwiftColor.Dynamic(light: Ink(.14), dark: Paper(.16));

    /// <summary>Row rules and dividers (canvas .utab td .5px .07).</summary>
    public static SwiftColor Hairline { get; } = SwiftColor.Dynamic(light: Ink(.09), dark: Paper(.08));

    /// <summary>A slider's empty track and a switch that is off.</summary>
    public static SwiftColor Track { get; } = SwiftColor.Dynamic(light: Ink(.12), dark: Paper(.16));

    /// <summary>Strong monochrome: a primary button, a ticked box, a slider's thumb ring in light (canvas --silver-hi on dark).</summary>
    public static SwiftColor Strong { get; } = SwiftColor.Dynamic(light: SwiftColor.Rgb(0x1D1D20), dark: SwiftColor.Rgb(0xECECEE));

    /// <summary>Text and glyphs on <see cref="Strong"/>.</summary>
    public static SwiftColor OnStrong { get; } = SwiftColor.Dynamic(light: SwiftColor.Rgb(0xFFFFFF), dark: SwiftColor.Rgb(0x121214));

    /// <summary>A code block's well.</summary>
    public static SwiftColor Well { get; } = SwiftColor.Dynamic(light: Ink(.045), dark: Ink(.28));

    /// <summary>A menu under a field (a select's options, a date's month): solid, so it reads over anything.</summary>
    public static SwiftColor Menu { get; } = SwiftColor.Dynamic(light: SwiftColor.Rgb(0xFFFFFF), dark: SwiftColor.Rgb(0x2A2A2F));

    /// <summary>The thread's colour an exported card sits on (`export png`), so its see-through fill and text read in any viewer.</summary>
    public static SwiftColor Backdrop { get; } = SwiftColor.Dynamic(light: SwiftColor.Rgb(0xF5F5F7), dark: SwiftColor.Rgb(0x1E1E21));

    /// <summary>The accent, only for the current value and focus (7.5).</summary>
    public static SwiftColor Accent => Marrow.UI.Chrome.Chrome.Accent;

    /// <summary>The colour of a tone's text and glyph; neutral is secondary text.</summary>
    public static SwiftColor Tone(string? tone) => tone switch
    {
        "info" => SwiftColor.Dynamic(light: SwiftColor.Rgb(0x0857B0), dark: SwiftColor.Rgb(0x6CB2FF)),
        "success" => SwiftColor.Dynamic(light: SwiftColor.Rgb(0x15692F), dark: SwiftColor.Rgb(0x5BD18B)),
        "warning" => SwiftColor.Dynamic(light: SwiftColor.Rgb(0x7D5100), dark: SwiftColor.Rgb(0xF2B84B)),
        "danger" => SwiftColor.Dynamic(light: SwiftColor.Rgb(0xB42129), dark: SwiftColor.Rgb(0xFF8A8A)),
        _ => Text2,
    };

    /// <summary>A tone's tinted fill (a badge, a callout).</summary>
    public static SwiftColor ToneFill(string? tone) => tone switch
    {
        "info" or "success" or "warning" or "danger" => Tone(tone).Opacity(.12),
        _ => ControlFill,
    };

    // ---- metrics (DIP) -------------------------------------------------------------------------------------------------

    public const double CardRadius = 16;
    public const double CardPadding = 14;
    public const double CardGap = 12;
    public const double ControlHeight = 32;
    public const double ControlRadius = 12;
    public const double ButtonHeight = 30;
    public const double ChipHeight = 28;
    public const double LabelGap = 5;
    public const double ControlsGap = 18;
    public const double RowGap = 8;

    /// <summary>The rise of a node arriving (7.4: "6 DIP rise").</summary>
    public const double Rise = 6;

    /// <summary>The fade of a node arriving (7.4: "180 ms").</summary>
    public const double FadeSeconds = .18;

    /// <summary>The fade that stands in for motion under Reduce Motion (10: "springs become short fades").</summary>
    public const double CalmFadeSeconds = .15;

    // ---- type ramp: the thread's (ChatFont) ----------------------------------------------------------------------------

    public static SwiftFont Body => ChatFont.Chat(SwiftFont.TextStyle.Body);

    public static SwiftFont BodyStrong => ChatFont.Chat(SwiftFont.TextStyle.Body, SwiftFont.Weight.Semibold);

    public static SwiftFont Small => ChatFont.Chat(SwiftFont.TextStyle.Callout);

    public static SwiftFont Caption => ChatFont.Chat(SwiftFont.TextStyle.Subheadline, SwiftFont.Weight.Medium);

    public static SwiftFont Numbers => ChatFont.Chat(SwiftFont.TextStyle.Body).MonospacedDigit();

    public static SwiftFont BigNumber => ChatFont.Chat(SwiftFont.TextStyle.Title2, SwiftFont.Weight.Semibold).MonospacedDigit();

    public static SwiftFont Mono => ChatFont.Chat(SwiftFont.TextStyle.Callout, design: SwiftFont.Design.Monospaced);

    public static SwiftFont Heading(int level) => level switch
    {
        1 => ChatFont.Chat(SwiftFont.TextStyle.Title2, SwiftFont.Weight.Semibold),
        2 => ChatFont.Chat(SwiftFont.TextStyle.Title3, SwiftFont.Weight.Semibold),
        _ => ChatFont.Chat(SwiftFont.TextStyle.Body, SwiftFont.Weight.Semibold),
    };

    /// <summary>The card's drop: 0 10 30 -14 in the theme's shadow colour.</summary>
    public static BoxShadows CardShadows(Color color) => new(new BoxShadow { OffsetY = 10, Blur = 30, Spread = -14, Color = color });

    /// <summary>Relative luminance (WCAG 2.x) of an opaque colour.</summary>
    public static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= .04045 ? s / 12.92 : Math.Pow((s + .055) / 1.055, 2.4);
        }
        return .2126 * Channel(c.R) + .7152 * Channel(c.G) + .0722 * Channel(c.B);
    }

    /// <summary>The WCAG contrast ratio of two opaque colours.</summary>
    public static double Contrast(Color a, Color b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + .05) / (Math.Min(la, lb) + .05);
    }

    /// <summary><paramref name="top"/> drawn over an opaque <paramref name="bottom"/>.</summary>
    public static Color Over(Color top, Color bottom)
    {
        var a = top.A / 255.0;
        byte Mix(byte t, byte b) => (byte)Math.Round(t * a + b * (1 - a), MidpointRounding.AwayFromZero);
        return Color.FromRgb(Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B));
    }

    internal static Thickness Uniform(double value) => new(value);
}
