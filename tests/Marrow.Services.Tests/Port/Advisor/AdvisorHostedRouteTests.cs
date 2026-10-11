using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Advisor;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Port.Advisor;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Advisor;

// PORT-ONLY: Ignis Advisor, the hosted route (ED-NEW-ignisadvisor-2; owner 2026-10-11: Hydra heads ask their lead). One
// class, so the process-wide AdvisorSeams are only ever set by one test at a time; the Choice fakes answer only for this
// class's own head id, so no other test's session ever sees an advisor. No model provider is ever called.
[Trait("Decision", "ED-NEW-ignisadvisor-2")]
public sealed class AdvisorHostedRouteTests
{
    private static readonly Guid Head = Guid.Parse("4f0c2d1e-7a3b-4c55-9e21-0a6d1b2c3d4e");
    private static readonly Guid Other = Guid.Parse("4f0c2d1e-7a3b-4c55-9e21-0a6d1b2c3d4f");
    private static readonly AdvisorChoice Lead = new(ProviderKind.Codex, "astra-6");

    private static SessionConfiguration Config(ProviderKind provider, Guid? thread, string? model = "sonnet") => new()
    {
        Provider = provider,
        WorkingDirectory = SwiftURL.Create("file:///C:/advisor-test")!,
        Environment = ValueMap<string, string>.Empty,
        RuntimeMode = RuntimeMode.FullAccess,
        InteractionMode = InteractionMode.Build,
        Model = model,
        IgnisThreadID = thread,
    };

    private static async Task WithLead(AdvisorChoice? lead, Func<Task> body, Action<Guid?, AdvisorAdvice>? asked = null)
    {
        var (choice, previous) = (AdvisorSeams.Choice, AdvisorSeams.Asked);
        AdvisorSeams.Choice = (thread, _, _) => thread == Head ? lead : null;
        AdvisorSeams.Asked = asked;
        try { await body(); }
        finally { (AdvisorSeams.Choice, AdvisorSeams.Asked) = (choice, previous); }
    }

