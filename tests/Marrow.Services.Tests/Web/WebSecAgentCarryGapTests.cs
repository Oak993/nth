using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces security review M3, second pass (the adversarial review of ED-NEW-web-sec-agent-5; DESIGN.md 5.10 "page text
// reaches the model, and a page can tell it to ... 'type this'", "page text, titles and CDP output reach the model marked as untrusted";
// ED-NEW-web-sec-agent-9). Text read on one site asks before it is typed into another site even when it goes in pieces (each call
// short, the pieces adding up on that site), dressed up (full-width digits, zero-width marks, a separator between characters), as a
// short code, as a page's title or address (web_tabs, web_open, web_navigate, web_read), or into another site's frame inside the page
// it was read on; and a frame's text belongs to the frame's site.
[Trait("Decision", "ED-NEW-web-w11-1")]
public sealed class WebSecAgentCarryGapTests
{
    private const string Mail = "Inbox. Your sign-in code is 483920 and your PIN is 4831. Reply to lee@example.com about key sk-live-7f3a9c21d. "
        + "Posted in 2026.";

    private static string Type(string tab, string reference, string text) => "{\"tab\":\"" + tab + "\",\"ref\":\"" + reference + "\",\"text\":\"" + text + "\"}";

    private static string Keys(string tab, string keys) => "{\"tab\":\"" + tab + "\",\"keys\":\"" + keys + "\"}";

    // t1 shows mail.test (read as text), t2 a form on forms.test (read for its refs: e1 is its Email field).
    private static async Task<(FakeAgentTab Mail, FakeAgentTab Form)> TwoSites(WebAgentRig rig, string mailUrl = "https://mail.test/inbox")
    {
        var inbox = AgentPage.Invite();
        inbox.Text = Mail;
        var mail = rig.Host.Add(mailUrl, page: inbox);
        var form = rig.Host.Add("https://forms.test/new");
        await rig.Call(WebToolSchemas.Tabs);
        Assert.Null((await rig.Call(WebToolSchemas.Read, "{\"tab\":\"t1\",\"mode\":\"text\"}")).Error);
        Assert.Null((await rig.Call(WebToolSchemas.Read, "{\"tab\":\"t2\"}")).Error);
        return (mail, form);
    }

    private static void AssertAskedFrom(WebAgentRig rig, string site, WebToolResult result)
    {
        Assert.Equal("Type text from " + site + "?", Assert.Single(rig.Asker.Of<WebGuardAsk>()).Title);
        Assert.Equal(WebErrorCodes.Cancelled, result.Error);
    }

    [Fact]
    public async Task ACodeTypedInPiecesAsksWhenThePiecesAddUp()
    {
        using var rig = new WebAgentRig();
        rig.Asker.Guard = _ => Task.FromResult(false);
        var (_, form) = await TwoSites(rig);

        var first = await rig.Call(WebToolSchemas.Type, Type("t2", "e1", "483"));
        var second = await rig.Call(WebToolSchemas.Type, Type("t2", "e1", "920"));

        Assert.Null(first.Error);
        AssertAskedFrom(rig, "mail.test", second);
        Assert.Equal(1, form.Page.Count("Input.insertText"));
    }

    [Fact]
    public async Task LettersThroughWebKeyInPiecesAskWhenThePiecesAddUp()
    {
        using var rig = new WebAgentRig();
        rig.Asker.Guard = _ => Task.FromResult(false);
        var (_, form) = await TwoSites(rig);
        form.Page.Focused = 2;

        var first = await rig.Call(WebToolSchemas.Key, Keys("t2", "4 8 3"));
        var second = await rig.Call(WebToolSchemas.Key, Keys("t2", "9 2 0"));

        Assert.Null(first.Error);
        AssertAskedFrom(rig, "mail.test", second);
        Assert.Equal(6, form.Page.Count("Input.dispatchKeyEvent"));
    }

    [Theory]
    [InlineData("４８３９２０")] // full-width digits
    [InlineData("4​8​3​9​2​0")] // zero-width spaces
    [InlineData("4-8-3-9-2-0")]
    [InlineData("s k - l i v e - 7 f 3 a 9 c 2 1 d")]
    [InlineData("l e e @ e x a m p l e . c o m")]
    [InlineData("4831")] // a short code
    public async Task ReadTextDressedUpOrShortAsksBeforeItIsTyped(string text)
    {
        using var rig = new WebAgentRig();
        rig.Asker.Guard = _ => Task.FromResult(false);
        var (_, form) = await TwoSites(rig);

        var result = await rig.Call(WebToolSchemas.Type, Type("t2", "e1", text));

        AssertAskedFrom(rig, "mail.test", result);
        Assert.Equal(0, form.Page.Count("Input.insertText"));
    }

    [Fact]
    public async Task AYearTypesWithoutACard()
    {
        using var rig = new WebAgentRig();
        var (_, form) = await TwoSites(rig);

        var result = await rig.Call(WebToolSchemas.Type, Type("t2", "e1", "2026"));

        Assert.Null(result.Error);
        Assert.Empty(rig.Asker.Of<WebGuardAsk>());
        Assert.Equal(1, form.Page.Count("Input.insertText"));
    }

    [Fact]
    public async Task ATitleListedByWebTabsAsksBeforeItIsTyped()
    {
        using var rig = new WebAgentRig();
        rig.Asker.Guard = _ => Task.FromResult(false);
        var (mail, form) = await TwoSites(rig);
        mail.Title = "Your code 772615 - Mail";
        await rig.Call(WebToolSchemas.Tabs);

        var result = await rig.Call(WebToolSchemas.Type, Type("t2", "e1", "772615"));

        AssertAskedFrom(rig, "mail.test", result);
        Assert.Equal(0, form.Page.Count("Input.insertText"));
    }

    [Fact]
    public async Task AnAddressListedByWebTabsAsksBeforeItIsTyped()
    {
        using var rig = new WebAgentRig();
        rig.Asker.Guard = _ => Task.FromResult(false);
        var (_, form) = await TwoSites(rig, "https://mail.test/magic?token=Qx7Lm2Vp9Zk4");

        var result = await rig.Call(WebToolSchemas.Type, Type("t2", "e1", "Qx7Lm2Vp9Zk4"));

        AssertAskedFrom(rig, "mail.test", result);
        Assert.Equal(0, form.Page.Count("Input.insertText"));
    }

    [Fact]
    public async Task ATitleWebReadShowsAsksBeforeItIsTyped()
    {
        using var rig = new WebAgentRig();
        rig.Asker.Guard = _ => Task.FromResult(false);
        var (mail, _) = await TwoSites(rig);
        mail.Title = "Your code 581406 - Mail";
        Assert.Null((await rig.Call(WebToolSchemas.Read, "{\"tab\":\"t1\",\"mode\":\"text\"}")).Error);

        var result = await rig.Call(WebToolSchemas.Type, Type("t2", "e1", "581406"));

        AssertAskedFrom(rig, "mail.test", result);
    }

    [Fact]
    public async Task ATitleWebNavigateShowsAsksBeforeItIsTyped()
    {
        using var rig = new WebAgentRig();
        rig.Asker.Guard = _ => Task.FromResult(false);
        var (mail, _) = await TwoSites(rig);
        mail.Title = "Your code 390177 - Mail";
        Assert.Null((await rig.Call(WebToolSchemas.Navigate, "{\"tab\":\"t1\",\"action\":\"reload\"}")).Error);

        var result = await rig.Call(WebToolSchemas.Type, Type("t2", "e1", "390177"));

        AssertAskedFrom(rig, "mail.test", result);
    }

    [Fact]
    public async Task ATitleWebOpenShowsAsksBeforeItIsTyped()
    {
        using var rig = new WebAgentRig();
        rig.Asker.Guard = _ => Task.FromResult(false);
        var (mail, _) = await TwoSites(rig);
        mail.Title = "Your code 662019 - Mail";
        Assert.Null((await rig.Call(WebToolSchemas.Open, "{\"tab\":\"t1\",\"url\":\"https://mail.test/sent\"}")).Error);

        var result = await rig.Call(WebToolSchemas.Type, Type("t2", "e1", "662019"));

        AssertAskedFrom(rig, "mail.test", result);
    }

    // crm.test's page with vendor.test's chat frame: a Message field and a ticket line inside the frame, a Note field on the page.
    private static AgentPage Crm()
    {
        var page = new AgentPage { Text = "Customer Ada, account 99417723.", Url = "https://crm.test/customer" };
        page.Nodes.AddRange(
        [
            new AgentNode { Backend = 2, Role = "textbox", Name = "Note", Box = (100, 100, 300, 30), Facts = AgentPage.FactsOf("input", "", "text", editable: true, frameUrl: "https://crm.test/customer") },
            new AgentNode { Backend = 20, Role = "Iframe", Name = "", Box = (500, 100, 300, 200), FrameId = "F1", Facts = AgentPage.FactsOf("iframe", "", frameUrl: "https://vendor.test/chat") },
        ]);
        var frame = new AgentPage { Text = "Chat", Url = "https://vendor.test/chat", FrameTarget = "F1", Viewport = (300, 200) };
        frame.Nodes.AddRange(
        [
            new AgentNode { Backend = 3, Role = "textbox", Name = "Message", Box = (10, 10, 200, 30), Facts = AgentPage.FactsOf("input", "", "text", editable: true, frameUrl: "https://vendor.test/chat") },
            new AgentNode { Backend = 4, Role = "StaticText", Name = "Ticket token tk-55a91b7e", Box = (10, 60, 200, 20), Facts = AgentPage.FactsOf("span", "Ticket token tk-55a91b7e", frameUrl: "https://vendor.test/chat") },
        ]);
        page.Sessions["S1"] = frame;
        return page;
    }

    // web_read as text (the page's own words) and as refs: e1 Note on the page, e2 Message inside the frame.
    private static async Task<FakeAgentTab> ReadCrm(WebAgentRig rig)
    {
        var tab = rig.Host.Add("https://crm.test/customer", page: Crm());
        await rig.Call(WebToolSchemas.Tabs);
        Assert.Null((await rig.Call(WebToolSchemas.Read, "{\"tab\":\"t1\",\"mode\":\"text\"}")).Error);
        var tree = await rig.Call(WebToolSchemas.Read, "{\"tab\":\"t1\"}");
        Assert.Contains("e2 textbox \"Message\"", tree.Text, StringComparison.Ordinal);
        Assert.Contains("e1 textbox \"Note\"", tree.Text, StringComparison.Ordinal);
        return tab;
    }

    [Fact]
    public async Task ThePagesTextTypedIntoAnotherSitesFrameInsideItAsks()
    {
        using var rig = new WebAgentRig();
        rig.Asker.Guard = _ => Task.FromResult(false);
        var tab = await ReadCrm(rig);

        var result = await rig.Call(WebToolSchemas.Type, Type("t1", "e2", "99417723"));

        AssertAskedFrom(rig, "crm.test", result);
        Assert.Equal(0, tab.Page.Count("Input.insertText"));
    }

    [Fact]
    public async Task ThePagesTextPressedIntoAnotherSitesFrameInsideItAsks()
    {
        using var rig = new WebAgentRig();
        rig.Asker.Guard = _ => Task.FromResult(false);
        var tab = await ReadCrm(rig);
        tab.Page.Focused = 20;
        tab.Page.Sessions["S1"].Focused = 3;

        var result = await rig.Call(WebToolSchemas.Key, Keys("t1", "9 9 4 1 7 7 2 3"));

        AssertAskedFrom(rig, "crm.test", result);
        Assert.Equal(0, tab.Page.Count("Input.dispatchKeyEvent"));
    }

    [Fact]
    public async Task AFramesTextTypedIntoThePageAroundItAsks()
    {
        using var rig = new WebAgentRig();
        rig.Asker.Guard = _ => Task.FromResult(false);
        var tab = await ReadCrm(rig);

        var result = await rig.Call(WebToolSchemas.Type, Type("t1", "e1", "tk-55a91b7e"));

        AssertAskedFrom(rig, "vendor.test", result);
        Assert.Equal(0, tab.Page.Count("Input.insertText"));
    }

    [Fact]
    public async Task ThePagesOwnTextTypedIntoItsOwnFieldStillTypesWithoutACard()
    {
        using var rig = new WebAgentRig();
        var tab = await ReadCrm(rig);

        var result = await rig.Call(WebToolSchemas.Type, Type("t1", "e1", "99417723"));

        Assert.Null(result.Error);
        Assert.Empty(rig.Asker.Of<WebGuardAsk>());
        Assert.Equal(1, tab.Page.Count("Input.insertText"));
    }
}
