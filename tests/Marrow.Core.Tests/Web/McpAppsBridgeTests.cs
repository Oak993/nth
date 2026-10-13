using System.Text.Json.Nodes;
using Marrow.Core.Web;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: web surfaces W19 (DESIGN.md 7.6 "Bridge", 13.1 McpAppsBridgeTests). The host side of the MCP Apps protocol as a
// state machine: nothing reaches the view before it says it is initialized, partial input stops once the full input is sent,
// the result follows the input, teardown waits for the view's answer, and a tool the app may not call is refused alone.
[Trait("Decision", "ED-NEW-web-w19-1")]
public sealed class McpAppsBridgeTests
{
    private readonly List<JsonObject> sent = [];
    private readonly List<McpAppRequest> requests = [];
    private readonly List<McpAppNotice> notices = [];
    private readonly McpAppsBridge bridge;
    private int initializeCalls;

    public McpAppsBridgeTests()
    {
        bridge = new McpAppsBridge(
            json => sent.Add(JsonNode.Parse(json)!.AsObject()),
            () =>
            {
                initializeCalls++;
                return new JsonObject
                {
                    ["protocolVersion"] = McpAppProtocol.Version,
                    ["hostInfo"] = new JsonObject { ["name"] = "nth", ["version"] = "1" },
                    ["hostCapabilities"] = new JsonObject(),
                    ["hostContext"] = new McpAppHostContext().ToJson(),
                };
            },
            name => name is "list_releases" or "promote_release");
        bridge.Requested += requests.Add;
        bridge.Noticed += notices.Add;
    }

    private void FromView(object message) => bridge.Receive(System.Text.Json.JsonSerializer.Serialize(message));

    private void Handshake(object? capabilities = null)
    {
        FromView(new { jsonrpc = "2.0", id = 1, method = "ui/initialize", @params = new { protocolVersion = McpAppProtocol.Version, clientInfo = new { name = "release-board", version = "1" }, appCapabilities = capabilities ?? new { } } });
        FromView(new { jsonrpc = "2.0", method = "ui/notifications/initialized" });
    }

    private List<string?> Methods() => sent.Select(m => m["method"]?.GetValue<string>()).ToList();

