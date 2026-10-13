using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Browser;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.App.Tests.Views.Browser;

// PORT-ONLY: web surfaces W10 (E:/Droppy/work/design/web-surfaces/DESIGN.md 8.1 "NewWindowRequested is always handled: user-initiated opens become a new Visus tab (or the default browser, per the link setting); others are blocked with a quiet 'Pop-up blocked'
// chip"; ED-NEW-web-w10-2). With "Open links from chats in" set to Default browser a normal tab's page sends the windows the reader opened to the default browser; with Ask or Visus they stay Visus tabs; a private, preview or agent
// tab keeps its own windows; a window the page opened by itself is still blocked.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w10-2")]
public sealed class WebW10PageWindowTests(HeadlessSession headless)
{
    [Fact]
    public void With_the_default_browser_chosen_a_normal_tabs_user_window_goes_to_it_and_opens_no_tab() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        VisusHost.PageWindowsInDefaultBrowser = () => true;
        var tab = visus.Page("https://a.example.org/");
        var tabs = visus.Session.Tabs.Count;
        var request = VisusRig.Live(tab).RaiseNewWindow("https://b.example.org/next", userInitiated: true);
        Assert.True(request.Decision.IsCompleted, "the window was not answered at once");
        Assert.Equal(WebNewWindowDecision.HandledByNth, request.Decision.Result);
        Assert.Equal(["https://b.example.org/next"], visus.OpenedInBrowser);
        Assert.Equal(tabs, visus.Session.Tabs.Count);
    });

    [Fact]
    public void With_Ask_or_Visus_chosen_the_window_is_a_Visus_tab_beside_the_page() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var tab = visus.Page("https://a.example.org/");
        var tabs = visus.Session.Tabs.Count;
        VisusRig.Live(tab).RaiseNewWindow("https://b.example.org/next", userInitiated: true);
        Assert.Empty(visus.OpenedInBrowser);
        Assert.Equal(tabs + 1, visus.Session.Tabs.Count);
        Assert.Equal("https://b.example.org/next", visus.Session.Selected!.Url);
    });

    [Theory]
    [InlineData(VisusTabKind.Private)]
    [InlineData(VisusTabKind.Preview)]
    [InlineData(VisusTabKind.Agent)]
    public void Other_kinds_of_tab_keep_their_own_windows_whatever_the_choice(VisusTabKind kind) => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        VisusHost.PageWindowsInDefaultBrowser = () => true;
        var url = kind == VisusTabKind.Preview ? "http://localhost:5173/" : "https://a.example.org/";
        var tab = visus.Page(url, kind);
        var tabs = visus.Session.Tabs.Count;
        var target = kind == VisusTabKind.Preview ? "http://localhost:5173/next" : "https://b.example.org/next";
        VisusRig.Live(tab).RaiseNewWindow(target, userInitiated: true);
        Assert.Empty(visus.OpenedInBrowser);
        Assert.Equal(tabs + 1, visus.Session.Tabs.Count);
    });

    [Fact]
    public void A_window_the_page_opened_by_itself_is_still_blocked_with_the_chip_and_never_goes_to_the_browser() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        VisusHost.PageWindowsInDefaultBrowser = () => true;
        var tab = visus.Page("https://a.example.org/");
        var request = VisusRig.Live(tab).RaiseNewWindow("https://ads.example.org/", userInitiated: false);
        Assert.True(request.Decision.IsCompleted, "the window was not answered at once");
        Assert.Equal(WebNewWindowDecision.Block, request.Decision.Result);
        Assert.Empty(visus.OpenedInBrowser);
        Assert.Equal("https://ads.example.org/", tab.BlockedPopup);
    });

    [Fact]
    public void An_address_the_default_browser_would_not_take_stays_with_Visus_rules_and_a_refused_hand_over_opens_the_tab() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        VisusHost.PageWindowsInDefaultBrowser = () => true;
        VisusHost.OpenInDefaultBrowser = _ => false; // the shell refused
        var tab = visus.Page("https://a.example.org/");
        var tabs = visus.Session.Tabs.Count;
        VisusRig.Live(tab).RaiseNewWindow("https://b.example.org/next", userInitiated: true);
        Assert.Equal(tabs + 1, visus.Session.Tabs.Count); // never lost: it opens in Visus instead
        var blocked = VisusRig.Live(tab).RaiseNewWindow("file:///C:/x", userInitiated: true);
        Assert.True(blocked.Decision.IsCompleted, "the window was not answered at once");
        Assert.Equal(WebNewWindowDecision.Block, blocked.Decision.Result);
    });
}
