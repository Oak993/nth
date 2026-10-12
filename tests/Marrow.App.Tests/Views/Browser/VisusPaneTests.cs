using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Browser;
using Marrow.App.Views.Panes;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.UI.Chrome;
using Marrow.UI.Panes;

namespace Marrow.App.Tests.Views.Browser;

// PORT-ONLY: web surfaces W8a (E:/Droppy/work/design/web-surfaces/DESIGN.md 13.3 "Visus chrome: address field rules, tab
// strip, find bar, prompts (focus never taken, Enter inert, Esc, F6 into the prompt, Tab and Space inside it, the 400 ms
// keyboard shield)", 5.1 pane anatomy, 3.7 "When focus is inside Visus, browser keys ... are Visus's", 3.12 the tab strip in
// the header row, 10 the trust strip's name; ED-NEW-web-w8a-2). The Visus pane in the workspace over the fake engine.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w8a-2")]
public sealed class VisusPaneTests(HeadlessSession headless)
{
    private static Func<PaneKind, PaneContent?> Contents(VisusRig visus) => kind => kind == PaneKind.Browser
        ? VisusPane.Create(visus.Session, null)
        : new PaneContent(new Border { Child = new TextBox { Text = PaneKinds.Title(kind) } }, PaneKinds.Title(kind));

    private static (PaneWorkspaceRig Rig, VisusPaneBody Body) Open(VisusRig visus, double width = 1400)
    {
        var rig = new PaneWorkspaceRig(width: width, contents: Contents(visus), popupLayers: true);
        rig.Workspace.Open(PaneKind.Browser);
        rig.Settle();
        var body = Assert.IsType<VisusPaneBody>(rig.Card(PaneKind.Browser).Content.Body);
        return (rig, body);
    }

    private static VisusTab Go(VisusRig visus, PaneWorkspaceRig rig, string address)
    {
        visus.Session.Go(address);
        var tab = visus.Session.Selected!;
        VisusRig.Live(tab).RaiseNavigationCompleted(tab.Url);
        rig.Layout();
        return tab;
    }

    private static KeyEventArgs KeyArgs(Key key, PhysicalKey physical, KeyModifiers modifiers = KeyModifiers.None) =>
        new() { RoutedEvent = InputElement.KeyDownEvent, Key = key, PhysicalKey = physical, KeyModifiers = modifiers };

