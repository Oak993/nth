// PORT-ONLY: Tactus (ED-NEW-pcctl-10; DESIGN.md 3.3, 5.2, critique C10). The nth-pc MCP server over the fake desktop:
// initialize, tools/list, tools/call with text and image parts, the error envelope, a ping and a second call answered while
// a call waits, cancellation by notifications/cancelled, progress notes and id handling.
using System.Text.Json.Nodes;
using Marrow.Core.Models;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-10")]
public sealed class PcControlMcpServerTests
{
    private const string Look = "{\"window\":\"w:1a\",\"screenshot\":\"yes\"}";

    [Fact]
    public async Task Initialize_names_the_server_and_gives_the_instructions_while_on()
    {
        var harness = new PcEngineHarness();
        var server = Server(harness);

        var reply = await Send(server, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\"}}");

        Assert.Equal("2025-06-18", reply["result"]!["protocolVersion"]!.GetValue<string>());
        Assert.Equal("nth-pc", reply["result"]!["serverInfo"]!["name"]!.GetValue<string>());
        Assert.Equal(PcInstructions.Text, reply["result"]!["instructions"]!.GetValue<string>());
        Assert.Equal(1, reply["id"]!.GetValue<int>());
    }

    [Fact]
    public async Task Tools_list_has_every_tool_with_its_schema_and_pc_delegate_only_with_the_fast_helper()
    {
        var harness = new PcEngineHarness();
        var server = Server(harness);

        var tools = (await Send(server, "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}"))["result"]!["tools"]!.AsArray();

        var names = tools.Select(tool => tool!["name"]!.GetValue<string>()).ToList();
        Assert.Equal(PcToolSchemas.All.Where(spec => spec.Name != PcToolSchemas.Delegate).Select(spec => spec.Name), names);
        var observe = tools.Single(tool => tool!["name"]!.GetValue<string>() == PcToolSchemas.Observe)!;
        Assert.Equal("object", observe["inputSchema"]!["type"]!.GetValue<string>());
        Assert.True(observe["annotations"]!["readOnlyHint"]!.GetValue<bool>());
        var click = tools.Single(tool => tool!["name"]!.GetValue<string>() == PcToolSchemas.Click)!;
        Assert.False(click["annotations"]!["readOnlyHint"]!.GetValue<bool>());

        harness.Settings = harness.Settings with { FastHelper = true };
        var withHelper = (await Send(server, "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/list\"}"))["result"]!["tools"]!.AsArray();
        Assert.Contains(withHelper, tool => tool!["name"]!.GetValue<string>() == PcToolSchemas.Delegate);
    }

