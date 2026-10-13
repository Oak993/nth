using System.Collections.Concurrent;
using System.Text;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Testing;

namespace Marrow.Services.Tests.MCP;

// PORT-ONLY: web surfaces W18 (DESIGN.md 7.6, ED-NEW-web-w18-2). The upgraded client over streamable HTTP, against a
// scripted loopback server (R-TEST-10: never the internet): the listen stream a server that announces list changes is
// given, the server's own notifications and requests on a reply's event stream, and a server that announces nothing
// being left alone (no extra request is sent to it).
[Trait("Decision", "ED-NEW-web-w18-2")]
public sealed class MCPClientUpgradeHttpTests
{
    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static async Task<MCPHub> Hub(LoopbackHttpServer server)
    {
        var hub = new MCPHub();
        await hub.Seed(new Dictionary<string, MCPServerSpec>(SwiftStringComparer.Instance)
        {
            ["s"] = new MCPServerSpec.Http(server.Url("/mcp").ToString(), ValueMap<string, string>.Empty),
        });
        return hub;
    }

    // Answers every request the client sends until the server is disposed. The handler gets the exchange and its parsed
    // JSON body (null for a GET).
    private static Task Serve(LoopbackHttpServer server, Func<LoopbackExchange, JSONValue?, Task> handle) => Task.Run(async () =>
    {
        try
        {
            while (true)
            {
                var exchange = await server.NextExchange();
                await handle(exchange, exchange.Body.Length == 0 ? null : JSONValue.Parse(exchange.Body));
            }
        }
        catch (Exception error) when (error is OperationCanceledException or TimeoutException or ObjectDisposedException)
        {
            // The test ended and the server was disposed.
        }
    });

    private static Task RespondResult(LoopbackExchange exchange, JSONValue request, string resultJson, params (string Name, string Value)[] headers) =>
        exchange.Respond(200, "OK",
            Bytes("{\"jsonrpc\":\"2.0\",\"id\":" + request["id"]!.JSONValueInt + ",\"result\":" + resultJson + "}"),
            [("Content-Type", "application/json"), .. headers]);

    private static async Task WaitFor(Func<bool> condition)
    {
        var started = DateTime.UtcNow;
        while (!condition())
        {
            if (DateTime.UtcNow - started > LoopbackHttpServer.Guard) throw new TimeoutException("The condition never became true.");
            await Task.Delay(20);
        }
    }

    // The handshake and a counted tools/list, shared by the tests below. announce says whether the server announces list changes.
    private static Func<LoopbackExchange, JSONValue?, Task<bool>> Handshake(bool announce, Func<int, string> toolsFor, Action<int> onList)
    {
        var lists = 0;
        return async (exchange, body) =>
        {
            switch (body?["method"]?.JSONValueString)
            {
                case "initialize":
                    await RespondResult(exchange, body, announce ? "{\"capabilities\":{\"tools\":{\"listChanged\":true}}}" : "{}", ("Mcp-Session-Id", "sess-1"));
                    return true;
                case "notifications/initialized":
                    await exchange.Respond(202, "Accepted", []);
                    return true;
                case "tools/list":
                    lists += 1;
                    onList(lists);
                    await RespondResult(exchange, body, toolsFor(lists));
                    return true;
                default:
                    return false;
            }
        };
    }

    private static string[] Names(IReadOnlyList<MCPHub.Tool> tools) => tools.Select(tool => tool.Name).ToArray();

