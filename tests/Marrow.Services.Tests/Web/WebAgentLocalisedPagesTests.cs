using System.Text.Json.Nodes;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces W22 (DESIGN.md 5.10 "Guarded actions ... localised lists", 13.2 "guarded-action matching (localised lists)", 14 W22;
// ED-NEW-web-w22-1). The engine run on a page written in Hindi and Urdu: the guard reads the names the page gave, in the page's own words.
// Every label here is the kind a Hindi or Urdu page carries; nothing is a real site's page.
[Trait("Decision", "ED-NEW-web-w22-1")]
public sealed class WebAgentLocalisedPagesTests
{
    private const string Url = "https://hind.test/seva";

    private static string Tab(string tab = "t1") => "{\"tab\":\"" + tab + "\"}";

    private static string Ref(string reference) => "{\"tab\":\"t1\",\"ref\":\"" + reference + "\"}";

    // e1 send (Hindi), e2 delete (Urdu), e3 buy now (Hindi), e4 pay (Urdu), e5 settings (Hindi), e6 search (Urdu), e7 a button with only an aria-label, which says
    // send (Hindi), e8 a button whose form posts to an address with "payment" in Hindi, e9 a button whose form
    // posts to an address with "delete" in Urdu.
    private static AgentPage Page()
    {
        var page = new AgentPage { Text = "सेवा" };
        var aria = AgentPage.FactsOf("button", "संदेश भेजें");
        aria["ariaLabel"] = "संदेश भेजें";
        page.Nodes.AddRange(
        [
            Node(2, "संदेश भेजें"),
            Node(3, "حذف کریں"),
            Node(4, "अभी खरीदें"),
            Node(5, "ادائیگی کریں"),
            Node(6, "सेटिंग्स"),
            Node(7, "تلاش کریں"),
            new AgentNode { Backend = 8, Role = "button", Name = "संदेश भेजें", Box = (100, 400, 120, 40), Facts = aria },
            Form(9, "जारी रखें", "https://hind.test/" + Uri.EscapeDataString("भुगतान") + "/pushti"),
            Form(10, "جاری رکھیں", "https://hind.test/" + Uri.EscapeDataString("حذف") + "/tasdeeq"),
        ]);
        return page;
    }

    private static AgentNode Node(long backend, string name) =>
        new() { Backend = backend, Role = "button", Name = name, Box = (100, 50 * backend, 120, 40), Facts = AgentPage.FactsOf("button", name) };

    private static AgentNode Form(long backend, string name, string action) =>
        new()
        {
            Backend = backend,
            Role = "button",
            Name = name,
            Box = (100, 50 * backend, 120, 40),
            Facts = AgentPage.FactsOf("button", name, inForm: true, submits: true, action: action, method: "post"),
        };

    private static async Task<FakeAgentTab> Read(WebAgentRig rig)
    {
        var tab = rig.Host.Add(Url, page: Page());
        await rig.Call(WebToolSchemas.Tabs);
        var read = await rig.Call(WebToolSchemas.Read, Tab());
        Assert.Null(read.Error);
        return tab;
    }

    [Fact]
    public async Task AHindiSendButtonAndAnUrduDeleteButtonShowTheGuardCard()
    {
        using var rig = new WebAgentRig();
        rig.Asker.Guard = _ => Task.FromResult(false);
        var tab = await Read(rig);

        var send = await rig.Call(WebToolSchemas.Click, Ref("e1"));
        var delete = await rig.Call(WebToolSchemas.Click, Ref("e2"));

        var cards = rig.Asker.Of<WebGuardAsk>().ToList();
        Assert.Equal([WebGuardCategory.Send, WebGuardCategory.Delete], cards.Select(card => card.Category));
        Assert.Equal(["send", "delete"], cards.Select(card => card.Word));
        Assert.Equal("संदेश भेजें?", cards[0].Title);
        Assert.Equal(WebErrorCodes.Cancelled, send.Error);
        Assert.Equal(WebErrorCodes.Cancelled, delete.Error);
        Assert.Empty(tab.Page.Mouse("mousePressed"));
    }

    [Fact]
    public async Task AConfirmedHindiSendRunsOnce()
    {
        using var rig = new WebAgentRig();
        var tab = await Read(rig);

        var result = await rig.Call(WebToolSchemas.Click, Ref("e1"));

        Assert.Null(result.Error);
        Assert.Single(rig.Asker.Of<WebGuardAsk>());
        Assert.Single(tab.Page.Mouse("mousePressed"));
    }

    [Fact]
    public async Task APaymentInHindiOrUrduIsRefusedAndNothingAsks()
    {
        using var rig = new WebAgentRig();
        var tab = await Read(rig);

        var buy = await rig.Call(WebToolSchemas.Click, Ref("e3"));
        var pay = await rig.Call(WebToolSchemas.Click, Ref("e4"));

        Assert.Equal(WebErrorCodes.Denied, buy.Error);
        Assert.Equal(WebAgentPolicy.PaymentRefusal, buy.Text);
        Assert.Equal(WebErrorCodes.Denied, pay.Error);
        Assert.Equal(WebAgentPolicy.PaymentRefusal, pay.Text);
        Assert.Empty(rig.Asker.Of<WebGuardAsk>());
        Assert.Empty(tab.Page.Mouse("mousePressed"));
    }

    [Fact]
    public async Task OrdinaryHindiAndUrduButtonsRunWithoutAsking()
    {
        using var rig = new WebAgentRig();
        var tab = await Read(rig);

        var settings = await rig.Call(WebToolSchemas.Click, Ref("e5"));
        var search = await rig.Call(WebToolSchemas.Click, Ref("e6"));

        Assert.Null(settings.Error);
        Assert.Null(search.Error);
        Assert.Empty(rig.Asker.Of<WebGuardAsk>());
        Assert.Equal(2, tab.Page.Mouse("mousePressed").Count());
    }

    [Fact]
    public async Task AButtonNamedOnlyByItsHindiAriaLabelAsks()
    {
        using var rig = new WebAgentRig();
        rig.Asker.Guard = _ => Task.FromResult(false);
        await Read(rig);

        await rig.Call(WebToolSchemas.Click, Ref("e7"));

        Assert.Equal(WebGuardCategory.Send, Assert.Single(rig.Asker.Of<WebGuardAsk>()).Category);
    }

    [Fact]
    public async Task AFormPostedToAnAddressNamingAHindiPaymentIsRefusedAndOneNamingAnUrduDeleteAsks()
    {
        using var rig = new WebAgentRig();
        rig.Asker.Guard = _ => Task.FromResult(false);
        var tab = await Read(rig);

        var pay = await rig.Call(WebToolSchemas.Click, Ref("e8"));
        var delete = await rig.Call(WebToolSchemas.Click, Ref("e9"));

        Assert.Equal(WebErrorCodes.Denied, pay.Error);
        Assert.Equal(WebAgentPolicy.PaymentRefusal, pay.Text);
        Assert.Equal(WebGuardCategory.Delete, Assert.Single(rig.Asker.Of<WebGuardAsk>()).Category);
        Assert.Equal(WebErrorCodes.Cancelled, delete.Error);
        Assert.Empty(tab.Page.Mouse("mousePressed"));
    }

    [Fact]
    public void TheJsonFactsKeepTheirWordsWhole()
    {
        // The facts are JSON from the page: a name in Hindi and Urdu survives the round trip the engine reads it through.
        var facts = AgentPage.FactsOf("button", "ادائیگی کریں");

        Assert.Equal("ادائیگی کریں", JsonNode.Parse(facts.ToJsonString())!["name"]!.GetValue<string>());
    }
}
