using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Tests.Views.Settings;

[Collection("Headless")]
public sealed class SettingsRestThemeTests(HeadlessSession headless) : SettingsRestFixture(headless)
{
    [Fact]
    [Trait("Req", "UI-THEME-001")] // App/Views/Settings/SettingsView.swift:424-426; Chrome.swift:1055-1080
    [Trait("Req", "UI-THEME-008")] // App/Views/Settings/SettingsView.swift:427-430
    public void Appearance_rows_have_exact_live_details_inherited_type_colours_order_and_no_row_action()
    {
        With(() => new AppearanceSettingsPage(), (view, model, _, w) =>
        {
            var card = Nodes<ChromeCard>(view)[0]; var rows = Nodes<ChromeRow>(card); Assert.Equal(new[] { "Theme", "Transparency", "Sidebar", "Activity list", "Open the sidebar at launch" }, rows.Select(r => Text(r).First()));
            CheckRow(rows[0], "Theme", "Follows Windows"); CheckRow(rows[1], "Transparency", "How much shows through the window");
            var content = Assert.IsType<StackPanel>(Assert.IsType<StackPanel>(Assert.IsType<Border>(card.Child).Child).Children[0]); Assert.IsType<ChromeRowDivider>(content.Children[1]); Assert.Same(rows[1], content.Children[2]);
            Click(w, Assert.IsType<Grid>(Assert.IsType<Border>(rows[0].Child).Child).Children[0]); Assert.Equal(Marrow.UI.Theme.AppTheme.System, model.Settings.Theme); Assert.False(Assert.Single(Nodes<Popup>(rows[0])).IsOpen);
            model.Settings.Theme = AppTheme.Nord; Drain(w); CheckRow(rows[0], "Theme", "Dark · Frost"); Assert.True(rows[1].IsEffectivelyEnabled);
            Click(w, Assert.IsType<Grid>(Assert.IsType<Border>(rows[1].Child).Child).Children[0]); Assert.Equal(0.5, model.Settings.BackdropOpacity);
        });
    }

    [Fact]
    [Trait("Req", "UI-THEME-002")] // App/Views/Settings/SettingsView.swift:1159-1202
    public void Theme_capsule_matches_geometry_fonts_ink_hover_curve_and_real_open_toggle()
    {
        With(() => new ThemePickerButton(Binding<AppTheme>.Constant(AppTheme.System)), (view, _, h, w) =>
        {
            Assert.Equal(28, view.Bounds.Height); Assert.InRange(view.Bounds.Width, 1, 200); var button = Assert.Single(Nodes<Button>(view)); var label = Assert.IsType<StackPanel>(button.Content); Assert.Equal(6, label.Spacing); Assert.Equal(28, label.Height); Assert.Equal(new Thickness(12, 0), label.Margin);
            var swatch = Assert.IsType<ThemeSwatch>(label.Children[0]); Assert.Equal(14, swatch.Width); Assert.Equal(14, swatch.Height); var title = Assert.IsType<TextBlock>(label.Children[1]); Assert.Equal("System", title.Text); Font(title, 12.5, FontWeight.Medium); Assert.Equal(1, title.MaxLines);
            var arrow = Assert.IsType<SwiftImage>(label.Children[2]); Assert.Equal("chevron.down", arrow.SymbolName); Assert.Equal(SwiftFont.System(8, SwiftFont.Weight.Bold), SwiftImage.GetFont(arrow)); Ink(arrow, arrow.Foreground, Chrome.SecondaryText); Ink(title, title.Foreground, Chrome.PrimaryText.Opacity(0.92));
            var width = view.Bounds.Width; Hover(w, button); Tick(h, 50); Ink(title, title.Foreground, Chrome.PrimaryText.Opacity(0.92 + 0.08 * Animation.EaseOut(0.1).Progress(0.05))); Tick(h, 50); Ink(title, title.Foreground, Chrome.PrimaryText); Ink(arrow, arrow.Foreground, Chrome.SecondaryText); Assert.Equal(width, view.Bounds.Width);
            Assert.True(OverlayLayer.GetOverlayLayer(button) is not null, "Overlay layer absent: " + string.Join(",", w.GetVisualDescendants().Select(v => v.GetType().Name)));
            Click(w, button); var popup = Assert.Single(Nodes<Popup>(view)); Assert.True(popup.IsOpen); Assert.Equal(PlacementMode.Bottom, popup.Placement);
            w.MouseDown(new Point(2, 2), MouseButton.Left); w.MouseUp(new Point(2, 2), MouseButton.Left); Drain(w); Assert.False(popup.IsOpen); Tick(h, 100); Ink(title, title.Foreground, Chrome.PrimaryText.Opacity(0.92));
        }, height: 1100, nativePopup: true);
    }

