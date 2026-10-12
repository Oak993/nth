using Marrow.UI.Port;

// PORT-ONLY: web surfaces W8a, the look of Visus's chrome as the approved canvas draws it (E:/Droppy/work/design/web-surfaces/
// index.html "browser (shared)": .bh, .tabs, .tab, .fav, .btb, .hb, .url, .schip, .zchip, .web, .privv; DESIGN.md 2.4 "The web
// rectangle is inset 6 DIP on the sides and bottom", 3.12, 5.1, 10 "text at least 4.5:1 on glass"; ED-NEW-web-w8a-2). No Swift
// counterpart. Dark values are the canvas's; light values are the same roles on the light glass (ink instead of mist), each
// checked for 4.5:1 text the way PaneTokens is.

namespace Marrow.App.Views.Browser;

/// <summary>The sizes and colours of Visus's tab strip, toolbar, chips and placeholders.</summary>
public static class VisusTokens
{
    /// <summary>The toolbar row (canvas .btb: 40 DIP, padding 0 6, gap 2).</summary>
    public const double ToolbarHeight = 40;

    /// <summary>A toolbar button (canvas .btb .hb: 28 DIP, radius 8, glyph 15).</summary>
    public const double ToolbarButton = 28;

    public const double ToolbarGlyph = 15;

    /// <summary>The address field (canvas .url: 28 DIP, radius 14, padding 0 4 0 9, margin 0 4, gap 7, 12.5 text).</summary>
    public const double AddressHeight = 28;

    public const double AddressFontSize = 12.5;

    /// <summary>A tab (canvas .tab: 28 DIP, at most 200 wide, padding 0 6 0 9, radius 9, gap 7, 12 text).</summary>
    public const double TabHeight = 28;

    public const double TabMaxWidth = 200;

    /// <summary>A tab at its narrowest before the strip overflows into its menu (the canvas's narrow container: 120).</summary>
    public const double TabMinWidth = 120;

    public const double TabFontSize = 12;

    /// <summary>The space between tabs (canvas .tabs gap 3).</summary>
    public const double TabGap = 3;

    /// <summary>A tab's close button (canvas .tab .x: 18 DIP, radius 5, glyph 10, opacity .7 when shown).</summary>
    public const double TabCloseSize = 18;

    public const double TabCloseOpacity = 0.7;

    /// <summary>The favicon (canvas .fav: 14 DIP, radius 4).</summary>
    public const double FaviconSize = 14;

    /// <summary>The site chip (canvas .schip: 20 DIP, padding 0 7, radius 10, 500 10.5 text, gap 5).</summary>
    public const double ChipHeight = 20;

    public const double ChipFontSize = 10.5;

    /// <summary>The zoom chip (canvas .zchip: 22 DIP, padding 0 8, radius 11, mono 11).</summary>
    public const double ZoomChipHeight = 22;

    /// <summary>The bezel around the web rectangle (DESIGN 2.4, canvas .web margin 0 6 6).</summary>
    public const double Bezel = 6;

    private static SwiftColor Ink(double opacity) => new(red: 0, green: 0, blue: 0, opacity: opacity);

    private static SwiftColor Paper(double opacity) => new(red: 1, green: 1, blue: 1, opacity: opacity);

    private static SwiftColor Rgb(int r, int g, int b, double opacity = 1) => new(red: r / 255.0, green: g / 255.0, blue: b / 255.0, opacity: opacity);

    /// <summary>The chosen tab (canvas .tab.on: white .10).</summary>
    public static SwiftColor TabChosen { get; } = SwiftColor.Dynamic(light: Ink(.07), dark: Paper(.10));

    /// <summary>A chosen private tab (canvas .tab.priv.on: rgba(120, 120, 140, .22)).</summary>
    public static SwiftColor TabChosenPrivate { get; } = SwiftColor.Dynamic(light: Rgb(90, 90, 120, .18), dark: Rgb(120, 120, 140, .22));

