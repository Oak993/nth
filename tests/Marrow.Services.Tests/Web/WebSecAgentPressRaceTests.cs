using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces security review M1, second pass (the adversarial review of ED-NEW-web-sec-agent-3; DESIGN.md 5.10 "the element
// under the click point is judged too", the guarded-action card, the hard denies; ED-NEW-web-sec-agent-8). The point is judged before
// the silver pointer's 500 ms glide, and a page can swap what is under it during the glide or on the mouse move itself (a timer, a
// mouseover handler). Just before every press, after the mouse has moved, the point is hit-tested again: the press goes ahead only on
// the same node, judged no stricter than before, and after a card with the content the card showed. A hover that only restyles the
// same node still clicks.
[Trait("Decision", "ED-NEW-web-w11-1")]
public sealed class WebSecAgentPressRaceTests
{
    private static string Ref(string reference, string extra = "") => "{\"tab\":\"t1\",\"ref\":\"" + reference + "\"" + extra + "}";

    // The invite page, read: e3 Send invite (a card), e6 Buy now (denied), e8 Remember me (a checkbox), e9 Next (no card).
    private static async Task<FakeAgentTab> Invite(WebAgentRig rig)
    {
        var tab = await rig.InviteTab();
        Assert.Null((await rig.Call(WebToolSchemas.Read, "{\"tab\":\"t1\"}")).Error);
        return tab;
    }

    // When the mouse moves to the point, the page puts another node there.
    private static void SwapOnMove(FakeAgentTab tab, long to) =>
        tab.Page.OnCall = (method, p) =>
        {
            if (method == "Input.dispatchMouseEvent" && (string?)p["type"] == "mouseMoved") tab.Page.HitOverride = to;
        };

    [Theory]
    [InlineData(7L)] // Buy now: denied
    [InlineData(4L)] // Send invite: asks
    public async Task ANodeSwappedUnderThePointAsTheMouseMovesIsNeverPressed(long swapped)
    {
        using var rig = new WebAgentRig();
        var tab = await Invite(rig);
        SwapOnMove(tab, swapped);

        var result = await rig.Call(WebToolSchemas.Click, Ref("e9"));

        Assert.Equal(WebErrorCodes.StaleRef, result.Error);
        Assert.Empty(tab.Page.Mouse("mousePressed"));
        Assert.Empty(rig.Asker.Of<WebGuardAsk>());
    }

    [Fact]
    public async Task AfterAConfirmANodeSwappedAsTheMouseMovesIsNeverPressed()
    {
        using var rig = new WebAgentRig();
        var tab = await Invite(rig);
        SwapOnMove(tab, 10);

        var result = await rig.Call(WebToolSchemas.Click, Ref("e3"));

        Assert.Single(rig.Asker.Of<WebGuardAsk>());
        Assert.Equal(WebErrorCodes.StaleRef, result.Error);
        Assert.Empty(tab.Page.Mouse("mousePressed"));
    }

    [Fact]
    public async Task ACheckboxWhoseNodeIsSwappedAsTheMouseMovesIsNeverPressed()
    {
        using var rig = new WebAgentRig();
        var tab = await Invite(rig);
        SwapOnMove(tab, 7);

        var result = await rig.Call(WebToolSchemas.Select, Ref("e8", ",\"value\":\"true\""));

        Assert.Equal(WebErrorCodes.StaleRef, result.Error);
        Assert.Empty(tab.Page.Mouse("mousePressed"));
    }

    [Fact]
    public async Task AHoverThatOnlyRestylesTheSameNodeStillClicks()
    {
        using var rig = new WebAgentRig();
        var tab = await Invite(rig);
        var next = tab.Page.Nodes.Single(n => n.Backend == 10);
        tab.Page.OnCall = (method, p) =>
        {
            if (method == "Input.dispatchMouseEvent" && (string?)p["type"] == "mouseMoved") next.Facts["className"] = "next is-hovered";
        };

        var result = await rig.Call(WebToolSchemas.Click, Ref("e9"));

        Assert.Null(result.Error);
        Assert.Single(tab.Page.Mouse("mousePressed"));
    }
}
