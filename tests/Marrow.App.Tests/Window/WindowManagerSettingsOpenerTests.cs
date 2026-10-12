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
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window;

[Collection("Headless")]
public sealed class WindowManagerSettingsOpenerTests(HeadlessSession headless) : SettingsWindowFixture(headless)
{
    [Fact]
    [Trait("Req", "PR-SETTINGS-001")]
    [Trait("Req", "UI-SETTINGS-001")]
    [Trait("Decision", "OD-0151")]
    public void Default_and_remapped_shortcuts_reopen_the_same_page_and_search()
    {
        // AppCommands.swift:12-15; WindowManager.swift:32-37; OD-0008 B7/C3. OD-0151: Settings is the panel in the main window, which the
        // shortcut opens there (and closes when it is open).
        WithManager((_, manager) =>
        {
            manager.ShowMain(); manager.ShowSettings(SettingsPage.Archive);
            var main = MainOf(manager); var settings = Settings(manager); Drain(settings);
            Assert.Same(main, settings);
            var view = View(manager);
            var input = Assert.Single(Nodes<TextBox>(Assert.Single(Nodes<SidebarSearchField>(view))));
            input.Text = "archive"; Drain(settings);
            Press(main, Key.W, PhysicalKey.W, RawInputModifiers.Control);
            Assert.False(manager.IsSettingsOpen);
            Assert.True(main.IsVisible); // Ctrl+W closes Settings, not the main window it is in

            Press(main, Key.OemComma, PhysicalKey.Comma, RawInputModifiers.Control);

            Assert.True(manager.IsSettingsOpen);
            Assert.Same(main, Settings(manager)); Assert.Same(view, View(manager));
            Assert.True(main.IsVisible); Assert.True(main.IsActive);
            Assert.Equal("archive", input.Text);
            Assert.Contains(view.GetVisualDescendants().OfType<Control>(), control => AutomationProperties.GetName(control) == "No archived threads");
            Assert.Null(SettingsNavigation.Shared.RequestedPage);
            Assert.Null(NativeMenu.GetMenu(main));
            Assert.Empty(main.GetVisualDescendants().OfType<Menu>()); // the Settings panel in it included

            manager.CloseSettings();
            Assert.False(manager.IsSettingsOpen);
            var rebound = new KeyChord(0x15, ChordModifiers.Control | ChordModifiers.Shift);
            ShortcutStore.Shared.Current.Set(rebound, AppShortcut.OpenSettings);
            Press(main, Key.OemComma, PhysicalKey.Comma, RawInputModifiers.Control);
            Assert.False(manager.IsSettingsOpen);
            Press(main, Key.Y, PhysicalKey.Y, RawInputModifiers.Control | RawInputModifiers.Shift);
            Assert.True(manager.IsSettingsOpen);
            Assert.Same(main, Settings(manager)); Assert.True(main.IsActive);
            Assert.Same(view, View(manager)); Assert.Equal("archive", input.Text);
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-001")]
    [Trait("Req", "UI-SETTINGS-001")]
    [Trait("Decision", "OD-0151")]
    public void Palette_row_click_closes_palette_then_reopens_retained_Settings()
    {
        // CommandPalette.swift:176-178,201-206. Main content is a host for the real palette, not RootView proof.
        WithManager((_, manager) =>
        {
            using var rig = new SettingsRiseRig();
            var host = new Border { Background = Brushes.Transparent };
            manager.MainContent = () => host;
            manager.ShowMain(); manager.ShowSettings(SettingsPage.Archive);
            var main = MainOf(manager); var settings = Settings(manager); Settle(rig, settings);
            Assert.Same(main, settings);
            var overlay = Overlay(manager);
            var view = View(manager);
            var input = Assert.Single(Nodes<TextBox>(Assert.Single(Nodes<SidebarSearchField>(view))));
            input.Text = "archive"; Drain(settings);
            manager.CloseSettings(); Settle(rig, main);
            Assert.False(manager.IsSettingsOpen); Assert.False(overlay.IsVisible); Assert.True(main.IsVisible);
            var palette = new CommandPalette(); host.Child = palette;
            manager.Model.IsCommandPalettePresented = true;
            Drain(main);
            palette.QueryField.Text = "Settings"; Drain(main);
            var row = Assert.Single(palette.RowsPanel.Children);
            Assert.Contains(Nodes<TextBlock>(row), text => text.Text == "Settings");
            Assert.Contains(Nodes<TextBlock>(row), text => text.Text == ShortcutStore.Label(AppShortcut.OpenSettings));
            // OD-0151: Settings shows as its panel becomes visible in the main window (it was the Settings window's platform show).
            var paletteFlagsAtShow = new List<bool>();
            overlay.PropertyChanged += (_, e) =>
            {
                if (e.Property == Visual.IsVisibleProperty && e.GetNewValue<bool>()) paletteFlagsAtShow.Add(manager.Model.IsCommandPalettePresented);
            };

            Click(main, row);

            Assert.False(Assert.Single(paletteFlagsAtShow));
            Assert.False(manager.Model.IsCommandPalettePresented);
            Assert.True(manager.IsSettingsOpen); Assert.True(main.IsActive);
            Assert.Same(main, Settings(manager)); Assert.Same(view, View(manager));
            Assert.Equal("archive", input.Text);
            Assert.Contains(view.GetVisualDescendants().OfType<Control>(), control => AutomationProperties.GetName(control) == "No archived threads");
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-010")]
    [Trait("Req", "PR-SETTINGS-001")]
    [Trait("Req", "UI-SETTINGS-001")]
    [Trait("Decision", "OD-0151")]
    public void Keyboard_close_preserves_state_and_a_field_that_uses_Escape_keeps_Settings_open()
    {
        // PR-WIN-010 includes ThreadWindow.swift:45-64. Escape belongs to a focused field that uses it (the search field empties itself,
        // Chrome.swift:814-815); Ctrl+W closes. OD-0151: what closes is the Settings panel; the main window it is in stays.
        WithManager((_, manager) =>
        {
            using var rig = new SettingsRiseRig();
            manager.ShowSettings(SettingsPage.Archive);
            var window = Settings(manager); Settle(rig, window);
            var view = View(manager);
            var input = Assert.Single(Nodes<TextBox>(Assert.Single(Nodes<SidebarSearchField>(view))));
            Click(window, input);
            Assert.True(input.IsFocused);
            input.Text = "arch"; Drain(window);
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.True(manager.IsSettingsOpen);
            Assert.Equal("", input.Text);
            input.Text = "archive"; Drain(window);
            Press(window, Key.W, PhysicalKey.W, RawInputModifiers.Control);
            Assert.False(manager.IsSettingsOpen);
            Assert.True(window.IsVisible);
            manager.ShowSettings(); Settle(rig, window);
            Assert.Same(window, Settings(manager)); Assert.Same(view, View(manager));
            Assert.Equal("archive", input.Text);
            var archive = Assert.Single(Nodes<SidebarRow>(view).SelectMany(Nodes<Button>),
                button => AutomationProperties.GetName(button) == "Archive");
            Assert.True(archive.Focus());
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.False(manager.IsSettingsOpen);
            Assert.True(window.IsVisible);
            manager.ShowSettings(); Settle(rig, window);
            Assert.Same(view, View(manager)); Assert.Equal("archive", input.Text);
        });
    }

    private static void Settle(SettingsRiseRig rig, HostWindow window)
    {
        for (var i = 0; i < 30; i++) { rig.Frame(1.0 / 60); Drain(window); }
    }

    [Fact]
    [Trait("Req", "PR-WIN-010")]
    public void Secondary_close_chord_yields_while_a_sheet_is_attached()
    {
        // ThreadWindow.swift:59, referenced by PR-WIN-010. No dialog is created, presented, or awaited.
        WithManager((_, manager) =>
        {
            manager.ShowSettings(SettingsPage.Archive);
            var window = Assert.IsType<global::Marrow.App.Window.ThreadWindow>(Settings(manager)); Drain(window);
            // OD-0151: Settings is the panel in the main window, attached as its sheet; a sheet attached over it keeps the chord.
            var panel = window.AttachedSheet;
            Assert.Same(Overlay(manager).Card, panel);
            window.AttachedSheet = new Border();
            try
            {
                Press(window, Key.W, PhysicalKey.W, RawInputModifiers.Control);
                Assert.True(manager.IsSettingsOpen);
                Assert.True(window.IsVisible);
            }
            finally { window.AttachedSheet = panel; }
            Press(window, Key.W, PhysicalKey.W, RawInputModifiers.Control);
            Assert.False(manager.IsSettingsOpen);
            Assert.True(window.IsVisible); // the chord closes Settings, not the main window it is in
        });
    }
}
