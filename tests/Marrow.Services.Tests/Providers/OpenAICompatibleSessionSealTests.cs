using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Assistant;
using Marrow.Services.MCP;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Dextra AS-5 security review (DESIGN.md rev 5, 12.1a, 12.3; ED-NEW-dextra-as5-16): an API (or local) provider's chat that
// nth launched sealed for a mission stays in the ask mode whatever a turn asks for, keeps no "Approve for turn" rule (it
// is neither offered nor kept when sent), and sees and calls only the MCP servers its assistant's profile lists. An
// unsealed chat is as before. Scripted rounds; no provider and no MCP server runs.
[Collection(OpenAICompatibleSessionCollection.Name)]
[Trait("Feature", "AS-5")]
[Trait("Decision", "ED-NEW-dextra-as5-16")]
public sealed class OpenAICompatibleSessionSealTests
{
    private static OpenAICompatibleSession.Config MakeConfig() => new()
    {
        Kind = ProviderKind.Deepseek,
        Title = "DeepSeek",
        AgentIdentity = model => $"agent ({model})",
        DefaultModel = "deepseek-chat",
        ChatURL = new Uri("https://api.deepseek.com/chat/completions"),
        ContextWindow = 64_000,
        IsVisionModel = _ => false,
    };

    private static SessionConfiguration MakeSessionConfiguration(bool sealedLaunch, RuntimeMode runtimeMode = RuntimeMode.FullAccess) => new()
    {
        Provider = ProviderKind.Deepseek,
        WorkingDirectory = SwiftURL.Create("file:///C:/work")!,
        Environment = ValueMap<string, string>.Empty,
        RuntimeMode = runtimeMode,
        InteractionMode = InteractionMode.Build,
        ApiKey = "sk-test",
        Sealed = sealedLaunch ? new SealedLaunch(Guid.Parse("0f3c9a2e-7b1d-4c55-8e6a-2d4f6b8a9c03")) { Connections = new(["calendar"]) } : null,
    };

    private static TurnInput Turn(RuntimeMode runtimeMode) => new()
    {
        Text = "go",
        Images = ValueList<Attachment>.Empty,
        RuntimeMode = runtimeMode,
        InteractionMode = InteractionMode.Build,
    };

    [Fact]
    public async Task A_sealed_chat_stays_in_the_ask_mode_whatever_its_turn_asks_for()
    {
        using var runtime = new TestRuntime();
        var sealedSession = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration(sealedLaunch: true)).Wired();
        Assert.True(sealedSession.RequiresApprovalForTests(ToolCall.Kind.Command)); // from the start
        await sealedSession.Start();
        sealedSession.Rounds.Enqueue(new OpenAICompatibleSession.StreamRound { Content = "done", ToolCalls = ValueList<OpenAICompatibleSession.PendingToolCall>.Empty, StatusCode = 200 });
        var turn = sealedSession.Send(Turn(RuntimeMode.FullAccess));
        runtime.Loop.RunUntil(() => turn.IsCompleted);
        await turn;
        Assert.True(sealedSession.RequiresApprovalForTests(ToolCall.Kind.Command));

        var plain = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration(sealedLaunch: false)).Wired();
        await plain.Start();
        plain.Rounds.Enqueue(new OpenAICompatibleSession.StreamRound { Content = "done", ToolCalls = ValueList<OpenAICompatibleSession.PendingToolCall>.Empty, StatusCode = 200 });
        var plainTurn = plain.Send(Turn(RuntimeMode.FullAccess));
        runtime.Loop.RunUntil(() => plainTurn.IsCompleted);
        await plainTurn;
        Assert.False(plain.RequiresApprovalForTests(ToolCall.Kind.Command));
    }

    [Fact]
    public async Task A_sealed_chat_offers_no_approve_for_turn_and_an_always_sent_anyway_counts_once()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration(sealedLaunch: true, SealedProviderConfig.CappedMode)).Wired();
        var first = runtime.OnMain(() => session.ApproveIfNeededForTests(ToolCall.Kind.Command, "npm test", null, null));
        runtime.Loop.RunUntilIdle();
        var request = session.Events.OfType<ProviderEvent.Approval>().Single().Value;
        Assert.Equal(new[] { ("allow", ApprovalRequest.Option.Role.Approve), ("decline", ApprovalRequest.Option.Role.Decline) },
            request.Options.Select(o => (o.Id, o.OptionRole)));

        session.ResolveApproval(request.Id, "always");
        runtime.Loop.RunUntilIdle();
        Assert.True(await first);

        // No rule was kept: the next command asks again.
        var second = runtime.OnMain(() => session.ApproveIfNeededForTests(ToolCall.Kind.Command, "npm run build", null, null));
        runtime.Loop.RunUntilIdle();
        Assert.Equal(2, session.Events.OfType<ProviderEvent.Approval>().Count());
        session.ResolveApproval(session.Events.OfType<ProviderEvent.Approval>().Last().Value.Id, "decline");
        runtime.Loop.RunUntilIdle();
        Assert.False(await second);
    }

    [Fact]
    public async Task A_sealed_chat_sees_and_calls_only_the_servers_its_profile_lists()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSession(MakeConfig(), MakeSessionConfiguration(sealedLaunch: true, SealedProviderConfig.CappedMode));
        Assert.True(session.SealAllows("calendar"));
        Assert.True(session.SealAllows("Calendar"));
        Assert.False(session.SealAllows("mail"));
        Assert.False(session.SealAllows("calendar-extra"));

        var schema = JSONValue.Parse("""{"type":"object"}""")!;
        MCPHub.Tool[] tools = [new("calendar", "list", "Lists events", schema), new("mail", "send", "Sends mail", schema)];
        Assert.Equal(new[] { "calendar" }, session.SealedTools(tools).Select(t => t.Server));

        var refused = await session.ExecuteToolForTests(new OpenAICompatibleSession.PendingToolCall("c1", "mcp__mail__send", "{}"));
        Assert.Equal("Error: mail is not available in this mission chat.", refused);

        var plain = new OpenAICompatibleSession(MakeConfig(), MakeSessionConfiguration(sealedLaunch: false));
        Assert.True(plain.SealAllows("mail"));
        Assert.Equal(2, plain.SealedTools(tools).Count());
    }
}
