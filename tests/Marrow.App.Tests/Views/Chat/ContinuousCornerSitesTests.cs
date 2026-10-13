using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Tests.Views.Chat;

/// <summary>
/// OD-0102 reaches every Border whose corner can sit in ED-275 (2)'s band (h / 3.057 &lt; r &lt; h / 2), not only the ones that call `.RoundedRectangle(...)`. Avalonia fills the
/// circle of a plain Border's CornerRadius, and Apple's corner at the largest radius that fits lies outside that circle near the diagonal, so a raw `CornerRadius = new
/// CornerRadius(r)` Border whose Swift is `.continuous` and whose height can be between 2 r and 3.057 r draws option B, not the owner's option A (the blitz-cornerband
/// re-review: "about 60 sites"; ED-275 (7) lists them as NOT DONE). This table accounts for EVERY such assignment in src: each is routed through
/// <see cref="SwiftShapes.RoundedRectangle{T}"/> (its fill, clip and ring then follow the curve) or is classed with the reason it never reaches the band. A new raw site, or a
/// routed one that loses its call, fails here.
/// </summary>
[Collection("Headless")]
public sealed class ContinuousCornerSitesTests(HeadlessSession headless)
{
    /// <summary>Swift `.continuous`, and the element can be between 2 r and 3.057 r tall: it must call RoundedRectangle (or UnevenRoundedRectangle).</summary>
    private const string Routed = "Routed";

    /// <summary>The radius is half the height or more by construction (999, 1000, height / 2, a pill of known height): the capsule regime, circular in both.</summary>
    private const string Capsule = "Capsule";

    /// <summary>A square element with radius half its side, or a circle by construction.</summary>
    private const string Circle = "Circle";

    /// <summary>A radius of at most 3 pt on a bar, tick, dot or cell (a capsule or a pixel-sized rectangle): no band a pixel shows.</summary>
    private const string Tiny = "Tiny";

    /// <summary>The Swift is `.circular` or a CALayer cornerRadius (HydraMergePopover.swift:126, DragHandle.swift:84-85), or the site is a design-page layout, not a Swift shape.</summary>
    private const string Circular = "Circular";

    /// <summary>
    /// Swift `.continuous` (or a Windows control theme), but the element is a panel, window, card or row taller than 3.057 r at every state, so Apple's corner at the stated
    /// radius fits and the band is not reached; the circle of the same radius differs from it by at most 0.0138 r (ED-275 (7), tracked, outside OD-0102).
    /// </summary>
    private const string Fit = "Fit";

    /// <summary>Not a Border drawn by Avalonia (a Panel's own property), a zero radius, or the shape helpers themselves.</summary>
    private const string Other = "Other";

