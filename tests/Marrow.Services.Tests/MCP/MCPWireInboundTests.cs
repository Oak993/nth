using System.Text;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Testing;

namespace Marrow.Services.Tests.MCP;

// PORT-ONLY: web surfaces W18 (DESIGN.md 7.6, ED-NEW-web-w18-2). What the server says on its own: requests (ping, and
// methods nth never advertised) that must be answered, notifications (list changes), the paged resource lists, the
// streamable HTTP reader that hands those to the client, and the listen stream a server uses when it has list changes
// to announce. Pure functions plus a loopback HTTP server (R-TEST-10: never the internet).
[Trait("Decision", "ED-NEW-web-w18-2")]
public sealed class MCPWireInboundTests
{
    private static JSONValue Json(string text) => JSONValue.Parse(text)!;

    private static Dictionary<string, string> NoHeaders => [];

    // MARK: - Classify and Reply

    [Theory]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"ping"}""", MCPWire.MessageKind.Request)]
    [InlineData("""{"jsonrpc":"2.0","id":"abc","method":"roots/list"}""", MCPWire.MessageKind.Request)]
    [InlineData("""{"jsonrpc":"2.0","method":"notifications/tools/list_changed"}""", MCPWire.MessageKind.Notification)]
    [InlineData("""{"jsonrpc":"2.0","id":null,"method":"notifications/x"}""", MCPWire.MessageKind.Notification)]
    [InlineData("""{"jsonrpc":"2.0","id":1,"result":{}}""", MCPWire.MessageKind.Response)]
    [InlineData("""{"jsonrpc":"2.0","id":"a","error":{"message":"x"}}""", MCPWire.MessageKind.Response)]
    [InlineData("""{"jsonrpc":"2.0"}""", MCPWire.MessageKind.Invalid)]
    [InlineData("""{"method":""}""", MCPWire.MessageKind.Invalid)]
    [InlineData("""{"method":5,"id":1}""", MCPWire.MessageKind.Response)]
    [InlineData("[]", MCPWire.MessageKind.Invalid)]
    [InlineData("\"text\"", MCPWire.MessageKind.Invalid)]
    public void A_message_is_a_request_a_notification_or_a_response_by_its_members(string message, MCPWire.MessageKind expected)
    {
        Assert.Equal(expected, MCPWire.Classify(Json(message)));
    }

    [Fact]
    public void Ping_is_answered_with_an_empty_result_and_the_same_id()
    {
        var number = MCPWire.Reply(Json("""{"jsonrpc":"2.0","id":7,"method":"ping"}"""));
        Assert.Equal("2.0", number["jsonrpc"]?.JSONValueString);
        Assert.Equal(7, number["id"]?.JSONValueInt);
        Assert.Empty(number["result"]!.JSONValueObject!);
        Assert.Null(number["error"]);

        var text = MCPWire.Reply(Json("""{"jsonrpc":"2.0","id":"req-9","method":"ping"}"""));
        Assert.Equal("req-9", text["id"]?.JSONValueString);
    }

    [Theory]
    [InlineData("roots/list")]
    [InlineData("sampling/createMessage")]
    [InlineData("elicitation/create")]
    [InlineData("something/unknown")]
    public void A_method_nth_did_not_advertise_is_answered_method_not_found(string method)
    {
        var reply = MCPWire.Reply(Json("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"" + method + "\"}"));
        Assert.Equal(3, reply["id"]?.JSONValueInt);
        Assert.Equal(-32601, reply["error"]?["code"]?.JSONValueInt);
        Assert.Equal("Method not found", reply["error"]?["message"]?.JSONValueString);
        Assert.Null(reply["result"]);
    }

    [Fact]
    public void List_changed_notifications_name_what_changed_and_other_methods_name_nothing()
    {
        Assert.Equal(MCPWire.ListKind.Tools, MCPWire.ListChangedKind("notifications/tools/list_changed"));
        Assert.Equal(MCPWire.ListKind.Resources, MCPWire.ListChangedKind("notifications/resources/list_changed"));
        Assert.Equal(MCPWire.ListKind.Prompts, MCPWire.ListChangedKind("notifications/prompts/list_changed"));
        Assert.Null(MCPWire.ListChangedKind("notifications/resources/updated"));
        Assert.Null(MCPWire.ListChangedKind("notifications/message"));
        Assert.Null(MCPWire.ListChangedKind(null));
    }

    [Fact]
    public void A_server_announces_list_changes_in_its_capabilities_and_an_unknown_server_is_asked_for_everything()
    {
        Assert.True(MCPWire.AnnouncesListChanges(Json("""{"tools":{"listChanged":true}}""")));
        Assert.True(MCPWire.AnnouncesListChanges(Json("""{"resources":{"listChanged":true},"tools":{}}""")));
        Assert.False(MCPWire.AnnouncesListChanges(Json("""{"tools":{"listChanged":false},"resources":{"subscribe":true}}""")));
        Assert.False(MCPWire.AnnouncesListChanges(Json("{}")));
        Assert.False(MCPWire.AnnouncesListChanges(null));

        Assert.True(MCPWire.MaySupport(null, "resources"));
        Assert.True(MCPWire.MaySupport(Json("{}"), "resources"));
        Assert.True(MCPWire.MaySupport(Json("""{"resources":{}}"""), "resources"));
        Assert.False(MCPWire.MaySupport(Json("""{"tools":{}}"""), "resources"));
    }

    // MARK: - Paging any list

    [Fact]
    public async Task List_pages_by_the_named_member_and_feeds_each_cursor_back()
    {
        var seen = new List<string?>();
        Task<JSONValue> Page(JSONValue parameters)
        {
            var cursor = parameters["cursor"]?.JSONValueString;
            seen.Add(cursor);
            return Task.FromResult(cursor switch
            {
                null => Json("""{"result":{"resources":[{"uri":"a"},{"uri":"b"}],"nextCursor":"two"}}"""),
                "two" => Json("""{"result":{"resources":[{"uri":"c"}],"nextCursor":""}}"""),
                _ => Json("""{"result":{"resources":[{"uri":"never"}]}}"""),
            });
        }

        var items = await MCPWire.List("resources", Page);

        Assert.Equal(["a", "b", "c"], items.Select(item => item["uri"]!.JSONValueString!).ToArray());
        Assert.Equal<string?>(new string?[] { null, "two" }, seen);
    }

    [Fact]
    public async Task List_of_a_member_the_reply_does_not_have_is_empty()
    {
        var items = await MCPWire.List("resourceTemplates", _ => Task.FromResult(Json("""{"result":{"resources":[{"uri":"a"}]}}""")));
        Assert.Empty(items);
    }

    // MARK: - Post hands the server's own messages on

    [Fact]
    public async Task Post_hands_notifications_and_requests_before_the_reply_to_the_inbound_handler_in_order()
    {
        await using var server = new LoopbackHttpServer();
        var inbound = new List<JSONValue>();

        var posting = MCPWire.Post(server.Url("/mcp"), NoHeaders, null, MCPWire.Request(5, "tools/list", null), 5, 30, inbound.Add);

        var exchange = await server.NextExchange();
        var frames = "data: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/tools/list_changed\"}\n\n"
            // A request of the server's own that happens to use our number: it is not our reply.
            + "data: {\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"ping\"}\n\n"
            + "data: {\"jsonrpc\":\"2.0\",\"id\":5,\"result\":{\"tools\":[]}}\n\n";
        await exchange.Respond(200, "OK", Encoding.UTF8.GetBytes(frames), ("Content-Type", "text/event-stream"));

        var reply = await posting.WaitAsync(LoopbackHttpServer.Guard);

        Assert.Equal(["notifications/tools/list_changed", "ping"], inbound.Select(message => MCPWire.MethodOf(message)!).ToArray());
        Assert.NotNull(reply.Body);
        Assert.Equal(MCPWire.MessageKind.Response, MCPWire.Classify(reply.Body!));
        Assert.NotNull(reply.Body!["result"]?["tools"]);
    }

    [Fact]
    public async Task Post_without_an_inbound_handler_still_skips_the_servers_own_messages()
    {
        await using var server = new LoopbackHttpServer();
        var posting = MCPWire.Post(server.Url("/mcp"), NoHeaders, null, MCPWire.Request(2, "tools/list", null), 2, 30);
        var exchange = await server.NextExchange();
        var frames = "data: {\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"ping\"}\n\ndata: {\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"ok\":true}}\n\n";
        await exchange.Respond(200, "OK", Encoding.UTF8.GetBytes(frames), ("Content-Type", "text/event-stream"));

        var reply = await posting.WaitAsync(LoopbackHttpServer.Guard);

        Assert.True(reply.Body!["result"]?["ok"]?.JSONValueBool);
    }

    // MARK: - Listen

    [Fact]
    public async Task Listen_sends_a_get_for_an_event_stream_with_the_session_and_hands_every_message_on()
    {
        await using var server = new LoopbackHttpServer();
        var received = new List<JSONValue>();

        var listening = MCPWire.Listen(server.Url("/mcp"), new Dictionary<string, string> { ["X-Api-Key"] = "k1" }, "sess-1", received.Add);

        var exchange = await server.NextExchange();
        Assert.Equal("GET", exchange.Method);
        Assert.Equal("/mcp", exchange.Target);
        Assert.Equal(["text/event-stream"], exchange.HeaderValues("Accept"));
        Assert.Equal(["2025-06-18"], exchange.HeaderValues("MCP-Protocol-Version"));
        Assert.Equal(["sess-1"], exchange.HeaderValues("Mcp-Session-Id"));
        Assert.Equal(["k1"], exchange.HeaderValues("X-Api-Key"));
        Assert.Empty(exchange.Body);

        await exchange.WriteChunkedHead(200, "OK", ("Content-Type", "text/event-stream"));
        await exchange.WriteChunk(Encoding.UTF8.GetBytes(": keep-alive\n\ndata: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/tools/list_changed\"}\n\n"));
        await exchange.WriteChunk(Encoding.UTF8.GetBytes("data: not json\n\ndata: {\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"ping\"}\n\n"));
        await exchange.EndChunks();

        Assert.Equal(MCPWire.ListenOutcome.Ended, await listening.WaitAsync(LoopbackHttpServer.Guard));
        Assert.Equal(["notifications/tools/list_changed", "ping"], received.Select(message => MCPWire.MethodOf(message)!).ToArray());
    }

    [Fact]
    public async Task Listen_to_a_server_that_refuses_is_unsupported_and_does_not_throw()
    {
        await using var server = new LoopbackHttpServer();
        var listening = MCPWire.Listen(server.Url("/mcp"), NoHeaders, null, _ => { });
        var exchange = await server.NextExchange();
        await exchange.Respond(405, "Method Not Allowed", []);
        Assert.Equal(MCPWire.ListenOutcome.Unsupported, await listening.WaitAsync(LoopbackHttpServer.Guard));
    }

    [Fact]
    public async Task Listen_to_an_answer_that_is_not_an_event_stream_is_unsupported()
    {
        await using var server = new LoopbackHttpServer();
        var listening = MCPWire.Listen(server.Url("/mcp"), NoHeaders, null, _ => { });
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", "{}"u8.ToArray(), ("Content-Type", "application/json"));
        Assert.Equal(MCPWire.ListenOutcome.Unsupported, await listening.WaitAsync(LoopbackHttpServer.Guard));
    }

    [Fact]
    public async Task Listen_to_a_server_that_cannot_be_reached_has_failed()
    {
        var outcome = await MCPWire.Listen(LoopbackHttpServer.RefusedUrl("/mcp"), NoHeaders, null, _ => { }).WaitAsync(LoopbackHttpServer.Guard);
        Assert.Equal(MCPWire.ListenOutcome.Failed, outcome);
    }
}