    /// <summary>The address field's well (canvas .url: white .06 with a .5 DIP white .10 rim).</summary>
    public static SwiftColor AddressFill { get; } = SwiftColor.Dynamic(light: Ink(.045), dark: Paper(.06));

    public static SwiftColor AddressRim { get; } = SwiftColor.Dynamic(light: Ink(.10), dark: Paper(.10));

    /// <summary>The address field while it has focus: a little brighter, no coloured ring (3.12 "No coloured focus frames").</summary>
    public static SwiftColor AddressFocusFill { get; } = SwiftColor.Dynamic(light: Ink(.06), dark: Paper(.09));

    public static SwiftColor AddressFocusRim { get; } = SwiftColor.Dynamic(light: Ink(.22), dark: Paper(.24));

    /// <summary>The toolbar's tint in a private tab (canvas: .btb background rgba(120, 120, 150, .1)).</summary>
    public static SwiftColor PrivateToolbar { get; } = SwiftColor.Dynamic(light: Rgb(90, 90, 130, .08), dark: Rgb(120, 120, 150, .10));

    /// <summary>The lock chip and any chip with no tint of its own (canvas .schip: white .10).</summary>
    public static SwiftColor ChipFill { get; } = SwiftColor.Dynamic(light: Ink(.07), dark: Paper(.10));

    /// <summary>"Preview" (canvas .schip.pv: --ok at .14 with a .5 DIP --ok .4 rim).</summary>
    public static SwiftColor PreviewFill { get; } = SwiftColor.Dynamic(light: Rgb(41, 140, 84, .12), dark: Rgb(91, 209, 139, .14));

    public static SwiftColor PreviewRim { get; } = SwiftColor.Dynamic(light: Rgb(41, 140, 84, .40), dark: Rgb(91, 209, 139, .40));

    /// <summary>"Agent" (canvas .schip.ag: white .16 with a .5 DIP white .5 rim).</summary>
    public static SwiftColor AgentFill { get; } = SwiftColor.Dynamic(light: Ink(.10), dark: Paper(.16));

    public static SwiftColor AgentRim { get; } = SwiftColor.Dynamic(light: Ink(.40), dark: Paper(.50));

    /// <summary>"Private" (canvas .schip.pr: rgba(140, 140, 170, .22)).</summary>
    public static SwiftColor PrivateFill { get; } = SwiftColor.Dynamic(light: Rgb(90, 90, 130, .16), dark: Rgb(140, 140, 170, .22));

    /// <summary>"Not secure": the canvas's --warn as text on the plain chip (the lock's place).</summary>
    public static SwiftColor NotSecureText { get; } = SwiftColor.Dynamic(light: Rgb(138, 90, 0), dark: Rgb(242, 184, 75));

    /// <summary>The page colour behind the web rectangle (canvas --page), shown until the page paints.</summary>
    public static SwiftColor Page { get; } = SwiftColor.Dynamic(light: Rgb(246, 245, 242), dark: Rgb(246, 245, 242));

    /// <summary>The private tab's start surface (canvas .privv: #1C1C22).</summary>
    public static SwiftColor PrivateSurface { get; } = SwiftColor.Dynamic(light: Rgb(234, 234, 240), dark: Rgb(28, 28, 34));

    /// <summary>A new tab's start surface: the pane's own glass, a shade deeper (canvas .vp: rgba(0, 0, 0, .18)).</summary>
    public static SwiftColor StartSurface { get; } = SwiftColor.Dynamic(light: Ink(.03), dark: Ink(.18));

    /// <summary>The favicon tile drawn when a page has none (canvas .fav: #7f8cff to #4a52c9).</summary>
    public static SwiftColor FaviconTop { get; } = Rgb(127, 140, 255);

    public static SwiftColor FaviconBottom { get; } = Rgb(74, 82, 201);
}
