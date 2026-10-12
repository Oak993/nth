using System.Text;
using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.NthUi;
using Marrow.Core.Port;
using Marrow.Core.Port.Local;
using Marrow.Core.Support;
using Marrow.Services.Port.Local;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// DESIGN.md 7.3 "where the runtime supports it (llama.cpp GBNF, ...), grammar-constrained output" (web surfaces W17; ED-NEW-web-w17-1).
// A llama.cpp server is recognised by its /props reply; only it is sent the lite-view grammar, lazily (free text until the model writes
// the opening fence), and only when the chat teaches live views and the request carries no tools (llama.cpp refuses a custom grammar next
// to tools). Ollama, LM Studio and unknown servers are taught by the prompt alone. A local model is taught the lite subset, in fenced
// blocks when it cannot call tools. No model provider is ever called.
[Collection(OpenAICompatibleSessionCollection.Name)]
[Trait("Decision", "ED-NEW-web-w17-1")]
public sealed class LocalViewGrammarTests
{
    private const string Props = """{"default_generation_settings":{"n_ctx":8192},"total_slots":1,"model_path":"tiny.gguf","chat_template":"x"}""";

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    [Fact]
    public void Only_a_props_reply_that_has_llama_cpps_shape_names_a_llama_cpp_server()
    {
        Assert.Equal(LocalServerKind.LlamaCpp, LocalServerIdentity.FromPropsReply(Json(Props)));
        foreach (var reply in new[]
        {
            """{"default_generation_settings":{"n_ctx":8192},"supports_tool_calling":true}""", // no slots
            """{"default_generation_settings":{"n_ctx":0},"total_slots":1}""",
            """{"default_generation_settings":{"n_ctx":"8192"},"total_slots":1}""",
            """{"default_generation_settings":[],"total_slots":1}""",
            """{"total_slots":1}""",
            """{"object":"list","data":[]}""",
            """[]""",
            "\"llama.cpp\"",
        })
            Assert.Equal(LocalServerKind.Unknown, LocalServerIdentity.FromPropsReply(Json(reply)));
    }

    [Fact]
    public async Task Discovery_finds_llama_cpp_by_props_and_the_target_remembers_it()
    {
        await using var server = new LocalProviderLoopback();
        server.Reply("/v1/models", """{"data":[{"id":"tiny"}]}""");
        server.Reply("/props", Props);
        using var discovery = new LocalDiscovery([server.BaseURL]);
        var entry = Assert.Single(await discovery.Discover());
        Assert.Equal(LocalServerKind.LlamaCpp, entry.Kind);
        Assert.Equal(8192, Assert.Single(entry.Models).Capabilities.ContextWindow);
        Assert.Equal(LocalServerKind.LlamaCpp, discovery.Resolve(entry.Models[0].SelectionId)!.Kind);

        await using var plain = new LocalProviderLoopback(); // a generic OpenAI-compatible server: no /props, no identity
        plain.Reply("/v1/models", """{"data":[{"id":"tiny","context_length":8192,"supports_tool_calling":true}]}""");
        using var other = new LocalDiscovery([plain.BaseURL]);
        Assert.Equal(LocalServerKind.Unknown, Assert.Single(await other.Discover()).Kind);
    }

    [Fact]
    public void The_request_fields_are_the_lazy_grammar_for_llama_cpp_and_nothing_for_other_servers()
    {
        var fields = LocalViewGrammar.Fields(LocalServerKind.LlamaCpp)!;
        Assert.Equal(["grammar", "grammar_lazy", "grammar_triggers"], fields.Keys);
        Assert.Equal(NthUiGrammar.Gbnf, fields["grammar"].JSONValueString);
        Assert.True(fields["grammar_lazy"].JSONValueBool);
        var trigger = Assert.Single(fields["grammar_triggers"].JSONValueArray!);
        Assert.Equal((0, "```nth-ui"), (trigger["type"]!.JSONValueInt, trigger["value"]!.JSONValueString));
        foreach (var kind in new[] { LocalServerKind.Unknown, LocalServerKind.Ollama, LocalServerKind.LMStudio })
            Assert.Null(LocalViewGrammar.Fields(kind));

        var plain = new Dictionary<string, JSONValue> { ["model"] = "tiny" };
        LocalViewGrammar.Apply(plain, LocalServerKind.LlamaCpp);
        Assert.Equal(["model", "grammar", "grammar_lazy", "grammar_triggers"], plain.Keys);
        var withTools = new Dictionary<string, JSONValue> { ["model"] = "tiny", ["tools"] = JSONValue.FromArrayLiteral() };
        LocalViewGrammar.Apply(withTools, LocalServerKind.LlamaCpp);
        Assert.Equal(["model", "tools"], withTools.Keys); // llama.cpp refuses a custom grammar next to tools
        var ollama = new Dictionary<string, JSONValue> { ["model"] = "tiny" };
        LocalViewGrammar.Apply(ollama, LocalServerKind.Ollama);
        Assert.Equal(["model"], ollama.Keys);
    }

