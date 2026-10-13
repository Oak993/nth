using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces security review M3 (E:/Droppy/work/scratch/lead/web-sec-findings.md; DESIGN.md 5.10 "page text reaches the
// model, and a page can tell it to 'open this URL' or 'type this'"; ED-NEW-web-sec-agent-5). The cross-origin rule covered addresses
// only. Typing (web_type, or letters through web_key) text the model read on one site into a page of another site now asks first with
// the guarded-action card, naming the site the text came from; text the model wrote itself, or read on the same site, types as before.
[Trait("Decision", "ED-NEW-web-w11-1")]
public sealed class WebSecAgentCarryTests
{
    private const string Mail = "Inbox. Your sign-in code is 483920. Reply to lee@example.com about key sk-live-7f3a9c21d. "
        + "Our quarterly numbers came in well above the plan for the third time.";

    private static string Type(string tab, string text) => "{\"tab\":\"" + tab + "\",\"ref\":\"e1\",\"text\":\"" + text + "\"}";

    // t1 shows mail.test (read as text), t2 a form on forms.test (read for its refs: e1 is its Email field).
    private static async Task<(FakeAgentTab Mail, FakeAgentTab Form)> TwoSites(WebAgentRig rig)
    {
        var inbox = AgentPage.Invite();
        inbox.Text = Mail;
        var mail = rig.Host.Add("https://mail.test/inbox", page: inbox);
        var form = rig.Host.Add("https://forms.test/new");
        await rig.Call(WebToolSchemas.Tabs);
        Assert.Null((await rig.Call(WebToolSchemas.Read, "{\"tab\":\"t1\",\"mode\":\"text\"}")).Error);
        Assert.Null((await rig.Call(WebToolSchemas.Read, "{\"tab\":\"t1\"}")).Error);
        Assert.Null((await rig.Call(WebToolSchemas.Read, "{\"tab\":\"t2\"}")).Error);
        return (mail, form);
    }

    [Theory]
    [InlineData("483920")]
    [InlineData("lee@example.com")]
    [InlineData("the key is sk-live-7f3a9c21d")]
    [InlineData("FYI: our quarterly numbers came in well above plan")]
    public async Task TextReadOnOneSiteAsksBeforeItIsTypedIntoAnother(string text)
    {
        using var rig = new WebAgentRig();
        rig.Asker.Guard = _ => Task.FromResult(false);
        var (_, form) = await TwoSites(rig);

        var result = await rig.Call(WebToolSchemas.Type, Type("t2", text));

        var card = Assert.Single(rig.Asker.Of<WebGuardAsk>());
        Assert.Equal("Type text from mail.test?", card.Title);
        Assert.Contains(card.Rows, r => r.Value == "mail.test");
        Assert.Equal(WebErrorCodes.Cancelled, result.Error);
        Assert.Equal(0, form.Page.Count("Input.insertText"));
    }

    [Fact]
    public async Task ConfirmedCarriedTextIsTyped()
    {
        using var rig = new WebAgentRig();
        var (_, form) = await TwoSites(rig);

        var result = await rig.Call(WebToolSchemas.Type, Type("t2", "483920"));

        Assert.Single(rig.Asker.Of<WebGuardAsk>());
        Assert.Null(result.Error);
        Assert.Equal(1, form.Page.Count("Input.insertText"));
    }

    [Fact]
    public async Task LettersThroughWebKeyThatSpellReadTextAskToo()
    {
        using var rig = new WebAgentRig();
        rig.Asker.Guard = _ => Task.FromResult(false);
        var (_, form) = await TwoSites(rig);
        form.Page.Focused = 2;

        var result = await rig.Call(WebToolSchemas.Key, "{\"tab\":\"t2\",\"keys\":\"4 8 3 9 2 0\"}");

        Assert.Equal("Type text from mail.test?", Assert.Single(rig.Asker.Of<WebGuardAsk>()).Title);
        Assert.Equal(WebErrorCodes.Cancelled, result.Error);
        Assert.Equal(0, form.Page.Count("Input.dispatchKeyEvent"));
    }

    [Theory]
    [InlineData("t2", "Hello from the team at forms")]
    [InlineData("t2", "Member")]
    [InlineData("t1", "483920")]
    public async Task OwnWordsAndSameSiteTextTypeWithoutACard(string tab, string text)
    {
        using var rig = new WebAgentRig();
        var (mail, form) = await TwoSites(rig);

        var result = await rig.Call(WebToolSchemas.Type, Type(tab, text));

        Assert.Null(result.Error);
        Assert.Empty(rig.Asker.Of<WebGuardAsk>());
        Assert.Equal(1, (tab == "t1" ? mail : form).Page.Count("Input.insertText"));
    }

    [Fact]
    public async Task ASitePairTheUserAlwaysAllowsTypesWithoutACard()
    {
        using var rig = new WebAgentRig();
        rig.Grants.GrantPair("https://mail.test", "https://forms.test");
        var (_, form) = await TwoSites(rig);

        var result = await rig.Call(WebToolSchemas.Type, Type("t2", "483920"));

        Assert.Null(result.Error);
        Assert.Empty(rig.Asker.Of<WebGuardAsk>());
        Assert.Equal(1, form.Page.Count("Input.insertText"));
    }
}