    private static Point CentreOf(Control control, Visual root) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root) ?? default;

    [Fact]
    public void Opening_Visus_shows_one_new_tab_with_the_keyboard_in_the_address_field() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;

        var tab = Assert.Single(visus.Session.Tabs);
        Assert.True(tab.IsBlank);
        Assert.IsType<VisusStartSurface>(body.BlankSurface);
        Assert.True(body.Toolbar.Address.Box.IsFocused);
        Assert.Equal(VisusAddressField.Placeholder, body.Toolbar.Address.Box.PlaceholderText);
        Assert.True(visus.Session.IsVisible);
        Assert.False(VisusHost.IsStarted); // nothing loaded yet: no browser (2.3)
    });

    [Fact]
    public void The_tab_strip_sits_in_the_header_and_moves_into_the_body_in_group_mode() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var card = rig.Card(PaneKind.Browser);

        Assert.True(card.Header.IsTitleContentInHeader);
        Assert.True(card.Content.IsHeaderContentInHeader);
        Assert.True(body.IsStripInHeader);
        Assert.Same(card.Header, body.Strip.GetVisualParent());

        rig.Workspace.Open(PaneKind.Changes);
        rig.Settle();
        rig.Workspace.SetState(rig.Workspace.State.JoinAsTab(PaneKind.Browser, PaneKind.Changes), animate: false);
        rig.Settle();

        Assert.False(card.Content.IsHeaderContentInHeader);
        Assert.False(body.IsStripInHeader);
        Assert.True(body.IsVisualAncestorOf(body.Strip));
        Assert.NotSame(card.Header, body.Strip.GetVisualParent());
    });

    [Fact]
    public void Browser_keys_in_the_address_field_are_Visus_keys_not_nth_chords() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        Go(visus, rig, "example.com");
        body.Toolbar.Address.Box.Focus();
        rig.Layout();

        Assert.True(ShortcutScopeHost.FocusClaims(rig.Window, KeyArgs(Key.W, PhysicalKey.W, KeyModifiers.Control))); // not Archive thread
        Assert.True(ShortcutScopeHost.FocusClaims(rig.Window, KeyArgs(Key.Tab, PhysicalKey.Tab, KeyModifiers.Control))); // not Next thread
        Assert.True(ShortcutScopeHost.FocusClaims(rig.Window, KeyArgs(Key.N, PhysicalKey.N, KeyModifiers.Control | KeyModifiers.Shift)));
        Assert.False(ShortcutScopeHost.FocusClaims(rig.Window, KeyArgs(Key.C, PhysicalKey.C, KeyModifiers.Control)));
        Assert.False(ShortcutScopeHost.FocusClaims(rig.Window, KeyArgs(Key.B, PhysicalKey.B, KeyModifiers.Control | KeyModifiers.Shift))); // Toggle Visus stays nth's

        rig.Chord(Key.T, PhysicalKey.T, RawInputModifiers.Control);
        Assert.Equal(2, visus.Session.Tabs.Count);
        Assert.True(visus.Session.Selected!.IsBlank);
        rig.Chord(Key.W, PhysicalKey.W, RawInputModifiers.Control);
        Assert.Single(visus.Session.Tabs);
        Assert.Equal("https://example.com/", visus.Session.Selected!.Url);
        Assert.True(rig.Workspace.State.IsOpen(PaneKind.Browser));

        body.Strip.TabViews[0].Focus(); // the strip sits in the header, outside the body: its keys are Visus's too
        rig.Layout();
        Assert.True(ShortcutScopeHost.FocusClaims(rig.Window, KeyArgs(Key.W, PhysicalKey.W, KeyModifiers.Control)));
        rig.Chord(Key.T, PhysicalKey.T, RawInputModifiers.Control);
        Assert.Equal(2, visus.Session.Tabs.Count);
    });

    [Fact]
    public void Outside_Visus_the_browser_keys_are_nths() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, _) = Open(visus);
        using var __ = rig;
        rig.Workspace.Open(PaneKind.Changes);
        rig.Settle();
        var other = Assert.IsType<Border>(rig.Card(PaneKind.Changes).Content.Body).Child!;
        other.Focus();
        rig.Layout();

        Assert.False(ShortcutScopeHost.FocusClaims(rig.Window, KeyArgs(Key.W, PhysicalKey.W, KeyModifiers.Control)));
        rig.Chord(Key.T, PhysicalKey.T, RawInputModifiers.Control);
        Assert.Single(visus.Session.Tabs);
    });

    [Fact]
    public void The_field_shows_the_address_without_its_scheme_and_the_whole_address_when_focused() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var field = body.Toolbar.Address;
        Go(visus, rig, "https://example.com/docs");
        body.Toolbar.More.Focus();
        rig.Layout();
        Assert.False(field.Box.IsFocused);
        Assert.Equal("example.com/docs", field.Box.Text);

        field.Box.Focus();
        rig.Layout();
        Assert.Equal("https://example.com/docs", field.Box.Text);
        Assert.Equal(0, field.Box.SelectionStart);
        Assert.Equal(field.Box.Text!.Length, field.Box.SelectionEnd);
        Assert.False(field.IsEditing);
    });

    [Fact]
    public void Typing_suggests_arrows_move_Esc_closes_then_reverts_then_is_not_the_fields() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var field = body.Toolbar.Address;
        var page = Go(visus, rig, "https://example.com/");
        field.Box.Focus();
        rig.Layout();

        field.Box.Text = "cook rice";
        Assert.True(field.IsEditing);
        Assert.True(field.AreSuggestionsOpen);
        Assert.Equal(VisusSuggestionKind.Search, field.Suggestions[0].Kind);
        Assert.Equal("example.com", field.Trust.Label); // the chip names the page's host while the address is edited
        Assert.Equal(-1, field.Highlight);
        rig.Key(Key.Down);
        Assert.Equal(0, field.Highlight);
        rig.Key(Key.Up);
        Assert.Equal(-1, field.Highlight);

        rig.Key(Key.Escape);
        Assert.False(field.AreSuggestionsOpen);
        Assert.True(field.IsEditing);
        rig.Key(Key.Escape);
        Assert.False(field.IsEditing);
        Assert.Equal("https://example.com/", field.Box.Text);
        var third = KeyArgs(Key.Escape, PhysicalKey.Escape);
        field.Box.RaiseEvent(third);
        Assert.False(third.Handled); // nothing edited: Esc is the pane's and the window's
        Assert.Equal("https://example.com/", page.Url);
    });

    [Fact]
    public void Enter_goes_where_the_first_row_says_and_Alt_Enter_opens_a_new_tab() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var field = body.Toolbar.Address;
        var first = Assert.Single(visus.Session.Tabs);
        field.Box.Focus();

        field.Box.Text = "how to cook rice";
        rig.Key(Key.Enter);
        Assert.Same(first, visus.Session.Selected);
        Assert.Equal("https://duckduckgo.com/?q=how%20to%20cook%20rice", first.Url);
        Assert.False(field.IsEditing);
        Assert.False(field.AreSuggestionsOpen);
        VisusRig.Live(first).RaiseNavigationCompleted(first.Url);

        field.Box.Focus();
        field.Box.Text = "example.org";
        rig.Key(Key.Enter, RawInputModifiers.Alt);
        Assert.Equal(2, visus.Session.Tabs.Count);
        Assert.Equal("https://example.org/", visus.Session.Selected!.Url);
        Assert.StartsWith("https://duckduckgo.com/", first.Url, StringComparison.Ordinal);
    });

    [Fact]
    public void A_chosen_suggestion_switches_to_its_tab() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var pricing = Go(visus, rig, "https://example.com/pricing");
        visus.Session.NewTab();
        rig.Layout();
        var field = body.Toolbar.Address;
        field.Box.Focus();

        field.Box.Text = "example.com/pri";
        var row = field.Suggestions.ToList().FindIndex(s => s.Kind == VisusSuggestionKind.SwitchToTab);
        Assert.True(row > 0);
        for (var i = 0; i <= row; i++) rig.Key(Key.Down);
        rig.Key(Key.Enter);

        Assert.Same(pricing, visus.Session.Selected);
        Assert.Equal(2, visus.Session.Tabs.Count);
    });

    [Fact]
    public void A_refused_address_keeps_the_text_and_shows_why() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var field = body.Toolbar.Address;
        field.Box.Focus();

        field.Box.Text = "javascript:alert(1)";
        rig.Key(Key.Enter);

        Assert.Equal(VisusAddress.ScriptRefusal, body.NoticeText);
        Assert.True(field.IsEditing);
        Assert.Equal("javascript:alert(1)", field.Box.Text);
        Assert.True(visus.Session.Selected!.IsBlank);
    });

    [Fact]
    public void The_trust_strip_names_the_site_and_the_kind_of_tab() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var trust = body.Toolbar.Trust;
        Assert.Equal("Visus · new tab", AutomationProperties.GetName(trust));

        Go(visus, rig, "https://example.com/");
        Assert.Equal(PaneGlyphKind.Lock, trust.Glyph);
        Assert.Equal("", trust.Label);
        Assert.Equal("Visus · example.com, secure connection", AutomationProperties.GetName(trust));

        Go(visus, rig, "http://plain.example/");
        Assert.Equal("Not secure", trust.Label);
        Assert.Equal(PaneGlyphKind.Warn, trust.Glyph);
        Assert.Equal("Visus · plain.example, not secure", AutomationProperties.GetName(trust));

        Go(visus, rig, "localhost:5173");
        Assert.Equal("Preview", trust.Label);
        Assert.Equal("Visus · localhost:5173, project preview", AutomationProperties.GetName(trust));

        visus.Session.NewTab(VisusTabKind.Private);
        Go(visus, rig, "https://example.com/");
        Assert.Equal("Private", trust.Label);
        Assert.Equal(PaneGlyphKind.Mask, trust.Glyph);
    });

    [Fact]
    public void The_strip_draws_a_tab_per_tab_and_its_buttons_and_mouse_work_like_a_browser() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var strip = body.Strip;
        Go(visus, rig, "https://a.example/");
        visus.Session.NewTab();
        Go(visus, rig, "https://b.example/");
        rig.Settle();

        Assert.Equal("Visus tabs", AutomationProperties.GetName(strip));
        Assert.Equal(2, strip.TabViews.Count);
        Assert.Equal(AutomationControlType.TabItem, strip.TabViews[0].GetValue(AutomationProperties.ControlTypeOverrideProperty));
        Assert.True(strip.TabViews[1].IsChosen);
        Assert.False(strip.TabViews[0].IsChosen);

        rig.Down(CentreOf(strip.TabViews[0], rig.Window));
        rig.Up(CentreOf(strip.TabViews[0], rig.Window));
        Assert.Same(visus.Session.Tabs[0], visus.Session.Selected);

        strip.NewTabButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        rig.Settle();
        Assert.Equal(3, visus.Session.Tabs.Count);
        Assert.Equal(3, strip.TabViews.Count);

        var middle = CentreOf(strip.TabViews[2], rig.Window);
        rig.Window.MouseDown(middle, MouseButton.Middle, RawInputModifiers.MiddleMouseButton);
        rig.Window.MouseUp(middle, MouseButton.Middle, RawInputModifiers.None);
        rig.Settle();
        Assert.Equal(2, visus.Session.Tabs.Count);
    });

    [Fact]
    public void Dragging_a_tab_along_the_strip_reorders_the_tabs() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var a = Go(visus, rig, "https://a.example/");
        visus.Session.NewTab();
        var b = Go(visus, rig, "https://b.example/");
        rig.Settle();
        var strip = body.Strip;
        var start = CentreOf(strip.TabViews[0], rig.Window);
        var width = strip.TabViews[0].Bounds.Width;

        rig.Down(start);
        rig.Move(start + new Vector(10, 0));
        Assert.True(strip.IsDragging);
        rig.Move(start + new Vector(width + 10, 0));
        rig.Up(start + new Vector(width + 10, 0));
        rig.Settle();

        Assert.False(strip.IsDragging);
        Assert.Equal(new[] { b, a }, visus.Session.Tabs);
        Assert.Same(a, visus.Session.Selected);
    });

    [Fact]
    public void Pages_keep_their_hosts_and_blank_tabs_show_the_start_or_private_surface() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var page = Go(visus, rig, "https://a.example/");
        Assert.True(body.Hosts[page].IsVisible);
        Assert.Null(body.BlankSurface);

        visus.Session.NewTab();
        rig.Layout();
        Assert.False(body.Hosts[page].IsVisible); // kept in the tree, hidden
        var start = Assert.IsType<VisusStartSurface>(body.BlankSurface);
        Assert.Equal("https://a.example/", Assert.Single(start.Recent).Url);

        visus.Session.NewTab(VisusTabKind.Private);
        rig.Layout();
        Assert.IsType<VisusPrivateSurface>(body.BlankSurface);
        Assert.Contains(VisusPrivateSurface.Kept, AutomationProperties.GetName(body.BlankSurface!), StringComparison.Ordinal);
        Assert.Contains(VisusPrivateSurface.Unseen, AutomationProperties.GetName(body.BlankSurface!), StringComparison.Ordinal);

        visus.Session.Select(page);
        rig.Layout();
        Assert.True(body.Hosts[page].IsVisible);
    });

    [Fact]
    public void The_menu_offers_the_tab_commands_the_link_and_the_update_restart() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var page = Go(visus, rig, "https://a.example/");
        string[] Titles() => body.Toolbar.MenuEntries().Where(row => row is not null).Select(row => row!.Title).ToArray();

        Assert.Equal(new[] { "New tab", "New private tab", "Reopen closed tab", "Copy link", "Open in default browser" }, Titles());
        Assert.False(body.Toolbar.MenuEntries().First(row => row?.Title == "Reopen closed tab")!.IsEnabled);
        body.Toolbar.MenuEntries().First(row => row?.Title == "Open in default browser")!.Run();
        Assert.Equal(new[] { page.Url }, visus.OpenedInBrowser);

        visus.Engine.RaiseNewRuntimeVersion();
        Assert.Contains("Restart Visus to update", Titles());

        visus.Session.NewTab(VisusTabKind.Private);
        Go(visus, rig, "https://secret.example/");
        Assert.False(body.Toolbar.MenuEntries().First(row => row?.Title == "Open in default browser")!.IsEnabled);
    });

    [Fact]
    public void An_app_prompt_never_takes_focus_ignores_Enter_and_shields_its_buttons_for_400_ms() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var tab = Go(visus, rig, "https://a.test/");
        body.Toolbar.Address.Box.Focus();
        rig.Layout();
        var focused = rig.Window.FocusManager?.GetFocusedElement();

        var request = VisusRig.Live(tab).RaiseExternalUri("mailto:dana@example.com", "https://a.test", userInitiated: true);
        rig.Layout();

        var prompt = Assert.IsType<VisusPrompt>(body.Prompt);
        Assert.Equal("a.test wants to open Mail (mailto:)", prompt.Title);
        Assert.NotNull(prompt.Remember);
        Assert.Same(focused, rig.Window.FocusManager?.GetFocusedElement()); // 8.4: never takes focus
        Assert.False(request.Decision.IsCompleted);

        var enter = KeyArgs(Key.Enter, PhysicalKey.Enter);
        prompt.OpenButton.RaiseEvent(enter);
        Assert.True(enter.Handled);
        Assert.False(request.Decision.IsCompleted); // Enter does nothing here

        Assert.True(prompt.IsShielded);
        prompt.OpenButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(request.Decision.IsCompleted); // a click as it appeared does nothing
        var space = KeyArgs(Key.Space, PhysicalKey.Space);
        prompt.OpenButton.RaiseEvent(space);
        Assert.True(space.Handled);

        visus.Time.Advance(VisusPrompt.Shield);
        Assert.False(prompt.IsShielded);
        prompt.OpenButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(request.Decision.Result);
        Assert.Null(body.Prompt);
    });

    [Fact]
    public void Esc_from_the_prompt_or_from_the_page_cancels_it() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var tab = Go(visus, rig, "https://a.test/");
        var page = VisusRig.Live(tab);

        var first = page.RaiseExternalUri("zoommtg://zoom.us/join", "https://a.test", userInitiated: true);
        var prompt = body.Prompt!;
        Assert.Null(prompt.Remember);
        Assert.Equal(VisusPrompt.Foot, "Enter does nothing here. Esc cancels.");
        var esc = KeyArgs(Key.Escape, PhysicalKey.Escape);
        prompt.CancelButton.RaiseEvent(esc);
        Assert.True(esc.Handled);
        Assert.False(first.Decision.Result);
        Assert.Null(body.Prompt);

        var second = page.RaiseExternalUri("zoommtg://zoom.us/join", "https://a.test", userInitiated: true);
        Assert.NotNull(body.Prompt);
        Assert.True(page.RaiseKey(new KeyChord(0x01, ChordModifiers.None)).Handled); // the page's Esc while a prompt is open
        Assert.False(second.Decision.Result);
        Assert.Null(body.Prompt);
        Assert.False(page.RaiseKey(new KeyChord(0x01, ChordModifiers.None)).Handled); // no prompt: Esc is the page's again
    });

    [Fact]
    public void F6_goes_into_the_open_prompt_and_choosing_another_tab_cancels_it() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var other = Go(visus, rig, "https://other.test/");
        visus.Session.NewTab();
        var tab = Go(visus, rig, "https://a.test/");

        var request = VisusRig.Live(tab).RaiseExternalUri("mailto:dana@example.com", "https://a.test", userInitiated: true);
        rig.Layout();
        var prompt = body.Prompt!;
        Assert.NotNull(rig.Workspace.PromptFirst);
        Assert.True(VisusPrompts.AnyIn(rig.Window));
        Assert.True(rig.Workspace.CycleFocus(forward: true));
        Assert.True(prompt.Remember!.IsFocused);

        visus.Session.Select(other);
        Assert.False(request.Decision.Result);
        Assert.Null(body.Prompt);
        Assert.False(VisusPrompts.AnyIn(rig.Window));
    });

    [Fact]
    public void A_second_prompt_waits_for_the_first() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var tab = Go(visus, rig, "https://a.test/");
        var page = VisusRig.Live(tab);

        var first = page.RaiseExternalUri("zoommtg://zoom.us/1", "https://a.test", userInitiated: true);
        var second = page.RaiseExternalUri("zoommtg://zoom.us/2", "https://a.test", userInitiated: true);
        Assert.Equal(1, body.WaitingPrompts);
        Assert.Equal("zoommtg://zoom.us/1", body.Prompt!.Launch.Uri);

        body.Prompt.Dismiss();
        Assert.False(first.Decision.Result);
        Assert.Equal("zoommtg://zoom.us/2", body.Prompt!.Launch.Uri);
        Assert.False(second.Decision.IsCompleted);
        visus.Time.Advance(VisusPrompt.Shield);
        body.Prompt.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(second.Decision.Result);
        Assert.Null(body.Prompt);
    });

    [Fact]
    public void A_pop_up_the_page_opened_by_itself_shows_the_chip() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var tab = Go(visus, rig, "https://a.example/");
        Assert.False(body.Toolbar.Address.PopupChip.IsVisible);

        VisusRig.Live(tab).RaiseNewWindow("https://ads.example/", userInitiated: false);
        rig.Layout();
        Assert.True(body.Toolbar.Address.PopupChip.IsVisible);
        tab.DismissBlockedPopup();
        rig.Layout();
        Assert.False(body.Toolbar.Address.PopupChip.IsVisible);
    });

    [Fact]
    public void The_toolbar_follows_the_page() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var toolbar = body.Toolbar;
        Assert.False(toolbar.Back.IsEnabled);
        Assert.False(toolbar.Reload.IsEnabled);

        visus.Session.Go("https://a.example/");
        var tab = visus.Session.Selected!;
        rig.Layout();
        Assert.Equal(PaneGlyphKind.Close, toolbar.Reload.Kind); // loading: Stop
        VisusRig.Live(tab).CanGoBack = true;
        VisusRig.Live(tab).RaiseNavigationCompleted(tab.Url);
        rig.Layout();
        Assert.Equal(PaneGlyphKind.Reload, toolbar.Reload.Kind);
        Assert.True(toolbar.Back.IsEnabled);
        Assert.True(toolbar.Reload.IsEnabled);
        Assert.False(toolbar.Forward.IsEnabled);

        tab.SetZoom(1.25);
        tab.Reload(); // any change of the tab redraws the chips
        VisusRig.Live(tab).RaiseNavigationCompleted(tab.Url);
        rig.Layout();
        Assert.True(toolbar.Address.ZoomChip.IsVisible);
    });

    [Fact]
    public void The_tab_layout_shrinks_tabs_together_then_keeps_a_window_round_the_chosen_one()
    {
        var fits = VisusTabLayout.Solve([100, 100], 0, 400, 26, 26, 3, 120);
        Assert.Equal(new[] { 100.0, 100.0 }, fits.Widths);
        Assert.False(fits.ShowsOverflow);

        var shrunk = VisusTabLayout.Solve([200, 200, 200], 0, 500, 26, 26, 3, 120);
        Assert.All(shrunk.Widths, w => Assert.Equal(155, w, 0.01));
        Assert.False(shrunk.ShowsOverflow);

        var small = VisusTabLayout.Solve([80, 300, 300], 0, 450, 26, 26, 3, 120);
        Assert.Equal(80, small.Widths[0], 0.01); // a short tab never grows to the minimum
        Assert.Equal(167.5, small.Widths[1], 0.01);

        var many = VisusTabLayout.Solve(Enumerable.Repeat(200.0, 10).ToList(), 5, 700, 26, 26, 3, 120);
        Assert.True(many.ShowsOverflow);
        Assert.Equal(new[] { 3, 4, 5, 6, 7 }, many.Widths.Select((w, i) => (w, i)).Where(p => p.w > 0).Select(p => p.i));
        Assert.All(many.Widths.Where(w => w > 0), w => Assert.Equal(120, w));
        Assert.Empty(VisusTabLayout.Solve([], 0, 300, 26, 26, 3, 120).Widths);
    }
}
