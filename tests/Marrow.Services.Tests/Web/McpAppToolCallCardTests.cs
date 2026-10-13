using System.Text.Json.Nodes;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces security review, finding M7 (DESIGN.md 7.6 "tools/call ... with the normal tool approvals").
// An MCP App's tool card hid the arguments, and "Always for shipyard" then let any arguments through in every chat, for
// good. The card now gets exactly the arguments the call sends, and "Always in this chat" covers that tool in that chat
// only; an older grant file's every-chat answers are not read.
[Trait("Decision", "ED-NEW-web-sec-mcpapps-2")]
public sealed class McpAppToolCallCardTests
{
    private static readonly Guid OtherChat = Guid.Parse("2b3c4d5e-6f70-4812-93a4-b5c6d7e8f901");

    private static JsonObject Call(string tool, JsonObject? arguments = null)
    {
        var parameters = new JsonObject { ["name"] = tool };
        if (arguments is not null) parameters["arguments"] = arguments;
        return parameters;
    }

    [Fact]
    public void The_tool_card_gets_exactly_the_arguments_the_call_sends()
    {
        using var rig = new McpAppsRig(csp: McpUiCsp.None);
        rig.Open();
        rig.Handshake();
        rig.Ui.ToolAnswer = McpAppToolDecision.AllowOnce;
        var arguments = new JsonObject { ["version"] = "v2.4.1", ["target"] = new JsonObject { ["stage"] = "production", ["force"] = true } };

        Assert.NotNull(rig.Call("t", McpAppProtocol.ToolsCall, Call("promote_release", (JsonObject)arguments.DeepClone()))["result"]);

        var shown = Assert.Single(rig.Ui.ToolArguments);
        Assert.True(JsonNode.DeepEquals(arguments, shown), shown.ToJsonString());
        Assert.True(JsonNode.DeepEquals(arguments, rig.Servers.Calls.Single().Arguments));

        rig.Call("e", McpAppProtocol.ToolsCall, Call("list_releases"));
        Assert.Empty(rig.Ui.ToolArguments[1]); // no arguments: an empty object, and the card says so
    }

    [Fact]
    public void Always_lets_the_tool_run_without_a_card_in_this_chat_only()
    {
        var grants = new McpAppGrantStore(null);
        using var first = new McpAppsRig(csp: McpUiCsp.None, grants: grants);
        first.Open();
        first.Handshake();
        first.Ui.ToolAnswer = McpAppToolDecision.AlwaysInChat;
        Assert.NotNull(first.Call("a", McpAppProtocol.ToolsCall, Call("promote_release"))["result"]);
        Assert.NotNull(first.Call("b", McpAppProtocol.ToolsCall, Call("promote_release"))["result"]);
        Assert.Equal(["promote_release"], first.Ui.ToolCards);
        Assert.Equal([new McpAppChatTool(McpAppsRig.Chat, "promote_release")], grants.Get("shipyard").AlwaysTools);

        // Another tool in the same chat still asks.
        first.Ui.ToolAnswer = McpAppToolDecision.Deny;
        Assert.NotNull(first.Call("c", McpAppProtocol.ToolsCall, Call("list_releases"))["error"]);
        Assert.Equal(["promote_release", "list_releases"], first.Ui.ToolCards);

        // The same tool from the same server in another chat asks again.
        using var other = new McpAppsRig(csp: McpUiCsp.None, grants: grants, chat: OtherChat);
        other.Open();
        other.Handshake();
        Assert.NotNull(other.Call("d", McpAppProtocol.ToolsCall, Call("promote_release"))["error"]);
        Assert.Equal(["promote_release"], other.Ui.ToolCards);
        Assert.Equal(2, first.Servers.Calls.Count);
        Assert.Empty(other.Servers.Calls);
    }

    [Fact]
    public void An_older_files_always_for_every_chat_is_not_read_and_the_new_answer_keeps_its_chat()
    {
        var data = Marrow.Testing.TestDataDirectory.Read();
        Assert.False(string.IsNullOrEmpty(data), "Set MARROW_PORT_DATA_DIR (R-TEST-9).");
        var path = Path.Combine(data!, "McpApps", Guid.NewGuid().ToString("N"), McpAppGrantStore.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ \"version\": 1, \"servers\": { \"shipyard\": { \"alwaysTools\": [\"promote_release\"], \"messageChats\": [\"" + McpAppsRig.Chat.ToString("D") + "\"] } } }");

        var old = new McpAppGrantStore(path).Get("shipyard");
        Assert.Empty(old.AlwaysTools);
        Assert.Contains(McpAppsRig.Chat, old.MessageChats); // the rest of the file still reads

        new McpAppGrantStore(path).Update("shipyard", g => g with { AlwaysTools = new HashSet<McpAppChatTool> { new(OtherChat, "promote_release") } });
        var text = File.ReadAllText(path);
        Assert.Contains("\"chatTools\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"alwaysTools\"", text, StringComparison.Ordinal);
        Assert.Equal([new McpAppChatTool(OtherChat, "promote_release")], new McpAppGrantStore(path).Get("shipyard").AlwaysTools);
    }
}