    // (file under src, the radius argument as written, how many assignments, kind)
    private static readonly (string File, string Radius, int Count, string Kind)[] Sites =
    [
        ("Marrow.App/Port/ChatDialogControls.cs", "6", 1, Fit),
        ("Marrow.App/Port/FirstRun/GettingReadySheet.cs", "GettingReadyLook.CornerRadius", 1, Circular), // GR-2 (ED-306): the sheet card, a design-page layout (getting-ready variant I, CSS border-radius 18), not a Swift shape
        ("Marrow.App/Port/Ignis/Settings/IgnisSavingsHistoryView.cs", "5, 5, 3, 3", 1, Circular), // Ignis (OD-0134, ED-NEW-handoverfix-3): a month bar, design-page layout (work/design/phoenix/phoenix-designs.html .mchart .mb, CSS border-radius 5px 5px 3px 3px), not a Swift shape
        ("Marrow.App/Port/Ignis/Settings/IgnisSavingsHistoryView.cs", "8", 1, Circular), // Ignis (OD-0134, ED-NEW-handoverfix-3): a month column's transparent face, design-page layout (.mchart button, CSS border-radius 8px)
        ("Marrow.App/Port/Ignis/Settings/IgnisSavingsHistoryView.cs", "size / 2", 1, Circle), // Ignis (OD-0134, ED-NEW-handoverfix-3): a source dot, size x size with r = size / 2, a circle by construction
        ("Marrow.App/Port/Ignis/Settings/IgnisSavingsHistoryView.cs", "13", 1, Circle), // Ignis (OD-0134, ED-NEW-handoverfix-3): the month picker's 26 x 26 button with r = 13 (.mpick button, CSS border-radius 13px), a circle by construction
        ("Marrow.App/Port/Ignis/Settings/IgnisSettingsPage.cs", "5", 1, Circular), // Ignis (OD-0134, ED-NEW-handoverfix-3): the "in use" chip, design-page layout (tr.now td::after, CSS border-radius 5px)
        ("Marrow.App/Port/Ignis/Views/IgnisEventLine.cs", "16", 1, Circular), // Ignis (OD-0134, ED-NEW-handoverfix-3): the event notice card, design-page layout (.notice, CSS border-radius 16px)
        ("Marrow.App/Port/Ignis/Views/IgnisSidebarMark.cs", "PillHeight / 2", 1, Capsule), // Ignis (OD-0134, ED-NEW-ignis-guards): the sidebar mark's Turn off / Turn on pill, 20 pt tall with r = PillHeight / 2
        ("Marrow.App/Port/Ignis/Views/KindleCapsule.cs", "14", 1, Capsule), // Ignis (OD-0134, ED-NEW-handoverfix-3): the Kindle capsule's glow, 28 pt tall with r = 14 = h / 2 (.kindle, CSS border-radius 14px)
        ("Marrow.App/Port/Ignis/Views/KindlePopover.cs", "i == 0 ? 3 : 0, i == sources.Length - 1 ? 3 : 0, i == sources.Length - 1 ? 3 : 0, i == 0 ? 3 : 0", 1, Tiny), // Ignis (OD-0134, ED-NEW-handoverfix-3): the end segments of the 6 pt sources bar, r 3 (.sbar, CSS border-radius 3px)
        ("Marrow.App/Port/Ignis/Views/KindlePopover.cs", "4, 4, 2, 2", 1, Circular), // Ignis (OD-0134, ED-NEW-handoverfix-3): a day bar of the week chart, design-page layout (CSS border-radius 4px 4px 2px 2px)
        ("Marrow.App/Port/Ignis/Views/KindlePopover.cs", "10", 1, Circular), // Ignis (OD-0134, ED-NEW-handoverfix-3): the report text, design-page layout (.rep textarea, CSS border-radius 10px)
        ("Marrow.App/Port/Ignis/Views/KindlePopover.cs", "2", 2, Tiny), // Ignis (OD-0134, ED-NEW-handoverfix-3): the 4 pt context bar's track and fill, r 2 (.bp-bar and .bp-bar i, CSS border-radius 2px)
        ("Marrow.App/Port/Ignis/Views/KindlePopover.cs", "12", 1, Circular), // Ignis (OD-0134, ED-NEW-handoverfix-3): the suggestion box, design-page layout (.sug, CSS border-radius 12px)
        ("Marrow.App/Port/Ignis/Views/KindleUi.cs", "height / 2", 1, Capsule), // Ignis (OD-0134, ED-NEW-handoverfix-3): the design's .btn pill, r = h / 2
        ("Marrow.App/Port/LookCard.cs", "10", 1, Circular),
        ("Marrow.App/Port/LookCard.cs", "12", 1, Circular),
        ("Marrow.App/Port/ModelsPalette/OpenRouterPicker.cs", "999", 1, Capsule), // RR-19 (ED-636): the 28 pt search field, openrouter-picker/Main.dc.html .field (CSS border-radius 999), r far past h / 2
        ("Marrow.App/Port/ModelsPalette/OpenRouterPicker.cs", "9", 1, Circular), // RR-19: the 46 pt model row button, design-page layout (Main.dc.html .row, CSS border-radius 9), not a Swift shape
        ("Marrow.App/Port/ModelsPalette/OpenRouterPicker.cs", "size/2", 1, Circle), // RR-19: a maker dot, size x size with r = size / 2 (Main.dc.html .dot, CSS border-radius 50%), a circle by construction
        ("Marrow.App/Port/ModelsPalette/PaletteColoursSection.cs", "6", 1, Circle), // RR-21 (OD-0127): the Model colours row's 12 x 12 dot (palette-refresh-locked.html .dot, CSS border-radius 50%), a circle by construction
        ("Marrow.App/Port/MultiAccount/Capsule/AccountCapsule.cs", "11", 1, Capsule), // MA-9 (ED-452): the blurred rim copy behind the 22 pt account capsule (styles 11, 11o), r = h / 2
        ("Marrow.App/Port/MultiAccount/Capsule/PillPreview.cs", "8", 1, Circular), // MA-9 (ED-452): the preview stage inside a style card, design-page layout (multi-account.html .pv, CSS border-radius 8), not a Swift shape
        ("Marrow.App/Port/MultiAccount/Capsule/PillPreview.cs", "15", 1, Circle), // MA-9 (ED-452): the 30 x 30 send button of the preview composer, a circle by construction
        ("Marrow.App/Port/MultiAccount/Capsule/PillPreview.cs", "23", 1, Capsule), // MA-9 (ED-452): the 46 pt preview composer, r = h / 2
        ("Marrow.App/Port/MultiAccount/Capsule/PillStyleCards.cs", "12", 3, Circular), // MA-9 (ED-452): the glow card, a style card button and its shell, design-page layout (CSS border-radius 12), as CzStyleCards
        ("Marrow.App/Port/MultiAccount/Capsule/PillStyleCards.cs", "999", 1, Capsule), // MA-9 (ED-452): the small "default" tag, a pill with r far past h / 2
        ("Marrow.App/Port/MultiAccount/Capsule/PillStyleCards.cs", "9", 1, Circular), // MA-9 (ED-452): the glow variant button, design-page layout (CSS border-radius 9)
        ("Marrow.App/Port/MultiAccount/Capsule/PillStyleCards.cs", "10", 1, Circular), // MA-9 (ED-452): the picked-card ring, design-page layout (CSS border-radius 10)
        ("Marrow.App/Port/MultiAccount/Opinions/OpinionRoundView.cs", "13", 1, Capsule), // MA-W (ED-NEW-ma-wire-lead-1): the 26 pt Combine button, r = h / 2
        ("Marrow.App/Port/MultiAccount/Capsule/PillStyleCards.cs", "3", 1, Circle), // MA-9 (ED-452): the 6 x 6 dot of the legend, a circle by construction
        ("Marrow.App/Port/Settings/GlassPreviewTile.cs", "23", 2, Capsule), // OD-0147 (ED-NEW-glasslook-4): the Glass preview's 46 pt glass pill and its 46 x 46 round button, r = h / 2
        ("Marrow.App/Port/Settings/PreviewMenuItem.cs", "7", 1, Circular), // CZ-3 (ED-335): a model menu row, design-page layout (CSS .pitem border-radius 7)
        ("Marrow.App/Port/Settings/PreviewStepper.cs", "11", 1, Circle), // CZ-3 (ED-335): the 22 x 22 stepper, a circle by construction
        ("Marrow.App/Port/Settings/PreviewSwitchChip.cs", "14", 1, Capsule), // CZ-3 (ED-335): the 28 pt chip of the preview model switch, r = h / 2
        ("Marrow.App/Port/Settings/UpdatesMainButton.cs", "ButtonHeight / 2", 1, Capsule), // UE-4 (ED-343): the Updates page's 30 pt glass capsule; CzButton draws its focus ring from this radius, r = h / 2
        ("Marrow.App/Port/Voice/VoiceLanguageQuestion.cs", "CardRadius", 2, Circular), // VT-12 (V-12.11, ED-292): the design-page question card's 14 px border radius, not a Swift shape (glass and rim)
        ("Marrow.App/Port/SettingsOverlay.cs", "Chrome.WindowCornerRadius", 1, Fit), // OD-0151 (ED-NEW-settingsinapp-1): the Settings panel's shadow carrier behind its card (the card itself calls RoundedRectangle), r 26 on a panel no less than the 560 pt main window less its two vertical margins tall, far past 3.057 r (the Settings window's shadow carrier, SettingsShell.cs, is gone with the window)
        ("Marrow.App/Port/Web/McpApps/McpAppWindow.cs", "9", 1, Circular), // web W19 (ED-NEW-web-w19-5): the page inset under the pop-out strip (canvas .popw, .mweb), design-page layout (CSS border-radius 9)
        ("Marrow.App/Port/Web/McpApps/McpAppWindow.cs", "14", 1, Circular), // web W19 (ED-NEW-web-w19-5): the pop-out window's frame (canvas .popw, CSS border-radius 14), not a Swift shape
        ("Marrow.App/Port/Web/McpApps/McpAppWindow.cs", "7", 1, Circular), // web W19 (ED-NEW-web-w19-5): the 24 DIP pin and Back inline button (canvas .popw button), design-page layout (CSS border-radius 7)
        ("Marrow.App/Views/Browser/VisusAgentCard.cs", "9", 1, Circular), // web W11 (ED-NEW-web-w11-1): the card text's focus ring, design-page layout (web-surfaces canvas, as VisusPrompt's)
        ("Marrow.App/Views/Browser/VisusAgentCard.cs", "10", 1, Circular), // web W11: the rows box, design-page layout (the canvas's .kv, CSS border-radius 10px)
        ("Marrow.App/Views/Browser/VisusAgentCard.cs", "11", 1, Capsule), // web W11: the chat and model chips, 22 DIPs tall with r = 11 (the canvas's .who span)
        ("Marrow.App/Views/Chat/ChatBadge.cs", "TimelineMetrics.PillRadius", 1, Other),
        ("Marrow.App/Views/Chat/HydraButton.cs", "999", 1, Capsule),
        ("Marrow.App/Views/Chat/HydraMergePopover.cs", "10", 1, Fit),
        ("Marrow.App/Views/Chat/HydraMergePopover.cs", "12", 1, Capsule),
        ("Marrow.App/Views/Chat/HydraMergePopover.cs", "6", 1, Circular),
        ("Marrow.App/Views/Chat/HydraPanel.cs", "14", 1, Circle),
        ("Marrow.App/Views/Chat/HydraPanel.cs", "16", 1, Routed),
        ("Marrow.App/Views/Chat/HydraPanel.cs", "6", 1, Fit),
        ("Marrow.App/Views/Chat/HydraPanel.cs", "Chrome.CapsuleHeight / 2", 1, Capsule),
        ("Marrow.App/Views/Chat/HydraPanel.cs", "CornerRadius", 1, Fit),
        ("Marrow.App/Views/Chat/HydraProgressBar.cs", "Chrome.CardCornerRadius", 2, Routed),
        ("Marrow.App/Views/Chat/HydraReportsPopover.cs", "10", 1, Fit),
        ("Marrow.App/Views/Chat/HydraReportsPopover.cs", "6", 1, Fit),
        ("Marrow.App/Views/Chat/HydraReportsPopover.cs", "999", 1, Capsule),
        ("Marrow.App/Views/Chat/InlineWebWidget.cs", "3", 1, Circle), // web W19 (ED-NEW-web-w19-4): the Live pill's 6 x 6 dot (canvas .live i), a circle by construction
        ("Marrow.App/Views/Chat/InlineWebWidget.cs", "10", 1, Capsule), // web W19 (ED-NEW-web-w19-4): the 20 DIP Live pill, r = h / 2 (canvas .live)
        ("Marrow.App/Views/Chat/InlineWebWidget.cs", "9", 2, Circular), // web W19 (ED-NEW-web-w19-4): the page inset under the card header (canvas .mweb) and the Inline / Pane / Pop out switch (canvas .mseg), design-page layout (CSS border-radius 9)
        ("Marrow.App/Views/Chat/InlineWebWidget.cs", "14", 1, Circular), // web W19 (ED-NEW-web-w19-4): the app card (canvas .mapp), design-page layout (CSS border-radius 14), not a Swift shape
        ("Marrow.App/Views/Chat/InlineWebWidget.cs", "12", 1, Circular), // web W19 (ED-NEW-web-w19-4): the card's placeholder while the page is in the pane or window (canvas .inl-ph), design-page layout (CSS border-radius 12)
        ("Marrow.App/Views/Chat/InlineWebWidget.cs", "7", 2, Circular), // web W19 (ED-NEW-web-w19-4): a segment of the display switch (canvas .mseg button) and the card's text button, design-page layout (CSS border-radius 7)
        ("Marrow.App/Views/Chat/McpAppRows.cs", "12", 1, Circular), // web W19 (ED-NEW-web-w19-2): the "Message from shipyard" row, the placeholder's design-page layout (CSS border-radius 12)
        ("Marrow.App/Views/Chat/SubagentPanel.cs", "Chrome.CapsuleHeight / 2", 1, Capsule),
        ("Marrow.App/Views/Chat/SubagentPanel.cs", "CornerRadius", 2, Fit),
        ("Marrow.App/Views/Chat/ThreadTimeline.cs", "999", 1, Capsule),
        ("Marrow.App/Views/Chat/ThreadTimeline.cs", "TimelineMetrics.PillRadius", 2, Routed),
        ("Marrow.App/Views/Chat/TimelineMinimap.cs", "1", 1, Tiny),
        ("Marrow.App/Views/Chat/TimelineRows.cs", "14", 1, Circle),
        ("Marrow.App/Views/Composer/AppContextChips.cs", "5", 1, Circular), // web W19 (ED-NEW-web-w19-2): the untrusted mark on an app context chip, a small outlined word, design-page layout (CSS border-radius 5)
        ("Marrow.App/Views/Composer/ComposerTextView.cs", "0", 1, Other),
        ("Marrow.App/Views/Composer/FollowUpQueueTab.cs", "12, 12, 0, 0", 1, Fit),
        ("Marrow.App/Views/Composer/FollowUpQueueTab.cs", "999", 1, Capsule),
        ("Marrow.App/Views/Composer/FollowUpQueueTab.cs", "isDragged || isPairTarget ? new CornerRadius(999) : new CornerRadius(0)", 1, Capsule), // :784 a lifted or pair-target row is a capsule (FollowUpQueueTab.swift:467-475); at rest 0
        ("Marrow.App/Views/Composer/HydraFusedPairIcon.cs", "0.5", 1, Tiny),
        ("Marrow.App/Views/Composer/PromptEditor.cs", "12", 1, Routed),
        ("Marrow.App/Views/Composer/QuestionTab.cs", "3", 1, Circle),
        ("Marrow.App/Views/Composer/ThreadChangesTab.cs", "12, 12, 0, 0", 1, Fit),
        ("Marrow.App/Views/Diff/DiffInspector.cs", "1000", 1, Capsule),
        ("Marrow.App/Views/Diff/DiffInspector.cs", "12", 1, Routed),
        ("Marrow.App/Views/Diff/DiffInspector.cs", "rounding", 1, Capsule), // :897 DiffLineRow, Swift UnevenRoundedRectangle(8, .continuous) (DiffInspector.swift:439, :477-481): a one-line run measures 16 pt headless, 2 r, the capsule edge, not in the band; a wrapped run is taller than 3.057 r
        ("Marrow.App/Views/Settings/AboutUpdateViews.cs", "1.5", 1, Tiny),
        ("Marrow.App/Views/Settings/MCPKeyPanel.cs", "8", 1, Fit),
        ("Marrow.App/Views/Settings/MCPKeyPanel.cs", "9", 1, Circle),
        ("Marrow.App/Views/Settings/MCPSetupPanel.cs", "9", 1, Circle),
        ("Marrow.App/Views/Settings/ModelsSettingsPage.cs", "10", 1, Fit),
        ("Marrow.App/Views/Settings/ModelsSettingsPage.cs", "Chrome.CardCornerRadius", 1, Fit),
        ("Marrow.App/Views/Settings/TokenActivitySection.cs", "999", 1, Capsule),
        ("Marrow.App/Views/Settings/TokenActivitySection.cs", "TokenActivityStyle.CellRadius", 1, Tiny),
        ("Marrow.App/Views/Settings/WallpaperLibrarySection.cs", "WallpaperThumbnail.Radius", 1, Circular), // WL-2 (ED-NEW-wltwo-1, ED-NEW-reltests-2): the hairline over a tile's live picture, which the engine draws with a circular 10 pt corner (WallpaperThumbnail.Radius; the design pages' tiles, CSS border-radius 12 to 13 px), a design-page layout, not a Swift shape
        ("Marrow.App/Views/Settings/WallpaperLibrarySection.cs", "WallpaperThumbnail.Radius + 4", 1, Circular), // WL-2 (ED-NEW-wltwo-1): the accent ring 4 pt out round a chosen tile, concentric with the picture's circular corner (r + 4), the same design-page layout
        ("Marrow.App/Views/Settings/WallpaperLibrarySection.cs", "Swatch / 2", 3, Circle), // WL-2 (ED-NEW-wltwo-1): a colour pack's 34 x 34 disc, its shine and its edge, each Swatch x Swatch with r = Swatch / 2, a circle by construction
        ("Marrow.App/Views/Settings/WallpaperLibrarySection.cs", "Swatch / 2 + 4", 1, Circle), // WL-2 (ED-NEW-wltwo-1): the chosen swatch's halo, (Swatch + 8) x (Swatch + 8) with r = Swatch / 2 + 4, a circle by construction
        ("Marrow.App/Views/Sidebar/FloatingSidebarPanel.cs", "22", 2, Fit),
        ("Marrow.App/Views/Terminal/TerminalPanel.cs", "16, 16, 0, 0", 1, Fit),
        ("Marrow.App/Views/Terminal/TerminalPanel.cs", "999", 1, Capsule),
        ("Marrow.App/Views/Usage/UsageFloatingPanel.cs", "cornerRadius", 2, Fit), // :84, :92 the panel and its scrim, r 22 (HydraPanel.CornerRadius), tall panels
        ("Marrow.App/Views/Usage/UsageViews.cs", "2.5", 2, Tiny),
        ("Marrow.App/Views/Panes/AppPane.cs", "9", 1, Circular), // web W19 (ED-NEW-web-w19-4): the page inset under the App pane header, design-page layout (canvas .mweb, CSS border-radius 9), not a Swift shape
        ("Marrow.App/Views/Panes/PaneCard.cs", "PaneTokens.CornerRadius", 1, Circular), // web W3a (ED-NEW-web-w3a-1): the pane card, design-page layout (canvas .gl, CSS border-radius 12), not a Swift shape
        ("Marrow.App/Views/Panes/PaneCard.cs", "PaneTokens.CornerRadius - 1", 1, Circular), // web W3a (ED-NEW-web-w3a-1): the card's content clip inside its 1 DIP rim, the same design-page corner
        ("Marrow.App/Views/Browser/DevicePresetBar.cs", "8", 3, Circular), // W9 (ED-NEW-web-w9-10): approved index.html:321,327 preset/rotate buttons and 16 DIP error badge, CSS circular corners
        ("Marrow.App/Views/Browser/DevicePresetBar.cs", "10", 1, Circular), // W9: index.html:320 segmented control, a design-page corner
        ("Marrow.App/Views/Browser/DevicePresetBar.cs", "13", 2, Capsule), // W9: index.html:324 Reload/Console toggles, height 26 and radius = height / 2
        ("Marrow.App/Views/Browser/DevicePresetBar.cs", "6", 1, Circular), // W9: ED-NEW-web-w9-3 shared preview action style; design-page control, not a Swift continuous shape
        ("Marrow.App/Views/Browser/PreviewConsoleDrawer.cs", "10", 1, Circular), // W9: index.html:344 drawer, CSS border-radius 10
        ("Marrow.App/Views/Browser/PreviewConsoleDrawer.cs", "7", 1, Circular), // W9: index.html:348 Console/Network tab loop, CSS border-radius 7
        ("Marrow.App/Views/Browser/PreviewConsoleDrawer.cs", "11", 1, Capsule), // W9: index.html:356 Send to chat, height 22 and radius = height / 2
        ("Marrow.App/Views/Browser/ProjectPreviewBody.cs", "6", 1, Circular), // W9: index.html:331 device frame, CSS border-radius 6; hidden in Fill (:332)
        ("Marrow.UI/Chrome/Splitter.cs", "radius", 1, Circular), // W9 shared grips: index.html:98 .spl .tip (8), :103 .spl .rd (7), a CSS design-page factory, not a Swift shape
        ("Marrow.UI/Panes/PaneHeader.cs", "12", 1, Capsule), // web W3a (ED-NEW-web-w3a-1): a 24 DIP tab pill, r = h / 2 (canvas .tabg button)
        ("Marrow.UI/Panes/PaneHeader.cs", "8", 2, Circular), // web W3a (ED-NEW-web-w3a-1): a 26 DIP header button and text chip, design-page layout (canvas .hb, CSS border-radius 8)
        ("Marrow.UI/Panes/PaneHeader.cs", "11", 1, Capsule), // web W3a (ED-NEW-web-w3a-1): the 22 DIP "Chat is working" chip, r = h / 2 (canvas .pchip)
        ("Marrow.UI/Panes/PaneHeader.cs", "3", 1, Circle), // web W3a (ED-NEW-web-w3a-1): the chip's 6 x 6 dot, a circle by construction
        ("Marrow.UI/Panes/NarrowTabStrip.cs", "PillHeight / 2", 1, Capsule), // web W3c (ED-NEW-web-w3c-2): the 34 DIP narrow tab strip's pill, r = h / 2 (canvas .ntabs .in, border-radius 999)
        ("Marrow.UI/Panes/NarrowTabStrip.cs", "TabHeight / 2", 1, Capsule), // web W3c (ED-NEW-web-w3c-2): a 28 DIP tab of the narrow strip, r = h / 2 (canvas .ntabs button)
        ("Marrow.UI/Chrome/Chrome.cs", "side / 2", 1, Circle),
        ("Marrow.UI/Chrome/Chrome.cs", "Chrome.CapsuleContentHeight / 2", 1, Circle), // ED-NEW-segmenthover-1 (ED-NEW-reltests-2): the hover plate behind one segment of a toolbar pill, in a Panel frame fixed at CapsuleContentHeight x CapsuleContentHeight (28 x 28) with r = side / 2, a circle by construction (PORT-ONLY, no Swift shape)
        ("Marrow.UI/Chrome/ChromeSegmentedPicker.cs", "11", 1, Capsule),
        ("Marrow.UI/Chrome/DragHandle.cs", "Metrics.GlowRadius", 1, Circular),
        ("Marrow.UI/Chrome/DragHandle.cs", "grip.Thickness / 2", 1, Capsule),
        ("Marrow.UI/Common/PreviewLoading.cs", "Size / 2", 1, Circle), // OD-0136 amendment 2 (nth step 13): the 44 x 44 glass puck of the large preview, r = side / 2, a circle by construction
        ("Marrow.UI/Port/PortControlThemes.cs", "10", 1, Circle),
        ("Marrow.UI/Port/PortControlThemes.cs", "2", 1, Tiny),
        ("Marrow.UI/Port/PortControlThemes.cs", "7", 1, Circle),
        ("Marrow.UI/Port/PortControlThemes.cs", "7 + 3 * fraction", 1, Circle),
        ("Marrow.UI/Port/SwiftShapeBorder.cs", "cornerRadii", 1, Other),
        ("Marrow.UI/Port/SwiftShapeBorder.cs", "cornerRadius", 1, Other),
        ("Marrow.UI/Port/SwiftShapes.cs", "CapsuleCornerRadius(border.Bounds.Size)", 1, Other),
        ("Marrow.UI/Port/SwiftShapes.cs", "CapsuleCornerRadius(e.NewSize)", 1, Other),
        ("Marrow.UI/Port/SwiftShapes.cs", "cornerRadii", 1, Other),
        ("Marrow.UI/Port/SwiftShapes.cs", "cornerRadius", 1, Other),
        ("Marrow.UI/Port/Settings/CzControls/CzAutoButton.cs", "10", 1, Capsule), // CZ-1 (ED-312): the 40 x 20 Auto capsule (cz.js .auto)
        ("Marrow.UI/Port/Settings/CzControls/CzButton.cs", "13", 1, Capsule), // CZ-1 (ED-312): the 26 pt capsule button
        ("Marrow.UI/Port/Settings/CzControls/CzColorWell.cs", "10", 1, Circle), // CZ-1 (ED-312): the 20 pt well dot
        ("Marrow.UI/Port/Settings/CzControls/CzMenuButton.cs", "14", 1, Capsule), // CZ-1 (ED-312): the 28 pt menu button
        ("Marrow.UI/Port/Settings/CzControls/CzMenuButton.cs", "12", 1, Circular), // CZ-1 (ED-312): the menu panel, a design-page layout (CSS border-radius 12)
        ("Marrow.UI/Port/Settings/CzControls/CzMenuItem.cs", "7", 1, Circular), // CZ-1 (ED-312): a menu row, design-page layout (CSS 7)
        ("Marrow.UI/Port/Settings/CzControls/CzOrbTiles.cs", "9", 1, Circular), // CZ-1 (ED-312): the orb tile button, design-page layout (CSS 9)
        ("Marrow.UI/Port/Settings/CzControls/CzOrbTiles.cs", "8", 1, Circular), // CZ-1 (ED-312): the 96 x 56 orb tile, design-page layout (CSS 8)
        ("Marrow.UI/Port/Settings/CzControls/CzSegmentedControl.cs", "small ? 11 : 12", 1, Capsule), // CZ-1 (ED-312): the 22 / 24 pt segment pill, r = h / 2
        ("Marrow.UI/Port/Settings/CzControls/CzStyleCards.cs", "12", 2, Circular), // CZ-1 (ED-312): the style card button and its shell, design-page layout (CSS border-radius 12)
        ("Marrow.UI/Port/Settings/CzControls/CzSwatchRow.cs", "12", 1, Circle), // CZ-1 (ED-312): the 24 pt swatch, a circle by construction
        ("Marrow.UI/Port/Settings/CzControls/CzSlider.cs", "10", 1, Capsule), // CZ-1 (ED-312): the 40 x 20 Auto capsule
        ("Marrow.UI/Port/Settings/Voice/VoiceGlyphButton.cs", "10", 1, Circle), // VT-14 (ED-342 (13)): the 20 pt round cancel and trash buttons of a model row, a circle by construction
        ("Marrow.UI/Port/Settings/GlassColorPicker.cs", "HexRadius", 1, Capsule), // CZ-2 (ED-313): the 28 pt hex pill, r 14 = h / 2 (lp-page.css .mg-hex)
        ("Marrow.UI/Port/Settings/GlassColorPicker.cs", "DotSize / 2", 1, Circle), // CZ-2 (ED-313): the 22 pt preview dot beside the hex field, a circle by construction
        ("Marrow.UI/Port/SystemButtons.cs", "radius", 2, Fit), // H3 (ED-291): a capsule (height / 2) under presentedChrome, else the kit's Radius 6 on a 22-24 pt button: never 2r < h < 3.057r
        ("Marrow.UI/Port/SystemButtons.cs", "radius + FocusBand", 1, Fit),
        ("Marrow.UI/Port/SystemButtons.cs", "Radius", 2, Fit),
        ("Marrow.UI/Port/SystemButtons.cs", "Radius + FocusBand", 1, Fit),
        // ED-NEW-web-w9cx-1: the sites the web units and the PC-control work added without a row (each file's header says PORT-ONLY and "No Swift counterpart" / "No Swift twin"; the kinds below follow the geometry first, then the design-page rule)
        ("Marrow.App/Port/PcControl/Overlay/OverlaySpike.cs", "12", 1, Fit), // P25a (ED-NEW-pcctl-22): a debug-only probe (never shipped), a fixed 60 DIP blurred box with r = 12: 60 > 3.057 r
        ("Marrow.App/Port/PcControl/Overlay/OverlaySpike.cs", "22", 1, Circular), // P25a: the probe's card (PillWidth wide, padding 16), a debug-only window (never shipped), not a Swift shape
        ("Marrow.App/Port/PcControl/Overlay/OverlaySpike.cs", "24", 1, Fit), // P25a: the probe's fixed 240 DIP card with r = 24: 240 > 3.057 r
        ("Marrow.App/Port/PcControl/Overlay/OverlaySpike.cs", "OverlaySpike.PillHeight / 2", 2, Capsule), // P25a: the probe's 48 DIP pill with r = height / 2
        ("Marrow.App/Views/Artifacts/TabulaAsk.cs", "12", 1, Circular), // web W14 (ED-NEW-web-w14-1): the "Change this part" chip over the composer, 26 DIP high (a 20 DIP clear button and 3 DIP padding) with r = 12, a W14 chip with no Swift counterpart (DESIGN.md 6.3), a circular arc by design
        ("Marrow.App/Views/Artifacts/TabulaAsk.cs", "17", 1, Circle), // web W14: the 34 x 34 send button (canvas .ask .sendb, CSS border-radius 50%), a circle by construction
        ("Marrow.App/Views/Artifacts/TabulaAsk.cs", "23", 1, Circular), // web W14: the Ask pill, canvas .ask (min-height 46, CSS border-radius 23px, a circular arc), which grows with its lines
        ("Marrow.App/Views/Artifacts/TabulaBridgeHost.cs", "9", 1, Circular), // security review L1 (ED-NEW-web-sec-artifacts-3): the consent card words' focus ring, as VisusPrompt's (the app's focus band round a 4 DIP padded row), a design-page layout, no Swift shape
        ("Marrow.App/Views/Artifacts/TabulaBridgeHost.cs", "14", 1, Circular), // web W14: the consent card, canvas .pop.consent (320 wide, CSS border-radius 14px), a design-page layout
        ("Marrow.App/Views/Artifacts/TabulaCode.cs", "chip ? 12 : 7", 1, Circular), // web W14: the file chip (24 DIP high, r = 12, canvas .vchip) in a narrow pane and the file row (26 DIP high, r = 7) beside the text in a wide one: neither reaches the band, and neither is a Swift shape
        ("Marrow.App/Views/Artifacts/TabulaGallery.cs", "10", 2, Circular), // web W14: the 32 DIP search field and the 20 DIP kind badge of a card (r = 10 = h / 2), design-page layout, no Swift counterpart
        ("Marrow.App/Views/Artifacts/TabulaGallery.cs", "12", 1, Fit), // web W14: a gallery card: the 20 DIP kind badge row inside 10 DIP of padding both sides, at least 40 DIP, with r = 12: 40 > 3.057 x 12
        ("Marrow.App/Views/Artifacts/TabulaParts.cs", "10", 3, Circular), // web W14: the segmented track (canvas .vbar .seg, padding 2, CSS border-radius 10px), its segment's focus ring (the 8 DIP face + 2) and the notice strip, design-page layouts, no Swift shape
        ("Marrow.App/Views/Artifacts/TabulaParts.cs", "15", 1, Capsule), // web W14: the 30 DIP pill button (canvas .pbtn, CSS border-radius 15px), r = h / 2
        ("Marrow.App/Views/Artifacts/TabulaParts.cs", "8", 2, Fit), // web W14: a 26 DIP segment (canvas .vbar .seg button, CSS border-radius 8px) and a 26 DIP text button: 26 > 3.057 x 8
        ("Marrow.App/Views/Artifacts/TabulaParts.cs", "radius", 1, Circular), // web W14: the skeleton bars (canvas .skel i: 14 DIP high with CSS border-radius 7px, and the 90 and 120 DIP blocks with 14px), design-page layout, no Swift shape
        ("Marrow.App/Views/Artifacts/TabulaParts.cs", "size / 3.25", 1, Fit), // web W14: the square glyph button (canvas .hb, 26 x 26 with CSS border-radius 8px): the height is 3.25 radii at every size, 3.25 > 3.057
        ("Marrow.App/Views/Artifacts/TabulaVersionStrip.cs", "5", 1, Circle), // web W14: a version dot, 10 x 10 (canvas .vn .dot, CSS border-radius 50%), a circle by construction
        ("Marrow.App/Views/Artifacts/TabulaVersionStrip.cs", "9", 1, Circle), // web W14: the dot's halo, 18 x 18 with r = 9, a circle by construction
        ("Marrow.App/Views/Artifacts/TabulaView.cs", "8", 2, Fit), // web W14: the two 26 DIP header buttons (permissions, compare) with r = 8: 26 > 3.057 x 8
        ("Marrow.App/Views/Browser/VisusCards.cs", "8", 1, Fit), // web W8b: the card's text field frame, 30 DIP high with r = 8: 30 > 3.057 x 8
        ("Marrow.App/Views/Browser/VisusFindBar.cs", "6", 1, Fit), // web W8c: the match-case toggle, 22 x 24 with r = 6: 24 > 3.057 x 6
        ("Marrow.App/Views/Browser/VisusFindBar.cs", "8", 1, Fit), // web W8c: a 26 x 26 glyph button (canvas .hb, CSS border-radius 8px): 26 > 3.057 x 8
        ("Marrow.App/Views/Browser/VisusPaneBody.cs", "0, 0, 6, 6", 1, Fit), // web W8a: the page area's two bottom corners (r = 6): the pane is at least 240 DIP high (DESIGN.md 3.2 minimums), far over 3.057 x 6
        ("Marrow.App/Views/Browser/VisusPaneBody.cs", "7", 1, Fit), // web W8a: a history row, 5 DIP of padding above and below a 12.5 DIP line (at least 26 DIP) with r = 7: 26 > 3.057 x 7
        ("Marrow.App/Views/Browser/VisusPaneBody.cs", "8", 1, Fit), // web W8c: the zoom stepper's 28 DIP button with r = 8: 28 > 3.057 x 8
        ("Marrow.App/Views/Browser/VisusPrompt.cs", "9", 1, Circular), // web W8a/W8b: the prompt text's focus ring (the app's focus band round a 4 DIP padded row), a design-page layout, no Swift shape
        ("Marrow.App/Views/Browser/VisusTabStrip.cs", "4", 1, Fit), // web W8a: the 14 x 14 favicon box (canvas .fav, CSS border-radius 4px): 14 > 3.057 x 4
        ("Marrow.App/Views/Browser/VisusTabStrip.cs", "5", 1, Fit), // web W8a: the 18 x 18 tab close button (canvas .tab .x, CSS border-radius 5px): 18 > 3.057 x 5
        ("Marrow.App/Views/Browser/VisusTabStrip.cs", "8", 1, Fit), // web W8a: the 26 x 26 new-tab and tab-list button (canvas .hb, CSS border-radius 8px): 26 > 3.057 x 8
        ("Marrow.App/Views/Browser/VisusTabStrip.cs", "9", 1, Fit), // web W8a: a tab, 28 DIP high (canvas .tab, CSS border-radius 9px): 28 > 3.057 x 9 (27.5)
        ("Marrow.App/Views/Browser/VisusToolbar.cs", "10", 2, Capsule), // web W8a: the 20 DIP trust strip and the 20 DIP chip with r = h / 2
        ("Marrow.App/Views/Browser/VisusToolbar.cs", "11", 1, Capsule), // web W8c: the 22 DIP zoom chip (canvas .zchip, CSS border-radius 11px), r = h / 2
        ("Marrow.App/Views/Browser/VisusToolbar.cs", "8", 2, Fit), // web W8a: the 28 DIP toolbar button and the 30 DIP suggestion row with r = 8: both over 3.057 x 8
        ("Marrow.App/Views/Browser/VisusToolbar.cs", "VisusTokens.AddressHeight / 2", 1, Capsule), // web W8a: the 28 DIP address field (canvas .url, CSS border-radius 14px), r = h / 2
        ("Marrow.App/Views/Chat/TactusStrip.cs", "StripHeight / 2", 1, Capsule), // Tactus strip (OD-0145, ED-NEW-pcctl-27): the 32 DIP strip with r = height / 2 (its glass shape is RoundedRectangle(StripHeight / 2))
        ("Marrow.App/Views/Diff/DiffPaneChrome.cs", "13", 1, Capsule), // web W4: the 26 DIP selection menu pill with r = h / 2
        ("Marrow.App/Views/Diff/DiffPaneChrome.cs", "8", 1, Fit), // web W4: the 26 x 26 More button (canvas .hb, CSS border-radius 8px): 26 > 3.057 x 8
        ("Marrow.App/Views/Panes/BackgroundTasksPane.cs", "10", 1, Circular), // web W5: a segment's focus ring (the 26 DIP face + 2 DIP each side, r = 8 + 2), a design-page layout, no Swift shape
        ("Marrow.App/Views/Panes/BackgroundTasksPane.cs", "11", 1, Circular), // web W5: the This thread / All threads track (canvas .tfilter .seg at 0.9: padding 3, CSS border-radius 12px, drawn at 11), a design-page layout
        ("Marrow.App/Views/Panes/BackgroundTasksPane.cs", "8", 1, Fit), // web W5: a 26 DIP segment (canvas .seg button at 0.9) with r = 8: 26 > 3.057 x 8
        ("Marrow.App/Views/Panes/JobCard.cs", "5", 1, Fit), // web W5: a 20 DIP text action with r = 5: 20 > 3.057 x 5
        ("Marrow.App/Views/Panes/JobCard.cs", "JobTokens.ButtonRadius", 1, Fit), // web W5: the 24 x 24 card button (canvas .tcard .ops button, CSS border-radius 7px): 24 > 3.057 x 7
        ("Marrow.App/Views/Panes/JobCard.cs", "JobTokens.CardRadius", 1, Fit), // web W5: the task card (canvas .tcard, CSS border-radius 11px): a title row, a kind line and 9 DIP of padding both sides, at least 40 DIP, over 3.057 x 11
        ("Marrow.App/Views/Panes/PaneRail.cs", "6", 2, Fit), // web W3b: the rail's 18 x 22 buttons (canvas .rail button, CSS border-radius 6px): 22 > 3.057 x 6
        ("Marrow.App/Views/Panes/PaneRail.cs", "PaneTokens.RailCornerRadius", 1, Fit), // web W3b: the collapse rail (canvas .rail, CSS border-radius 11px), as tall as its pane (at least 150 DIP), over 3.057 x 11
        ("Marrow.App/Views/Panes/SideChatComposer.cs", "SendSize / 2", 1, Circle), // web W6: the 30 x 30 send button (canvas .scomp .sendb, CSS border-radius 50%), a circle by construction
        ("Marrow.App/Views/Panes/SideChatPane.cs", "14", 1, Capsule), // web W6: the 28 DIP Send to main chip with r = h / 2
        ("Marrow.UI/Chrome/SplitterGrip.cs", "3", 2, Tiny), // web W1: the grip's 4 x 28 pill and its lit copy (canvas .spl .grip, CSS border-radius 3px): a radius of 3 on a 4 DIP bar
        ("Marrow.UI/Chrome/SplitterGrip.cs", "3 + RingWidth", 1, Circular), // web W1: the grip's focus ring, 2 DIP out round the pill (8 x 32 for an edge grip, 13 x 13 for the corner glyph) with r = 5, the app's focus band, a design-page layout, no Swift twin
        ("Marrow.UI/Chrome/SplitterGrip.cs", "glyphBright.CornerRadius = radius", 1, Tiny), // web W1: the corner glyph's lit copy shares its statement with the glyph: one corner of 3 on a 9 x 9 stroke (canvas .spl.cn .grip)
        ("Marrow.UI/Chrome/SplitterGrip.cs", "radius", 1, Tiny), // web W1: the corner glyph, one corner of 3 on a 9 x 9 stroke (canvas .spl.cn .grip, CSS border-radius 0 0 3px 0)
        ("Marrow.UI/NthUi/Charts/NthUiChartLegend.cs", "8", 1, Circular), // web W16b: a pie key's row, padding 4 round one text line, a design-page layout, no Swift counterpart
        ("Marrow.UI/NthUi/Diagram/NthUiSvgView.cs", "10", 1, Fit), // web W16c: the note that stands in for a picture that cannot be read, a 16 DIP icon row inside 10 DIP of padding both sides (at least 36 DIP), over 3.057 x 10
        ("Marrow.UI/NthUi/NthUiBlocks.cs", "10", 3, Circular), // web W16a: the 20 DIP badge (r = h / 2), the image box and the degraded line's note, design-page layouts, no Swift counterpart
        ("Marrow.UI/NthUi/NthUiChoices.cs", "9", 1, Fit), // web W16a: a 30 DIP segment (canvas .seg button, CSS border-radius 9px): 30 > 3.057 x 9
        ("Marrow.UI/NthUi/NthUiControls.cs", "radius", 1, Circular), // web W16a: the pressable face every component uses; each caller states its own radius (height / 2, 8, 10, 11, 12 or 14), design-page layouts, no Swift counterpart
        ("Marrow.UI/NthUi/NthUiInputs.cs", "12", 1, Circular), // web W16a: the select menu's surface (CSS border-radius 12px), grows with its options, a design-page layout
        ("Marrow.UI/NthUi/NthUiInputs.cs", "15", 1, Circle), // web W16a: a 30 x 30 day cell with r = 15, a circle by construction
        ("Marrow.UI/NthUi/NthUiInputs.cs", "NthUiTokens.ControlRadius", 2, Circular), // web W16a: the 32 DIP stepper capsule (canvas .stp, CSS border-radius 12px) and the text field (32 DIP, or 76 for several lines), design-page layouts (a circular arc), no Swift counterpart
        ("Marrow.UI/Panes/PaneHeader.cs", "7", 1, Fit), // web W3a: the title menu button, 24 DIP high, with r = 7: 24 > 3.057 x 7
        ("Marrow.UI/Port/PcControl/ApprovalCardBody.cs", "12", 1, Circular), // Tactus pill v2 (OD-0145, ED-NEW-pcctl-26): the risk box (9 DIP of padding round its lines), a card part drawn from the pc-control-ui canvas, no Swift twin
        ("Marrow.UI/Port/PcControl/CardParts.cs", "5", 1, Circular), // Tactus pill v2: a key chip (canvas kbd, CSS border-radius 5px), 14.5 DIP high, a design-page layout
        ("Marrow.UI/Port/PcControl/CardParts.cs", "radius", 1, Capsule), // Tactus pill v2: a card button, 30 or 34 DIP high with r = h / 2 (the link look has no box and radius 0)
        ("Marrow.UI/Port/PcControl/CardParts.cs", "radius + 4", 1, Capsule), // Tactus pill v2: the card button's focus ring, 4 DIP out round a capsule, r = (h + 8) / 2
        ("Marrow.UI/Port/PcControl/ConfirmCardBody.cs", "12", 1, Circular), // Tactus pill v2: the "what changed" box of the confirm card, a card part drawn from the pc-control-ui canvas, no Swift twin
        ("Marrow.UI/Port/PcControl/ConfirmCardBody.cs", "14", 1, Circular), // Tactus pill v2: the confirm card's rows box, a card part drawn from the pc-control-ui canvas, no Swift twin
        ("Marrow.UI/Port/PcControl/ConfirmCardBody.cs", "5", 1, Fit), // Tactus pill v2: the element chip, 26 DIP high with r = 5: 26 > 3.057 x 5
        ("Marrow.UI/Port/PcControl/ControlPill.cs", "1", 1, Tiny), // Tactus pill v2: one of the two 2 x 8 bars of the pause glyph with r = 1
        ("Marrow.UI/Port/PcControl/ControlPill.cs", "12", 1, Fit), // Tactus pill v2: a 42 DIP row of the list below the capsule with r = 12: 42 > 3.057 x 12
        ("Marrow.UI/Port/PcControl/ControlPill.cs", "15", 1, Capsule), // Tactus pill v2: the 30 DIP hint (HintHeight) with r = h / 2
        ("Marrow.UI/Port/PcControl/ControlPill.cs", "18", 1, Circular), // Tactus pill v2: the list below the capsule (6 DIP of padding round rows of 42 DIP, from one row up), a design-page layout (a circular arc), no Swift twin
        ("Marrow.UI/Port/PcControl/ControlPill.cs", "5", 1, Circular), // Tactus pill v2: a key chip in the hint (canvas kbd: mono 10.5, padding 2 5, radius 5), a design-page layout
        ("Marrow.UI/Port/PcControl/ControlPill.cs", "6", 1, Circular), // Tactus pill v2: the key cap beside Stop (padding 5 round a 10.5 line), a design-page layout, no Swift twin
        ("Marrow.UI/Port/PcControl/ControlPill.cs", "9", 1, Capsule), // Tactus pill v2: the 18 DIP count badge with r = h / 2
        ("Marrow.UI/Port/PcControl/ControlPill.cs", "radius", 1, Capsule), // Tactus pill v2: a pill button, 28 / 30 / 18 / 36 DIP high with r = h / 2 in every look
        ("Marrow.UI/Port/PcControl/ControlPill.cs", "radius + 4", 1, Capsule), // Tactus pill v2: the pill button's focus ring, 4 DIP out round a capsule, r = (h + 8) / 2
        ("Marrow.UI/Port/PcControl/MiniComposer.cs", "20", 1, Circular), // Tactus pill v2: the card's reply field (one line growing to four, r = 20), a design-page layout drawn from the pc-control-ui canvas, no Swift twin
        ("Marrow.UI/Port/PcControl/ProviderPointerBody.cs", "10", 1, Fit), // Tactus pill v2: the command well (canvas pre.kcm), 9 DIP of padding round a 12 / 18 mono line (at least 36 DIP), over 3.057 x 10
        ("Marrow.UI/Port/PcControl/TactusPointer.cs", "BadgeSize * 0.24", 1, Fit), // Tactus pointer: the 13 x 13 app badge with r = 3.1: the height is 4.2 radii
        ("Marrow.UI/Port/PcControl/TactusPointer.cs", "PauseSize / 2", 1, Circle), // Tactus pointer: the 15 x 15 pause disc, a circle by construction
        ("Marrow.UI/Port/PcControl/WithdrawnCardBody.cs", "14", 1, Circular), // Tactus pill v2: the kept draft's box, a card part drawn from the pc-control-ui canvas, no Swift twin
        ("Marrow.UI/Port/PcControl/PcAppIcons.cs", "size * 0.24", 1, Circular), // Tactus (OD-0145, ED-NEW-pcctl-35, ED-NEW-pcctl-46, PORT-ONLY, no Swift twin): an app's lettered tile (.ctile, tile(k,20)), moved from the approval card to serve the pill, the strip and Settings too, canvas pc-control-ui layout (CSS border-radius), not a Swift shape
        ("Marrow.Setup/Screens/ProgressPanel.cs", "2", 2, Tiny), // U3's stand-in bar: the 4 DIP track and its fill (superseded by bar B, kept with its tests)
        ("Marrow.Setup/Shell/SetupWindow.cs", "radius", 3, Circular), // the window's own 8 DIP edge (PLAN.md 3: "the 8 px radius"), which Windows 11 rounds as a circle: backdrop, rim and top light
        ("Marrow.Setup/Ui/DesktopPreview.cs", "DeskWidth * .06", 1, Circular), // the Ready and Options desktop preview's rounded screen, a canvas layout
        ("Marrow.Setup/Ui/Pieces.cs", "10", 2, Circular), // two of the canvas's pieces, CSS border-radius 10
        ("Marrow.Setup/Ui/Pieces.cs", "8", 1, Circular), // one of the canvas's pieces, CSS border-radius 8
        ("Marrow.Setup/Ui/Pieces.cs", "13", 1, Circular), // one of the canvas's pieces, CSS border-radius 13
        ("Marrow.Setup/Ui/SetupButton.cs", "radius", 6, Capsule), // the button is a capsule by construction (r = height / 2): its fill, rim, light and shape
        ("Marrow.Setup/Ui/SetupButton.cs", "radius.TopLeft + 4", 1, Capsule), // the focus ring outside that capsule (r + 4)
        ("Marrow.Setup/Ui/SetupButton.cs", "height / 2 + 4", 1, Capsule), // the focus ring outside a capsule button (r + 4)
        ("Marrow.Setup/Ui/SetupButton.cs", "height / 2, height / 2, height * .45 * .4, height * .45 * .4", 1, Capsule), // the button's gloss, the top half of that capsule
        ("Marrow.Setup/Ui/SetupButton.cs", "2", 1, Tiny), // a 2 DIP line
    ];