    [Fact]
    public void Nothing_but_the_initialize_answer_reaches_the_view_before_it_is_initialized()
    {
        bridge.SetToolInputPartial(new JsonObject { ["env"] = "stag" });
        bridge.SetToolInput(new JsonObject { ["env"] = "staging" });
        bridge.SetToolResult(new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "3 releases" }) });
        bridge.ChangeHostContext(new JsonObject { ["theme"] = "light" });
        Assert.Empty(sent);

        FromView(new { jsonrpc = "2.0", id = 1, method = "ui/initialize", @params = new { } });
        var answer = Assert.Single(sent);
        Assert.Equal(1, answer["id"]!.GetValue<int>());
        Assert.Equal(McpAppProtocol.Version, answer["result"]!["protocolVersion"]!.GetValue<string>());
        Assert.Equal(McpAppBridgeState.Loading, bridge.State);

        FromView(new { jsonrpc = "2.0", method = "ui/notifications/initialized" });
        Assert.Equal(McpAppBridgeState.Ready, bridge.State);
        Assert.Equal([null, "ui/notifications/tool-input", "ui/notifications/tool-result"], Methods());
        Assert.Equal("staging", sent[1]["params"]!["arguments"]!["env"]!.GetValue<string>());
        Assert.Equal("3 releases", sent[2]["params"]!["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void Initialized_without_an_initialize_first_is_ignored()
    {
        bridge.SetToolInput(new JsonObject());
        FromView(new { jsonrpc = "2.0", method = "ui/notifications/initialized" });
        Assert.Equal(McpAppBridgeState.Loading, bridge.State);
        Assert.Empty(sent);
    }

    [Fact]
    public void Partial_input_streams_until_the_full_input_and_never_after()
    {
        Handshake();
        sent.Clear();
        bridge.SetToolInputPartial(new JsonObject { ["env"] = "st" });
        bridge.SetToolInputPartial(new JsonObject { ["env"] = "st" }); // the same again is not resent
        bridge.SetToolInputPartial(new JsonObject { ["env"] = "stag" });
        bridge.SetToolInput(new JsonObject { ["env"] = "staging" });
        bridge.SetToolInputPartial(new JsonObject { ["env"] = "late" });
        bridge.SetToolInput(new JsonObject { ["env"] = "twice" });
        Assert.Equal(["ui/notifications/tool-input-partial", "ui/notifications/tool-input-partial", "ui/notifications/tool-input"], Methods());
        Assert.Equal("staging", sent[^1]["params"]!["arguments"]!["env"]!.GetValue<string>());
    }

    [Fact]
    public void A_result_without_an_input_sends_the_last_partial_input_first()
    {
        Handshake();
        sent.Clear();
        bridge.SetToolInputPartial(new JsonObject { ["env"] = "prod" });
        bridge.SetToolResult(new JsonObject { ["structuredContent"] = new JsonObject { ["count"] = 3 } });
        Assert.Equal(["ui/notifications/tool-input-partial", "ui/notifications/tool-input", "ui/notifications/tool-result"], Methods());
        Assert.Equal("prod", sent[1]["params"]!["arguments"]!["env"]!.GetValue<string>());
    }

    [Fact]
    public void A_cancelled_call_says_so_once_and_a_result_never_follows_it()
    {
        Handshake();
        sent.Clear();
        bridge.SetToolInput(new JsonObject());
        bridge.SetToolCancelled("The user stopped the turn.");
        bridge.SetToolCancelled("again");
        Assert.Equal(["ui/notifications/tool-input", "ui/notifications/tool-cancelled"], Methods());
        Assert.Equal("The user stopped the turn.", sent[1]["params"]!["reason"]!.GetValue<string>());
    }

    [Fact]
    public void Requests_before_the_handshake_are_refused_and_never_reach_the_host()
    {
        FromView(new { jsonrpc = "2.0", id = 7, method = "tools/call", @params = new { name = "list_releases" } });
        FromView(new { jsonrpc = "2.0", id = 8, method = "ui/message", @params = new { role = "user", content = new { type = "text", text = "hi" } } });
        Assert.Empty(requests);
        Assert.All(sent, m => Assert.Equal(McpAppsBridge.RefusedCode, m["error"]!["code"]!.GetValue<int>()));
        Assert.Equal(McpAppsBridge.NotInitializedMessage, sent[0]["error"]!["message"]!.GetValue<string>());
    }

    [Fact]
    public void Ping_is_answered_at_any_time_and_unknown_methods_are_not_found()
    {
        FromView(new { jsonrpc = "2.0", id = "p1", method = "ping" });
        Assert.Equal("p1", sent[0]["id"]!.GetValue<string>());
        Assert.NotNull(sent[0]["result"]);
        Handshake();
        FromView(new { jsonrpc = "2.0", id = 9, method = "sampling/createMessage", @params = new { } });
        Assert.Equal(McpAppsBridge.MethodNotFoundCode, sent[^1]["error"]!["code"]!.GetValue<int>());
        Assert.Empty(requests);
    }

    [Fact]
    public void Only_tools_the_app_may_call_reach_the_host()
    {
        Handshake();
        FromView(new { jsonrpc = "2.0", id = 10, method = "tools/call", @params = new { name = "list_releases", arguments = new { } } });
        FromView(new { jsonrpc = "2.0", id = 11, method = "tools/call", @params = new { name = "delete_everything" } }); // model-only or another server's
        FromView(new { jsonrpc = "2.0", id = 12, method = "tools/call", @params = new { name = "mcp__other__list" } });
        FromView(new { jsonrpc = "2.0", id = 13, method = "tools/call", @params = new { } });
        var request = Assert.Single(requests);
        Assert.Equal("tools/call", request.Method);
        Assert.Equal("list_releases", request.Params["name"]!.GetValue<string>());
        var refusals = sent.Where(m => m["error"] is not null).ToList();
        Assert.Equal(3, refusals.Count);
        Assert.All(refusals, m => Assert.Equal(McpAppsBridge.UnknownToolMessage, m["error"]!["message"]!.GetValue<string>()));
    }

    [Fact]
    public void Answers_go_to_the_document_that_asked_and_never_to_a_reloaded_one()
    {
        Handshake();
        FromView(new { jsonrpc = "2.0", id = 20, method = "ui/open-link", @params = new { url = "https://shipyard.dev" } });
        var request = Assert.Single(requests);
        bridge.NewDocument();
        sent.Clear();
        bridge.Answer(request, new JsonObject());
        Assert.Empty(sent);
        Assert.Equal(McpAppBridgeState.Loading, bridge.State);
    }

    [Fact]
    public void A_new_document_gets_the_tool_data_again_after_its_own_handshake()
    {
        bridge.SetToolInput(new JsonObject { ["env"] = "staging" });
        bridge.SetToolResult(new JsonObject { ["content"] = new JsonArray() });
        Handshake();
        bridge.NewDocument();
        sent.Clear();
        Handshake();
        Assert.Equal([null, "ui/notifications/tool-input", "ui/notifications/tool-result"], Methods());
        Assert.Equal(2, initializeCalls);
    }

    [Fact]
    public void Teardown_asks_a_ready_view_and_closes_when_it_answers()
    {
        var tornDown = 0;
        bridge.TornDown += () => tornDown++;
        Handshake();
        sent.Clear();
        bridge.Teardown("The chat closed.");
        Assert.Equal(McpAppBridgeState.TearingDown, bridge.State);
        var ask = Assert.Single(sent);
        Assert.Equal(McpAppProtocol.ResourceTeardown, ask["method"]!.GetValue<string>());
        Assert.Equal("The chat closed.", ask["params"]!["reason"]!.GetValue<string>());

        // While tearing down, new requests are refused and tool data no longer flows.
        FromView(new { jsonrpc = "2.0", id = 30, method = "tools/call", @params = new { name = "list_releases" } });
        Assert.Equal(McpAppsBridge.ClosingMessage, sent[^1]["error"]!["message"]!.GetValue<string>());
        Assert.Empty(requests);

        FromView(new { jsonrpc = "2.0", id = "wrong", result = new { } });
        Assert.Equal(McpAppBridgeState.TearingDown, bridge.State);
        FromView(new { jsonrpc = "2.0", id = ask["id"]!.GetValue<string>(), result = new { } });
        Assert.Equal(McpAppBridgeState.Closed, bridge.State);
        Assert.Equal(1, tornDown);

        sent.Clear();
        bridge.SetToolInput(new JsonObject());
        FromView(new { jsonrpc = "2.0", id = 31, method = "ping" });
        Assert.Empty(sent);
    }

    [Fact]
    public void A_view_that_never_finished_its_handshake_is_closed_without_a_word()
    {
        var tornDown = 0;
        bridge.TornDown += () => tornDown++;
        bridge.Teardown("gone");
        Assert.Equal(McpAppBridgeState.Closed, bridge.State);
        Assert.Empty(sent);
        Assert.Equal(1, tornDown);
    }

    [Fact]
    public void Host_context_changes_reach_only_a_ready_view()
    {
        bridge.ChangeHostContext(new JsonObject { ["theme"] = "light" });
        Handshake();
        sent.Clear();
        bridge.ChangeHostContext(new JsonObject());
        bridge.ChangeHostContext(new JsonObject { ["displayMode"] = "fullscreen" });
        var change = Assert.Single(sent);
        Assert.Equal(McpAppProtocol.HostContextChanged, change["method"]!.GetValue<string>());
        Assert.Equal("fullscreen", change["params"]!["displayMode"]!.GetValue<string>());
    }

    [Fact]
    public void The_views_display_modes_limit_what_the_host_may_switch_to()
    {
        Assert.True(bridge.Supports(McpAppDisplayMode.PopOut));
        Handshake(new { availableDisplayModes = new[] { "inline", "fullscreen" } });
        Assert.Equal("release-board", bridge.AppName);
        Assert.True(bridge.Supports(McpAppDisplayMode.Pane));
        Assert.False(bridge.Supports(McpAppDisplayMode.PopOut));
        Assert.True(bridge.Supports(McpAppDisplayMode.Inline));
    }

    [Fact]
    public void Size_and_log_notices_reach_the_host_and_junk_is_ignored()
    {
        bridge.Receive("not json");
        bridge.Receive("[1,2]");
        bridge.Receive("{\"jsonrpc\":\"1.0\",\"method\":\"ping\",\"id\":1}");
        bridge.Receive("{\"jsonrpc\":\"2.0\",\"method\":\"ping\",\"id\":{\"x\":1}}");
        Assert.Empty(sent);
        Handshake();
        FromView(new { jsonrpc = "2.0", method = "ui/notifications/size-changed", @params = new { width = 400, height = 312 } });
        FromView(new { jsonrpc = "2.0", method = "notifications/message", @params = new { level = "info", data = "ready" } });
        Assert.Equal(["ui/notifications/size-changed", "notifications/message"], notices.Select(n => n.Method));
        Assert.Equal(312, notices[0].Params["height"]!.GetValue<int>());
    }

    [Fact]
    public void Display_mode_names_follow_the_spec()
    {
        Assert.Equal("fullscreen", McpAppProtocol.SpecName(McpAppDisplayMode.Pane));
        Assert.Equal("pip", McpAppProtocol.SpecName(McpAppDisplayMode.PopOut));
        Assert.Equal(McpAppDisplayMode.Pane, McpAppProtocol.ParseMode("fullscreen"));
        Assert.Equal(McpAppDisplayMode.PopOut, McpAppProtocol.ParseMode("pip"));
        Assert.Null(McpAppProtocol.ParseMode("cinema"));
    }
}
