using System.Text.Json.Nodes;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces security review, finding M7, gap after the first fix (DESIGN.md 7.6 "tools/call ... with the normal
// tool approvals", 8.2). The tool card showed the first 1,500 characters of the arguments while the call sent all of them, and
// still offered Allow once and "Always in this chat": 2,000 characters of padding in a first argument hid the one that
// mattered. Now a call whose arguments the card cannot show in full is refused before any card; without a card (tool
// approvals off, or "Always in this chat" given earlier) it runs as before.
[Trait("Decision", "ED-NEW-web-sec-mcpapps-6")]
public sealed class McpAppLongArgumentsTests
{
    private static JsonObject Call(string tool, JsonObject arguments) => new() { ["name"] = tool, ["arguments"] = arguments };

    private static JsonObject Padded() => new() { ["note"] = new string('x', 2000), ["command"] = "promote --prod --force" };

    [Fact]
    public void Arguments_the_card_cannot_show_in_full_are_refused_before_any_card()
    {
        using var rig = new McpAppsRig(csp: McpUiCsp.None);
        rig.Open();
        rig.Handshake();
        rig.Ui.ToolAnswer = McpAppToolDecision.AlwaysInChat;

        var reply = rig.Call("t", McpAppProtocol.ToolsCall, Call("promote_release", Padded()));
        var message = reply["error"]?["message"]?.GetValue<string>() ?? "";
        Assert.Contains("too long to show", message, StringComparison.Ordinal);
        Assert.Empty(rig.Ui.ToolCards);
        Assert.Empty(rig.Servers.Calls);
        Assert.Empty(rig.Grants.Get("shipyard").AlwaysTools);

        // Arguments that fit still get the card.
        Assert.NotNull(rig.Call("u", McpAppProtocol.ToolsCall, Call("promote_release", new JsonObject { ["command"] = "promote --staging" }))["result"]);
        Assert.Equal(["promote_release"], rig.Ui.ToolCards);
    }

    [Fact]
    public void Without_a_card_long_arguments_run_as_before()
    {
        using var rig = new McpAppsRig(csp: McpUiCsp.None);
        rig.Open();
        rig.Handshake();
        rig.Ui.Supervised = false;
        Assert.NotNull(rig.Call("t", McpAppProtocol.ToolsCall, Call("promote_release", Padded()))["result"]);
        Assert.True(JsonNode.DeepEquals(Padded(), rig.Servers.Calls.Single().Arguments));

        // "Always in this chat", given on a card that showed its arguments in full, covers the tool in this chat.
        rig.Ui.Supervised = true;
        rig.Ui.ToolAnswer = McpAppToolDecision.AlwaysInChat;
        Assert.NotNull(rig.Call("u", McpAppProtocol.ToolsCall, Call("promote_release", new JsonObject { ["command"] = "promote --staging" }))["result"]);
        Assert.NotNull(rig.Call("v", McpAppProtocol.ToolsCall, Call("promote_release", Padded()))["result"]);
        Assert.Equal(["promote_release"], rig.Ui.ToolCards);
        Assert.Equal(3, rig.Servers.Calls.Count);
    }
}
