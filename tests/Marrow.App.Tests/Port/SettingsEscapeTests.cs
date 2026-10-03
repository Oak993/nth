using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window;
using Marrow.App.Views.Settings;
using Marrow.App.Window;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Port;

// OD-0088 (ED-253): Escape in the real Settings window (WindowManager's SecondaryWindow). The source's SecondaryWindow closes
// on Escape in sendEvent (App/Window/ThreadWindow.swift:47-55), but a menu or a popover over it is a window of its own on the
// Mac (NSMenu, NSPopover), so the Escape that closes it never reaches the Settings window. The port draws those over the
// window, so the window's Escape first closes the newest of them, and only with none open closes the window. The theme menu
// (SPEC 7: "Escape closes and returns focus to segment 4", opened by keyboard or pointer, SPEC 9.4) and the page popovers.
[Collection("Headless")]
public sealed class SettingsEscapeTests(HeadlessSession headless) : SettingsWindowFixture(headless)
{
    private void Open(SettingsPage page, Action<SecondaryWindow, SettingsView, RecordingPresenter> check) =>
        WithManager((h, manager) =>
        {
            var presenter = new RecordingPresenter();
            using var presentation = Presentation.Presenter.Override(presenter);
            using var rig = new SettingsRiseRig { Accepts = false };
            manager.Model.Settings.Theme = AppTheme.Dark;
            manager.SettingsCloak = rig.Cloak;
            manager.ShowSettings(page);
            Dispatcher.UIThread.RunJobs();
            var window = Settings(manager);
            Drain(window);
            for (var i = 0; i < 60; i++) { rig.Frame(1.0 / 60); Drain(window); }
            check(window, View(manager), presenter);
        });

    private static void Escape(HostWindow window) => Press(window, Key.Escape, PhysicalKey.Escape);

    private static IInputElement? Focused(HostWindow window) => window.FocusManager?.GetFocusedElement();

    [Fact]
    [Trait("Decision", "ED-253")]
    public void Escape_in_the_theme_menu_opened_by_keyboard_closes_only_the_menu_and_focus_returns_to_segment_4() => Open(SettingsPage.Appearance, (window, view, _) =>
    {
        var card = Nodes<LookCard>(view).Single();
        Assert.True(card.Segments.Buttons[3].Focus(NavigationMethod.Tab));
        Press(window, Key.Enter, PhysicalKey.Enter);
        Assert.True(card.Menu.IsOpen);
        Assert.Contains(card.Menu.Rows, r => r.IsFocused);
        Escape(window);
        Assert.False(card.Menu.IsOpen);
        Assert.True(window.IsVisible); // the window stays
        Assert.Same(card.Segments.Buttons[3], Focused(window));
        // With nothing open over it, Escape closes the window as the source's does (WindowManager hides it).
        Escape(window);
        Assert.False(window.IsVisible); // the window closes (hidden, WindowManager keeps it)
    });

    [Fact]
    [Trait("Decision", "ED-253")]
    public void Escape_in_the_theme_menu_opened_by_pointer_closes_only_the_menu_and_focus_returns_to_segment_4() => Open(SettingsPage.Appearance, (window, view, _) =>
    {
        var card = Nodes<LookCard>(view).Single();
        Click(window, card.Segments.Buttons[3]);
        Assert.True(card.Menu.IsOpen);
        Escape(window);
        Assert.False(card.Menu.IsOpen);
        Assert.True(window.IsVisible); // the window stays
        Assert.Same(card.Segments.Buttons[3], Focused(window));
        Escape(window);
        Assert.False(window.IsVisible); // the window closes (hidden, WindowManager keeps it)
    });

    // A page popover (PlainControls.Popover: GlassPickerButton, ChromeVisualPicker, the theme capsule, the archive and model
    // popovers, MCP's): Escape dismisses it (NSPopover's own Escape) and the window stays.
    [Fact]
    [Trait("Decision", "ED-253")]
    public void Escape_with_a_page_popover_open_dismisses_the_popover_and_the_window_stays() => Open(SettingsPage.General, (window, view, presenter) =>
    {
        var picker = view.GetVisualDescendants().OfType<Control>().First(c => c.GetType().Name.StartsWith("GlassPickerButton", StringComparison.Ordinal) && c.IsEffectivelyVisible);
        Click(window, picker);
        var popup = Assert.Single(presenter.Calls, c => c.Presented).Popup;
        Escape(window);
        Assert.Equal((popup, false), presenter.Calls[^1]);
        Assert.True(window.IsVisible); // the window stays
        Escape(window);
        Assert.False(window.IsVisible); // the window closes (hidden, WindowManager keeps it)
    });

    // A popover the page builds on its own Popup (HydraSettingsPage's cookbook, as the About page's release notes): the same.
    [Fact]
    [Trait("Decision", "ED-253")]
    public void Escape_with_the_hydra_cookbook_popover_open_dismisses_it_and_the_window_stays() => Open(SettingsPage.Hydra, (window, view, presenter) =>
    {
        var button = view.GetVisualDescendants().OfType<Button>().First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Open cookbook"));
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); // below the page's fold
        Drain(window);
        var popup = Assert.Single(presenter.Calls, c => c.Presented).Popup;
        Escape(window);
        Assert.Equal((popup, false), presenter.Calls[^1]);
        Assert.True(window.IsVisible); // the window stays
        Escape(window);
        Assert.False(window.IsVisible); // the window closes (hidden, WindowManager keeps it)
    });
}