    [Fact]
    [Trait("Req", "UI-THEME-003")] // App/Views/Settings/SettingsView.swift:1164-1166,1195-1208
    [Trait("Req", "UI-THEME-004")] // App/Views/Settings/SettingsView.swift:1198
    [Trait("Req", "UI-THEME-005")] // App/Views/Settings/SettingsView.swift:1200
    [Trait("Req", "UI-THEME-006")] // App/Views/Settings/SettingsView.swift:1206-1210; AppTheme.swift:44-73
    public void Theme_menu_has_literal_palette_order_typed_headers_checks_selection_and_real_dismissal()
    {
        With(() => new ThemePickerButton(new Binding<AppTheme>(() => ThemeManager.Current, v => ThemeManager.Current = v)), (view, _, h, w) =>
        {
            var button = Assert.Single(Nodes<Button>(view)); Click(w, button); var popup = Assert.Single(Nodes<Popup>(view)); Assert.True(popup.IsOpen); var menu = popup.Child!;
            var names = new[] { "System", "Dark", "Catppuccin Mocha", "Dracula", "Tokyo Night", "Nord", "Gruvbox", "One Dark", "Everforest", "Kanagawa", "Rosé Pine", "Solarized Dark", "GitHub Dark", "Ayu", "Night Owl", "Monokai", "Claude", "Codex", "Cursor", "Matrix", "Light", "Catppuccin Latte", "Gruvbox Light", "Solarized Light", "GitHub Light", "Claude Light" };
            Assert.Equal(names, Nodes<PopoverItem>(menu).Select(i => Text(i).Single())); Assert.Null(Popovers.GetTint(menu));
            var items = Assert.IsType<StackPanel>(Nodes<PopoverItem>(menu)[0].Parent); Assert.Equal(28, items.Children.Count); Assert.IsType<PopoverItem>(items.Children[0]); Assert.Equal("Dark", Text(items.Children[1]).Single()); Assert.Equal("Light", Text(items.Children[21]).Single());
            foreach (var header in Nodes<PopoverSectionHeader>(menu)) { var text = Assert.IsType<TextBlock>(header.Child); Font(text, 11, FontWeight.SemiBold); Ink(text, text.Foreground, Chrome.SecondaryText); Assert.Equal(new Thickness(8, 6, 8, 3), text.Margin); Assert.Equal(Avalonia.Layout.HorizontalAlignment.Left, text.HorizontalAlignment); Assert.Empty(Nodes<Button>(header)); }
            foreach (var item in Nodes<PopoverItem>(menu)) { var check = Nodes<SwiftImage>(item).Single(i => i.SymbolName == "checkmark"); Assert.Equal(Text(item).Single() == "System" ? 1 : 0, check.Opacity); Assert.Single(Nodes<ThemeSwatch>(item)); var face = Assert.Single(Nodes<Button>(item)); var row = Assert.IsType<StackPanel>(Assert.IsType<Border>(face.Content).Child); Assert.Equal(26, row.Height); Assert.Equal(new Thickness(8, 0), row.Margin); Assert.Equal(8, row.Spacing); Font(Assert.Single(Nodes<TextBlock>(item)), 13); Assert.True(face.IsEnabled); }
            var nord = Nodes<PopoverItem>(menu).Single(i => Text(i).Contains("Nord")); var nordButton = Assert.Single(Nodes<Button>(nord)); var nordSurface = Assert.IsType<Border>(nordButton.Content); Hover(w, nordButton); Tick(h, 100); Ink(nordSurface, nordSurface.Background, Chrome.Overlay(0.1)); Click(w, nordButton); Assert.Equal(AppTheme.Nord, ThemeManager.Current); Assert.False(popup.IsOpen);
            Click(w, button); Assert.True(popup.IsOpen); menu = popup.Child!; Assert.Equal(1, Nodes<SwiftImage>(Nodes<PopoverItem>(menu).Single(i => Text(i).Contains("Nord"))).Single(i => i.SymbolName == "checkmark").Opacity); Assert.Equal(SwiftColor.Hex("88C0D0"), Popovers.GetTint(menu)); // AppTheme.swift:121 Nord accent 88C0D0.
            w.MouseDown(new Point(2, 2), MouseButton.Left); w.MouseUp(new Point(2, 2), MouseButton.Left); Drain(w); Assert.False(popup.IsOpen); Assert.Equal(AppTheme.Nord, ThemeManager.Current);
        }, height: 1100, nativePopup: true);
    }
}