    [Fact]
    public async Task Without_a_registration_or_while_off_no_tool_is_listed_or_run()
    {
        var harness = new PcEngineHarness();
        harness.Notepad();
        var unregistered = Server(harness, registered: false);

        Assert.Empty((await Send(unregistered, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}"))["result"]!["tools"]!.AsArray());
        Assert.Equal(-32602, (await Send(unregistered, Call(2, PcToolSchemas.Observe, Look)))["error"]!["code"]!.GetValue<int>());

        harness.Settings = harness.Settings with { Enabled = false };
        var off = Server(harness);
        Assert.Empty((await Send(off, "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/list\"}"))["result"]!["tools"]!.AsArray());
        Assert.Equal(-32602, (await Send(off, Call(4, PcToolSchemas.Observe, Look)))["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_look_returns_the_text_part_then_the_screenshot_as_a_jpeg_image_part()
    {
        var harness = new PcEngineHarness();
        harness.Notepad();
        var server = Server(harness);

        var result = (await Send(server, Call(5, PcToolSchemas.Observe, Look)))["result"]!;

        var content = result["content"]!.AsArray();
        Assert.False(result["isError"]!.GetValue<bool>());
        Assert.Equal("text", content[0]!["type"]!.GetValue<string>());
        Assert.StartsWith("ok", content[0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("image", content[1]!["type"]!.GetValue<string>());
        Assert.Equal("image/jpeg", content[1]!["mimeType"]!.GetValue<string>());
        Assert.NotEmpty(Convert.FromBase64String(content[1]!["data"]!.GetValue<string>()));
    }

    [Fact]
    public async Task A_model_without_vision_gets_the_text_part_only()
    {
        var harness = new PcEngineHarness();
        harness.Notepad();
        var server = Server(harness, vision: false);

        var content = (await Send(server, Call(6, PcToolSchemas.Observe, Look)))["result"]!["content"]!.AsArray();

        Assert.Single(content);
        Assert.Equal("text", content[0]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_failed_step_is_a_tool_error_with_its_recovery_not_a_protocol_error()
    {
        var harness = new PcEngineHarness();
        var server = Server(harness);

        var reply = await Send(server, Call(7, PcToolSchemas.Observe, "{\"window\":\"w:99\"}"));

        Assert.Null(reply["error"]);
        Assert.True(reply["result"]!["isError"]!.GetValue<bool>());
        Assert.StartsWith("error: ", reply["result"]!["content"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Protocol_errors_have_their_codes_and_notifications_get_no_reply()
    {
        var harness = new PcEngineHarness();
        var server = Server(harness);

        Assert.Equal(-32700, (await Send(server, "{not json"))["error"]!["code"]!.GetValue<int>());
        Assert.Equal(-32600, (await Send(server, "[1,2]"))["error"]!["code"]!.GetValue<int>());
        Assert.Equal(-32601, (await Send(server, "{\"jsonrpc\":\"2.0\",\"id\":8,\"method\":\"resources/list\"}"))["error"]!["code"]!.GetValue<int>());
        Assert.Equal(-32602, (await Send(server, Call(9, "pc_unknown", "{}")))["error"]!["code"]!.GetValue<int>());
        Assert.Equal(-32602, (await Send(server, "{\"jsonrpc\":\"2.0\",\"id\":10,\"method\":\"tools/call\",\"params\":{\"name\":\"pc_observe\",\"arguments\":[1]}}"))["error"]!["code"]!.GetValue<int>());
        Assert.Null(await server.Handle("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}", CancellationToken.None));
    }

    [Fact]
    public async Task A_ping_and_a_second_call_are_answered_while_a_call_waits_for_the_user()
    {
        var harness = new PcEngineHarness();
        harness.Notepad();
        harness.Notepad(handle: 0x2b, processId: 202, exe: "mspaint.exe");
        harness.Approver.Standings[("mspaint.exe", PcCapability.See)] = PcApproval.Always;
        harness.Approver.Hold = new TaskCompletionSource<PcApproval>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = Server(harness);

        var waiting = server.Handle(Call(11, PcToolSchemas.Observe, Look), CancellationToken.None);
        await WaitUntil(() => harness.Approver.Asked.Count == 1);

        var ping = await Send(server, "{\"jsonrpc\":\"2.0\",\"id\":12,\"method\":\"ping\"}");
        var other = await Send(server, Call(13, PcToolSchemas.Observe, "{\"window\":\"w:2b\",\"screenshot\":\"no\"}"));

        Assert.Equal(12, ping["id"]!.GetValue<int>());
        Assert.False(other["result"]!["isError"]!.GetValue<bool>());
        Assert.False(waiting.IsCompleted);
        Assert.Equal(1, server.Running);

        harness.Approver.Hold.SetResult(PcApproval.Chat);
        var first = JsonNode.Parse((await waiting)!)!;
        Assert.Equal(11, first["id"]!.GetValue<int>());
        Assert.False(first["result"]!["isError"]!.GetValue<bool>());
        Assert.Equal(0, server.Running);
    }

    [Fact]
    public async Task Notifications_cancelled_cancels_that_call_which_then_sends_nothing()
    {
        var harness = new PcEngineHarness();
        harness.Notepad();
        harness.Approver.Hold = new TaskCompletionSource<PcApproval>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = Server(harness);

        var waiting = server.Handle(Call(14, PcToolSchemas.Observe, Look), CancellationToken.None);
        await WaitUntil(() => harness.Approver.Asked.Count == 1);
        // A string id "14" is another call: it cancels nothing.
        Assert.Null(await server.Handle("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/cancelled\",\"params\":{\"requestId\":\"14\"}}", CancellationToken.None));
        Assert.False(waiting.IsCompleted);

        Assert.Null(await server.Handle("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/cancelled\",\"params\":{\"requestId\":14}}", CancellationToken.None));

        Assert.Null(await waiting.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(0, server.Running);
    }

    [Fact]
    public async Task A_closed_connection_cancels_its_calls()
    {
        var harness = new PcEngineHarness();
        harness.Notepad();
        harness.Approver.Hold = new TaskCompletionSource<PcApproval>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = Server(harness);
        using var connection = new CancellationTokenSource();

        var waiting = server.Handle(Call(15, PcToolSchemas.Observe, Look), connection.Token);
        await WaitUntil(() => harness.Approver.Asked.Count == 1);
        await connection.CancelAsync();

        Assert.Null(await waiting.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task A_running_id_cannot_be_reused()
    {
        var harness = new PcEngineHarness();
        harness.Notepad();
        harness.Approver.Hold = new TaskCompletionSource<PcApproval>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = Server(harness);

        var waiting = server.Handle(Call(16, PcToolSchemas.Observe, Look), CancellationToken.None);
        await WaitUntil(() => harness.Approver.Asked.Count == 1);

        Assert.Equal(-32600, (await Send(server, Call(16, PcToolSchemas.ListApps, "{}")))["error"]!["code"]!.GetValue<int>());

        harness.Approver.Hold.SetResult(PcApproval.Chat);
        Assert.NotNull(await waiting);
    }

    [Fact]
    public async Task A_call_with_a_progress_token_reports_progress_until_it_ends()
    {
        var harness = new PcEngineHarness();
        harness.Notepad();
        harness.Approver.Hold = new TaskCompletionSource<PcApproval>(TaskCreationOptions.RunContinuationsAsynchronously);
        var notes = new List<string>();
        var server = Server(harness, notes: notes);

        var waiting = server.Handle(
            "{\"jsonrpc\":\"2.0\",\"id\":17,\"method\":\"tools/call\",\"params\":{\"name\":\"pc_observe\",\"arguments\":" + Look + ",\"_meta\":{\"progressToken\":\"p-17\"}}}",
            CancellationToken.None);
        await WaitUntil(() => { lock (notes) return notes.Count >= 2; });
        harness.Approver.Hold.SetResult(PcApproval.Chat);
        await waiting;
        int after;
        lock (notes) after = notes.Count;
        await Task.Delay(120);

        List<JsonNode> parsed;
        lock (notes) parsed = [.. notes.Select(note => JsonNode.Parse(note)!)];
        Assert.Equal(after, parsed.Count);
        Assert.All(parsed, note => Assert.Equal("notifications/progress", note["method"]!.GetValue<string>()));
        Assert.All(parsed, note => Assert.Equal("p-17", note["params"]!["progressToken"]!.GetValue<string>()));
        Assert.Equal(new[] { 1, 2 }, parsed.Take(2).Select(note => note["params"]!["progress"]!.GetValue<int>()));
    }

    [Fact]
    public async Task A_call_without_a_progress_token_sends_no_progress()
    {
        var harness = new PcEngineHarness();
        harness.Notepad();
        var notes = new List<string>();
        var server = Server(harness, notes: notes);

        await Send(server, Call(18, PcToolSchemas.Observe, Look));

        Assert.Empty(notes);
    }

    [Fact]
    public void Content_puts_the_screenshot_first_then_each_detail_crop()
    {
        var result = PcResult.Success(body: "tree") with
        {
            Image = new PcImage([1, 2, 3], 10, 10, "o1"),
            Details = [new PcImage([4], 5, 5, "o1"), new PcImage([5], 5, 5, "o1")],
        };

        var content = PcControlMcpServer.Content(result, vision: true)["content"]!.AsArray();
        var blind = PcControlMcpServer.Content(result, vision: false)["content"]!.AsArray();

        Assert.Equal(new[] { "text", "image", "image", "image" }, content.Select(part => part!["type"]!.GetValue<string>()));
        Assert.Equal(Convert.ToBase64String([1, 2, 3]), content[1]!["data"]!.GetValue<string>());
        Assert.Equal(Convert.ToBase64String([5]), content[3]!["data"]!.GetValue<string>());
        Assert.Single(blind);
    }

    private static PcControlMcpServer Server(PcEngineHarness harness, bool vision = true, bool registered = true, List<string>? notes = null)
    {
        var engine = harness.Engine(vision: vision);
        var registration = registered ? new PcRegistration("chat-1", ProviderKind.Claude, "claude-test", vision) : null;
        Func<string, Task>? notify = notes is null ? null : line =>
        {
            lock (notes) notes.Add(line);
            return Task.CompletedTask;
        };
        return new PcControlMcpServer(registration, _ => engine, () => harness.Settings, notify) { ProgressMs = 20 };
    }

    private static string Call(int id, string tool, string arguments) =>
        "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"tools/call\",\"params\":{\"name\":\"" + tool + "\",\"arguments\":" + arguments + "}}";

    private static async Task<JsonNode> Send(PcControlMcpServer server, string line) =>
        JsonNode.Parse((await server.Handle(line, CancellationToken.None))!)!;

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 10_000;
        while (!condition())
        {
            Assert.True(Environment.TickCount64 < deadline, "timed out waiting");
            await Task.Delay(5);
        }
    }
}