    [Fact]
    public async Task A_server_that_announces_list_changes_gets_a_listen_stream_and_its_notification_drops_the_tools_cache()
    {
        await using var server = new LoopbackHttpServer();
        var hub = await Hub(server);
        try
        {
            LoopbackExchange? listen = null;
            var handshake = Handshake(true, lists => lists == 1 ? "{\"tools\":[{\"name\":\"a\"}]}" : "{\"tools\":[{\"name\":\"a\"},{\"name\":\"b\"}]}", _ => { });
            _ = Serve(server, async (exchange, body) =>
            {
                if (exchange.Method == "GET")
                {
                    listen = exchange;
                    await exchange.WriteChunkedHead(200, "OK", ("Content-Type", "text/event-stream"));
                    return;
                }
                await handshake(exchange, body);
            });
            var changed = new TaskCompletionSource<MCPListChange>(TaskCreationOptions.RunContinuationsAsynchronously);
            hub.ListChanged += change => changed.TrySetResult(change);

            Assert.Equal(["a"], Names(await hub.Tools().WaitAsync(LoopbackHttpServer.Guard)));
            await WaitFor(() => listen is not null);
            Assert.Equal(["sess-1"], listen!.HeaderValues("Mcp-Session-Id"));
            Assert.Equal(["text/event-stream"], listen.HeaderValues("Accept"));

            await listen.WriteChunk(Bytes("data: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/tools/list_changed\"}\n\n"));
            Assert.Equal(new MCPListChange("s", MCPWire.ListKind.Tools), await changed.Task.WaitAsync(LoopbackHttpServer.Guard));

            Assert.Equal(["a", "b"], Names(await hub.Tools().WaitAsync(LoopbackHttpServer.Guard)));
        }
        finally
        {
            await hub.Reload();
        }
    }

    [Fact]
    public async Task A_ping_on_the_listen_stream_is_answered_with_a_post()
    {
        await using var server = new LoopbackHttpServer();
        var hub = await Hub(server);
        try
        {
            LoopbackExchange? listen = null;
            var replies = new ConcurrentQueue<JSONValue>();
            var handshake = Handshake(true, _ => "{\"tools\":[]}", _ => { });
            _ = Serve(server, async (exchange, body) =>
            {
                if (exchange.Method == "GET")
                {
                    listen = exchange;
                    await exchange.WriteChunkedHead(200, "OK", ("Content-Type", "text/event-stream"));
                    return;
                }
                if (await handshake(exchange, body)) return;
                if (body is not null) replies.Enqueue(body);
                await exchange.Respond(202, "Accepted", []);
            });

            await hub.Tools().WaitAsync(LoopbackHttpServer.Guard);
            await WaitFor(() => listen is not null);
            await listen!.WriteChunk(Bytes("data: {\"jsonrpc\":\"2.0\",\"id\":41,\"method\":\"ping\"}\n\n"));

            await WaitFor(() => !replies.IsEmpty);
            Assert.True(replies.TryDequeue(out var reply));
            Assert.Equal(41, reply!["id"]?.JSONValueInt);
            Assert.Empty(reply["result"]!.JSONValueObject!);
            Assert.Null(reply["method"]);
        }
        finally
        {
            await hub.Reload();
        }
    }

    [Fact]
    public async Task A_refused_listen_stream_ends_quietly_and_the_client_keeps_working()
    {
        await using var server = new LoopbackHttpServer();
        var hub = await Hub(server);
        try
        {
            var gets = 0;
            var handshake = Handshake(true, _ => "{\"tools\":[{\"name\":\"a\"}]}", _ => { });
            _ = Serve(server, async (exchange, body) =>
            {
                if (exchange.Method == "GET")
                {
                    Interlocked.Increment(ref gets);
                    await exchange.Respond(405, "Method Not Allowed", []);
                    return;
                }
                await handshake(exchange, body);
            });

            Assert.Equal(["a"], Names(await hub.Tools().WaitAsync(LoopbackHttpServer.Guard)));
            await WaitFor(() => Volatile.Read(ref gets) == 1);
            await Task.Delay(400);
            Assert.Equal(1, Volatile.Read(ref gets)); // a refusal is final: no retry
        }
        finally
        {
            await hub.Reload();
        }
    }

    [Fact]
    public async Task A_server_that_announces_nothing_is_never_sent_a_get()
    {
        await using var server = new LoopbackHttpServer();
        var hub = await Hub(server);
        try
        {
            var methods = new ConcurrentQueue<string>();
            var handshake = Handshake(false, _ => "{\"tools\":[{\"name\":\"a\"}]}", _ => { });
            _ = Serve(server, async (exchange, body) =>
            {
                methods.Enqueue(exchange.Method);
                if (!await handshake(exchange, body)) await exchange.Respond(202, "Accepted", []);
            });

            Assert.Equal(["a"], Names(await hub.Tools().WaitAsync(LoopbackHttpServer.Guard)));
            await Task.Delay(300);
            Assert.All(methods, method => Assert.Equal("POST", method));
            Assert.Equal(3, methods.Count); // initialize, notifications/initialized, tools/list
        }
        finally
        {
            await hub.Reload();
        }
    }

