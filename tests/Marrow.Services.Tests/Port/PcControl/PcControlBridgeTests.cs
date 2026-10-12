// PORT-ONLY: Tactus (ED-NEW-pcctl-10; DESIGN.md 3.3). The nth-pc relay entry and its connection: the entry only while
// Tactus is on and bound, the registration a token stands for, one engine per chat, the turn hooks, and a connection served
// over an in-process named pipe (token line, then concurrent JSON-RPC lines). No provider is launched.
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-10")]
public sealed class PcControlBridgeTests
{
    [Fact]
    public void No_entry_while_unbound_or_off()
    {
        var harness = new PcEngineHarness();
        using (PcControlBridge.Parts.Override(() => null))
            Assert.Null(PcControlBridge.Server(Configuration(ProviderKind.Claude)));

        harness.Settings = harness.Settings with { Enabled = false };
        using (PcControlBridge.Parts.Override(() => harness.Parts))
            Assert.Null(PcControlBridge.Server(Configuration(ProviderKind.Claude)));
    }

    [Fact]
    public void The_entry_launches_the_app_as_a_relay_with_a_token_for_the_chat()
    {
        var harness = new PcEngineHarness();
        var thread = Guid.NewGuid();
        using var parts = PcControlBridge.Parts.Override(() => harness.Parts);
        using var executable = PcControlBridge.Executable.Override(() => @"C:\nth\Marrow.App.exe");

        var server = PcControlBridge.Server(Configuration(ProviderKind.Claude, thread, "claude-test"))!;

        Assert.Equal("nth-pc", server.Id);
        Assert.Equal(@"C:\nth\Marrow.App.exe", server.Command);
        Assert.Equal(3, server.Args.Count);
        Assert.Equal(PcControlBridge.Command, server.Args[0]);
        Assert.StartsWith("MarrowPc-", server.Args[1], StringComparison.Ordinal);
        var registration = PcControlBridge.Registration(server.Args[2])!;
        Assert.Equal(thread.ToString("D"), registration.Thread);
        Assert.Equal(ProviderKind.Claude, registration.Provider);
        Assert.Equal("claude-test", registration.Model);
        Assert.True(registration.Vision);
        Assert.Null(registration.Head);
        Assert.Null(PcControlBridge.Registration("not-a-token"));
        Assert.Null(PcControlBridge.Registration((string?)null));
    }

    [Fact]
    public void A_provider_without_images_registers_without_vision_and_a_head_is_named()
    {
        var harness = new PcEngineHarness();
        using var parts = PcControlBridge.Parts.Override(() => harness.Parts);

        var registration = PcControlBridge.Registration(Configuration(ProviderKind.Grok, head: true))!;

        Assert.False(registration.Vision);
        Assert.Equal("a Hydra head", registration.Head);
    }

    [Fact]
    public void One_engine_serves_each_chat_until_the_chat_is_forgotten()
    {
        var harness = new PcEngineHarness();
        using var parts = PcControlBridge.Parts.Override(() => harness.Parts);
        var thread = "chat-" + Guid.NewGuid().ToString("N");
        var registration = new PcRegistration(thread, ProviderKind.Codex, "gpt-test", true);

        var first = PcControlBridge.Engine(registration);
        var again = PcControlBridge.Engine(registration with { Model = "other" });
        var elsewhere = PcControlBridge.Engine(registration with { Thread = thread + "-b" });

        Assert.NotNull(first);
        Assert.Same(first, again);
        Assert.NotSame(first, elsewhere);
        PcControlBridge.StopTurn(thread);
        PcControlBridge.EndTurn(thread);
        PcControlBridge.Forget(thread);
        PcControlBridge.Forget(thread + "-b");
        Assert.NotSame(first, PcControlBridge.Engine(registration));
        PcControlBridge.Forget(thread);
    }

    [Fact]
    public void No_engine_while_unbound()
    {
        using var parts = PcControlBridge.Parts.Override(() => null);

        Assert.Null(PcControlBridge.Engine(new PcRegistration("chat-" + Guid.NewGuid().ToString("N"), ProviderKind.Claude, null, true)));
    }

