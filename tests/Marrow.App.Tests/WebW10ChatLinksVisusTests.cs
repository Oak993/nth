using Marrow.App.Port.Web;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Browser;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Web;

namespace Marrow.App.Tests;

// PORT-ONLY: web surfaces W10, a web link opened in Visus (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.11; ED-NEW-web-w10-2). The link becomes a tab in the Visus session of the chat's own workspace and the Visus pane opens; a dev host opens as a
// preview tab; the same page clicked again selects its tab instead of piling up copies; a side chat's link opens in the main thread's workspace, where its pane is; an address Visus does not load goes to the default browser.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w10-2")]
public sealed class WebW10ChatLinksVisusTests(HeadlessSession headless)
{
    private static SwiftURL Url(string text) => SwiftURL.Create(text)!;

    [Fact]
    public void A_web_link_becomes_a_Visus_tab_in_the_chats_workspace_and_the_pane_opens() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        using var chat = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        chat.H.Main.OnMain(() =>
        {
            Assert.False(chat.Runtime.Workspace.IsOpen(PaneKind.Browser));
            ChatLinks.OpenVisus(chat.Runtime, Url("https://docs.example.org/a"));
            var tab = Assert.Single(VisusHost.SessionFor(chat.Runtime).Tabs);
            Assert.Equal((VisusTabKind.Browse, "https://docs.example.org/a"), (tab.Kind, tab.Url));
            Assert.True(chat.Runtime.Workspace.IsOpen(PaneKind.Browser));
        });
    });

    [Fact]
    public void A_dev_host_opens_as_a_preview_tab_and_the_same_page_again_selects_its_tab() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        using var chat = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        chat.H.Main.OnMain(() =>
        {
            ChatLinks.OpenVisus(chat.Runtime, Url("http://localhost:5173/app"));
            ChatLinks.OpenVisus(chat.Runtime, Url("https://docs.example.org/a"));
            var session = VisusHost.SessionFor(chat.Runtime);
            Assert.Equal([VisusTabKind.Preview, VisusTabKind.Browse], session.Tabs.Select(tab => tab.Kind));
            Assert.Equal("https://docs.example.org/a", session.Selected!.Url);
            ChatLinks.OpenVisus(chat.Runtime, Url("http://localhost:5173/app"));
            Assert.Equal(2, session.Tabs.Count);
            Assert.Equal("http://localhost:5173/app", session.Selected!.Url);
            ChatLinks.OpenVisus(chat.Runtime, Url("https://docs.example.org/b"));
            Assert.Equal(3, session.Tabs.Count);
        });
    });

    [Fact]
    public void A_private_tab_showing_the_same_address_is_never_taken_for_the_chats_link() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        using var chat = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        chat.H.Main.OnMain(() =>
        {
            var session = VisusHost.SessionFor(chat.Runtime);
            var secret = session.NewTab(VisusTabKind.Private, "https://docs.example.org/a");
            ChatLinks.OpenVisus(chat.Runtime, Url("https://docs.example.org/a"));
            Assert.Equal(2, session.Tabs.Count);
            Assert.Equal(VisusTabKind.Browse, session.Selected!.Kind);
            Assert.NotSame(secret, session.Selected);
        });
    });

    [Fact]
    public void An_address_Visus_does_not_load_goes_to_the_default_browser_instead() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        using var chat = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        var external = new List<string>();
        var before = ChatLinks.OpenExternal;
        ChatLinks.OpenExternal = url => external.Add(url.AbsoluteString);
        try
        {
            chat.H.Main.OnMain(() =>
            {
                ChatLinks.OpenVisus(chat.Runtime, Url("ftp://files.example.org/readme"));
                Assert.Empty(VisusHost.SessionFor(chat.Runtime).Tabs);
            });
            Assert.Equal(["ftp://files.example.org/readme"], external);
        }
        finally
        {
            ChatLinks.OpenExternal = before;
        }
    });

    [Fact]
    public void A_side_chats_link_opens_in_the_main_threads_workspace() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        using var rig = new SideChatRig();
        rig.H.Main.OnMain(() =>
        {
            rig.MakeSide();
            var side = rig.SideRuntime;
            var main = rig.Main.Runtime;
            Assert.NotSame(main, side);
            ChatLinks.OpenVisus(side, Url("https://docs.example.org/a"));
            Assert.Single(VisusHost.SessionFor(main).Tabs);
            Assert.Empty(VisusHost.SessionFor(side).Tabs);
            Assert.True(main.Workspace.IsOpen(PaneKind.Browser));
        });
    });
}
