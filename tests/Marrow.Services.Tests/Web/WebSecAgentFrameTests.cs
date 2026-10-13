using System.Text.RegularExpressions;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces security review H1 (E:/Droppy/work/scratch/lead/web-sec-findings.md; DESIGN.md 5.10 "iframes are reached with
// Target.setAutoAttach {flatten: true} and per-session calls", the hard denies and the guarded-action card; ED-NEW-web-sec-agent-1).
// A frame of another site runs in its own process, so the page around it sees only an <iframe>. A click on a point inside the frame
// and keys into a field inside it are judged on the element inside, read through the frame's own session; a frame nth cannot reach
// takes no click and no key that needs judging.
[Trait("Decision", "ED-NEW-web-w11-1")]
public sealed partial class WebSecAgentFrameTests
{
    private static string Tab(string extra = "") => "{\"tab\":\"t1\"" + extra + "}";

    // The checkout page: Continue, and pay.test's card frame at (400, 100) with Pay now, a password field and Next inside.
    private static AgentPage Checkout(bool reachable = true)
    {
        var page = new AgentPage { Text = "Checkout", Url = "https://a.test/checkout" };
        page.Nodes.AddRange(
        [
            new AgentNode { Backend = 2, Role = "button", Name = "Continue", Box = (100, 100, 120, 40), Facts = AgentPage.FactsOf("button", "Continue", frameUrl: "https://a.test/checkout") },
            new AgentNode { Backend = 20, Role = "Iframe", Name = "", Box = (400, 100, 300, 200), FrameId = "F1", Facts = AgentPage.FactsOf("iframe", "", frameUrl: "https://pay.test/card") },
        ]);
        var frame = new AgentPage { Text = "Card", Url = "https://pay.test/card", FrameTarget = "F1", Viewport = (300, 200) };
        frame.Nodes.AddRange(
        [
            new AgentNode { Backend = 2, Role = "button", Name = "Pay now", Box = (10, 10, 100, 40), Facts = AgentPage.FactsOf("button", "Pay now", frameUrl: "https://pay.test/card") },
            new AgentNode { Backend = 3, Role = "textbox", Name = "Password", Box = (10, 60, 200, 30), Facts = AgentPage.FactsOf("input", "", "password", editable: true, frameUrl: "https://pay.test/card") },
            new AgentNode { Backend = 4, Role = "button", Name = "Next", Box = (10, 100, 100, 40), Facts = AgentPage.FactsOf("button", "Next", frameUrl: "https://pay.test/card") },
        ]);
        if (reachable) page.Sessions["S1"] = frame;
        else page.Sessions["S0"] = new AgentPage { Url = "https://other.test/", FrameTarget = "F0" };
        return page;
    }

    private static async Task<FakeAgentTab> Open(WebAgentRig rig, AgentPage page)
    {
        var tab = rig.Host.Add("https://a.test/checkout", page: page);
        await rig.Call(WebToolSchemas.Tabs);
        Assert.Null((await rig.Call(WebToolSchemas.Read, Tab())).Error);
        return tab;
    }

    [GeneratedRegex("(e[0-9]+) button \"Next\"")]
    private static partial Regex NextRef();

    [Fact]
    public async Task APointInsideAnotherSitesFrameIsJudgedOnTheButtonThere()
    {
        using var rig = new WebAgentRig();
        var tab = await Open(rig, Checkout());

        var result = await rig.Call(WebToolSchemas.Click, Tab(",\"x\":450,\"y\":130"));

        Assert.Equal(WebErrorCodes.Denied, result.Error);
        Assert.Equal(WebAgentPolicy.PaymentRefusal, result.Text);
        Assert.Empty(tab.Page.Mouse("mousePressed"));
    }

    [Fact]
    public async Task KeysIntoAPasswordFieldInsideAnotherSitesFrameAreRefused()
    {
        using var rig = new WebAgentRig();
        var page = Checkout();
        var tab = await Open(rig, page);
        page.Focused = 20;
        page.Sessions["S1"].Focused = 3;

        var result = await rig.Call(WebToolSchemas.Key, Tab(",\"keys\":\"a b c\""));

        Assert.Equal(WebErrorCodes.Denied, result.Error);
        Assert.Equal(WebAgentPolicy.PasswordRefusal, result.Text);
        Assert.Equal(0, tab.Page.Count("Input.dispatchKeyEvent"));
    }

    [Fact]
    public async Task AFrameNthCannotReachTakesNoClickAndNoTyping()
    {
        using var rig = new WebAgentRig();
        var page = Checkout(reachable: false);
        var tab = await Open(rig, page);
        page.Focused = 20;

        var click = await rig.Call(WebToolSchemas.Click, Tab(",\"x\":450,\"y\":130"));
        var keys = await rig.Call(WebToolSchemas.Key, Tab(",\"keys\":\"x\""));
        var moves = await rig.Call(WebToolSchemas.Key, Tab(",\"keys\":\"tab\""));

        Assert.Equal(WebErrorCodes.Denied, click.Error);
        Assert.Equal(WebErrorCodes.Denied, keys.Error);
        Assert.Contains("frame", keys.Text, StringComparison.Ordinal);
        Assert.Empty(tab.Page.Mouse("mousePressed"));
        Assert.Null(moves.Error);
        Assert.Equal(2, tab.Page.Count("Input.dispatchKeyEvent"));
    }

    [Fact]
    public async Task ARefInsideAFrameIsClickedAsItselfNotAsACover()
    {
        using var rig = new WebAgentRig();
        var tab = await Open(rig, Checkout());
        var found = await rig.Call(WebToolSchemas.Find, Tab(",\"query\":\"Next\""));
        var reference = NextRef().Match(found.Text).Groups[1].Value;
        Assert.NotEmpty(reference);

        var result = await rig.Call(WebToolSchemas.Click, Tab(",\"ref\":\"" + reference + "\""));

        Assert.Null(result.Error);
        Assert.DoesNotContain("covers", result.Text, StringComparison.Ordinal);
        var press = Assert.Single(tab.Page.Mouse("mousePressed"));
        Assert.Equal(460, press["x"]!.GetValue<double>());
        Assert.Equal(220, press["y"]!.GetValue<double>());
    }
}
