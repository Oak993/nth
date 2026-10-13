using System.Globalization;
using System.Text.RegularExpressions;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces security review H1, second pass (the adversarial review of ED-NEW-web-sec-agent-1; DESIGN.md 5.10 "Coordinates
// come from DOM.getContentQuads in CSS pixels. iframes are reached with Target.setAutoAttach {flatten: true} and per-session calls", the
// hard denies; ED-NEW-web-sec-agent-7). A page can scale, turn, flip or zoom another site's frame (a CSS transform or zoom, often on an
// invisible cover). Chromium maps a click through that; moving the point by the frame's top-left corner does not, so nth would judge one
// element and press another. A frame whose place is more than a move (its box not upright, or not as large as the frame's own viewport)
// is sealed: no click lands in it, by point or by ref. A frame that is only moved takes clicks as before.
[Trait("Decision", "ED-NEW-web-w11-1")]
public sealed partial class WebSecAgentFrameTransformTests
{
    private const string Pay = "https://pay.test/card";

    private static string Tab(string extra = "") => "{\"tab\":\"t1\"" + extra + "}";

    // A shop page holding pay.test's 300 x 200 card frame wherever quad puts its box (corners top-left, top-right, bottom-right,
    // bottom-left). Inside the frame: Help at its top-left corner and Pay now just past it.
    private static AgentPage Shop(double[] quad)
    {
        double left = Math.Min(Math.Min(quad[0], quad[2]), Math.Min(quad[4], quad[6])), right = Math.Max(Math.Max(quad[0], quad[2]), Math.Max(quad[4], quad[6]));
        double top = Math.Min(Math.Min(quad[1], quad[3]), Math.Min(quad[5], quad[7])), bottom = Math.Max(Math.Max(quad[1], quad[3]), Math.Max(quad[5], quad[7]));
        var page = new AgentPage { Text = "Shop", Url = "https://a.test/shop" };
        page.Nodes.Add(new AgentNode { Backend = 20, Role = "Iframe", Name = "", Box = (left, top, right - left, bottom - top), Quad = quad, FrameId = "F1", Facts = AgentPage.FactsOf("iframe", "", frameUrl: Pay) });
        var frame = new AgentPage { Text = "Card", Url = Pay, FrameTarget = "F1", Viewport = (300, 200) };
        frame.Nodes.AddRange(
        [
            new AgentNode { Backend = 2, Role = "button", Name = "Help", Box = (0, 0, 30, 30), Facts = AgentPage.FactsOf("button", "Help", frameUrl: Pay) },
            new AgentNode { Backend = 3, Role = "button", Name = "Pay now", Box = (32, 32, 120, 40), Facts = AgentPage.FactsOf("button", "Pay now", frameUrl: Pay) },
        ]);
        page.Sessions["S1"] = frame;
        return page;
    }

    private static double[] Quad(string corners) => [.. corners.Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture))];

    private static async Task<FakeAgentTab> Open(WebAgentRig rig, AgentPage page)
    {
        var tab = rig.Host.Add("https://a.test/shop", page: page);
        await rig.Call(WebToolSchemas.Tabs);
        Assert.Null((await rig.Call(WebToolSchemas.Read, Tab())).Error);
        return tab;
    }

    [GeneratedRegex("(e[0-9]+) button \"Help\"")]
    private static partial Regex HelpRef();

    // Each point is 20 px right and down of the frame's box's top-left corner: Help there if the frame were only moved; the real
    // click lands elsewhere (on Pay now when the frame is shown at half its size).
    [Theory]
    [InlineData("475,150,625,150,625,250,475,250", 495, 170)] // scale(.5)
    [InlineData("400,100,1000,100,1000,500,400,500", 420, 120)] // zoom: 2
    [InlineData("700,300,400,300,400,100,700,100", 420, 120)] // rotate(180deg)
    [InlineData("650,50,650,350,450,350,450,50", 470, 70)] // rotate(90deg)
    [InlineData("700,100,400,100,400,300,700,300", 420, 120)] // scaleX(-1)
    [InlineData("410,100,710,100,700,300,400,300", 420, 120)] // skewX
    public async Task APointInAFrameThePageScaledTurnedOrZoomedIsNeverClicked(string corners, double x, double y)
    {
        using var rig = new WebAgentRig();
        var tab = await Open(rig, Shop(Quad(corners)));

        var result = await rig.Call(WebToolSchemas.Click, Tab(",\"x\":" + x.ToString(CultureInfo.InvariantCulture) + ",\"y\":" + y.ToString(CultureInfo.InvariantCulture)));

        Assert.Equal(WebErrorCodes.Denied, result.Error);
        Assert.Contains("frame", result.Text, StringComparison.Ordinal);
        Assert.Empty(tab.Page.Mouse("mousePressed"));
    }

    [Fact]
    public async Task ARefInAFrameThePageScaledIsNeverClicked()
    {
        using var rig = new WebAgentRig();
        var tab = await Open(rig, Shop(Quad("475,150,625,150,625,250,475,250")));
        var found = await rig.Call(WebToolSchemas.Find, Tab(",\"query\":\"Help\""));
        var reference = HelpRef().Match(found.Text).Groups[1].Value;
        Assert.NotEmpty(reference);

        var result = await rig.Call(WebToolSchemas.Click, Tab(",\"ref\":\"" + reference + "\""));

        Assert.Equal(WebErrorCodes.Denied, result.Error);
        Assert.Contains("frame", result.Text, StringComparison.Ordinal);
        Assert.Empty(tab.Page.Mouse("mousePressed"));
    }

    [Fact]
    public async Task AFrameThePageOnlyMovedTakesClicksAsBefore()
    {
        using var rig = new WebAgentRig();
        var tab = await Open(rig, Shop(Quad("400,100,700,100,700,300,400,300")));

        var result = await rig.Call(WebToolSchemas.Click, Tab(",\"x\":420,\"y\":120"));

        Assert.Null(result.Error);
        var press = Assert.Single(tab.Page.Mouse("mousePressed"));
        Assert.Equal(420, press["x"]!.GetValue<double>());
    }
}
