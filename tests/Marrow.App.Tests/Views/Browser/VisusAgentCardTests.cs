using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Browser;
using Marrow.App.Views.Panes;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.Services.Web;
using Marrow.UI.Panes;

namespace Marrow.App.Tests.Views.Browser;

// PORT-ONLY: web surfaces W11 (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.10: the per-site card with Allow once / Always
// allow / Don't allow and never Always on a shared tab, the cross-origin card with the full address, its query spelled out and
// "This address carries text from the page you were on", the guarded-action card with the element's words, the rows, the chat
// and the model and "Enter never confirms", placed near the target; "the Tactus sheet rules: no default focus, Enter does
// nothing, 400 ms shield, never under the pointer, the keyboard path of 10.1"; 13.3 headless; ED-NEW-web-w11-1). The cards in
// the Visus pane over the fake engine.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w11-1")]
public sealed class VisusAgentCardTests(HeadlessSession headless)
{
    private const string Thread = "6f1d3c1e-0b7a-4a43-9d1e-2b8c4b8f6a11";

    private static (PaneWorkspaceRig Rig, VisusPaneBody Body, VisusAgentAsks Asks) Open(VisusRig visus)
    {
        var asks = VisusAgentAsks.ResetForTests();
        visus.Session.AgentThread = Thread;
        var rig = new PaneWorkspaceRig(width: 1400, contents: kind => kind == PaneKind.Browser
            ? VisusPane.Create(visus.Session, null)
            : new PaneContent(new Border { Child = new TextBox { Text = PaneKinds.Title(kind) } }, PaneKinds.Title(kind)), popupLayers: true);
        rig.Workspace.Open(PaneKind.Browser);
        rig.Settle();
        return (rig, Assert.IsType<VisusPaneBody>(rig.Card(PaneKind.Browser).Content.Body), asks);
    }

    private static VisusTab Go(VisusRig visus, PaneWorkspaceRig rig, string address, VisusTabKind kind = VisusTabKind.Browse)
    {
        var tab = visus.Session.NewTab(kind, address);
        VisusRig.Live(tab).RaiseNavigationCompleted(tab.Url);
        rig.Layout();
        return tab;
    }

    private static List<string> Texts(Control root) => [.. root.GetLogicalDescendants().OfType<TextBlock>()
        .Select(t => t.Inlines is { Count: > 0 } inlines ? string.Concat(inlines.OfType<Run>().Select(r => r.Text)) : t.Text ?? "")];

    private static List<string> Labels(VisusAgentCard card) => [.. card.ButtonLabels];

    // 10.1: "The prompt's buttons are named in full".
    private static List<string> Names(VisusAgentCard card) => [.. card.Buttons.Select(b => AutomationProperties.GetName(b) ?? "")];

    private static KeyEventArgs KeyArgs(Key key, PhysicalKey physical) => new() { RoutedEvent = InputElement.KeyDownEvent, Key = key, PhysicalKey = physical };

    private static void Settle(PaneWorkspaceRig rig)
    {
        Dispatcher.UIThread.RunJobs();
        rig.Layout();
    }

