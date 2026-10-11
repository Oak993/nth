using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Tests.Views.Settings;

// SettingsView.swift:417-457 (AppearanceSettingsPage), :519-564 (BackdropOpacitySlider), :1157-1209 (ThemePickerButton);
// UI/Theme/AppTheme.swift:14-176; UI/Chrome/Popovers.swift:215-316 (PopoverItem); UI/Chrome/Chrome.swift:189-260 (WindowBackdrop.scrim).
[Collection("Headless")]
public sealed class SettingsViewFinishAppearanceTests(HeadlessSession headless) : SettingsFinishFixture(headless)
{
    private static AppTheme Theme(string raw) => SwiftEnum<AppTheme>.AllCases.Single(t => SwiftEnum<AppTheme>.RawValue(t) == raw);

    [Fact]
    [Trait("Req", "UI-SETTINGS-017")] // App/Views/Settings/SettingsView.swift:424-426,1157-1209; UI/Theme/AppTheme.swift:14-176; UI/Chrome/Popovers.swift:215-316
    public void Theme_picker_lists_system_then_the_dark_and_light_themes_with_swatches_and_a_check_and_selects_immediately_by_click()
    {
        var table = SettingsThemeTable.All;
        // DEVIATION OD-0088 (ED-250): the capsule is no longer on the page (the Look card's Theme row replaces it); it is
        // hosted beside the page, and the page's Theme detail is the Look card's.
        With(ThemePickerHost.Page, (view, model, h, w) =>
        {
            try
            {
                // AppTheme.swift:14-41 the 26 cases in declaration order; the port's enum carries the same raw values in the same order.
                Assert.Equal(table.Select(t => t.Raw), SwiftEnum<AppTheme>.AllCases.Select(SwiftEnum<AppTheme>.RawValue));
                var look = Assert.Single(Nodes<Marrow.App.Port.LookCard>(view)); var picker = Assert.Single(Nodes<ThemePickerButton>(view)); var button = Assert.Single(Nodes<Button>(picker)); Assert.Equal(ThemePickerHost.Detail(AppTheme.System), look.ThemeDetail.Text); Assert.Equal(AppTheme.System, model.Settings.Theme);
                // :1168-1189 the capsule: 12pt either side, 6pt between swatch (14 x 14), name (.system(12.5) medium, one line) and chevron (8 bold secondary), 28pt high, fitted to its content.
                var stack = Assert.Single(Nodes<StackPanel>(picker), s => s.Height == 28); var swatch = Assert.Single(Nodes<ThemeSwatch>(picker)); var title = Assert.Single(Nodes<TextBlock>(picker)); var chevron = Assert.Single(Nodes<SwiftImage>(picker));
                Assert.Equal(28, picker.Bounds.Height, 3); Assert.Equal(new Size(14, 14), swatch.Bounds.Size); Assert.Equal(12, TopLeft(swatch, picker).X, 3); Assert.Equal(6, TopLeft(title, picker).X - (TopLeft(swatch, picker).X + 14), 3); Assert.Equal(6, TopLeft(chevron, picker).X - (TopLeft(title, picker).X + title.Bounds.Width), 3); Assert.Equal(picker.Bounds.Width - 12, TopLeft(chevron, picker).X + chevron.Bounds.Width, 3);
                Assert.Equal("System", title.Text); Font(title, 12.5, FontWeight.Medium); Assert.Equal(1, title.MaxLines); Assert.Equal("chevron.down", chevron.SymbolName); Assert.Equal(SwiftFont.System(8, SwiftFont.Weight.Bold), SwiftImage.GetFont(chevron)); Ink(chevron, chevron.Foreground, Chrome.SecondaryText); Ink(title, title.Foreground, Chrome.PrimaryText.Opacity(0.92));
                var effect = Glass(picker); Assert.Equal(GlassShapeKind.Capsule, effect.Shape.Kind); Assert.Equal(RoundedCornerStyle.Continuous, effect.Shape.Style); Assert.Equal(Chrome.GlassTint, effect.TintColor); Assert.Equal(0.3, effect.TintOpacity); Assert.True(effect.IsInteractive);
                // :149-162 System's swatch is a hard half-and-half of #FFFFFF and #1E1E20 split at 0.5 from leading to trailing, rimmed 0.5pt in Color.primary at 0.22 (:152).
                var split = Assert.IsType<LinearGradientBrush>(swatch.Fill); Assert.Equal(new[] { 0.0, 0.5, 0.5, 1.0 }, split.GradientStops.Select(s => s.Offset)); Assert.Equal(new[] { "FFFFFF", "FFFFFF", "1E1E20", "1E1E20" }.Select(hex => SwiftColor.Hex(hex).Resolve(swatch.ActualThemeVariant)), split.GradientStops.Select(s => s.Color));
                Assert.Equal(new RelativePoint(0, 0.5, RelativeUnit.Relative), split.StartPoint); Assert.Equal(new RelativePoint(1, 0.5, RelativeUnit.Relative), split.EndPoint); Assert.Equal(0.5, swatch.StrokeThickness); Ink(swatch, swatch.Stroke, SwiftColor.Primary.Opacity(0.22));
                // Hovering raises the label to full opacity; so does being open (opened here from the keyboard with the pointer away).
                Hover(w, button); Tick(h, 150); Ink(title, title.Foreground, Chrome.PrimaryText); w.MouseMove(new Point(1, 1)); Drain(w); Tick(h, 150); Ink(title, title.Foreground, Chrome.PrimaryText.Opacity(0.92));
                Assert.True(button.Focus()); Press(w, Key.Space); var popup = Assert.Single(Nodes<Popup>(picker)); Assert.True(popup.IsOpen); Assert.Equal(PlacementMode.Bottom, popup.Placement); Tick(h, 150); Ink(title, title.Foreground, Chrome.PrimaryText);
                // :1194-1200 System on top and belonging to neither section, then "Dark" with the dark themes and "Light" with the light ones, each in declaration order.
                var menu = popup.Child!; var shown = Assert.IsType<StackPanel>(Nodes<PopoverItem>(menu)[0].Parent).Children; var dark = table.Where(t => t.Scheme == "dark").ToArray(); var light = table.Where(t => t.Scheme == "light").ToArray();
                Assert.Equal(19, dark.Length); Assert.Equal(6, light.Length); // the record's 17 dark and 8 light is stale: the Swift palette has 19 and 6
                var expected = new List<string> { "System", "#Dark" }; expected.AddRange(dark.Select(t => t.Name)); expected.Add("#Light"); expected.AddRange(light.Select(t => t.Name));
                Assert.Equal(expected, shown.Select(c => c is PopoverSectionHeader ? "#" + Text(c).Single() : Text(c).First()));
                // :1204-1207 each item carries the theme's name, its swatch (16 x 16 in the row, Popovers.swift:277-279) and a checkmark only on the current theme.
                foreach (var item in Nodes<PopoverItem>(menu)) { var theme = table.Single(t => t.Name == Text(item).First()); var mark = Assert.Single(Nodes<SwiftImage>(item), i => i.SymbolName == "checkmark"); Assert.Equal(theme.Raw == "system" ? 1 : 0, mark.Opacity); var small = Assert.Single(Nodes<ThemeSwatch>(item)); Assert.Equal(new Size(16, 16), small.Bounds.Size); }
                // Clicking an item selects that theme at once: the setting, the stored value, the row detail, the capsule's name and swatch, the window's accent and the open state all follow.
                foreach (var entry in table.Where(t => t.Raw != "system").Concat([table[0]]))
                {
                    if (!popup.IsOpen) { Assert.True(button.Focus()); Press(w, Key.Space); Assert.True(popup.IsOpen); }
                    var itemButton = Assert.Single(Nodes<Button>(Nodes<PopoverItem>(popup.Child!).Single(i => Text(i).First() == entry.Name))); itemButton.BringIntoView(); Drain(w); /* the menu scrolls: the far items are out of view until brought in */ Click(w, itemButton); Tick(h, 150);
                    Assert.False(popup.IsOpen); Assert.Equal(Theme(entry.Raw), model.Settings.Theme); Assert.Equal(Theme(entry.Raw), ThemeManager.Current); Assert.Equal(entry.Raw, h.Defaults.Object("appTheme"));
                    // OD-0008 B2: upstream AppTheme.swift:78 reads "Follows your Mac"; the port names its OS.
                    Assert.Equal(ThemePickerHost.Detail(Theme(entry.Raw)), look.ThemeDetail.Text); picker = Assert.Single(Nodes<ThemePickerButton>(view)); button = Assert.Single(Nodes<Button>(picker)); title = Assert.Single(Nodes<TextBlock>(picker)); swatch = Assert.Single(Nodes<ThemeSwatch>(picker)); popup = Assert.Single(Nodes<Popup>(picker));
                    Assert.Equal(entry.Name, title.Text); Assert.Equal(new Size(14, 14), swatch.Bounds.Size); var fill = Assert.IsType<LinearGradientBrush>(swatch.Fill); var variant = swatch.ActualThemeVariant;
                    if (entry.Raw == "system") Assert.Equal(new[] { 0.0, 0.5, 0.5, 1.0 }, fill.GradientStops.Select(s => s.Offset));
                    else
                    {
                        Assert.Equal(new[] { 0.0, 0.42, 1.0 }, fill.GradientStops.Select(s => s.Offset)); Assert.Equal(new RelativePoint(0, 0, RelativeUnit.Relative), fill.StartPoint); Assert.Equal(new RelativePoint(1, 1, RelativeUnit.Relative), fill.EndPoint);
                        Assert.Equal(SwiftColor.Hex(entry.Surface).Resolve(variant), fill.GradientStops[0].Color); Assert.Equal(SwiftColor.Hex(entry.Surface).Resolve(variant), fill.GradientStops[1].Color); if (entry.Accent is { } accent) { Assert.Equal(SwiftColor.Hex(accent).Resolve(variant), fill.GradientStops[2].Color); Assert.Equal(SwiftColor.Hex(accent).Resolve(variant), Chrome.Accent.Resolve(variant)); }
                    }
                    Assert.True(button.Focus()); Press(w, Key.Space); Assert.True(popup.IsOpen); Assert.Equal(table.Select(t => t.Raw == entry.Raw ? 1.0 : 0.0), table.Select(t => Nodes<SwiftImage>(Nodes<PopoverItem>(popup.Child!).Single(i => Text(i).First() == t.Name)).Single(i => i.SymbolName == "checkmark").Opacity));
                    ((IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(button)!).Invoke(); Drain(w); Assert.False(popup.IsOpen); // a click on the capsule toggles the popover (:1170)
                }
            }
            finally { model.Settings.Theme = AppTheme.System; }
        }, nativePopup: true, height: 1500);
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-017")] // App/Views/Settings/SettingsView.swift:1172 HStack(spacing: 6), centre alignment by default
    public void Theme_picker_capsule_puts_the_swatch_the_name_and_the_chevron_on_the_28_point_centre_line()
    {
        With(ThemePickerHost.Page, (view, model, h, w) => // OD-0088 (ED-250): the capsule beside the page
        {
            var picker = Assert.Single(Nodes<ThemePickerButton>(view));
            var stack = Assert.Single(Nodes<StackPanel>(picker), s => s.Height == 28);
            // An HStack centres each child vertically; the stretched name drew in the top 16 points of the 28-point capsule.
            foreach (Control child in new Control[] { Assert.Single(Nodes<ThemeSwatch>(picker)), Assert.Single(Nodes<TextBlock>(picker)), Assert.Single(Nodes<SwiftImage>(picker)) })
            {
                Assert.Equal(child.DesiredSize.Height, child.Bounds.Height, 0.5); // at its own height
                Assert.Equal(14, TopLeft(child, stack).Y + child.Bounds.Height / 2, 0.5);
            }
        });
    }

    private static double Scrim(double t) { const double stock = 0.18; var c = Math.Min(Math.Max(t, 0), 1); return c <= 0.5 ? stock * (c / 0.5) : stock + (1 - stock) * ((c - 0.5) / 0.5); } // Chrome.swift:WindowBackdrop.scrim(for:isDark: false)

    [Fact]
    [Trait("Req", "UI-SETTINGS-018")] // App/Views/Settings/SettingsView.swift:428-430,519-564; App/AppSettings.swift:299
    public void Transparency_slider_follows_a_real_drag_live_and_shows_its_reset_only_off_stock_without_taking_layout_room()
    {
        With(() => new SettingsView(), (view, model, h, w) =>
        {
            Click(w, RowButton(view, "Appearance")); var row = Row(view, "Transparency"); CheckRow(row, "Transparency", "How much shows through the window"); var settings = model.Settings; var backdrop = Assert.Single(Nodes<WindowBackdrop>(view));
            var group = Assert.Single(Nodes<BackdropOpacitySlider>(row)); Assert.Equal("Window transparency", AutomationProperties.GetName(group)); var slider = Assert.Single(Nodes<Slider>(group)); var reset = Assert.Single(Nodes<Button>(group));
            // :532-534 Slider(0...1) at 140pt; the default is the stock 0.5 (AppSettings.swift:299); :528-539 a glyph each side, 8pt away.
            Assert.Equal(140, slider.Width); Assert.Equal(140, slider.Bounds.Width, 3); Assert.Equal(0, slider.Minimum); Assert.Equal(1, slider.Maximum); Assert.Equal(0.5, settings.BackdropOpacity); Assert.Equal(0.5, slider.Value, 6);
            var clear = Assert.Single(Nodes<SwiftImage>(group), i => i.SymbolName == "circle.dotted"); var solid = Assert.Single(Nodes<SwiftImage>(group), i => i.SymbolName == "circle.fill"); var counter = Assert.Single(Nodes<SwiftImage>(group), i => i.SymbolName == "arrow.counterclockwise");
            foreach (var glyph in new[] { clear, solid }) { Assert.Equal(SwiftFont.System(SwiftFont.TextStyle.Subheadline).Resolve(), SwiftImage.GetFont(glyph)!.Resolve()); Ink(glyph, glyph.Foreground, Chrome.SecondaryText); }
            Assert.Equal("Clear", ToolTip.GetTip(Assert.IsType<Border>(clear.Parent))); Assert.Equal("Solid", ToolTip.GetTip(Assert.IsType<Border>(solid.Parent))); /* the tip sits on the glyph's transparent frame (UI-THEME-010) */ Assert.Equal(0, TopLeft(clear, group).X, 3); Assert.Equal(8, TopLeft(slider, group).X - clear.Bounds.Width, 3); Assert.Equal(8, TopLeft(solid, group).X - (TopLeft(slider, group).X + 140), 3); Assert.Equal(TopLeft(solid, group).X + solid.Bounds.Width, group.Bounds.Width, 3);
            // The slider takes the window's tint: its filled side is Chrome.accent.
            Assert.Equal(Chrome.Accent.Resolve(slider.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(slider.GetVisualDescendants().OfType<Border>().First(b => b.Height == 4).Background).Color);
            // :524,543-545 at stock there is no reset; it is 22 x 22, glyph .subheadline medium in secondary, helped "Back to the stock look" (:548-555).
            Assert.False(reset.IsEffectivelyVisible); Assert.Equal("Back to the stock look", ToolTip.GetTip(reset)); Assert.Equal(new Size(22, 22), new Size(reset.Width, reset.Height)); Assert.Equal(SwiftFont.System(SwiftFont.TextStyle.Subheadline, weight: SwiftFont.Weight.Medium).Resolve(), SwiftImage.GetFont(counter)!.Resolve()); Ink(counter, counter.Foreground, Chrome.SecondaryText);
            var width = group.Bounds.Width; var sliderAt = TopLeft(slider, row); var knob = Assert.Single(slider.GetVisualDescendants().OfType<Thumb>());
            // The threshold is |value - 0.5| >= 0.005 (:524), either side of stock.
            foreach (var (value, off) in new[] { (0.504, false), (0.506, true), (0.496, false), (0.494, true), (0.5, false) }) { settings.BackdropOpacity = value; Drain(w); Tick(h, Animation.Snappy(0.2).Duration * 1000 + 1); Assert.Equal(off, reset.IsEffectivelyVisible); Assert.Equal(width, group.Bounds.Width, 3); }
            // A real drag: the setting and the window backdrop follow the knob while it is held (:118), and the reset fades in with a 6pt rise over .snappy(0.2).
            var at = knob.TranslatePoint(new Point(knob.Bounds.Width / 2, knob.Bounds.Height / 2), w)!.Value; w.MouseMove(at); w.MouseDown(at, MouseButton.Left); w.MouseMove(at + new Vector(40, 0)); Drain(w);
            Assert.True(settings.BackdropOpacity > 0.6); SettingsBackdropAssertions.GlassOpacity(backdrop, Scrim(settings.BackdropOpacity)); var live = settings.BackdropOpacity; Assert.Equal(live, slider.Value, 6); Assert.True(reset.IsEffectivelyVisible);
            Tick(h, 50); Assert.InRange(reset.Opacity, 0.001, 0.999); Assert.Equal(-28 + 6 * (1 - reset.Opacity), TopLeft(reset, group).X, 3); /* .transition(.opacity.combined(with: .offset(x: 6))): one curve for both */ Tick(h, 160);
            w.MouseMove(at + new Vector(-30, 0)); Drain(w); Assert.True(settings.BackdropOpacity < live); var left = settings.BackdropOpacity; Assert.Equal(left, slider.Value, 6); SettingsBackdropAssertions.GlassOpacity(backdrop, Scrim(left));
            w.MouseMove(at + new Vector(-50, 0)); Drain(w); Assert.True(settings.BackdropOpacity < left); Assert.True(settings.BackdropOpacity < 0.5); SettingsBackdropAssertions.GlassOpacity(backdrop, Scrim(settings.BackdropOpacity));
            w.MouseUp(at + new Vector(-50, 0), MouseButton.Left); Drain(w); Tick(h, Animation.Snappy(0.2).Duration * 1000 + 1); var released = settings.BackdropOpacity; Assert.Equal(released, h.Defaults.Object("backdropOpacity")); Assert.Equal(width, group.Bounds.Width, 3); Assert.Equal(sliderAt, TopLeft(slider, row)); // the overlay takes no room: the slider stays where it was
            // Fully shown: 22 x 22 and 28pt to the left of the group's leading edge, centred on the slider, taking no room in the row.
            Assert.Equal(1, reset.Opacity, 6); Assert.Equal(-28, TopLeft(reset, group).X, 3); Assert.Equal(22, reset.Bounds.Width, 3); Assert.Equal(22, reset.Bounds.Height, 3); Assert.Equal(TopLeft(slider, row).Y + slider.Bounds.Height / 2, TopLeft(reset, row).Y + 11, 1);
            // Clicking the reset sends the value back to 0.5 and the reset fades out again (the animation is snappy 0.2).
            Click(w, reset); Assert.Equal(0.5, settings.BackdropOpacity); Assert.Equal(0.5, h.Defaults.Object("backdropOpacity")); Tick(h, Animation.Snappy(0.2).Duration * 1000 + 1); Assert.Equal(0.5, slider.Value, 6); Assert.False(reset.IsEffectivelyVisible); SettingsBackdropAssertions.GlassOpacity(backdrop, Scrim(0.5)); Assert.Equal(width, group.Bounds.Width, 3);
        }, height: 1100);
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-012")] // App/Views/Settings/SettingsView.swift:296-305,444-452; App/Views/Settings/HydraSettingsPage.swift:32; App/Views/Settings/SettingsView.swift:668-671
    public void Settings_switch_is_also_the_toggle_of_the_appearance_hydra_and_provider_pages_and_dims_the_sidebar_row_in_panel_only_mode()
    {
        With(() => new SettingsView(), (view, model, h, w) =>
        {
            var s = model.Settings;
            // Appearance :444-452: "Open the sidebar at launch" is dimmed and inert while only the panel is shown.
            Click(w, RowButton(view, "Appearance")); var open = Row(view, "Open the sidebar at launch"); var presenter = Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Avalonia.Controls.Presenters.ScrollContentPresenter>(open)!; presenter.Offset = new Vector(0, Math.Min(open.TranslatePoint(default, presenter.Child!)!.Value.Y - 40, presenter.Extent.Height - presenter.Viewport.Height)); Drain(w); Tick(h, 200); Drain(w); /* OD-0088: below the Look card the row starts out of view: scrolled to it (WL-2, ED-NEW-wltwo-1: the wallpaper library made the page longer than one scroll to the end), and past the scroll's 150 ms input freeze (SettingsScrollTracker) */ var toggle = Assert.Single(Nodes<SmallSwitch>(open)); Assert.Empty(Text(Assert.Single(Nodes<SettingsSwitch>(open)))); var start = model.Sidebar.StartsOpen;
            Assert.True(open.IsEnabled); Assert.Equal(1, open.Opacity, 6); Click(w, toggle); Assert.Equal(!start, model.Sidebar.StartsOpen); Assert.Equal(!start, h.Defaults.Object("sidebarStartsOpen")); Click(w, toggle); Assert.Equal(start, model.Sidebar.StartsOpen);
            s.SidebarMode = SidebarMode.PanelOnly; Drain(w); Assert.False(open.IsEnabled); Assert.Equal(0.5, open.Opacity, 6); Click(w, toggle); Assert.Equal(start, model.Sidebar.StartsOpen); Assert.True(toggle.Focus() || !toggle.IsEffectivelyEnabled); Press(w, Key.Space); Assert.Equal(start, model.Sidebar.StartsOpen);
            s.SidebarMode = SidebarMode.Floating; Drain(w); Assert.True(open.IsEnabled); Assert.Equal(1, open.Opacity, 6); Click(w, toggle); Assert.Equal(!start, model.Sidebar.StartsOpen);
            // Hydra :32 the page's own switch is the same control with no label of its own.
            Click(w, RowButton(view, "Hydra")); var page = Assert.Single(Nodes<HydraSettingsPage>(view)); var hydra = Nodes<SettingsSwitch>(page).First(); var hydraToggle = Assert.Single(Nodes<SmallSwitch>(hydra)); Assert.Empty(Text(hydra)); var on = s.HydraEnabled;
            Click(w, hydraToggle); Assert.Equal(!on, s.HydraEnabled); Assert.Equal(!on, h.Defaults.Object("hydraEnabled")); hydraToggle = Assert.Single(Nodes<SmallSwitch>(Nodes<SettingsSwitch>(Assert.Single(Nodes<HydraSettingsPage>(view))).First())); Assert.True(hydraToggle.Focus()); Press(w, Key.Space); Assert.Equal(on, s.HydraEnabled);
            // Providers :668-671 each provider's header row ends in the switch bound to that provider being enabled.
            Click(w, RowButton(view, "Providers")); var codex = Nodes<ProviderSettingsSection>(view).First(x => Text(x).Contains("Codex")); Click(w, Assert.Single(Nodes<Button>(Assert.Single(Nodes<CollapsibleProviderHeader>(codex))))); Tick(h, Chrome.PanelSlide.Duration * 1000 + 1); var providerToggle = Nodes<SmallSwitch>(codex).First(); // a collapsed card is neither tappable nor focusable (:763-765): expand it first
            Assert.Empty(Text(Nodes<SettingsSwitch>(codex).First())); var enabled = s.IsEnabled(ProviderKind.Codex);
            Click(w, providerToggle); Assert.Equal(!enabled, s.IsEnabled(ProviderKind.Codex)); Assert.True(providerToggle.Focus()); Press(w, Key.Space); Assert.Equal(enabled, s.IsEnabled(ProviderKind.Codex));
        }, height: 1600);
    }
}
