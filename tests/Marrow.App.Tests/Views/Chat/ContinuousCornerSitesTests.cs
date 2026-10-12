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
        ("Marrow.App/Port/Settings/PreviewMenuItem.cs", "7", 1, Circular), // CZ-3 (ED-335): a model menu row, design-page layout (CSS .pitem border-radius 7)
        ("Marrow.App/Port/Settings/PreviewStepper.cs", "11", 1, Circle), // CZ-3 (ED-335): the 22 x 22 stepper, a circle by construction
        ("Marrow.App/Port/Settings/PreviewSwitchChip.cs", "14", 1, Capsule), // CZ-3 (ED-335): the 28 pt chip of the preview model switch, r = h / 2
        ("Marrow.App/Port/Settings/UpdatesMainButton.cs", "ButtonHeight / 2", 1, Capsule), // UE-4 (ED-343): the Updates page's 30 pt glass capsule; CzButton draws its focus ring from this radius, r = h / 2
        ("Marrow.App/Port/Voice/VoiceLanguageQuestion.cs", "CardRadius", 2, Circular), // VT-12 (V-12.11, ED-292): the design-page question card's 14 px border radius, not a Swift shape (glass and rim)
        ("Marrow.App/Port/SettingsShell.cs", "8", 1, Circular), // RR-37 (ED-NEW-design-gaps-lead-1): the Settings window's shadow carrier, design-page layout (settings-open-rise-2-locked .glass, CSS border-radius 8 with its 0 34px 90px / 0 8px 24px shadows)
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
        ("Marrow.App/Views/Chat/SubagentPanel.cs", "Chrome.CapsuleHeight / 2", 1, Capsule),
        ("Marrow.App/Views/Chat/SubagentPanel.cs", "CornerRadius", 2, Fit),
        ("Marrow.App/Views/Chat/ThreadTimeline.cs", "999", 1, Capsule),
        ("Marrow.App/Views/Chat/ThreadTimeline.cs", "TimelineMetrics.PillRadius", 2, Routed),
        ("Marrow.App/Views/Chat/TimelineMinimap.cs", "1", 1, Tiny),
        ("Marrow.App/Views/Chat/TimelineRows.cs", "14", 1, Circle),
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
        ("Marrow.App/Views/Sidebar/FloatingSidebarPanel.cs", "22", 2, Fit),
        ("Marrow.App/Views/Terminal/TerminalPanel.cs", "16, 16, 0, 0", 1, Fit),
        ("Marrow.App/Views/Terminal/TerminalPanel.cs", "999", 1, Capsule),
        ("Marrow.App/Views/Usage/UsageFloatingPanel.cs", "cornerRadius", 2, Fit), // :84, :92 the panel and its scrim, r 22 (HydraPanel.CornerRadius), tall panels
        ("Marrow.App/Views/Usage/UsageViews.cs", "2.5", 2, Tiny),
        ("Marrow.App/Views/Panes/PaneCard.cs", "PaneTokens.CornerRadius", 1, Circular), // web W3a (ED-NEW-web-w3a-1): the pane card, design-page layout (canvas .gl, CSS border-radius 12), not a Swift shape
        ("Marrow.App/Views/Panes/PaneCard.cs", "PaneTokens.CornerRadius - 1", 1, Circular), // web W3a (ED-NEW-web-w3a-1): the card's content clip inside its 1 DIP rim, the same design-page corner
        ("Marrow.UI/Panes/PaneHeader.cs", "12", 1, Capsule), // web W3a (ED-NEW-web-w3a-1): a 24 DIP tab pill, r = h / 2 (canvas .tabg button)
        ("Marrow.UI/Panes/PaneHeader.cs", "8", 2, Circular), // web W3a (ED-NEW-web-w3a-1): a 26 DIP header button and text chip, design-page layout (canvas .hb, CSS border-radius 8)
        ("Marrow.UI/Panes/PaneHeader.cs", "11", 1, Capsule), // web W3a (ED-NEW-web-w3a-1): the 22 DIP "Chat is working" chip, r = h / 2 (canvas .pchip)
        ("Marrow.UI/Panes/PaneHeader.cs", "3", 1, Circle), // web W3a (ED-NEW-web-w3a-1): the chip's 6 x 6 dot, a circle by construction
        ("Marrow.UI/Panes/NarrowTabStrip.cs", "PillHeight / 2", 1, Capsule), // web W3c (ED-NEW-web-w3c-2): the 34 DIP narrow tab strip's pill, r = h / 2 (canvas .ntabs .in, border-radius 999)
        ("Marrow.UI/Panes/NarrowTabStrip.cs", "TabHeight / 2", 1, Capsule), // web W3c (ED-NEW-web-w3c-2): a 28 DIP tab of the narrow strip, r = h / 2 (canvas .ntabs button)
        ("Marrow.UI/Chrome/Chrome.cs", "side / 2", 1, Circle),
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
