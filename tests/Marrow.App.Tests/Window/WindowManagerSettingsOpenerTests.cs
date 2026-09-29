// Real manager factories, keyboard routing, and hit-tested palette controls.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Palette;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Window;

[Collection("Headless")]
public sealed class WindowManagerSettingsOpenerTests(HeadlessSession headless) : SettingsWindowFixture(headless)
{
    [Fact]
    [Trait("Req", "PR-SETTINGS-001")]
    [Trait("Req", "UI-SETTINGS-001")]
    public void Default_and_remapped_shortcuts_reopen_the_same_page_and_search()
    {
        // AppCommands.swift:12-15; WindowManager.swift:32-37; OD-0008 B7/C3.
        WithManager((_, manager) =>
        {
            manager.ShowMain(); manager.ShowSettings(SettingsPage.Archive);
            var main = MainOf(manager); var settings = Settings(manager); Drain(settings);
            var view = View(manager);
            var input = Assert.Single(Nodes<TextBox>(Assert.Single(Nodes<SidebarSearchField>(view))));
            input.Text = "archive"; Drain(settings);
            var primary = SettingsScreenScope.Monitor(new PixelRect(0, 0, 3000, 2000), new PixelRect(0, 0, 3000, 1960), primary: true);
            using var screens = new SettingsScreenScope(main, [primary], primary);
            Press(settings, Key.W, PhysicalKey.W, RawInputModifiers.Control);
            Assert.False(settings.IsVisible);
            main.Position = new PixelPoint(120, 100);

            Press(main, Key.OemComma, PhysicalKey.Comma, RawInputModifiers.Control);

            Assert.Same(settings, Settings(manager)); Assert.Same(view, View(manager));
            Assert.True(settings.IsVisible); Assert.True(settings.IsActive);
            Assert.Equal("archive", input.Text);
            Assert.Contains(view.GetVisualDescendants().OfType<Control>(), control => AutomationProperties.GetName(control) == "No archived threads");
            Assert.Null(SettingsNavigation.Shared.RequestedPage);
            Assert.Equal(new PixelPoint(390, 240), settings.Position);
            Assert.Null(NativeMenu.GetMenu(main)); Assert.Null(NativeMenu.GetMenu(settings));
            Assert.Empty(main.GetVisualDescendants().OfType<Menu>());
            Assert.Empty(settings.GetVisualDescendants().OfType<Menu>());

            settings.Close();
            var rebound = new KeyChord(0x15, ChordModifiers.Control | ChordModifiers.Shift);
            ShortcutStore.Shared.Current.Set(rebound, AppShortcut.OpenSettings);
            Press(main, Key.OemComma, PhysicalKey.Comma, RawInputModifiers.Control);
            Assert.False(settings.IsVisible);
            Press(main, Key.Y, PhysicalKey.Y, RawInputModifiers.Control | RawInputModifiers.Shift);
            Assert.True(settings.IsVisible); Assert.True(settings.IsActive);
            Assert.Same(view, View(manager)); Assert.Equal("archive", input.Text);
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-001")]
    [Trait("Req", "UI-SETTINGS-001")]
    public void Palette_row_click_closes_palette_then_reopens_retained_Settings()
    {
        // CommandPalette.swift:176-178,201-206. Main content is a host for the real palette, not RootView proof.
        WithManager((_, manager) =>
        {
            var host = new Border { Background = Brushes.Transparent };
            manager.MainContent = () => host;
            manager.ShowMain(); manager.ShowSettings(SettingsPage.Archive);
            var main = MainOf(manager); var settings = Settings(manager); Drain(settings);
            var view = View(manager);
            var input = Assert.Single(Nodes<TextBox>(Assert.Single(Nodes<SidebarSearchField>(view))));
            input.Text = "archive"; Drain(settings);
            settings.Close(); Assert.False(settings.IsVisible);
            var primary = SettingsScreenScope.Monitor(new PixelRect(0, 0, 3000, 2000), new PixelRect(0, 0, 3000, 1960), primary: true);
            using var screens = new SettingsScreenScope(main, [primary], primary);
            main.Position = new PixelPoint(140, 120);
            var palette = new CommandPalette(); host.Child = palette;
            manager.Model.IsCommandPalettePresented = true;
            Drain(main);
            palette.QueryField.Text = "Settings"; Drain(main);
            var row = Assert.Single(palette.RowsPanel.Children);
            Assert.Contains(Nodes<TextBlock>(row), text => text.Text == "Settings");
            Assert.Contains(Nodes<TextBlock>(row), text => text.Text == ShortcutStore.Label(AppShortcut.OpenSettings));
            using var platform = new SettingsWindowPlatformScope(settings);
            var paletteFlagsAtShow = new List<bool>();
            platform.Spy.BeforeShow = () => paletteFlagsAtShow.Add(manager.Model.IsCommandPalettePresented);

            Click(main, row);

            Assert.False(Assert.Single(paletteFlagsAtShow));
            Assert.False(manager.Model.IsCommandPalettePresented);
            Assert.True(settings.IsVisible); Assert.True(settings.IsActive);
            Assert.Same(settings, Settings(manager)); Assert.Same(view, View(manager));
            Assert.Equal("archive", input.Text);
            Assert.Contains(view.GetVisualDescendants().OfType<Control>(), control => AutomationProperties.GetName(control) == "No archived threads");
            Assert.Equal(new PixelPoint(410, 260), settings.Position);
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-010")]
    [Trait("Req", "PR-SETTINGS-001")]
    [Trait("Req", "UI-SETTINGS-001")]
    public void Secondary_keyboard_close_preserves_state_and_field_Escape_keeps_the_window_open()
    {
        // PR-WIN-010 includes ThreadWindow.swift:45-64. Escape belongs to a focused field; Ctrl+W closes.
        WithManager((_, manager) =>
        {
            manager.ShowSettings(SettingsPage.Archive);
            var window = Settings(manager); Drain(window);
            var view = View(manager);
            var input = Assert.Single(Nodes<TextBox>(Assert.Single(Nodes<SidebarSearchField>(view))));
            Click(window, input);
            Assert.True(input.IsFocused);
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.True(window.IsVisible);
            input.Text = "archive"; Drain(window);
            Press(window, Key.W, PhysicalKey.W, RawInputModifiers.Control);
            Assert.False(window.IsVisible);
            manager.ShowSettings(); Drain(window);
            Assert.Same(window, Settings(manager)); Assert.Same(view, View(manager));
            Assert.Equal("archive", input.Text);
            var archive = Assert.Single(Nodes<SidebarRow>(view).SelectMany(Nodes<Button>),
                button => AutomationProperties.GetName(button) == "Archive");
            Assert.True(archive.Focus());
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.False(window.IsVisible);
            manager.ShowSettings(); Drain(window);
            Assert.Same(view, View(manager)); Assert.Equal("archive", input.Text);
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-010")]
    public void Secondary_close_chord_yields_while_a_sheet_is_attached()
    {
        // ThreadWindow.swift:59, referenced by PR-WIN-010. No dialog is created, presented, or awaited.
        WithManager((_, manager) =>
        {
            manager.ShowSettings(SettingsPage.Archive);
            var window = Settings(manager); Drain(window);
            window.AttachedSheet = new Border();
            try
            {
                Press(window, Key.W, PhysicalKey.W, RawInputModifiers.Control);
                Assert.True(window.IsVisible);
            }
            finally { window.AttachedSheet = null; }
            Press(window, Key.W, PhysicalKey.W, RawInputModifiers.Control);
            Assert.False(window.IsVisible);
        });
    }
}
