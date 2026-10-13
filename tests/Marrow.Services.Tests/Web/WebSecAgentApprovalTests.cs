using System.Text.Json.Nodes;
using Marrow.Core.Web;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces security review M1 (E:/Droppy/work/scratch/lead/web-sec-findings.md; DESIGN.md 5.10 the guarded-action card,
// "the same call again joins it"; ED-NEW-web-sec-agent-3). A Confirm answers the card the user saw: the same element (its node), on the
// same address, with the same content. A late Confirm never lets a look-alike element through, it is used once, and the click or key
// goes ahead only when the page under it is still what the card showed.
[Trait("Decision", "ED-NEW-web-w11-1")]
public sealed class WebSecAgentApprovalTests
{
    private static string Ref(string reference) => "{\"tab\":\"t1\",\"ref\":\"" + reference + "\"}";

    // Two rows, each with its own Delete button, alike in every attribute.
    private static AgentPage Rows()
    {
        var page = new AgentPage { Text = "Two invoices" };
        page.Nodes.AddRange(
        [
            new AgentNode { Backend = 2, Role = "button", Name = "Delete", Box = (100, 100, 80, 30), Facts = AgentPage.FactsOf("button", "Delete") },
            new AgentNode { Backend = 3, Role = "button", Name = "Delete", Box = (100, 150, 80, 30), Facts = AgentPage.FactsOf("button", "Delete") },
        ]);
        return page;
    }

    private static async Task<FakeAgentTab> Read(WebAgentRig rig, AgentPage? page = null)
    {
        var tab = page is null ? rig.Host.Add("https://a.test/invite") : rig.Host.Add("https://a.test/invoices", page: page);
        await rig.Call(WebToolSchemas.Tabs);
        Assert.Null((await rig.Call(WebToolSchemas.Read, "{\"tab\":\"t1\"}")).Error);
        return tab;
    }

    // Runs a call whose card may wait: it finishes at once, or waits on its card until the clock passes the 25 s.
    private static async Task<WebToolResult> Settle(WebAgentRig rig, Task<WebToolResult> call, int pending)
    {
        await PcTestWait.Until(() => call.IsCompleted || rig.Clock.Pending >= pending);
        if (!call.IsCompleted) rig.Clock.Advance(WebAgentEngine.AskWaitMs);
        return await call;
    }

    [Fact]
    public async Task ALateConfirmForOneRowNeverPressesTheOther()
    {
        using var rig = new WebAgentRig();
        var late = new TaskCompletionSource<bool>();
        var cards = 0;
        rig.Asker.Guard = _ => ++cards == 1 ? late.Task : new TaskCompletionSource<bool>().Task;
        var tab = await Read(rig, Rows());
        var first = await Settle(rig, rig.Start(WebToolSchemas.Click, Ref("e1")), 2);
        Assert.Equal(WebErrorCodes.WaitingForUser, first.Error);
        late.SetResult(true);

        var other = await Settle(rig, rig.Start(WebToolSchemas.Click, Ref("e2")), 3);

        Assert.Equal(2, rig.Asker.Of<WebGuardAsk>().Count());
        Assert.Equal(WebErrorCodes.WaitingForUser, other.Error);
        Assert.Empty(tab.Page.Mouse("mousePressed"));
    }

    [Fact]
    public async Task AConfirmIsUsedOnce()
    {
        using var rig = new WebAgentRig();
        var late = new TaskCompletionSource<bool>();
        var cards = 0;
        rig.Asker.Guard = _ => ++cards == 1 ? late.Task : Task.FromResult(false);
        var tab = await Read(rig, Rows());
        Assert.Equal(WebErrorCodes.WaitingForUser, (await Settle(rig, rig.Start(WebToolSchemas.Click, Ref("e1")), 2)).Error);
        late.SetResult(true);

        var joined = await rig.Call(WebToolSchemas.Click, Ref("e1"));
        var again = await rig.Call(WebToolSchemas.Click, Ref("e1"));

        Assert.Null(joined.Error);
        Assert.Equal(WebErrorCodes.Cancelled, again.Error);
        Assert.Single(tab.Page.Mouse("mousePressed"));
        Assert.Equal(2, rig.Asker.Of<WebGuardAsk>().Count());
    }

    [Fact]
    public async Task AFormWhoseFieldsChangedIsAskedAgain()
    {
        using var rig = new WebAgentRig();
        var late = new TaskCompletionSource<bool>();
        var cards = 0;
        rig.Asker.Guard = _ => ++cards == 1 ? late.Task : new TaskCompletionSource<bool>().Task;
        var tab = await Read(rig);
        Assert.Equal(WebErrorCodes.WaitingForUser, (await Settle(rig, rig.Start(WebToolSchemas.Click, Ref("e3")), 2)).Error);
        tab.Page.Nodes.Single(n => n.Backend == 4).Facts["rows"] = new JsonArray { new JsonArray("To", "eve@evil.test") };
        late.SetResult(true);

        var changed = await Settle(rig, rig.Start(WebToolSchemas.Click, Ref("e3")), 3);

        var cardsShown = rig.Asker.Of<WebGuardAsk>().ToList();
        Assert.Equal(2, cardsShown.Count);
        Assert.Contains(new KeyValuePair<string, string>("To", "eve@evil.test"), cardsShown[1].Rows);
        Assert.Equal(WebErrorCodes.WaitingForUser, changed.Error);
        Assert.Empty(tab.Page.Mouse("mousePressed"));
    }

    [Fact]
    public async Task APageThatChangesUnderTheCardIsNotClicked()
    {
        using var rig = new WebAgentRig();
        var tab = await Read(rig);
        rig.Asker.Guard = _ =>
        {
            tab.Page.HitOverride = 7; // Buy now slides under Send invite while the card is up
            return Task.FromResult(true);
        };

        var result = await rig.Call(WebToolSchemas.Click, Ref("e3"));

        Assert.Equal(WebErrorCodes.StaleRef, result.Error);
        Assert.Empty(tab.Page.Mouse("mousePressed"));
    }

    [Fact]
    public async Task AConfirmedKeyGoesOnlyToTheElementTheCardShowed()
    {
        using var rig = new WebAgentRig();
        var tab = await Read(rig);
        tab.Page.Focused = 4;
        rig.Asker.Guard = _ =>
        {
            tab.Page.Focused = 10; // the page moves focus to Next while the card is up
            return Task.FromResult(true);
        };

        var result = await rig.Call(WebToolSchemas.Key, "{\"tab\":\"t1\",\"keys\":\"enter\"}");

        Assert.Equal(WebErrorCodes.StaleRef, result.Error);
        Assert.Equal(0, tab.Page.Count("Input.dispatchKeyEvent"));
    }
}