    [Fact]
    public void The_site_card_for_a_shared_tab_never_offers_Always_takes_no_focus_and_shields_its_buttons() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body, asks) = Open(visus);
        using var _ = rig;
        var tab = Go(visus, rig, "https://mail.test/");
        body.Toolbar.Address.Box.Focus();
        rig.Layout();
        var focused = rig.Window.FocusManager?.GetFocusedElement();

        var answer = asks.AskSite(new WebSiteAsk(Thread, "t1", "https://mail.test", "Opus 5.5", Shared: true) { TabId = tab.Id }, CancellationToken.None);
        Settle(rig);

        var card = Assert.IsType<VisusAgentCard>(body.AgentCard);
        Assert.Equal("Let Opus 5.5 use mail.test in Visus?", card.Title);
        Assert.Equal(new[] { "Allow once", "Don't allow" }, Labels(card));
        Assert.Equal(new[] { "Allow Opus 5.5 to use mail.test this time", "Don't allow Opus 5.5 to use mail.test" }, Names(card));
        Assert.Equal("Let Opus 5.5 use mail.test in Visus? Your tab, shared with the model in this chat. Press F6 to answer.", card.Announcement);
        Assert.Equal(card.Announcement, AutomationProperties.GetName(card));
        Assert.Equal(card.Announcement, body.Announcement); // the pane's live region reads it as it appears (10.1)
        Assert.Contains("nth · Visus", Texts(card));
        Assert.Contains(VisusPrompt.Foot, Texts(card));
        Assert.Same(focused, rig.Window.FocusManager?.GetFocusedElement()); // never takes focus

        var enter = KeyArgs(Avalonia.Input.Key.Enter, PhysicalKey.Enter);
        card.Buttons[0].RaiseEvent(enter);
        Assert.True(enter.Handled);
        card.Buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(answer.IsCompleted); // Enter does nothing, and a click as it appeared does nothing

        visus.Time.Advance(VisusPrompt.Shield);
        card.ButtonFor(VisusAgentChoice.Once)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle(rig);

        Assert.Equal(WebSiteAnswer.AllowOnce, answer.Result);
        Assert.Null(body.AgentCard);
    });

    [Fact]
    public void The_site_card_for_an_agent_tab_offers_Always_allow() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body, asks) = Open(visus);
        using var _ = rig;
        var tab = Go(visus, rig, "https://a.test/", VisusTabKind.Agent);

        var answer = asks.AskSite(new WebSiteAsk(Thread, "t1", "https://a.test", "Opus 5.5", Shared: false) { TabId = tab.Id }, CancellationToken.None);
        Settle(rig);
        var card = body.AgentCard!;
        visus.Time.Advance(VisusPrompt.Shield);
        card.ButtonFor(VisusAgentChoice.Always)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(new[] { "Allow once", "Always allow", "Don't allow" }, Labels(card));
        Assert.Equal(WebSiteAnswer.AlwaysAllow, answer.Result);
    });

    [Fact]
    public void The_cross_origin_card_spells_out_the_address_and_says_it_carries_page_text() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body, asks) = Open(visus);
        using var _ = rig;
        var tab = Go(visus, rig, "https://a.test/", VisusTabKind.Agent);
        const string url = "https://collect.test/log?note=Dana%20works%20at%20Acme&v=2";
        IReadOnlyList<WebQueryField> query = [new("note", "Dana works at Acme", FromPage: true), new("v", "2", FromPage: false)];

        var answer = asks.AskNavigate(new WebNavigateAsk(Thread, "t1", ["https://a.test"], url, query, "Opus 5.5") { TabId = tab.Id }, CancellationToken.None);
        Settle(rig);
        var card = body.AgentCard!;
        var texts = Texts(card);

        Assert.Equal("Let Opus 5.5 open collect.test?", card.Title);
        Assert.Contains(url, texts);
        Assert.Contains("Dana works at Acme", texts);
        Assert.Contains("note", texts);
        Assert.Contains(WebNavigateAsk.CarriesNote, texts);
        Assert.Equal("This address carries text from the page you were on", WebNavigateAsk.CarriesNote);
        Assert.Equal(new[] { "Allow once", "Always for this site pair", "Don't allow" }, Labels(card));

        var esc = KeyArgs(Avalonia.Input.Key.Escape, PhysicalKey.Escape);
        card.Buttons[0].RaiseEvent(esc); // Esc refuses, even inside the shield
        Settle(rig);

        Assert.True(esc.Handled);
        Assert.Equal(WebNavigateAnswer.Deny, answer.Result);
        Assert.Null(body.AgentCard);
    });

    [Fact]
    public void The_guard_card_names_the_element_the_rows_the_chat_and_the_model_and_sits_near_the_target() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body, asks) = Open(visus);
        using var _ = rig;
        var tab = Go(visus, rig, "http://localhost:5173/team", VisusTabKind.Agent);
        var ask = new WebGuardAsk(Thread, "t1", "http://localhost:5173", WebGuardCategory.Send, "send", "Send invite", "Opus 5.5")
        {
            TabId = tab.Id,
            Chat = "Ship the store page",
            Rows = [new("To", "dana@example.com"), new("Role", "Editor"), new("Site", "localhost:5173")],
            Near = new WebPoint(220, 340),
        };

        var answer = asks.Confirm(ask, CancellationToken.None);
        Settle(rig);
        var card = body.AgentCard!;
        var texts = Texts(card);
        var popup = body.AgentCardPopup!;

        Assert.Equal("Send invite?", card.Title);
        Assert.Contains("The model will press Send invite in Visus for you.", texts);
        Assert.Contains("dana@example.com", texts);
        Assert.Contains("Ship the store page", texts);
        Assert.Contains("Opus 5.5", texts);
        Assert.Contains("Asked because this sends a message. Enter never confirms.", texts);
        Assert.Equal(new[] { "Send invite", "Cancel" }, Labels(card));
        Assert.Equal(new[] { "Send invite on localhost:5173 for Opus 5.5", "Cancel Send invite on localhost:5173" }, Names(card));
        Assert.Equal(VisusAgentCard.GuardWidth, card.Width);
        Assert.Equal(PlacementMode.AnchorAndGravity, popup.Placement);
        Assert.Equal(PopupGravity.Top, popup.PlacementGravity);
        Assert.True(popup.PlacementRect is { } near && near.Contains(new Point(220, 340)) && near.Width <= 2);

        visus.Time.Advance(VisusPrompt.Shield);
        card.ButtonFor(VisusAgentChoice.Deny)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(answer.Result);
    });

    [Fact]
    public void An_ask_brings_its_tab_forward_once_and_waits_while_the_user_is_elsewhere() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body, asks) = Open(visus);
        using var _ = rig;
        var agent = Go(visus, rig, "https://a.test/", VisusTabKind.Agent);
        var mine = Go(visus, rig, "https://news.test/");
        Assert.Same(mine, visus.Session.Selected);

        var answer = asks.Confirm(new WebGuardAsk(Thread, "t1", "https://a.test", WebGuardCategory.Send, "send", "Send", "Opus 5.5") { TabId = agent.Id }, CancellationToken.None);
        Settle(rig);
        var forward = visus.Session.Selected;
        var shown = body.AgentCard;
        visus.Session.Select(mine);
        Settle(rig);
        var away = body.AgentCard;
        visus.Session.Select(agent);
        Settle(rig);

        Assert.Same(agent, forward);
        Assert.NotNull(shown);
        Assert.Null(away); // hidden while the user reads another tab; not answered
        Assert.False(answer.IsCompleted);
        Assert.NotNull(body.AgentCard);
    });

    [Fact]
    public void A_withdrawn_ask_takes_its_card_away_and_the_page_Esc_refuses_the_next() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body, asks) = Open(visus);
        using var _ = rig;
        var tab = Go(visus, rig, "https://a.test/", VisusTabKind.Agent);
        using var stop = new CancellationTokenSource();

        var first = asks.Confirm(new WebGuardAsk(Thread, "t1", "https://a.test", WebGuardCategory.Delete, "delete", "Delete", "Opus 5.5") { TabId = tab.Id }, stop.Token);
        Settle(rig);
        Assert.NotNull(body.AgentCard);
        stop.Cancel();
        Settle(rig);
        Assert.Null(body.AgentCard);
        Assert.True(first.IsCanceled);

        var second = asks.Confirm(new WebGuardAsk(Thread, "t1", "https://a.test", WebGuardCategory.Delete, "delete", "Delete", "Opus 5.5") { TabId = tab.Id }, CancellationToken.None);
        Settle(rig);
        Assert.True(VisusRig.Live(tab).RaiseKey(new KeyChord(0x01, ChordModifiers.None)).Handled); // the page's Esc
        Settle(rig);

        Assert.False(second.Result);
        Assert.Null(body.AgentCard);
    });

    [Fact]
    public void F6_goes_into_the_card_and_another_chats_ask_never_shows_here() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body, asks) = Open(visus);
        using var _ = rig;
        var tab = Go(visus, rig, "https://a.test/", VisusTabKind.Agent);

        var elsewhere = asks.AskSite(new WebSiteAsk("another-chat", "t1", "https://a.test", "Opus 5.5", Shared: false) { TabId = "nope" }, CancellationToken.None);
        Settle(rig);
        Assert.Null(body.AgentCard);

        var evalAsk = asks.AskEval(new WebEvalAsk(Thread, "t1", "http://localhost:5173", "Opus 5.5") { TabId = tab.Id }, CancellationToken.None);
        Settle(rig);
        var card = body.AgentCard!;

        Assert.Equal("Let Opus 5.5 run scripts in localhost:5173?", card.Title);
        Assert.Equal(new[] { "Allow Opus 5.5 to run scripts in localhost:5173", "Don't allow Opus 5.5 to run scripts in localhost:5173" }, Names(card));
        body.Toolbar.Address.Box.Focus();
        rig.Layout();
        var before = rig.Window.FocusManager?.GetFocusedElement();
        visus.Time.Advance(VisusPromptCard.Shield); // the appearance shield is over

        Assert.True(rig.Workspace.CycleFocus(forward: true)); // F6: the newest card first
        Assert.True(card.Lead.IsFocused); // 10.1: the card's text, not a button
        Assert.True(card.IsShielded); // the shield runs again from the keyboard's entry
        rig.Key(Avalonia.Input.Key.Tab);
        Assert.True(card.Buttons[0].IsFocused); // Tab reaches the buttons
        rig.Key(Avalonia.Input.Key.Space);
        Assert.False(evalAsk.IsCompleted); // a Space within 400 ms of focus entering it is ignored
        rig.Key(Avalonia.Input.Key.Enter);
        Assert.False(evalAsk.IsCompleted); // Enter does nothing
        rig.Key(Avalonia.Input.Key.F6);
        Assert.Same(before, rig.Window.FocusManager?.GetFocusedElement()); // F6 again: back where it was

        Assert.False(elsewhere.IsCompleted);
        Assert.False(evalAsk.IsCompleted); // focus alone never answers the card
    });

    [Fact]
    public void One_card_at_a_time_a_model_card_waits_behind_the_tabs_own_question_and_shows_once_it_is_answered() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body, asks) = Open(visus);
        using var _ = rig;
        var tab = Go(visus, rig, "http://localhost:5173/");
        var request = VisusRig.Live(tab).RaisePermission("http://localhost:5173", WebPermissionKind.Camera);
        rig.Layout();
        var permission = Assert.IsType<VisusPermissionPrompt>(body.Card);

        var answer = asks.AskSite(new WebSiteAsk(Thread, "t1", "http://localhost:5173", "Opus 5.5", Shared: true) { TabId = tab.Id }, CancellationToken.None);
        Settle(rig);

        Assert.Null(body.AgentCard); // never two cards over one tab
        Assert.Same(permission, body.Card);

        visus.Time.Advance(VisusPromptCard.Shield);
        permission.AllowOnceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle(rig);

        Assert.Equal(WebPermissionDecision.AllowOnce, request.Decision.Result);
        Assert.Null(body.Card);
        var card = Assert.IsType<VisusAgentCard>(body.AgentCard);
        Assert.Equal(card.Announcement, body.Announcement);

        var later = VisusRig.Live(tab).RaisePermission("http://localhost:5173", WebPermissionKind.Microphone);
        rig.Layout();
        Assert.Null(body.Card); // and the tab's next question waits behind the model's card
        Assert.False(answer.IsCompleted);
        Assert.False(later.Decision.IsCompleted);
    });
}