    [Fact]
    public async Task A_connection_sends_its_token_then_gets_concurrent_replies_and_closing_it_ends_the_serve()
    {
        var harness = new PcEngineHarness();
        harness.Notepad();
        harness.Approver.Hold = new TaskCompletionSource<PcApproval>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var parts = PcControlBridge.Parts.Override(() => harness.Parts);
        var server = PcControlBridge.Server(Configuration(ProviderKind.Claude, Guid.NewGuid()))!;
        var name = "MarrowPcTest-" + Guid.NewGuid().ToString("N");
        await using var host = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var accepted = host.WaitForConnectionAsync();
        await client.ConnectAsync(5000);
        await accepted;
        var serving = PcControlBridge.Serve(host, CancellationToken.None);
        var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);

        await writer.WriteLineAsync(server.Args[2]);
        await writer.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"pc_observe\",\"arguments\":{\"window\":\"w:1a\",\"screenshot\":\"no\"}}}");
        await writer.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"ping\"}");
        var ping = JsonNode.Parse((await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)))!)!;
        await writer.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/list\"}");
        var list = JsonNode.Parse((await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)))!)!;
        harness.Approver.Hold.SetResult(PcApproval.Chat);
        var look = JsonNode.Parse((await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)))!)!;

        Assert.Equal(2, ping["id"]!.GetValue<int>());
        Assert.Equal(3, list["id"]!.GetValue<int>());
        Assert.NotEmpty(list["result"]!["tools"]!.AsArray());
        Assert.Equal(1, look["id"]!.GetValue<int>());
        Assert.False(look["result"]!["isError"]!.GetValue<bool>());

        writer.Dispose();
        reader.Dispose();
        await client.DisposeAsync();
        await serving.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task An_unknown_token_gets_a_connection_with_no_tools()
    {
        var harness = new PcEngineHarness();
        using var parts = PcControlBridge.Parts.Override(() => harness.Parts);
        var name = "MarrowPcTest-" + Guid.NewGuid().ToString("N");
        await using var host = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var accepted = host.WaitForConnectionAsync();
        await client.ConnectAsync(5000);
        await accepted;
        var serving = PcControlBridge.Serve(host, CancellationToken.None);
        var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);

        await writer.WriteLineAsync(Guid.NewGuid().ToString("N"));
        await writer.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}");
        var list = JsonNode.Parse((await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)))!)!;

        Assert.Empty(list["result"]!["tools"]!.AsArray());
        writer.Dispose();
        reader.Dispose();
        await client.DisposeAsync();
        await serving.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData("")]
    [InlineData("--marrow-pc-mcp|MarrowPc-x|y")]
    [InlineData("--marrow-advisor-mcp|MarrowPc-0123456789abcdef0123456789abcdef|0123456789abcdef0123456789abcdef")]
    [InlineData("--marrow-pc-mcp|Other-0123456789abcdef0123456789abcdef|0123456789abcdef0123456789abcdef")]
    [InlineData("--marrow-pc-mcp|MarrowPc-0123456789abcdef0123456789abcdef")]
    public async Task The_relay_refuses_malformed_arguments_without_connecting(string joined)
    {
        var args = joined.Length == 0 ? [] : joined.Split('|');
        using var input = new MemoryStream();
        using var output = new MemoryStream();

        Assert.Equal(1, await PcControlBridge.RunClient(args, input, output));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void The_acp_entry_carries_the_relay_command()
    {
        var entry = PcControlBridge.AcpEntry(new MCPResolvedServer("nth-pc", "nth-pc") { Command = "nth.exe", Args = new ValueList<string>(["--marrow-pc-mcp", "p", "t"]) });

        Assert.Equal("nth-pc", entry["name"]!.JSONValueString);
        Assert.Equal("nth.exe", entry["command"]!.JSONValueString);
        Assert.Equal(3, entry["args"]!.JSONValueArray!.Count);
    }

    private static SessionConfiguration Configuration(ProviderKind provider, Guid? thread = null, string? model = null, bool head = false) => new()
    {
        Provider = provider,
        WorkingDirectory = SwiftURL.Create("marrow-native-path:C:/work")!,
        Environment = ValueMap<string, string>.Empty,
        Model = model,
        RuntimeMode = RuntimeMode.Supervised,
        InteractionMode = InteractionMode.Build,
        IgnisThreadID = thread,
        IsHydraHead = head,
    };
}