    private static async Task<JsonNode> Mcp(AdvisorMcpServer server, string method, object? parameters = null)
    {
        var line = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 7, method, @params = parameters });
        return JsonNode.Parse((await server.Handle(line, CancellationToken.None))!)!;
    }

    private static IEnumerable<string> FunctionNames(NativeSkillTools tools) => tools.Definitions.Select(d => d["function"]!["name"]!.JSONValueString!);

    [Fact]
    public Task The_tool_is_listed_only_for_a_chat_whose_advisor_choice_is_not_null() => WithLead(Lead, async () =>
    {
        var advised = SkillSessionFactory.NativeTools(Config(ProviderKind.Deepseek, Head));
        Assert.Equal(new[] { AdvisorTool.Name }, FunctionNames(advised));
        Assert.True(advised.Owns(AdvisorTool.Name));
        foreach (var plain in new[] { SkillSessionFactory.NativeTools(Config(ProviderKind.Deepseek, Other)), SkillSessionFactory.NativeTools(Config(ProviderKind.Deepseek, null)) })
        {
            Assert.Empty(plain.Definitions);
            Assert.False(plain.Owns(AdvisorTool.Name));
        }

        var listed = await Mcp(new AdvisorMcpServer(AdvisorTool.Registration(Config(ProviderKind.Codex, Head))), "tools/list");
        Assert.Equal(AdvisorTool.Name, Assert.Single(listed["result"]!["tools"]!.AsArray())!["name"]!.GetValue<string>());
        Assert.Null(AdvisorTool.Registration(Config(ProviderKind.Codex, Other)));
        var unregistered = new AdvisorMcpServer(null);
        Assert.Empty((await Mcp(unregistered, "tools/list"))["result"]!["tools"]!.AsArray());
        Assert.Equal(-32602, (await Mcp(unregistered, "tools/call", new { name = AdvisorTool.Name, arguments = new { question = "q" } }))["error"]!["code"]!.GetValue<int>());
    });

    [Fact]
    public Task The_tool_schema_asks_for_a_question_and_an_optional_context_and_says_when_to_call() => WithLead(Lead, async () =>
    {
        var schema = AdvisorTool.InputSchema();
        Assert.Equal("object", schema["type"]!.JSONValueString);
        Assert.Equal(new[] { "question" }, schema["required"]!.JSONValueArray!.Select(v => v.JSONValueString!));
        Assert.Equal(new HashSet<string> { "question", "context" }, schema["properties"]!.JSONValueObject!.Keys.ToHashSet());
        Assert.All(new[] { "question", "context" }, key => Assert.Equal("string", schema["properties"]![key]!["type"]!.JSONValueString));
        Assert.Equal(new JSONValue.Bool(false), schema["additionalProperties"]);
        Assert.Contains("stuck", AdvisorTool.Description, StringComparison.Ordinal);
        Assert.Contains("before a risky change", AdvisorTool.Description, StringComparison.Ordinal);
        Assert.Contains("rather than guessing", AdvisorTool.Description, StringComparison.Ordinal);
        Assert.Contains("Do not call it for routine steps", AdvisorTool.Description, StringComparison.Ordinal);

        var function = AdvisorTool.FunctionDefinition()["function"]!;
        Assert.Equal(schema.CompactString, function["parameters"]!.CompactString);
        var tool = (await Mcp(new AdvisorMcpServer(AdvisorTool.Registration(Config(ProviderKind.Codex, Head))), "tools/list"))["result"]!["tools"]![0]!;
        Assert.Equal(AdvisorTool.Description, tool["description"]!.GetValue<string>());
        Assert.Equal(JsonNode.Parse(schema.CompactString)!.ToJsonString(), tool["inputSchema"]!.ToJsonString());
    });

    [Fact]
    public async Task Advice_runs_as_one_headless_provider_turn_and_reports_its_usage_and_price()
    {
        var session = new AdvisorFakeSession(
        [
            new ProviderEvent.MessageDelta("m1", "Use a "),
            new ProviderEvent.MessageCompleted("m1", "Use a lock around the cache."),
            new ProviderEvent.CacheUsage(new IgnisTurnUsage(ProviderKind.Codex, "astra-6", null, 1_000, 4_000, 0, 0, 0, 500, null, null, true)),
            new ProviderEvent.CacheUsage(new IgnisTurnUsage(ProviderKind.Codex, "astra-6", "sub", 9_999, 0, 0, 0, 0, 9_999, null, null, true)),
            new ProviderEvent.TurnCompleted(TurnStatus.Completed, null),
        ]);
        var asked = new List<(Guid? Thread, AdvisorAdvice Advice)>();
        using var runner = AdvisorHost.Runner.Override(() => new SessionAdvisorRunner(choice => { Assert.Equal(Lead, choice); return session; }));
        using var points = IgnisRuntimeSeams.Points.Override(() => new AdvisorFakePoints(ProviderKind.Codex, "astra-6"));
        await WithLead(Lead, async () =>
        {
            var server = new AdvisorMcpServer(AdvisorTool.Registration(Config(ProviderKind.Claude, Head)));
            var result = (await Mcp(server, "tools/call", new { name = AdvisorTool.Name, arguments = new { question = "Why does the cache test flake?", context = "Tried a retry; the race stays." } }))["result"]!;
            Assert.False(result["isError"]!.GetValue<bool>());
            Assert.Equal("Use a lock around the cache.", result["content"]![0]!["text"]!.GetValue<string>());
        }, (thread, advice) => asked.Add((thread, advice)));

        var (thread, advice) = Assert.Single(asked);
        Assert.Equal(Head, thread);
        Assert.Equal(new AdvisorAdvice("Use a lock around the cache.", Lead, 5_000, 500, 4_000, advice.Usd), advice);
        Assert.Equal(0.0078, advice.Usd!.Value, 9); // (1000 x 2 + 4000 x 0.2 + 500 x 10) / 1e6
        var sent = Assert.IsType<TurnInput>(session.Sent);
        Assert.StartsWith(AdvisorHost.SystemLine + "\n\nQuestion:\nWhy does the cache test flake?\n\nContext:\nTried a retry; the race stays.", sent.Text, StringComparison.Ordinal);
        Assert.Equal(("astra-6", true, InteractionMode.Plan), (sent.Model, sent.IsFinalReport, sent.InteractionMode));
        Assert.True(session.Stopped);
    }

    [Fact]
    public async Task A_timeout_an_error_or_no_runner_is_a_short_error_text_and_never_a_throw()
    {
        var asked = 0;
        await WithLead(Lead, async () =>
        {
            Assert.StartsWith("Error: no advisor is available", (await AdvisorHost.Ask(Head, Lead, "q", null, CancellationToken.None)).Text, StringComparison.Ordinal);
            Assert.Equal(new AdvisorReply("Error: a question is required.", true), await AdvisorHost.Ask(Head, Lead, "  ", null, CancellationToken.None));

            var hanging = new AdvisorFakeSession([], hang: true);
            using (AdvisorHost.Runner.Override(() => new SessionAdvisorRunner(_ => hanging)))
            using (AdvisorHost.Timeout.Override(() => TimeSpan.FromMilliseconds(200)))
            {
                var reply = await AdvisorHost.Ask(Head, Lead, "q", null, CancellationToken.None);
                Assert.True(reply.Failed);
                Assert.StartsWith("Error: the advisor did not answer within 0 seconds.", reply.Text, StringComparison.Ordinal);
                // The API providers' tool loop gets the same text as a failed row, not an exception.
                var native = await SkillSessionFactory.NativeTools(Config(ProviderKind.OpenRouter, Head)).Execute(AdvisorTool.Name, JSONValue.FromDictionaryLiteral(("question", "q")));
                Assert.Equal((AdvisorTool.Title, true), (native.Title, native.Failed));
                Assert.StartsWith("Error: the advisor did not answer", native.Output, StringComparison.Ordinal);
            }

            using (AdvisorHost.Runner.Override(() => new SessionAdvisorRunner(_ => throw new InvalidOperationException("No enabled Codex account."))))
                Assert.Equal(new AdvisorReply("Error: the advisor could not answer (No enabled Codex account.). Carry on with your best judgement.", true),
                    await AdvisorHost.Ask(Head, Lead, "q", null, CancellationToken.None));

            var failed = new AdvisorFakeSession([new ProviderEvent.TurnCompleted(TurnStatus.Failed, "rate limited")]);
            using (AdvisorHost.Runner.Override(() => new SessionAdvisorRunner(_ => failed)))
                Assert.Equal(new AdvisorReply("Error: the advisor could not answer (rate limited). Carry on with your best judgement.", true),
                    await AdvisorHost.Ask(Head, Lead, "q", null, CancellationToken.None));
            Assert.True(failed.Stopped);
        }, (_, _) => asked++);
        Assert.Equal(0, asked);
    }

    [Fact]
    public async Task Long_advice_is_capped_and_a_declined_approval_keeps_the_advisor_tool_less()
    {
        var approval = new ApprovalRequest("a1", ApprovalRequest.Kind.Command, "rm -rf",
            new ValueList<ApprovalRequest.Option>([new("yes", "Allow", ApprovalRequest.Option.Role.Approve), new("no", "Deny", ApprovalRequest.Option.Role.Decline)]));
        var session = new AdvisorFakeSession(
        [
            new ProviderEvent.Approval(approval),
            new ProviderEvent.MessageCompleted("m", new string('x', AdvisorHost.MaximumAdvice + 50)),
            new ProviderEvent.TurnCompleted(TurnStatus.Completed, null),
        ]);
        using var runner = AdvisorHost.Runner.Override(() => new SessionAdvisorRunner(_ => session));
        await WithLead(Lead, async () =>
        {
            var reply = await AdvisorHost.Ask(Head, Lead, "q", null, CancellationToken.None);
            Assert.False(reply.Failed);
            Assert.Equal(new string('x', AdvisorHost.MaximumAdvice) + "\n[cut: 50 more characters]", reply.Text);
        });
        Assert.Equal([("a1", "no")], session.Resolved);
    }

    [Fact]
    public Task A_Claude_head_with_a_Claude_lead_is_hosted_inside_Hydra() => WithLead(new AdvisorChoice(ProviderKind.Claude, "opus"), () =>
    {
        // Owner 2026-10-11: heads are separate sessions, so a Claude lead advises a Claude head through the hosted tool too.
        var registration = Assert.IsType<AdvisorRegistration>(AdvisorTool.Registration(Config(ProviderKind.Claude, Head, "sonnet")));
        Assert.Equal(new AdvisorRegistration(Head, ProviderKind.Claude, "sonnet", new AdvisorChoice(ProviderKind.Claude, "opus")), registration);
        Assert.NotNull(MCPProviderConfig.AdvisorServer(Config(ProviderKind.Claude, Head)));
        return Task.CompletedTask;
    });

    [Fact]
    public Task MCPProviderConfig_adds_the_advisor_server_only_for_an_advised_chat() => WithLead(Lead, () =>
    {
        Assert.Null(MCPProviderConfig.AdvisorServer(Config(ProviderKind.Codex, Other)));
        Assert.Null(MCPProviderConfig.AdvisorServer(Config(ProviderKind.Codex, null)));
        var connected = JSONValue.FromDictionaryLiteral(("github", JSONValue.FromDictionaryLiteral(("url", "https://example.test/mcp"))));
        Assert.Same(connected, MCPProviderConfig.CodexServers(connected, null));
        Assert.Null(MCPProviderConfig.CodexServers(null, null));
        Assert.Null(MCPProviderConfig.CopilotSessionServers(null));
        Assert.Null(MCPProviderConfig.ClaudeAdvisorConfigPath(null, Other));
        var acp = JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("name", "github")));
        Assert.Same(acp, MCPProviderConfig.AcpServers(acp, null));

        var server = Assert.IsType<MCPResolvedServer>(MCPProviderConfig.AdvisorServer(Config(ProviderKind.Codex, Head)));
        Assert.Equal((AdvisorMcpServer.ServerName, AdvisorMcpBridge.Executable.Current(), AdvisorMcpBridge.Command), (server.Id, server.Command, server.Args[0]));
        Assert.Equal(new AdvisorRegistration(Head, ProviderKind.Codex, "sonnet", Lead), AdvisorMcpBridge.Registration(server.Args[2]));
        Assert.Null(AdvisorMcpBridge.Registration(Guid.NewGuid().ToString("N")));

        var codex = MCPProviderConfig.CodexServers(connected, server)!;
        Assert.Equal(new HashSet<string> { "github", AdvisorMcpServer.ServerName }, codex.JSONValueObject!.Keys.ToHashSet());
        Assert.Equal(server.Args.ToArray(), codex[AdvisorMcpServer.ServerName]!["args"]!.JSONValueArray!.Select(a => a.JSONValueString!));
        var copilot = MCPProviderConfig.CopilotSessionServers(server)![AdvisorMcpServer.ServerName]!;
        Assert.Equal(("local", "*"), (copilot["type"]!.JSONValueString, copilot["tools"]![0]!.JSONValueString));
        var acpAll = MCPProviderConfig.AcpServers(acp, server).JSONValueArray!;
        Assert.Equal(new[] { "github", AdvisorMcpServer.ServerName }, acpAll.Select(e => e["name"]!.JSONValueString!));
        Assert.Empty(acpAll[1]["env"]!.JSONValueArray!);

        using var storage = new StorageScratchFolder();
        var path = Assert.IsType<string>(MCPProviderConfig.ClaudeAdvisorConfigPath(server, Head));
        var written = JSONValue.Parse(File.ReadAllBytes(path))!;
        Assert.Equal(new[] { AdvisorMcpServer.ServerName }, written["mcpServers"]!.JSONValueObject!.Keys);
        Assert.Equal(server.Command, written["mcpServers"]![AdvisorMcpServer.ServerName]!["command"]!.JSONValueString);
        return Task.CompletedTask;
    });
}
