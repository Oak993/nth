using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Port.Assistant;

// Dextra AS-1 (DESIGN.md rev 5, 12.1a, 15.5; ED-NEW-dextracore-2): MCP tools are placed by their name, made stricter
// (never looser) by their description and annotations, and Unknown when the table cannot place them. nth-assistant's
// own tools map by name with the guard the engine applies.
public sealed class McpToolClassifierTests
{
    private static Classification Tool(string server, string tool, string? description = null, McpToolAnnotations? annotations = null) =>
        ActionClassifier.Classify(new ActionRequest.McpTool(server, tool, description, annotations), ActionClassifierTests.Context());

    [Theory]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-2")]
    [InlineData("gmail", "send_message", ActionClass.Outward)]
    [InlineData("gmail", "sendMessage", ActionClass.Outward)]
    [InlineData("gmail", "reply_to_thread", ActionClass.Outward)]
    [InlineData("slack", "post_message", ActionClass.Outward)]
    [InlineData("slack", "chat.postMessage", ActionClass.Outward)]
    [InlineData("drive", "share_file", ActionClass.Outward)]
    [InlineData("calendar", "invite_attendees", ActionClass.Outward)]
    [InlineData("gmail", "create_draft", ActionClass.ConnectorWrite)]
    [InlineData("gmail", "search_messages", ActionClass.ReadWeb)]
    [InlineData("gmail", "list_sent_emails", ActionClass.ReadWeb)]
    [InlineData("github", "list_closed_issues", ActionClass.ReadWeb)]
    [InlineData("github", "get_posts", ActionClass.ReadWeb)]
    [InlineData("drive", "delete_file", ActionClass.Destructive)]
    [InlineData("tasks", "cancel_order", ActionClass.Money)]
    [InlineData("shop", "buy_item", ActionClass.Money)]
    [InlineData("calendar", "book_meeting", ActionClass.Commitment)]
    [InlineData("calendar", "accept_invite", ActionClass.Outward)]
    [InlineData("site", "login", ActionClass.Login)]
    [InlineData("sheets", "update_cell", ActionClass.ConnectorWrite)]
    [InlineData("desktop", "execute_command", ActionClass.Unknown)]
    [InlineData("acme", "do_thing", ActionClass.Unknown)]
    public void Tool_names_are_placed_by_the_name_table(string server, string tool, ActionClass expected) =>
        Assert.Equal(expected, Tool(server, tool).Class);

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-2")]
    public void Annotations_and_descriptions_only_make_a_tool_stricter()
    {
        // readOnlyHint true never loosens a send.
        Assert.Equal(ActionClass.Outward, Tool("slack", "post_message", annotations: new McpToolAnnotations(ReadOnlyHint: true)).Class);
        // destructiveHint true makes a read Destructive; readOnlyHint false makes a read a write.
        Assert.Equal(ActionClass.Destructive, Tool("files", "get_file", annotations: new McpToolAnnotations(DestructiveHint: true)).Class);
        Assert.Equal(ActionClass.ConnectorWrite, Tool("files", "get_file", annotations: new McpToolAnnotations(ReadOnlyHint: false)).Class);
        // An unplaceable name stays Unknown whatever it claims.
        Assert.Equal(ActionClass.Unknown, Tool("acme", "do_thing", annotations: new McpToolAnnotations(ReadOnlyHint: true)).Class);
        // A description's present-tense verb adds its class; a description never places a read.
        Assert.Equal(ActionClass.Outward, Tool("crm", "sync_contact", "Sends the contact to every list member").Class);
        Assert.Equal(ActionClass.Unknown, Tool("acme", "do_thing", "Gets and lists things").Class);
        Assert.Equal(ActionClass.Money, Tool("acme", "complete_step", "Charges the saved card").Class);
    }

    [Theory]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-2")]
    [InlineData("buy_item", MoneyKind.Purchase)]
    [InlineData("create_subscription", MoneyKind.Subscription)]
    [InlineData("transfer_funds", MoneyKind.Transfer)]
    [InlineData("send_money", MoneyKind.ToPerson)]
    [InlineData("pay_invoice", MoneyKind.ToPerson)]
    public void Money_tools_name_their_kind_and_anything_but_a_purchase_wins(string tool, MoneyKind kind)
    {
        var classification = Tool("bank", tool);
        Assert.Equal(ActionClass.Money, classification.Class);
        Assert.Equal(kind, classification.Money);
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-2")]
    public void A_connector_tool_names_its_server_as_the_grant_target()
    {
        var classification = Tool("sheets", "update_cell");
        Assert.Equal(GrantTarget.Connector("sheets"), classification.GrantTarget);
        Assert.Equal("write", classification.Field);
        var read = Tool("gmail", "search_messages");
        Assert.True(read.ReadsUntrusted);
    }

    [Theory]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-2")]
    [InlineData("start_mission", ActionClass.Agents, AssistantToolGuard.Delegation)]
    [InlineData("spawn_chat", ActionClass.Agents, AssistantToolGuard.Delegation)]
    [InlineData("launch_hydra_team", ActionClass.Agents, AssistantToolGuard.Delegation)]
    [InlineData("message_agent", ActionClass.Agents, AssistantToolGuard.Delegation)]
    [InlineData("stop_agent", ActionClass.Agents, AssistantToolGuard.None)]
    [InlineData("forget", ActionClass.Work, AssistantToolGuard.MemoryDelete)]
    [InlineData("list_sessions", ActionClass.ReadLocal, AssistantToolGuard.None)]
    [InlineData("recall", ActionClass.ReadLocal, AssistantToolGuard.None)]
    [InlineData("remember", ActionClass.Work, AssistantToolGuard.MemoryWrite)]
    [InlineData("suggest", ActionClass.Work, AssistantToolGuard.Suggestion)]
    [InlineData("delegate_to", ActionClass.Agents, AssistantToolGuard.Delegation)]
    [InlineData("schedule_duty", ActionClass.WriteElsewhere, AssistantToolGuard.PermanentConfiguration)]
    [InlineData("watch_folder", ActionClass.WriteElsewhere, AssistantToolGuard.PermanentConfiguration)]
    [InlineData("watch_page", ActionClass.WriteElsewhere, AssistantToolGuard.PermanentConfiguration)]
    [InlineData("create_assistant", ActionClass.WriteElsewhere, AssistantToolGuard.PermanentConfiguration)]
    [InlineData("request_grant", ActionClass.WriteElsewhere, AssistantToolGuard.PermanentConfiguration)]
    [InlineData("propose_capability", ActionClass.Install, AssistantToolGuard.PermanentConfiguration)]
    [InlineData("rewrite_rules", ActionClass.Unknown, AssistantToolGuard.None)]
    public void Assistant_tools_map_by_name_with_their_guard(string name, ActionClass expected, AssistantToolGuard guard)
    {
        var classification = ActionClassifier.Classify(new ActionRequest.AssistantTool(name), ActionClassifierTests.Context());
        Assert.Equal(expected, classification.Class);
        Assert.Equal(guard, classification.Guard);
    }
}
