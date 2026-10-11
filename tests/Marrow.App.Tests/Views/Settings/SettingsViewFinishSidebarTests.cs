using Marrow.App.Port.Settings;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// SettingsView.swift:136-194 (the sidebar), :100-107 and :130-133 (the page filter); UI/Chrome/Chrome.swift:681-823.
// Every literal is transcribed from those lines; the rig is the real window with real pointer and keyboard input.
[Collection("Headless")]
public sealed class SettingsViewFinishSidebarTests(HeadlessSession headless) : SettingsFinishFixture(headless)
{
    [Fact]
    [Trait("Req", "UI-SETTINGS-003")] // App/Views/Settings/SettingsView.swift:100-107,130-133,141-145; UI/Chrome/Chrome.swift:775-823
    public void Sidebar_search_is_placed_and_drawn_per_the_source_filters_titles_and_keywords_and_answers_return_escape_and_arrows()
    {
        With(() => new SettingsView(), (view, _, h, w) =>
        {
            var search = Assert.Single(Nodes<SidebarSearchField>(view)); var sidebar = Field<Control>(view, "sidebar"); var field = Assert.Single(Nodes<TextBox>(search));
            // :138-145: a 14pt clear strip placed 16pt down, then the field with 10pt either side and 14pt above it, filling the 212pt sidebar.
            Assert.Equal(212, sidebar.Bounds.Width); Assert.Equal(new Point(10, 16 + 14 + 14), TopLeft(search, sidebar)); Assert.Equal(212 - 2 * 10, search.Bounds.Width, 3);
            // Chrome.swift:806-813: height 30, 9pt inside the capsule, continuous capsule filled with Chrome.overlay(0.07).
            var capsule = Assert.IsType<Border>(search.Child); Assert.Equal(30, capsule.Bounds.Height, 3); Assert.Equal(new Thickness(9, 0), capsule.Padding);
            Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(capsule)); Assert.Equal(new CornerRadius(15), capsule.CornerRadius); Ink(capsule, capsule.Background, Chrome.Overlay(0.07));
            // :786-791: the magnifier .callout medium at 0.85 of secondary, the prompt "Search", the field .body, 6pt between them (:785).
            var glass = Assert.Single(Nodes<SwiftImage>(search)); Assert.Equal("magnifyingglass", glass.SymbolName); Assert.Equal(SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Medium), SwiftImage.GetFont(glass)); Ink(glass, glass.Foreground, Chrome.SecondaryText.Opacity(0.85));
            Assert.Equal("Search", field.PlaceholderText); Assert.Equal(13, field.FontSize); Assert.Equal(6, TopLeft(field, search).X - (TopLeft(glass, search).X + glass.Bounds.Width), 3);
            Assert.Equal(9, TopLeft(glass, search).X, 3); Assert.Equal(15, TopLeft(glass, search).Y + glass.Bounds.Height / 2, 1);
            // :148 the focus ring is left on (only ChromeSearchField disables it, :841); :796 empty: no clear button laid out at all.
            Assert.False(field.IsSet(Control.FocusAdornerProperty)); Assert.Empty(Nodes<Button>(search)); Assert.Equal(Marrow.App.Port.Settings.SettingsSidebar.Pages("").Count, Titles(view).Length);

            // :100-107 a query trimmed, case-insensitive, over the title and every keyword; typing really filters (and the first match takes over, :130-133).
            Type(w, field, "  THEME "); Assert.Equal(new[] { "Appearance" }, Titles(view)); Assert.Single(Nodes<AppearanceSettingsPage>(view)); Assert.Empty(Nodes<GeneralSettingsPage>(view));
            Assert.True(((ISelectionItemProvider)ControlAutomationPeer.CreatePeerForElement(RowButton(view, "Appearance"))!).IsSelected);
            // :796-805 non-empty: "xmark.circle.fill" .callout at 0.75 of secondary labelled "Clear search", 6pt after the field, at the capsule's trailing 9pt.
            var clear = Assert.Single(Nodes<Button>(search)); Assert.Equal("Clear search", AutomationProperties.GetName(clear)); var x = Assert.Single(Nodes<SwiftImage>(clear)); Assert.Equal("xmark.circle.fill", x.SymbolName);
            Assert.Equal(SwiftFont.System(SwiftFont.TextStyle.Callout), SwiftImage.GetFont(x)); Ink(x, x.Foreground, Chrome.SecondaryText.Opacity(0.75));
            Assert.Equal(6, TopLeft(clear, search).X - (TopLeft(field, search).X + field.Bounds.Width), 3); Assert.Equal(search.Bounds.Width - 9, TopLeft(clear, search).X + clear.Bounds.Width, 3);
            Tick(h, 500); Click(w, clear, new Point(4, 8)); /* a point on the drawn ring: the glyph is hit-tested by its pixels, the knocked-out cross is not */ Assert.Equal("", field.Text ?? ""); Assert.Equal(Marrow.App.Port.Settings.SettingsSidebar.Pages("").Count, Titles(view).Length); Assert.Empty(Nodes<Button>(search)); Assert.Single(Nodes<AppearanceSettingsPage>(view));
            Type(w, field, "git"); Assert.Equal(new[] { "Appearance", "Providers", "MCP", "Source control" }, Titles(view)); // "github" is a keyword of three pages, "git" of Source control itself.
            field.Text = ""; Drain(w); Type(w, field, "gen"); Assert.Equal(new[] { "General", "Hydra", "Source control" }, Titles(view)); // the title "General" and the keywords "agents" and "text generation".
            field.Text = ""; Drain(w); Type(w, field, "zen"); Assert.Equal(new[] { "Providers" }, Titles(view)); Assert.Single(Nodes<ProvidersSettingsPage>(view));

            // :142 Return selects the first matching page, not the selected one.
            field.Text = ""; Drain(w); Type(w, field, "git"); Click(w, RowButton(view, "MCP")); Assert.Single(Nodes<MCPSettingsPage>(view)); Assert.Empty(Nodes<AppearanceSettingsPage>(view));
            Click(w, field); Press(w, Key.Enter); Assert.Single(Nodes<AppearanceSettingsPage>(view)); Assert.Empty(Nodes<MCPSettingsPage>(view)); Assert.Equal("git", field.Text);
            // :793-795 Up and Down are ignored by the field's handler (no onMove here): nothing is consumed ahead of the text field and nothing moves.
            Click(w, RowButton(view, "Source control")); Click(w, field); var arrived = new List<(Key, bool)>();
            field.AddHandler(InputElement.KeyDownEvent, (_, e) => arrived.Add((e.Key, e.Handled)), RoutingStrategies.Tunnel, handledEventsToo: true);
            Press(w, Key.Up); Press(w, Key.Down); Assert.Equal(new[] { (Key.Up, false), (Key.Down, false) }, arrived); Assert.Single(Nodes<SourceControlSettingsPage>(view)); Assert.Equal("git", field.Text);
            // :814 Escape empties the field.
            Press(w, Key.Escape); Assert.Equal("", field.Text ?? ""); Assert.Equal(Marrow.App.Port.Settings.SettingsSidebar.Pages("").Count, Titles(view).Length); Assert.Single(Nodes<SourceControlSettingsPage>(view));
            // No match: "No results" and Return changes nothing (:142 `if let first`), the page left as it was.
            Type(w, field, "zzz"); Assert.Empty(Titles(view)); Press(w, Key.Enter); Assert.Single(Nodes<SourceControlSettingsPage>(view)); Press(w, Key.Escape); Assert.Equal(Marrow.App.Port.Settings.SettingsSidebar.Pages("").Count, Titles(view).Length);
        });
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-004")] // App/Views/Settings/SettingsView.swift:4-15,148-164,179-182; UI/Chrome/Chrome.swift:681-757
    public void Page_rows_are_ten_continuous_28pt_rows_with_selected_and_hover_fills_a_selected_trait_a_freeze_under_scroll_and_click_to_select()
    {
        With(() => new SettingsView(), (view, _, h, w) =>
        {
            var titles = Marrow.App.Port.Settings.SettingsSidebar.Pages("").Select(page => page.Title).ToArray(); // :4-15 declaration order, :19-32 titles
            Assert.Equal(titles, Titles(view)); var list = Field<StackPanel>(view, "rows"); Assert.Equal(1, list.Spacing); // :147 rows separated by 1pt
            void Check(string selected, double settle = 150)
            {
                Tick(h, settle); // the wash crossfades over Chrome.hover (UI-SETTINGS-004); the checks below are of the settled look
                var rows = Nodes<SidebarRow>(view);
                for (var i = 0; i < rows.Length; i++)
                {
                    var button = Assert.Single(Nodes<Button>(rows[i])); var shape = Assert.IsType<Border>(button.Content); var label = Assert.Single(Nodes<TextBlock>(rows[i])); var on = titles[i] == selected;
                    // Chrome.swift:719-727: height 28, 7pt continuous corners, 8pt leading and (8 + accessoryWidth 0) trailing, the 20pt icon column, 8pt spacing, the full 192pt of the list.
                    Assert.Equal(28, shape.Bounds.Height, 3); Assert.Equal(192, shape.Bounds.Width, 3); Assert.Equal(new CornerRadius(7), shape.CornerRadius); Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(shape)); Assert.Equal(new Thickness(8, 0), shape.Padding); ContinuousCornerChecks.Drawn(shape, expectApple: true); // ED-181 closed: 28 pt holds the 21 pt two run-ins of a 7 pt corner
                    var badge = Assert.Single(Nodes<SidebarIconBadge>(rows[i])); Assert.Equal(20, badge.Bounds.Width, 3); Assert.Equal(8, TopLeft(badge, shape).X, 3); Assert.Equal(8 + 20 + 8, TopLeft(label, shape).X, 3);
                    if (i > 0) Assert.Equal(28 + 1, TopLeft(shape, list).Y - TopLeft(Nodes<Button>(rows[i - 1]).Single(), list).Y, 3);
                    // :713-716 .body, medium when selected else regular, primary at 1 when selected else 0.92, one line truncated at the tail; :754 the isSelected trait.
                    Font(label, 13, on ? FontWeight.Medium : FontWeight.Normal); Ink(label, label.Foreground, Chrome.PrimaryText.Opacity(on ? 1 : 0.92)); Assert.Equal(1, label.MaxLines); Assert.Equal(TextTrimming.CharacterEllipsis, label.TextTrimming);
                    Assert.Equal(on, ((ISelectionItemProvider)ControlAutomationPeer.CreatePeerForElement(button)!).IsSelected); Assert.Equal(titles[i], AutomationProperties.GetName(button));
                    var glyph = ((Panel)badge.Child!).Children[0]; Ink(glyph, Avalonia.Controls.Documents.TextElement.GetForeground(glyph), Chrome.PrimaryText.Opacity(0.85)); // :150 SidebarIconBadge's glyph at 0.85
                    Ink(shape, shape.Background, on ? Chrome.Overlay(0.12) : SwiftColor.Clear); // :739-743 the selected fill; hovering is exercised below
                }
            }
            Check("General");
            var models = RowButton(view, "Models"); var fill = Assert.IsType<Border>(models.Content);
            // :744-745 hovering: 0.06, animated with Chrome.hover (easeOut 0.1); leaving fades it away again.
            Hover(w, models); Tick(h, 50); Ink(fill, fill.Background, Chrome.Overlay(0.06 * Animation.EaseOut(0.1).Progress(0.05))); Tick(h, 51); Ink(fill, fill.Background, Chrome.Overlay(0.06));
            w.MouseMove(new Point(1, 1)); Drain(w); Tick(h, 101); Ink(fill, fill.Background, SwiftColor.Clear);
            // :149 a click selects the page; the selected row keeps 0.12 even while the pointer is on it (:740 selected before hovering).
            // Chrome.swift:729 the wash moves to the new row on Chrome.hover (easeOut 0.1 s), so the rows are read once its 0.1 s are over.
            Click(w, models); Assert.Single(Nodes<ModelsSettingsPage>(view)); Tick(h, 110); Check("Models"); fill = Assert.IsType<Border>(RowButton(view, "Models").Content); Hover(w, RowButton(view, "Models")); Tick(h, 150); Ink(fill, fill.Background, Chrome.Overlay(0.12));
            foreach (var title in titles.Skip(1)) { Reveal(w, h, RowButton(view, title)); Click(w, RowButton(view, title)); Tick(h, 110); Check(title); if (title == "Archive") Assert.Empty(Nodes<ChromeCompactTitle>(view)); /* :198 the empty archive is one symbol */ else Assert.Equal(title, Text(Assert.Single(Nodes<ChromeCompactTitle>(view)))[0]); }
            // :182 frozen while the pane scrolls: a wheel turn over the page holds every row away from the pointer (no hover fill, no click) until 150ms after the last movement.
            Reveal(w, h, RowButton(view, "General")); Click(w, RowButton(view, "General")); Tick(h, 101); /* About's wash finishes fading out */ Reveal(w, h, RowButton(view, "About")); /* OD-0134: About is the sidebar's last row, scrolled to */ var scroll = Assert.Single(Nodes<ScrollViewer>(Field<Control>(view, "detail"))); var at = scroll.TranslatePoint(new Point(scroll.Bounds.Width / 2, scroll.Bounds.Height / 2), w)!.Value;
            Assert.True(list.IsHitTestVisible); w.MouseMove(at); w.MouseWheel(at, new Vector(0, -3)); Drain(w); Assert.Equal(0, scroll.Offset.Y); /* ED-NEW-softscroll-1: the page glides (OD-0071), so the freeze starts with its first frame of movement */ Tick(h, 1000 / 60.0); Drain(w); Assert.InRange(scroll.Offset.Y, 0.01, 149.99); Assert.False(list.IsHitTestVisible);
            var about = RowButton(view, "About"); var aboutFill = Assert.IsType<Border>(about.Content); var point = about.TranslatePoint(new Point(10, 10), w)!.Value;
            w.MouseMove(point); Drain(w); Tick(h, 30); Assert.False(about.IsPointerOver); Ink(aboutFill, aboutFill.Background, SwiftColor.Clear);
            w.MouseDown(point, MouseButton.Left); w.MouseUp(point, MouseButton.Left); Drain(w); Assert.Single(Nodes<GeneralSettingsPage>(view)); Check("General", 101); /* still inside the 150 ms freeze: a longer wait would thaw the rows and let the pointer light About */
            Tick(h, 400); Assert.Equal(Math.Min(150, scroll.Extent.Height - scroll.Viewport.Height), scroll.Offset.Y, 3); /* the glide has come to rest */ Tick(h, 200); Assert.True(list.IsHitTestVisible); Click(w, RowButton(view, "About")); Assert.Single(Nodes<AboutSettingsPage>(view)); Check("About");
        });
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-004")] // App/Views/Settings/SettingsView.swift:148-164; UI/Chrome/Chrome.swift:723-724,739-745; the reference runtime (settings.sidebar-navigation: S 47.765-47.850, 124.5, 162.2, 180.5, 199.1; press.row-tile-dim: S 47.578)
    public void Selecting_a_page_keeps_the_rows_crossfades_the_old_and_new_wash_over_chrome_hover_and_dims_the_pressed_row()
    {
        With(() => new SettingsView(), (view, _, h, w) =>
        {
            var rows = Nodes<SidebarRow>(view); Assert.Equal(Marrow.App.Port.Settings.SettingsSidebar.Pages("").Count, rows.Length); var list = Field<StackPanel>(view, "rows"); // CZ-4: Customization is listed
            var models = RowButton(view, "Models"); var general = RowButton(view, "General"); var modelsFill = Assert.IsType<Border>(models.Content); var generalFill = Assert.IsType<Border>(general.Content);
            var selectedAlpha = Chrome.Overlay(0.12).Resolve(modelsFill.ActualThemeVariant).A; Assert.True(selectedAlpha > 0);
            byte Alpha(Border fill) => Assert.IsAssignableFrom<ISolidColorBrush>(fill.Background).Color.A;
            // :149 a click selects the page: the rows stay (the state is theirs, not a new set of rows), and the label's weight and ink change at once ...
            Click(w, models); Assert.Single(Nodes<ModelsSettingsPage>(view));
            Assert.Equal(rows, Nodes<SidebarRow>(view)); Assert.Same(models, RowButton(view, "Models")); Assert.Same(modelsFill, models.Content);
            Font(Assert.Single(Nodes<TextBlock>(models)), 13, FontWeight.Medium); Font(Assert.Single(Nodes<TextBlock>(general)), 13, FontWeight.Normal); Assert.True(((ISelectionItemProvider)ControlAutomationPeer.CreatePeerForElement(models)!).IsSelected); Assert.False(((ISelectionItemProvider)ControlAutomationPeer.CreatePeerForElement(general)!).IsSelected);
            // ... and the wash crossfades in place over Chrome.hover (0.1 s ease-out): the new row part-way up, the old row part-way down, then exact.
            Tick(h, 40); Assert.InRange(Alpha(modelsFill), 1, selectedAlpha - 1); Assert.InRange(Alpha(generalFill), 1, selectedAlpha - 1);
            Tick(h, 101); Ink(modelsFill, modelsFill.Background, Chrome.Overlay(0.12)); Ink(generalFill, generalFill.Background, SwiftColor.Clear);
            // Chrome.swift:723-724 the same for a search that takes the selection to the first match: Appearance is selected while the list narrows to it.
            var search = Assert.Single(Nodes<TextBox>(Assert.Single(Nodes<SidebarSearchField>(view)))); Type(w, search, "theme"); Assert.Equal(new[] { "Appearance" }, Titles(view)); Tick(h, 150); Ink(Assert.IsType<Border>(RowButton(view, "Appearance").Content), Assert.IsType<Border>(RowButton(view, "Appearance").Content).Background, Chrome.Overlay(0.12));
            search.Text = ""; Drain(w); Tick(h, 150); Assert.Equal(Marrow.App.Port.Settings.SettingsSidebar.Pages("").Count, Titles(view).Length);

            // press.row-tile-dim (S 47.578): while the pointer holds a row its content is at 0.8 and its wash at x0.7 of what it was; letting go restores both (0.05 s in, 0.1 s out).
            var hydra = RowButton(view, "Hydra"); var hydraFill = Assert.IsType<Border>(hydra.Content); var content = Assert.IsAssignableFrom<Control>(Assert.Single(Nodes<TextBlock>(hydra)).Parent); var at = hydra.TranslatePoint(new Point(10, 10), w)!.Value;
            w.MouseMove(at); Drain(w); Tick(h, 150); Ink(hydraFill, hydraFill.Background, Chrome.Overlay(0.06)); Assert.Equal(1, content.Opacity);
            w.MouseDown(at, MouseButton.Left); Drain(w); Tick(h, 51); Assert.Equal(0.8, content.Opacity, 3); Ink(hydraFill, hydraFill.Background, Chrome.Overlay(0.06).Opacity(0.7)); Assert.Single(Nodes<AppearanceSettingsPage>(view)); // pressing selects nothing yet (the search above left Appearance selected)
            w.MouseUp(at, MouseButton.Left); Drain(w); Tick(h, 101); Assert.Equal(1, content.Opacity, 3); Ink(hydraFill, hydraFill.Background, Chrome.Overlay(0.12)); Assert.Single(Nodes<HydraSettingsPage>(view));
        });
    }
}
