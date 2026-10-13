using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Browser;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.App.Tests.Views.Browser;

// PORT-ONLY: web surfaces security review M2 (E:/Droppy/work/scratch/lead/web-sec-findings.md; DESIGN.md 5.10 the cross-origin rule,
// 8.1 "NewWindowRequested is always handled"; ED-NEW-web-sec-agent-4). Before a tab's page opens a pop-up as a new Visus tab, the web
// tools watching the tab (through its agent adapter) look at it: one they stop opens nothing and waits on the quiet chip for the user;
// one they let through opens as before.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w11-1")]
public sealed class VisusModelPopupTests(HeadlessSession headless)
{
    [Fact]
    public void A_pop_up_the_web_tools_stop_opens_no_tab_and_waits_on_the_chip() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        var tab = rig.Page("https://a.example/", VisusTabKind.Agent);
        var seen = new List<string>();
        VisusAgentHost.Adapter(rig.Session, tab).PopupOpening += e =>
        {
            seen.Add(e.Url);
            e.Cancel = true;
        };

        var request = VisusRig.Live(tab).RaiseNewWindow("https://b.example/?q=secret", userInitiated: true);

        Assert.Equal(WebNewWindowDecision.Block, request.Decision.Result);
        Assert.Equal(["https://b.example/?q=secret"], seen);
        Assert.Same(tab, Assert.Single(rig.Session.Tabs));
        Assert.Equal("https://b.example/?q=secret", tab.BlockedPopup);
    });

    [Fact]
    public void A_pop_up_the_web_tools_let_through_opens_a_tab_as_before() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        var tab = rig.Page("https://a.example/");
        var seen = new List<string>();
        void Watch(WebPopupOpening e) => seen.Add(e.Url);
        var adapter = VisusAgentHost.Adapter(rig.Session, tab);
        adapter.PopupOpening += Watch;

        var request = VisusRig.Live(tab).RaiseNewWindow("https://b.example/", userInitiated: true);
        adapter.PopupOpening -= Watch;
        VisusRig.Live(tab).RaiseNewWindow("https://c.example/", userInitiated: true);

        Assert.Equal(WebNewWindowDecision.HandledByNth, request.Decision.Result);
        Assert.Equal(["https://b.example/"], seen);
        Assert.Equal(3, rig.Session.Tabs.Count);
    });
}