    [Fact]
    public async Task The_servers_own_requests_on_a_replys_event_stream_are_each_answered_with_a_post()
    {
        await using var server = new LoopbackHttpServer();
        var hub = await Hub(server);
        try
        {
            var replies = new ConcurrentQueue<JSONValue>();
            var handshake = Handshake(false, _ => "{\"tools\":[{\"name\":\"a\"}]}", _ => { });
            _ = Serve(server, async (exchange, body) =>
            {
                if (await handshake(exchange, body)) return;
                if (body?["method"]?.JSONValueString == "tools/call")
                {
                    var frames = "data: {\"jsonrpc\":\"2.0\",\"id\":77,\"method\":\"ping\"}\n\n"
                        + "data: {\"jsonrpc\":\"2.0\",\"id\":88,\"method\":\"roots/list\"}\n\n"
                        + "data: {\"jsonrpc\":\"2.0\",\"id\":" + body["id"]!.JSONValueInt + ",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"done\"}]}}\n\n";
                    await exchange.Respond(200, "OK", Bytes(frames), ("Content-Type", "text/event-stream"));
                    return;
                }
                if (body is not null) replies.Enqueue(body);
                await exchange.Respond(202, "Accepted", []);
            });

            var result = await hub.Call("mcp__s__a", new JSONValue.Null()).WaitAsync(LoopbackHttpServer.Guard);
            Assert.Equal("done", result.Text);

            await WaitFor(() => replies.Count == 2);
            var byId = replies.ToDictionary(reply => reply["id"]!.JSONValueInt!.Value);
            Assert.Empty(byId[77]["result"]!.JSONValueObject!);
            Assert.Equal(-32601, byId[88]["error"]?["code"]?.JSONValueInt);
        }
        finally
        {
            await hub.Reload();
        }
    }

    [Fact]
    public async Task A_list_changed_notification_ahead_of_a_reply_drops_the_tools_cache()
    {
        await using var server = new LoopbackHttpServer();
        var hub = await Hub(server);
        try
        {
            var handshake = Handshake(false, lists => lists == 1 ? "{\"tools\":[{\"name\":\"a\"}]}" : "{\"tools\":[{\"name\":\"a\"},{\"name\":\"b\"}]}", _ => { });
            _ = Serve(server, async (exchange, body) =>
            {
                if (await handshake(exchange, body)) return;
                if (body?["method"]?.JSONValueString == "tools/call")
                {
                    var frames = "data: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/tools/list_changed\"}\n\n"
                        + "data: {\"jsonrpc\":\"2.0\",\"id\":" + body["id"]!.JSONValueInt + ",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]}}\n\n";
                    await exchange.Respond(200, "OK", Bytes(frames), ("Content-Type", "text/event-stream"));
                    return;
                }
                await exchange.Respond(202, "Accepted", []);
            });
            var changed = new TaskCompletionSource<MCPListChange>(TaskCreationOptions.RunContinuationsAsynchronously);
            hub.ListChanged += change => changed.TrySetResult(change);

            Assert.Equal(["a"], Names(await hub.Tools().WaitAsync(LoopbackHttpServer.Guard)));
            await hub.Call("mcp__s__a", new JSONValue.Null()).WaitAsync(LoopbackHttpServer.Guard);
            Assert.Equal(MCPWire.ListKind.Tools, (await changed.Task.WaitAsync(LoopbackHttpServer.Guard)).Kind);

            Assert.Equal(["a", "b"], Names(await hub.Tools().WaitAsync(LoopbackHttpServer.Guard)));
        }
        finally
        {
            await hub.Reload();
        }
    }
}
