using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.Local;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

[Collection(OpenAICompatibleSessionCollection.Name)]
[Trait("Rule", "R-TEST-10")]
[Trait("Feature", "MA-3")]
public sealed class LocalSessionTests
{
    [Fact]
    public async Task A_keyless_Local_session_streams_and_runs_a_Marrow_read_tool_then_returns_the_result()
    {
        await using var server = new LocalProviderLoopback();
        using var runtime = new TestRuntime();
        using var scratch = new LocalProviderFolder(atFileSystemRoot: true); // this collection disables all test parallelism
        using var storage = Storage.Locations.Override(StorageLocations.Create(
            Path.Combine(scratch.Root, "storage"), Path.Combine(scratch.Root, "home")));
        File.WriteAllText(Path.Combine(scratch.Root, "proof.txt"), "native local proof");
        server.Reply("/v1/models", """{"data":[{"id":"tiny","context_length":8192,"supports_tool_calling":true}]}""");
        using var discovery = new LocalDiscovery([server.BaseURL]);
        var entry = Assert.Single(await discovery.Discover());
        var target = discovery.Resolve(Assert.Single(entry.Models).SelectionId)!;
        var session = new LocalSession(Configuration(scratch.Root), target);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        session.SeedMcpToolsForTests([]);
        server.Reply("/v1/chat/completions", "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"read1\",\"function\":{\"name\":\"read_file\",\"arguments\":\"{\\\"path\\\":\\\"proof.txt\\\"}\"}}]}}]}\n\ndata: [DONE]\n\n");
        server.Reply("/v1/chat/completions", "data: {\"choices\":[{\"delta\":{\"content\":\"Read complete\"}}],\"usage\":{\"prompt_tokens\":42,\"completion_tokens\":4}}\n\ndata: [DONE]\n\n");
        await Task.Run(() => runtime.Loop.Run(async () =>
        {
            await session.Start();
            await session.Send(new TurnInput { Images = ValueList<Attachment>.Empty, Text = "Read proof.txt", Model = target.Model.SelectionId,
                RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build });
        }, LoopbackHttpServer.Guard));
        var chats = server.Requests.Where(request => request.Target == "/v1/chat/completions").ToArray();
        Assert.Equal(2, chats.Length);
        Assert.All(chats, request => Assert.Empty(request.HeaderValues("Authorization")));
        var first = JSONValue.Parse(Encoding.UTF8.GetString(chats[0].Body))!;
        Assert.Equal("tiny", first["model"]?.JSONValueString);
        Assert.Equal(2048, first["max_tokens"]?.JSONValueInt);
        Assert.Contains(first["tools"]!.JSONValueArray!, tool => tool["function"]?["name"]?.JSONValueString == "read_file");
        var second = JSONValue.Parse(Encoding.UTF8.GetString(chats[1].Body))!;
        var result = Assert.Single(second["messages"]!.JSONValueArray!, message => message["role"]?.JSONValueString == "tool");
        Assert.Equal("read1", result["tool_call_id"]?.JSONValueString);
        Assert.Contains("native local proof", result["content"]!.JSONValueString!, StringComparison.Ordinal);
        Assert.Contains(events, item => item is ProviderEvent.TurnCompleted { Status: TurnStatus.Completed });
        Assert.Contains(events, item => item is ProviderEvent.Usage { Value.WindowTokens: 8192 });
        session.Stop();
    }

