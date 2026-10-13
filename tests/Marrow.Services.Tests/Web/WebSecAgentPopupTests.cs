using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces security review M2 (E:/Droppy/work/scratch/lead/web-sec-findings.md; DESIGN.md 5.10 the cross-origin rule,
// "a link click ... needs approval", the URL policy; 8.1 new windows; ED-NEW-web-sec-agent-4). A model's click is a trusted gesture,
// so a target=_blank link or window.open in its handler would open a new tab with none of the engine's checks. While the model drives
// a tab (and for the 5 s a click's gesture lasts), the web tools stop such a pop-up and tell the model to ask through web_open, which
// runs the deny list and the cross-origin card. The user's own pop-ups are never judged.
[Trait("Decision", "ED-NEW-web-w11-1")]
public sealed class WebSecAgentPopupTests
{
    private static string Ref(string reference) => "{\"tab\":\"t1\",\"ref\":\"" + reference + "\"}";

    private static async Task<FakeAgentTab> Invite(WebAgentRig rig)
    {
        var tab = await rig.InviteTab();
        Assert.Null((await rig.Call(WebToolSchemas.Read, "{\"tab\":\"t1\"}")).Error);
        return tab;
    }

    [Fact]
    public async Task APopUpTheModelsClickOpensIsStoppedAndWebOpenAsksForIt()
    {
        using var rig = new WebAgentRig();
        var tab = await Invite(rig);
        WebPopupOpening? opened = null;
        tab.Page.OnCall = (method, p) =>
        {
            if (method == "Input.dispatchMouseEvent" && (string?)p["type"] == "mouseReleased") opened = tab.RaisePopup("https://b.test/docs?q=42%20apples");
        };

        var click = await rig.Call(WebToolSchemas.Click, Ref("e5"));
        tab.Page.OnCall = null;
        var open = await rig.Call(WebToolSchemas.Open, "{\"url\":\"https://b.test/docs?q=42%20apples\"}");

        Assert.True(opened!.Cancel);
        Assert.Equal(WebErrorCodes.Blocked, click.Error);
        Assert.Contains("web_open", click.Text, StringComparison.Ordinal);
        Assert.Contains("https://b.test/docs?q=42%20apples", click.Text, StringComparison.Ordinal);
        var ask = Assert.Single(rig.Asker.Of<WebNavigateAsk>());
        Assert.Equal("https://b.test/docs?q=42%20apples", ask.Url);
        Assert.Null(open.Error);
    }

    [Fact]
    public async Task APopUpADeniedAddressAsksForIsStoppedWithTheReason()
    {
        using var rig = new WebAgentRig();
        var tab = await Invite(rig);
        WebPopupOpening? opened = null;
        tab.Page.OnCall = (method, p) =>
        {
            if (method == "Input.dispatchMouseEvent" && (string?)p["type"] == "mouseReleased") opened = tab.RaisePopup("https://github.com/settings/tokens");
        };

        var click = await rig.Call(WebToolSchemas.Click, Ref("e9"));

        Assert.True(opened!.Cancel);
        Assert.Equal(WebErrorCodes.Blocked, click.Error);
        Assert.Contains(WebAgentUrls.DeniedReason("https://github.com/settings/tokens")!, click.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APopUpWhileTheClicksGestureLastsIsStoppedToo()
    {
        using var rig = new WebAgentRig();
        var tab = await Invite(rig);
        await rig.Call(WebToolSchemas.Click, Ref("e9"));

        rig.Clock.Advance(4_000);
        var late = tab.RaisePopup("https://b.test/later");

        Assert.True(late.Cancel);
    }

    [Fact]
    public async Task TheUsersOwnPopUpsAreNeverJudged()
    {
        using var rig = new WebAgentRig();
        var tab = await Invite(rig);
        var before = tab.RaisePopup("https://b.test/before"); // read, but nothing driven yet
        await rig.Call(WebToolSchemas.Click, Ref("e9"));
        tab.RaiseUserInput(); // the user takes the tab over
        var takenOver = tab.RaisePopup("https://b.test/mine");
        rig.Hub.EndTurn(WebAgentRig.Thread);
        rig.Clock.Advance(10_000);
        var after = tab.RaisePopup("https://b.test/after");

        Assert.False(before.Cancel);
        Assert.False(takenOver.Cancel);
        Assert.False(after.Cancel);
    }
}