    private static SessionConfiguration Configuration(string root, Guid? chat) => new()
    {
        Provider = ProviderKind.Local,
        WorkingDirectory = SwiftURL.Create("file:" + root.Replace('\\', '/'))!,
        Environment = ValueMap<string, string>.Empty,
        RuntimeMode = RuntimeMode.FullAccess,
        InteractionMode = InteractionMode.Build,
        IgnisThreadID = chat,
    };

    /// <summary>Runs one turn against a loopback server that answers as <paramref name="models"/> and <paramref name="props"/> say; returns the chat request's body.</summary>
    private static async Task<JSONValue> Request(string models, string? props, bool teaches, string? ollamaTags = null)
    {
        await using var server = new LocalProviderLoopback();
        using var runtime = new TestRuntime();
        using var folder = new LocalProviderFolder();
        server.Reply("/v1/models", models);
        if (props is not null) server.Reply("/props", props);
        if (ollamaTags is not null) server.Reply("/api/tags", ollamaTags);
        using var discovery = new LocalDiscovery([server.BaseURL]);
        var target = discovery.Resolve(Assert.Single(Assert.Single(await discovery.Discover()).Models).SelectionId)!;
        var configuration = Configuration(folder.Root, teaches ? Guid.NewGuid() : null);
        var session = new LocalSession(configuration, target);
        if (teaches) session.SkillTools = SkillSessionFactory.NativeTools(configuration);
        session.SeedMcpToolsForTests([]);
        server.Reply("/v1/chat/completions", "data: {\"choices\":[{\"delta\":{\"content\":\"Fine.\"}}]}\n\ndata: [DONE]\n\n");
        await Task.Run(() => runtime.Loop.Run(async () =>
        {
            await session.Start();
            await session.Send(new TurnInput { Images = ValueList<Attachment>.Empty, Text = "Hello", RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build });
        }, LoopbackHttpServer.Guard));
        session.Stop();
        return JSONValue.Parse(Assert.Single(server.Requests, r => r.Target == "/v1/chat/completions").Body)!;
    }

    [Fact]
    public async Task A_llama_cpp_model_that_cannot_call_tools_is_sent_the_grammar_when_the_chat_teaches_live_views()
    {
        using var scope = new ArtifactHostScope();
        const string NoTools = """{"data":[{"id":"tiny","supports_tool_calling":false}]}""";
        var body = await Request(NoTools, Props, teaches: true);
        Assert.Equal(NthUiGrammar.Gbnf, body["grammar"]!.JSONValueString);
        Assert.True(body["grammar_lazy"]!.JSONValueBool);
        Assert.Equal("```nth-ui", Assert.Single(body["grammar_triggers"]!.JSONValueArray!)["value"]!.JSONValueString);
        Assert.Null(body["tools"]);
        var system = body["messages"]![0]!["content"]!.JSONValueString!;
        Assert.Contains("THE VIEW LANGUAGE (SMALL SET)", system, StringComparison.Ordinal);
        Assert.Contains("a fenced block", system, StringComparison.Ordinal);
        Assert.DoesNotContain("artifact_create", system, StringComparison.Ordinal);

        // The same server, but the model calls tools: tools go, the grammar does not (llama.cpp would refuse both), and the block names the tools.
        var tools = await Request("""{"data":[{"id":"tiny","supports_tool_calling":true}]}""", Props, teaches: true);
        Assert.NotNull(tools["tools"]);
        Assert.Null(tools["grammar"]);
        Assert.Contains("artifact_create", tools["messages"]![0]!["content"]!.JSONValueString!, StringComparison.Ordinal);
        Assert.Contains("THE VIEW LANGUAGE (SMALL SET)", tools["messages"]![0]!["content"]!.JSONValueString!, StringComparison.Ordinal);

        // A chat that teaches nothing (no artifact tools), and servers that are not llama.cpp: no grammar, and no block.
        var silent = await Request(NoTools, Props, teaches: false);
        Assert.Null(silent["grammar"]);
        Assert.DoesNotContain("LIVE ANSWERS", silent["messages"]![0]!["content"]!.JSONValueString!, StringComparison.Ordinal);
        var generic = await Request(NoTools, null, teaches: true);
        Assert.Null(generic["grammar"]);
        Assert.Contains("THE VIEW LANGUAGE (SMALL SET)", generic["messages"]![0]!["content"]!.JSONValueString!, StringComparison.Ordinal); // taught by the prompt alone
        var ollama = await Request("""{"data":[{"id":"tiny"}]}""", null, teaches: true, ollamaTags: """{"models":[{"name":"tiny"}]}""");
        Assert.Null(ollama["grammar"]);
        Assert.Contains("THE VIEW LANGUAGE (SMALL SET)", ollama["messages"]![0]!["content"]!.JSONValueString!, StringComparison.Ordinal);
    }
}