    [Theory]
    [InlineData("false")]
    [InlineData("null")]
    public async Task Unsupported_or_unknown_tools_send_text_only_even_if_an_unrelated_API_key_is_supplied(string support)
    {
        await using var server = new LocalProviderLoopback();
        using var runtime = new TestRuntime();
        using var scratch = new LocalProviderFolder();
        server.Reply("/v1/models", "{\"data\":[{\"id\":\"text\",\"supports_tool_calling\":" + support + "}]}");
        using var discovery = new LocalDiscovery([server.BaseURL]);
        var model = Assert.Single(Assert.Single(await discovery.Discover()).Models);
        var session = new LocalSession(Configuration(scratch.Root) with { ApiKey = "hosted-key-never-send" }, discovery.Resolve(model.SelectionId)!);
        session.SeedMcpToolsForTests([]);
        server.Reply("/v1/chat/completions", "data: {\"choices\":[{\"delta\":{\"content\":\"Text only\"}}]}\n\ndata: [DONE]\n\n");
        await Task.Run(() => runtime.Loop.Run(async () =>
        {
            await session.Start();
            await session.Send(new TurnInput { Images = ValueList<Attachment>.Empty, Text = "Hello", RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build });
        }, LoopbackHttpServer.Guard));
        var chat = Assert.Single(server.Requests, request => request.Target == "/v1/chat/completions");
        var body = Encoding.UTF8.GetString(chat.Body);
        Assert.DoesNotContain("\"tools\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("tool_choice", body, StringComparison.Ordinal);
        Assert.DoesNotContain("hosted-key", body, StringComparison.Ordinal);
        Assert.Empty(chat.HeaderValues("Authorization"));
        session.Stop();
    }

    [Fact]
    public async Task Selecting_the_second_server_with_the_same_model_id_sends_chat_only_there()
    {
        await using var first = new LocalProviderLoopback();
        await using var second = new LocalProviderLoopback();
        using var runtime = new TestRuntime();
        using var scratch = new LocalProviderFolder();
        foreach (var server in new[] { first, second })
            server.Reply("/v1/models", """{"data":[{"id":"same","context_length":8192,"supports_tool_calling":true}]}""");
        using var discovery = new LocalDiscovery([first.BaseURL, second.BaseURL]);
        var entries = await discovery.Discover();
        var target = discovery.Resolve(entries[1].Models[0].SelectionId)!;
        var session = new LocalSession(Configuration(scratch.Root), target);
        session.SeedMcpToolsForTests([]);
        second.Reply("/v1/chat/completions", "data: {\"choices\":[{\"delta\":{\"content\":\"Second server\"}}]}\n\ndata: [DONE]\n\n");
        await Task.Run(() => runtime.Loop.Run(async () =>
        {
            await session.Start();
            await session.Send(new TurnInput { Images = ValueList<Attachment>.Empty, Text = "Hello", Model = target.Model.SelectionId,
                RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build });
        }, LoopbackHttpServer.Guard));
        Assert.DoesNotContain(first.Requests, request => request.Target == "/v1/chat/completions");
        Assert.Single(second.Requests, request => request.Target == "/v1/chat/completions");
        session.Stop();
    }

    [Fact]
    public async Task A_declined_supervised_file_write_preserves_the_file_and_returns_a_tool_result()
    {
        await using var server = new LocalProviderLoopback();
        using var runtime = new TestRuntime();
        using var scratch = new LocalProviderFolder();
        var path = Path.Combine(scratch.Root, "proof.txt");
        File.WriteAllText(path, "original");
        server.Reply("/v1/models", """{"data":[{"id":"tiny","supports_tool_calling":true}]}""");
        using var discovery = new LocalDiscovery([server.BaseURL]);
        var entry = Assert.Single(await discovery.Discover());
        var session = new LocalSession(Configuration(scratch.Root), discovery.Resolve(entry.Models[0].SelectionId)!);
        var events = new List<ProviderEvent>();
        session.OnEvent = item =>
        {
            events.Add(item);
            if (item is ProviderEvent.Approval approval)
                runtime.Loop.Enqueue(() => session.ResolveApproval(approval.Value.Id, "decline"));
        };
        session.SeedMcpToolsForTests([]);
        server.Reply("/v1/chat/completions", "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"write1\",\"function\":{\"name\":\"write_file\",\"arguments\":\"{\\\"path\\\":\\\"proof.txt\\\",\\\"content\\\":\\\"changed\\\"}\"}}]}}]}\n\ndata: [DONE]\n\n");
        server.Reply("/v1/chat/completions", "data: {\"choices\":[{\"delta\":{\"content\":\"Declined\"}}]}\n\ndata: [DONE]\n\n");
        await Task.Run(() => runtime.Loop.Run(async () =>
        {
            await session.Start();
            await session.Send(new TurnInput { Images = ValueList<Attachment>.Empty, Text = "Write proof.txt", RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build });
        }, LoopbackHttpServer.Guard));
        Assert.Single(events.OfType<ProviderEvent.Approval>());
        Assert.Equal("original", File.ReadAllText(path));
        var chats = server.Requests.Where(request => request.Target == "/v1/chat/completions").ToArray();
        Assert.Equal(2, chats.Length);
        Assert.Contains("\"role\":\"tool\"", Encoding.UTF8.GetString(chats[1].Body), StringComparison.Ordinal);
        session.Stop();
    }

    [Fact]
    public async Task A_chat_redirect_does_not_send_the_conversation_to_another_server()
    {
        await using var source = new LocalProviderLoopback();
        await using var destination = new LocalProviderLoopback();
        using var runtime = new TestRuntime();
        using var scratch = new LocalProviderFolder();
        source.Reply("/v1/models", """{"data":[{"id":"tiny","supports_tool_calling":true}]}""");
        using var discovery = new LocalDiscovery([source.BaseURL]);
        var entry = Assert.Single(await discovery.Discover());
        var session = new LocalSession(Configuration(scratch.Root), discovery.Resolve(entry.Models[0].SelectionId)!);
        session.SeedMcpToolsForTests([]);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        source.Reply("/v1/chat/completions", "{\"error\":{\"message\":\"redirect refused\"}}", 307,
            ("Location", destination.BaseURL + "v1/chat/completions"));
        // ProviderSession.Send reports turn failures as events (the existing OpenAI-compatible contract).
        await Task.Run(() => runtime.Loop.Run(async () =>
        {
            await session.Start();
            await session.Send(new TurnInput { Images = ValueList<Attachment>.Empty, Text = "Private conversation", RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build });
        }, LoopbackHttpServer.Guard));
        Assert.Contains(events, item => item is ProviderEvent.TurnCompleted { Status: TurnStatus.Failed });
        Assert.Empty(destination.Requests);
        session.Stop();
    }

    [Fact]
    public async Task Separate_servers_do_not_exchange_cookies_and_a_selected_vision_model_keeps_its_images()
    {
        await using var first = new LocalProviderLoopback();
        await using var second = new LocalProviderLoopback();
        using var runtime = new TestRuntime();
        using var folder = new LocalProviderFolder();
        var imagePath = Path.Combine(folder.Root, "fake.png");
        File.WriteAllBytes(imagePath, [1, 2, 3]);
        first.Reply("/cookie-seed", "{}", 200, ("Set-Cookie", "ma3seed=private; Path=/"));
        await URLSession.Shared.Data(new URLRequest(new Uri(first.BaseURL, "cookie-seed")));
        try
        {
            foreach (var server in new[] { first, second })
            {
                server.Reply("/v1/models", """{"data":[{"id":"vision","supports_vision":true,"supports_tool_calling":true}]}""");
                server.Reply("/v1/chat/completions", "data: {\"choices\":[{\"delta\":{\"content\":\"Saw image\"}}]}\n\ndata: [DONE]\n\n", 200,
                    ("Set-Cookie", "ma3local=must-not-share; Path=/"));
            }
            using var discovery = new LocalDiscovery([first.BaseURL, second.BaseURL]);
            var entries = await discovery.Discover();
            foreach (var entry in entries)
            {
                var target = discovery.Resolve(entry.Models[0].SelectionId)!;
                var session = new LocalSession(Configuration(folder.Root), target);
                session.SeedMcpToolsForTests([]);
                await Task.Run(() => runtime.Loop.Run(async () =>
                {
                    await session.Start();
                    await session.Send(new TurnInput { Text = "Image", Model = target.Model.SelectionId,
                        Images = new ValueList<Attachment>([new Attachment { Name = "fake.png", Path = imagePath, MimeType = "image/png" }]),
                        RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build });
                }, LoopbackHttpServer.Guard));
                session.Stop();
            }
            foreach (var server in new[] { first, second })
            {
                var chat = Assert.Single(server.Requests, request => request.Target == "/v1/chat/completions");
                Assert.Empty(chat.HeaderValues("Cookie"));
                Assert.Contains("data:image/png;base64,AQID", Encoding.UTF8.GetString(chat.Body), StringComparison.Ordinal);
            }
            second.Reply("/cookie-check", "{}");
            await URLSession.Shared.Data(new URLRequest(new Uri(second.BaseURL, "cookie-check")));
            var check = Assert.Single(second.Requests, request => request.Target == "/cookie-check");
            Assert.Contains("ma3seed=private", string.Join(';', check.HeaderValues("Cookie")), StringComparison.Ordinal); // negative control: ordinary HTTP shares host cookies
            Assert.DoesNotContain("ma3local", string.Join(';', check.HeaderValues("Cookie")), StringComparison.Ordinal);
        }
        finally
        {
            first.Reply("/cookie-clear", "{}", 200, ("Set-Cookie", "ma3seed=; Path=/; Max-Age=0"), ("Set-Cookie", "ma3local=; Path=/; Max-Age=0"));
            await URLSession.Shared.Data(new URLRequest(new Uri(first.BaseURL, "cookie-clear")));
        }
    }

    private static SessionConfiguration Configuration(string root) => new()
    {
        // Windows uses the same file:drive spelling as OpenRouter's fixture; Linux uses an actual POSIX file URL.
        Provider = ProviderKind.Local, WorkingDirectory = OperatingSystem.IsWindows()
            ? SwiftURL.Create("file:" + root.Replace('\\', '/'))! : SwiftURL.FileURLWithPath(root),
        Environment = ValueMap<string, string>.Empty,
        RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build,
    };
}