    private static string SourceRoot([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "..", "..", "..", "..", "src"));

    // Every assignment to a CornerRadius under src: `CornerRadius = new CornerRadius(<arg>)` (the radius is the argument) and any other value (a variable, a ternary: the radius is
    // the expression as written), with the statement it sits in (to the `;` that ends it, past the initializer's closing brace) and its 1-based line. A constant named
    // CornerRadius is a declaration, not an assignment to a Border, and is not read.
    private static List<(string File, string Radius, string Statement, int Line)> Found()
    {
        var root = SourceRoot();
        var found = new List<(string, string, string, int)>();
        var constructor = new Regex(@"\Gnew\s*(?:CornerRadius)?\s*\(");
        foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = System.IO.Path.GetRelativePath(root, path).Replace('\\', '/');
            if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal)) continue;
            var text = File.ReadAllText(path);
            foreach (Match m in Regex.Matches(text, @"\bCornerRadius\s*=(?![=>])\s*"))
            {
                var before = text[(text.LastIndexOf('\n', m.Index) + 1)..m.Index];
                if (before.Contains("const ", StringComparison.Ordinal) || before.Contains("double ", StringComparison.Ordinal)) continue;
                var line = 1 + text.AsSpan(0, m.Index).Count('\n');
                int i = m.Index + m.Length, j = i;
                string radius;
                var ctor = constructor.Match(text, i);
                if (ctor.Success)
                {
                    i += ctor.Length;
                    var depth = 1;
                    j = i;
                    while (depth > 0 && j < text.Length) { depth += text[j] == '(' ? 1 : text[j] == ')' ? -1 : 0; j++; }
                    radius = text[i..(j - 1)].Trim();
                }
                else
                {
                    // an expression: to the `,` or `;` that ends it, or the closer of the initializer it sits in
                    var depth = 0;
                    while (j < text.Length)
                    {
                        var c = text[j];
                        if (c is '(' or '[' or '{') depth++;
                        else if (c is ')' or ']' or '}') { if (depth == 0) break; depth--; }
                        else if (c is ',' or ';' && depth == 0) break;
                        j++;
                    }
                    radius = text[i..j].Trim();
                }
                int braces = 0, k = j;
                while (k < text.Length)
                {
                    if (text[k] == '{') braces++;
                    else if (text[k] == '}') braces--;
                    else if (text[k] == ';' && braces <= 0) break;
                    k++;
                }
                found.Add((relative, radius, text[m.Index..Math.Min(k + 1, text.Length)], line));
            }
        }
        return found;
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // OD-0102 / ED-275 (7): the table is the proof that no raw CornerRadius Border is left unaccounted for; the pixels of the routed ones are below
    [Trait("Decision", "ED-NEW-web-w9-10")]
    public void Every_raw_CornerRadius_assignment_is_routed_through_the_continuous_shape_or_classed_with_why_it_never_reaches_the_band()
    {
        var found = Found();
        var problems = new List<string>();
        foreach (var group in found.GroupBy(f => (f.File, f.Radius)))
        {
            var entry = Sites.Where(s => s.File == group.Key.File && s.Radius == group.Key.Radius).ToArray();
            if (entry.Length != 1) { problems.Add($"{group.Key.File}: CornerRadius({group.Key.Radius}) x{group.Count()} is not in the table"); continue; }
            if (entry[0].Count != group.Count()) problems.Add($"{group.Key.File}: CornerRadius({group.Key.Radius}) is {group.Count()} assignments, the table says {entry[0].Count}");
            if (entry[0].Kind == Routed && group.Any(g => !g.Statement.Contains("RoundedRectangle(", StringComparison.Ordinal)))
                problems.Add($"{group.Key.File}: CornerRadius({group.Key.Radius}) is a Swift `.continuous` Border that can sit in the band and does not call RoundedRectangle(...) / UnevenRoundedRectangle(...)");
        }
        foreach (var entry in Sites)
            if (!found.Any(f => f.File == entry.File && f.Radius == entry.Radius)) problems.Add($"{entry.File}: CornerRadius({entry.Radius}) is in the table but no longer in the source");
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    // ---- the routed sites on the screen ----------------------------------------------------------------------------------------------------------------------

    private static HydraHeadInfo Info() =>
        new HydraHeadInfo(0, "Task", HydraHeadInfo.Kind.Native, HydraHeadInfo.Origin.Delegated) { HydraHeadInfoStatus = HydraHeadInfo.Status.Completed, ToolCalls = 3 };

    [Fact]
    [Trait("Req", "PR-HYDRA-178")] // HydraPanel.swift:466-471 the footer bar is a 16-point continuous rounded rectangle; one line tall it is about 37 pt, in the band (32.1 < h < 48.9)
    public void The_head_footer_bar_one_line_tall_draws_Apples_corner_at_the_largest_radius_that_fits() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var footer = new HydraHeadFooter(Info()) { VerticalAlignment = VerticalAlignment.Bottom };
        var window = new Avalonia.Controls.Window { Content = new Grid { Children = { footer } }, Width = 420, Height = 200 };
        try
        {
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var bar = Assert.IsType<Border>(footer.Child);
            Assert.InRange(bar.Bounds.Height, 16 * 2 + 0.01, 16 * 2 * ContinuousCorner.Extent - 0.01);
            Assert.Equal(new CornerRadius(16), SwiftShapes.StatedCornerRadius(bar));
            Assert.True(ContinuousCornerChecks.Drawn(bar, expectApple: true));
            // The Border fills no circle of its own: the clip (Apple's path) makes the corner.
            Assert.Equal(default, bar.CornerRadius);
            Assert.NotNull(bar.Clip);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-178")] // HydraProgressBar.swift:571-578 ProgressCardSurface: the card is a 16-point continuous rounded rectangle (Chrome.cardCornerRadius)
    public void The_progress_cards_surface_is_a_continuous_shape_that_draws_Apples_corner_in_the_band() => headless.OnUiThread(() =>
    {
        var surface = Assert.IsType<Border>(new ProgressCardSurface(IsOnGlassPanel: true, IsDark: true).Body(new Border { Height = 37, Width = 260 }));
        surface.HorizontalAlignment = HorizontalAlignment.Left;
        surface.VerticalAlignment = VerticalAlignment.Top;
        var window = new Avalonia.Controls.Window { Content = new Grid { Children = { surface } }, Width = 400, Height = 120 };
        try
        {
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(surface));
            Assert.Equal(new CornerRadius(ChromeTokens.CardCornerRadius), SwiftShapes.StatedCornerRadius(surface));
            Assert.True(ContinuousCornerChecks.Drawn(surface, expectApple: true));
        }
        finally { window.Close(); }
    });
    // ---- REVIEW (blitz-cornerband CB-0 re-check, 2026-10-04) ------------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Rule", "R-TEST-4")] // REVIEW: the census must read every CornerRadius assignment (a variable, a ternary, a property set), not only `new CornerRadius(...)`; each is then in the table
    public void REVIEW_every_CornerRadius_assignment_in_src_is_in_the_census_not_only_the_new_CornerRadius_ones()
    {
        var root = SourceRoot();
        var census = Found().Select(f => (f.File, f.Line)).ToHashSet();
        var missed = new List<string>();
        foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = System.IO.Path.GetRelativePath(root, path).Replace('\\', '/');
            if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal)) continue;
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                // an assignment (object initializer or property set) of a Border's CornerRadius, whatever its value
                var m = Regex.Match(lines[i], @"\bCornerRadius\s*=(?![=>])(.+)");
                if (!m.Success) continue;
                var line = lines[i].TrimStart();
                if (line.StartsWith("private const", StringComparison.Ordinal) || line.StartsWith("public const", StringComparison.Ordinal)
                    || line.StartsWith("internal const", StringComparison.Ordinal) || line.Contains("double CornerRadius", StringComparison.Ordinal)) continue;
                if (!census.Contains((relative, i + 1))) missed.Add($"{relative}:{i + 1}: {line}");
            }
        }
        Assert.True(missed.Count == 0, "CornerRadius assignments outside the census (each is a raw Border the table neither routes nor classes):" + Environment.NewLine + string.Join(Environment.NewLine, missed));
    }
}
