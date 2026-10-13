using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port.Web.McpApps;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Artifacts;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Panes;
using Marrow.Core.Localisation;
using Marrow.Core.Models;
using Marrow.Core.Web;
using Marrow.Services.Web;
using Marrow.UI.Markdown;

namespace Marrow.App.Tests.Web;

// PORT-ONLY: web surfaces security review residual R2 (ED-NEW-web-sec-res-2; DESIGN.md 8.1 "A user click on a link inside an
// artifact is cancelled and offered to the link setting ... (the same list applies to MCP Apps and inline widgets)", 5.10 "the
// full URL with the query string expanded"). An MCP App's link the user clicked opened with no notice. Now the click only offers
// it, the way an inline widget's card does (ED-NEW-web-sec-artifacts-10): wherever the app is (its card in the thread, the App
// pane, its own window), a notice there says "Open link from the app" and shows the whole address, query and fragment included,
// with no line cap; only its Open opens exactly that address, through the chat's link setting. A navigation the app's script
// starts offers nothing, and a dismissed offer opens nothing.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-sec-res-2")]
public sealed class McpAppLinkNoticeTests(HeadlessSession headless)
{
    private const string Address = "https://evil.example/collect?board=v2.4.1&note=my%20salary%20is%2090k#k=secret";

    private static TimelineEntry Call() =>
        new(new TimelineItem(null, new TimelineItem.Content.Tool(new ToolCall { ToolCallKind = ToolCall.Kind.Mcp, Title = "shipyard · list_releases", Detail = "{}", ToolCallStatus = ToolCall.Status.Completed })));

    private static string TextOf(TextBlock block) =>
        block.Inlines is { Count: > 0 } inlines ? string.Concat(inlines.OfType<Run>().Select(run => run.Text)) : block.Text ?? "";

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Pump(TimelineMount m) => m.Advance(100);

    // The notice offers the whole address in one unclipped line of text, with Open as its one action.
    private static void AssertOffers(TabulaNotice notice, string address)
    {
        Assert.True(notice.IsVisible);
        Assert.Equal("Open link from the app", notice.Title);
        Assert.Equal(UiText.Get("live.app.open_link"), notice.Title);
        Assert.Equal(address, notice.Code);
        var showing = Assert.Single(notice.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsVisible), block => TextOf(block).Contains("evil.example", StringComparison.Ordinal));
        Assert.Equal(address, TextOf(showing));
        Assert.Equal(0, showing.MaxLines);
        Assert.Equal(UiText.Get("tabula.notice.open"), AutomationProperties.GetName(Assert.Single(notice.Actions)));
    }

    private static Button Dismiss(TabulaNotice notice) =>
        Assert.Single(notice.GetVisualDescendants().OfType<Button>(), button => AutomationProperties.GetName(button) == UiText.Get("tabula.notice.dismiss"));

    private static (Avalonia.Controls.Window Window, AppPane Pane, Marrow.App.Views.Panes.PaneContent Content) OpenPane(ThreadRuntime runtime)
    {
        var content = PaneContents.Create(PaneKind.App, runtime)!;
        var pane = Assert.IsType<AppPane>(content.Body);
        var window = new Avalonia.Controls.Window { Width = 700, Height = 500, Content = pane };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return (window, pane, content);
    }

    [Fact]
    public void An_apps_link_opens_only_from_the_offer_that_shows_its_whole_address_wherever_the_app_is() => ThreadTimelineRig.Run(headless, m =>
    {
        var hook = InlineText.OpenUrl;
        var opened = new List<string>();
        using var apps = new McpAppsAppRig(m.Runtime);
        try
        {
            InlineText.OpenUrl = url => opened.Add(url.AbsoluteString);
            m.SetEntries([Call()]);
            Pump(m);
            var row = Assert.Single(m.View.GetVisualDescendants().OfType<McpAppRow>());
            var card = row.Card!;
            var session = row.Session!;
            if (session.View.Engine is not FakeWebView { IsAttached: true }) session.View.AttachNative(1, 2);
            var fake = Assert.IsType<FakeWebView>(session.View.Engine);
            var notice = card.LinkNotice;

            // In the thread: the click opens nothing by itself; the card offers the whole address, and Open opens exactly it.
            fake.RaiseNavigationStarting(Address, userInitiated: true);
            Pump(m);
            Assert.Empty(opened);
            Assert.True(notice.IsEffectivelyVisible);
            AssertOffers(notice, Address);
            Click(Assert.Single(notice.Actions));
            Pump(m);
            Assert.Equal([Address], opened);
            Assert.False(notice.IsVisible);

            // A navigation the app's script starts offers nothing.
            fake.RaiseNavigationStarting("https://evil.example/?by=script", userInitiated: false);
            Pump(m);
            Assert.False(notice.IsVisible);

            // In the App pane: the pane offers it under its trust strip; dismissing it opens nothing.
            Click(card.SwitchButton(McpAppDisplayMode.Pane)!);
            Pump(m);
            var (paneWindow, pane, content) = OpenPane(m.Runtime);
            try
            {
                fake.RaiseNavigationStarting(Address + "&in=pane", userInitiated: true);
                Pump(m);
                paneWindow.UpdateLayout();
                Assert.True(pane.LinkNotice.IsEffectivelyVisible);
                AssertOffers(pane.LinkNotice, Address + "&in=pane");
                Click(Dismiss(pane.LinkNotice));
                Pump(m);
                Assert.False(pane.LinkNotice.IsVisible);
                Assert.Null(session.PendingLink);
                Assert.Equal([Address], opened);

                // In its own window: the window offers it; Open opens exactly that address.
                content.Actions[0].Invoke();
                Pump(m);
                var window = Assert.IsType<McpAppWindow>(session.Window);
                window.Settle();
                fake.RaiseNavigationStarting(Address + "&in=window", userInitiated: true);
                Pump(m);
                AssertOffers(window.LinkNotice, Address + "&in=window");
                Assert.Contains(window.LinkNotice, window.Window.GetVisualDescendants());
                Click(Assert.Single(window.LinkNotice.Actions));
                Pump(m);
                Assert.Equal([Address, Address + "&in=window"], opened);
                Assert.False(window.LinkNotice.IsVisible);
                session.WindowClosed(window);
                Pump(m);
            }
            finally
            {
                paneWindow.Close();
            }
        }
        finally
        {
            InlineText.OpenUrl = hook;
        }
    }, height: 900);
}
