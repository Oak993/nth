using System.Text.Json.Nodes;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces security review L4 (E:/Droppy/work/scratch/lead/web-sec-findings.md; DESIGN.md 5.10 the guarded-action card,
// "web_eval: preview origins only, after the user allows it per project", 8.1 "values of password inputs never reach the model";
// ED-NEW-web-sec-agent-6). Enter in a text area or in a field outside a form asks like a send; card, code and password values inside
// frames stay hidden; web_select is judged on the option it chooses and cannot send a form past its card; script grants belong to
// the project (a chat outside a project keeps its own only while its web tools live).
[Trait("Decision", "ED-NEW-web-w11-1")]
public sealed class WebSecAgentGapTests
{
    private static string Tab(string tab = "t1", string extra = "") => "{\"tab\":\"" + tab + "\"" + extra + "}";

    // A chat composer: a text area, a message field outside any form, a select of actions and a card frame with a typed card number.
    private static AgentPage Composer()
    {
        var page = new AgentPage { Text = "Chat", Url = "https://a.test/chat" };
        page.Nodes.AddRange(
        [
            new AgentNode { Backend = 2, Role = "textbox", Name = "Message", Box = (100, 100, 300, 60), Facts = AgentPage.FactsOf("textarea", "", editable: true) },
            new AgentNode { Backend = 3, Role = "textbox", Name = "Reply", Box = (100, 200, 300, 30), Facts = AgentPage.FactsOf("input", "", "text", editable: true) },
            new AgentNode { Backend = 4, Role = "combobox", Name = "Actions", Box = (100, 250, 200, 30), Options = ["Keep", "Delete", "Pay now"], Facts = AgentPage.FactsOf("select", "", isSelect: true) },
            new AgentNode { Backend = 5, Role = "combobox", Name = "Sort", Box = (100, 300, 200, 30), Options = ["Newest", "Oldest"], Facts = AgentPage.FactsOf("select", "", isSelect: true, inForm: true, action: "https://a.test/chat", method: "get") },
            new AgentNode { Backend = 20, Role = "Iframe", Name = "", Box = (500, 100, 300, 200), FrameId = "F1", Facts = AgentPage.FactsOf("iframe", "", frameUrl: "https://pay.test/card") },
        ]);
        var frame = new AgentPage { Text = "Card", Url = "https://pay.test/card", FrameTarget = "F1" };
        frame.Nodes.Add(new AgentNode { Backend = 3, Role = "textbox", Name = "Card number", Value = "4242 4242 4242 4242", Box = (10, 10, 200, 30), Facts = AgentPage.FactsOf("input", "", "text", autocomplete: "cc-number", editable: true, frameUrl: "https://pay.test/card") });
        frame.Secrets.Add(3);
        page.Sessions["S1"] = frame;
        return page;
    }

    private static async Task<FakeAgentTab> Chat(WebAgentRig rig)
    {
        var tab = rig.Host.Add("https://a.test/chat", page: Composer());
        await rig.Call(WebToolSchemas.Tabs);
        var read = await rig.Call(WebToolSchemas.Read, Tab());
        Assert.Null(read.Error);
        return tab;
    }

    [Fact]
    public async Task EnterInATextAreaAsksBeforeItCanSend()
    {
        using var rig = new WebAgentRig();
        rig.Asker.Guard = _ => Task.FromResult(false);
        var tab = await Chat(rig);
        tab.Page.Focused = 2;

        var result = await rig.Call(WebToolSchemas.Key, Tab(extra: ",\"keys\":\"enter\""));

        Assert.Equal(WebGuardCategory.Send, Assert.Single(rig.Asker.Of<WebGuardAsk>()).Category);
        Assert.Equal(WebErrorCodes.Cancelled, result.Error);
        Assert.Equal(0, tab.Page.Count("Input.dispatchKeyEvent"));
    }

    [Fact]
    public async Task EnterInAFieldOutsideAFormAsksBeforeItCanSend()
    {
        using var rig = new WebAgentRig();
        rig.Asker.Guard = _ => Task.FromResult(false);
        var tab = await Chat(rig);

        var result = await rig.Call(WebToolSchemas.Type, Tab(extra: ",\"ref\":\"e2\",\"text\":\"on my way\",\"submit\":true"));

        Assert.Equal(WebGuardCategory.Send, Assert.Single(rig.Asker.Of<WebGuardAsk>()).Category);
        Assert.Equal(WebErrorCodes.Cancelled, result.Error);
        Assert.Equal(0, tab.Page.Count("Input.insertText"));
    }

    [Fact]
    public async Task CardValuesInsideAFrameNeverReachTheModel()
    {
        using var rig = new WebAgentRig();
        rig.Host.Add("https://a.test/chat", page: Composer());
        await rig.Call(WebToolSchemas.Tabs);

        var read = await rig.Call(WebToolSchemas.Read, Tab());
        var found = await rig.Call(WebToolSchemas.Find, Tab(extra: ",\"query\":\"Card\""));

        Assert.Contains("Card number", read.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("4242", read.Text, StringComparison.Ordinal);
        Assert.Contains("Card number", found.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("4242", found.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChoosingADeleteOptionAsksAndAPayOptionIsDenied()
    {
        using var rig = new WebAgentRig();
        rig.Asker.Guard = _ => Task.FromResult(false);
        var tab = await Chat(rig);

        var delete = await rig.Call(WebToolSchemas.Select, Tab(extra: ",\"ref\":\"e3\",\"value\":\"Delete\""));
        var pay = await rig.Call(WebToolSchemas.Select, Tab(extra: ",\"ref\":\"e3\",\"value\":\"Pay now\""));
        var keep = await rig.Call(WebToolSchemas.Select, Tab(extra: ",\"ref\":\"e3\",\"value\":\"Keep\""));

        Assert.Equal(WebGuardCategory.Delete, Assert.Single(rig.Asker.Of<WebGuardAsk>()).Category);
        Assert.Equal(WebErrorCodes.Cancelled, delete.Error);
        Assert.Equal(WebErrorCodes.Denied, pay.Error);
        Assert.Equal(WebAgentPolicy.PaymentRefusal, pay.Text);
        Assert.Null(keep.Error);
        Assert.Equal("Keep", tab.Page.Nodes.Single(n => n.Backend == 4).Chosen);
    }

    [Fact]
    public async Task AnOptionThatSendsAPostFormIsStopped()
    {
        using var rig = new WebAgentRig();
        var tab = await rig.InviteTab();
        Assert.Null((await rig.Call(WebToolSchemas.Read, Tab())).Error);
        tab.Page.OnCall = (method, p) =>
        {
            var applies = p["arguments"] is not JsonArray args || args.Count < 2 || args[1]?["value"]?.GetValue<bool>() != false;
            if (method == "Runtime.callFunctionOn" && ((string?)p["functionDeclaration"] ?? "").Contains("function (want", StringComparison.Ordinal) && applies)
                tab.Navigate("https://a.test/invite/role");
        };

        var result = await rig.Call(WebToolSchemas.Select, Tab(extra: ",\"ref\":\"e2\",\"value\":\"Admin\""));

        Assert.Equal(WebErrorCodes.Blocked, result.Error);
        Assert.Contains("submit button", result.Text, StringComparison.Ordinal);
        Assert.Equal("https://a.test/invite", tab.Url);
    }

    [Fact]
    public async Task AnOptionThatReloadsAGetFilterStillRuns()
    {
        using var rig = new WebAgentRig();
        var tab = await Chat(rig);
        tab.Page.OnCall = (method, p) =>
        {
            var applies = p["arguments"] is not JsonArray args || args.Count < 2 || args[1]?["value"]?.GetValue<bool>() != false;
            if (method == "Runtime.callFunctionOn" && ((string?)p["functionDeclaration"] ?? "").Contains("function (want", StringComparison.Ordinal) && applies)
                tab.Navigate("https://a.test/chat?sort=oldest");
        };

        var result = await rig.Call(WebToolSchemas.Select, Tab(extra: ",\"ref\":\"e4\",\"value\":\"Oldest\""));

        Assert.Null(result.Error);
        Assert.Equal("https://a.test/chat?sort=oldest", tab.Url);
    }

    [Fact]
    public async Task ScriptGrantsBelongToTheProject()
    {
        using var rig = new WebAgentRig();
        rig.Host.Add("http://localhost:5173/", VisusTabKind.Preview, shared: true);
        await rig.Call(WebToolSchemas.Tabs);

        await rig.Call(WebToolSchemas.Eval, Tab(extra: ",\"expression\":\"1+1\""));
        await rig.Call(WebToolSchemas.Eval, Tab(extra: ",\"expression\":\"2+2\""));
        rig.Host.Project = "project-2";
        var other = await rig.Call(WebToolSchemas.Eval, Tab(extra: ",\"expression\":\"3+3\""));

        Assert.Equal(2, rig.Asker.Of<WebEvalAsk>().Count());
        Assert.Null(other.Error);
    }

    [Fact]
    public async Task AChatOutsideAProjectKeepsItsScriptGrantOnlyWhileItsWebToolsLive()
    {
        using var rig = new WebAgentRig();
        rig.Host.Project = null;
        rig.Host.Add("http://localhost:5173/", VisusTabKind.Preview, shared: true);
        await rig.Call(WebToolSchemas.Tabs);
        await rig.Call(WebToolSchemas.Eval, Tab(extra: ",\"expression\":\"1+1\""));
        await rig.Call(WebToolSchemas.Eval, Tab(extra: ",\"expression\":\"2+2\""));
        Assert.Single(rig.Asker.Of<WebEvalAsk>());

        rig.Hub.Forget(WebAgentRig.Thread);
        await rig.Call(WebToolSchemas.Tabs);
        await rig.Call(WebToolSchemas.Eval, Tab(extra: ",\"expression\":\"3+3\""));

        Assert.Equal(2, rig.Asker.Of<WebEvalAsk>().Count());
    }
}
