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
    private void Open(SettingsPage page, Action<SecondaryWindow, SettingsView, RecordingPresenter> check, Action<Marrow.App.AppModel>? arrange = null) =>
        WithManager((h, manager) =>
        {
            var presenter = new RecordingPresenter();
            using var presentation = Presentation.Presenter.Override(presenter);
            using var rig = new SettingsRiseRig { Accepts = false };
            rise = rig;
            manager.Model.Settings.Theme = AppTheme.Dark;
            arrange?.Invoke(manager.Model);
            manager.SettingsCloak = rig.Cloak;
            manager.ShowSettings(page);
            Dispatcher.UIThread.RunJobs();
            var window = Settings(manager);
            Drain(window);
            for (var i = 0; i < 60; i++) { rig.Frame(1.0 / 60); Drain(window); }
            check(window, View(manager), presenter);
        });

    private SettingsRiseRig? rise;

    // At the Settings sizes (ED-NEW-textsize-2) a control can sit below the 446-point page's first screen: scrolled into view as a
    // person scrolls it, past the page's 150 ms scroll freeze (SettingsScrollTracker), so the click meets it.
    private void Reveal(HostWindow window, Control control) { control.BringIntoView(); Drain(window); rise!.Frame(0.16); Drain(window); }

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
        Reveal(window, picker); // at the Settings sizes (ED-NEW-textsize-2) the first picker is below the window's first screen
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

    // UI-MCP-047 (MCPSettingsPage.swift:570-572): the custom server editor's popover (an "Add server" row of the MCP page, PlainControls.Popover through
    // MCPSettingsPage.Popover): Escape dismisses it as NSPopover's own Escape does, the draft goes with it (the content is built each time it shows), and the window stays.
    // With the field focused (add mode) and with none (edit mode) is the native MCPCustomEditorNativeTests.
    [Fact]
    [Trait("Req", "UI-MCP-047")]
    public void Escape_with_the_mcp_custom_server_editor_open_dismisses_it_and_the_window_stays() => Open(SettingsPage.Mcp, (window, view, presenter) =>
    {
        var add = view.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && b.Bounds.Width > 100 && b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Add server"));
        Click(window, add);
        var popup = Assert.Single(presenter.Calls, c => c.Presented).Popup;
        Assert.True(WindowTransients.IsShown(popup));
        Escape(window);
        Assert.Equal((popup, false), presenter.Calls[^1]);
        Assert.False(WindowTransients.IsShown(popup));
        Assert.True(window.IsVisible); // the window stays
        Escape(window);
        Assert.False(window.IsVisible); // the window closes (hidden, WindowManager keeps it)
    });

    // ED-253 round 4 (F1): a link's popover (LinkPopover, right-click on a link in the Licenses popover or the About page's
    // release notes) is presented with its anchor's window: Escape closes it, the Settings window stays, and the next Escape
    // closes the window.
    [Fact]
    [Trait("Decision", "ED-253")]
    public void Escape_with_a_link_popover_open_closes_it_and_the_next_escape_closes_the_window() => Open(SettingsPage.About, (window, view, presenter) =>
    {
        var anchor = view.GetVisualDescendants().OfType<TextBlock>().First(t => t.IsEffectivelyVisible);
        try
        {
            Marrow.UI.Markdown.LinkPopover.Shared.Show(Marrow.Core.Port.SwiftURL.Create("https://example.com")!, null, new Rect(0, 0, 10, 10), anchor);
            var popup = Assert.Single(presenter.Calls, c => c.Presented).Popup;
            Assert.True(WindowTransients.IsShown(popup));
            Escape(window);
            Assert.Equal((popup, false), presenter.Calls[^1]);
            Assert.False(WindowTransients.IsShown(popup));
            Assert.True(window.IsVisible); // the window stays
            Escape(window);
            Assert.False(window.IsVisible); // the window closes (hidden, WindowManager keeps it)
        }
        finally { Marrow.UI.Markdown.LinkPopover.Shared.Close(); }
    });

    // A link popover closed by a click outside it (its light dismiss, popoverDidClose) is no longer open over the window.
    [Fact]
    [Trait("Decision", "ED-253")]
    public void A_link_popover_closed_by_its_light_dismiss_is_no_longer_over_the_window() => Open(SettingsPage.About, (window, view, presenter) =>
    {
        var anchor = view.GetVisualDescendants().OfType<TextBlock>().First(t => t.IsEffectivelyVisible);
        try
        {
            Marrow.UI.Markdown.LinkPopover.Shared.Show(Marrow.Core.Port.SwiftURL.Create("https://example.com")!, null, new Rect(0, 0, 10, 10), anchor);
            var popup = Assert.Single(presenter.Calls, c => c.Presented).Popup;
            Marrow.UI.Markdown.LinkPopover.Shared.PopoverDidClose();
            Assert.False(WindowTransients.IsShown(popup));
            // And one closed by its own rows (Open Link, Copy Link: close()).
            Marrow.UI.Markdown.LinkPopover.Shared.Show(Marrow.Core.Port.SwiftURL.Create("https://example.com")!, null, new Rect(0, 0, 10, 10), anchor);
            Assert.True(WindowTransients.IsShown(popup));
            Marrow.UI.Markdown.LinkPopover.Shared.Close();
            Assert.False(WindowTransients.IsShown(popup));
            Escape(window);
            Assert.False(window.IsVisible); // nothing open over it: Escape closes the window
        }
        finally { Marrow.UI.Markdown.LinkPopover.Shared.Close(); }
    });

    // The text menu of a paragraph (ParagraphTextPresenter's Copy / Select All, NativeContextMenus) is a window of its own as
    // NSMenu is: an Escape that reaches the window while it is open closes the menu, not the window (EscapeInsideMenu presses it
    // while the menu is open).
    [Fact]
    [Trait("Decision", "ED-253")]
    public void Escape_while_a_paragraph_text_menu_is_open_closes_the_menu_and_the_window_stays() => Open(SettingsPage.About, (window, view, _) =>
    {
        var host = view.GetVisualDescendants().OfType<Panel>().First(p => p.IsEffectivelyVisible && p.Bounds.Width > 100);
        var markdown = new Marrow.UI.Markdown.MarkdownView("Words a reader may copy.");
        host.Children.Add(markdown);
        try
        {
            Drain(window);
            var paragraph = markdown.GetVisualDescendants().OfType<ParagraphTextPresenter>().First();
            var menu = new EscapeInsideMenu(() => { Escape(window); return window.IsVisible; });
            using (NativeContextMenus.Presenter.Override(menu))
                paragraph.RaiseEvent(new ContextRequestedEventArgs());
            Assert.True(menu.ShownAfterEscape, "Escape over the paragraph's text menu closed the Settings window");
            Assert.Equal(1, menu.Dismissed);
            Assert.True(window.IsVisible);
            Escape(window);
            Assert.False(window.IsVisible); // the menu is gone: Escape closes the window
        }
        finally { host.Children.Remove(markdown); }
    });

    // A text menu closed without Escape (a row chosen, a click outside it) is no longer over the window.
    [Fact]
    [Trait("Decision", "ED-253")]
    public void A_paragraph_text_menu_closed_otherwise_leaves_the_next_escape_to_the_window() => Open(SettingsPage.About, (window, view, _) =>
    {
        var host = view.GetVisualDescendants().OfType<Panel>().First(p => p.IsEffectivelyVisible && p.Bounds.Width > 100);
        var markdown = new Marrow.UI.Markdown.MarkdownView("Words a reader may copy.");
        host.Children.Add(markdown);
        try
        {
            Drain(window);
            var paragraph = markdown.GetVisualDescendants().OfType<ParagraphTextPresenter>().First();
            using (NativeContextMenus.Presenter.Override(new EscapeInsideMenu(escape: null)))
                paragraph.RaiseEvent(new ContextRequestedEventArgs());
            Escape(window);
            Assert.False(window.IsVisible);
        }
        finally { host.Children.Remove(markdown); }
    });

    // ED-253 round 4 (F6): the About page's release-note cards (UpdateReleaseSectionCards, AboutUpdateViews.swift:241-244) each
    // present their popover over the window: Escape closes it, the window stays, the next Escape closes the window.
    [Fact]
    [Trait("Decision", "ED-253")]
    public void Escape_with_a_release_note_popover_open_closes_it_and_the_window_stays() => Open(SettingsPage.About, (window, view, presenter) =>
    {
        var host = view.GetVisualDescendants().OfType<Panel>().First(p => p.IsEffectivelyVisible && p.Bounds.Width > 100);
        var sections = ReleaseNoteCategoryExtensions.AllCases.Select(c => new UpdateReleaseNotesDigest.Section
            { Category = c, Bullets = new Marrow.Core.Port.ValueList<string>(["Change"]), RollupCount = 0 }).ToArray();
        var cards = new UpdateReleaseSectionCards(new UpdateReleaseNotesDigest { Sections = new Marrow.Core.Port.ValueList<UpdateReleaseNotesDigest.Section>(sections) }, "9.0.0");
        host.Children.Add(cards);
        try
        {
            Drain(window);
            var card = cards.GetVisualDescendants().OfType<Button>().First();
            card.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Drain(window);
            var popup = Assert.Single(presenter.Calls, c => c.Presented).Popup;
            Assert.True(WindowTransients.IsShown(popup));
            Escape(window);
            Assert.Equal((popup, false), presenter.Calls[^1]);
            Assert.True(window.IsVisible); // the window stays
            Escape(window);
            Assert.False(window.IsVisible); // the window closes (hidden, WindowManager keeps it)
        }
        finally { host.Children.Remove(cards); }
    });

    // ED-253 round 4 (F6): a Hydra pair's editor (HydraPairRow, HydraSettingsPage.swift:264-271) is a popover over the window:
    // Escape closes it (its draft is the pair itself, kept), the window stays, the next Escape closes the window.
    [Fact]
    [Trait("Decision", "ED-253")]
    public void Escape_with_a_hydra_pair_editor_open_closes_it_and_the_window_stays() => Open(SettingsPage.Hydra, (window, view, presenter) =>
    {
        var row = view.GetVisualDescendants().OfType<HydraPairRow>().Single();
        var open = row.GetVisualDescendants().OfType<Button>().First(); // the row's own button, the remove control trails it
        open.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Drain(window);
        var popup = Assert.Single(presenter.Calls, c => c.Presented).Popup;
        Assert.Same(open, popup.PlacementTarget);
        Escape(window);
        Assert.Equal((popup, false), presenter.Calls[^1]);
        Assert.True(window.IsVisible); // the window stays
        Assert.Single(view.GetVisualDescendants().OfType<HydraPairRow>()); // the pair is still there
        Escape(window);
        Assert.False(window.IsVisible); // the window closes (hidden, WindowManager keeps it)
    }, model => model.Settings.AddHydraPair(new Marrow.Core.Models.HydraPair(Marrow.Core.Models.ProviderKind.Codex)));

    // ED-253 round 4 (F6): a popover open over another window (the main window's) is not over the Settings window: the Settings
    // window's Escape leaves it open and closes the Settings window.
    [Fact]
    [Trait("Decision", "ED-253")]
    public void Escape_in_settings_never_closes_a_popover_of_another_window() => Open(SettingsPage.General, (window, view, presenter) =>
    {
        var anchor = new Button();
        var other = new HostWindow { Width = 200, Height = 120, Content = anchor };
        var popup = new Avalonia.Controls.Primitives.Popup { PlacementTarget = anchor };
        var dismissed = 0;
        other.Show();
        try
        {
            Drain(other);
            Presentation.SetPresented(popup, true, anchor, () => dismissed++);
            Escape(window);
            Assert.Equal(0, dismissed);
            Assert.True(WindowTransients.IsShown(popup)); // the other window's popover stays
            Assert.Equal((popup, true), presenter.Calls[^1]);
            Assert.False(window.IsVisible); // nothing over Settings: its Escape closes it
        }
        finally
        {
            Presentation.SetPresented(popup, false, anchor, () => dismissed++);
            other.Close();
        }
    });
}
