using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// The order of the checks in one iteration of the turn loop: OpenAICompatibleSession.swift:311-341 (B3 52bc792; the record cites
// hydra-prompt-corpus.md section 6): round ceiling, budget spent / final report, wrap-up, pacing, condenseHistory, stream.
// On the round where the budget is spent neither the wrap-up nor the pacing note is appended. This is the only provider session
// with the loop and the Hydra budget (the CLI sessions keep their budget in ThreadRuntime).
[Collection(OpenAICompatibleSessionCollection.Name)]
public sealed class OpenAICompatibleSessionBudgetOrderTests
{
    private static OpenAICompatibleSessionTestSeam Head(OpenAICompatibleSession.Config config, SessionConfiguration configuration) =>
        new OpenAICompatibleSessionTestSeam(config, configuration).Wired();

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

    private static SessionConfiguration MakeSessionConfiguration() => new()
    {
        Provider = ProviderKind.Deepseek,
        WorkingDirectory = SwiftURL.Create("file:///C:/work")!,
        Environment = ValueMap<string, string>.Empty,
        RuntimeMode = RuntimeMode.FullAccess,
        InteractionMode = InteractionMode.Build,
        ApiKey = "sk-test",
        Transcript = ValueList<TranscriptMessage>.Empty,
        IsHydraHead = true,
        Hydra = new HydraLaunch { HeadsProvider = ProviderKind.Deepseek, RunsNatively = true },
    };

    private static TurnInput MakeTurn() => new()
    {
        Text = "hi",
        Images = ValueList<Attachment>.Empty,
        RuntimeMode = RuntimeMode.FullAccess,
        InteractionMode = InteractionMode.Build,
    };

    private static OpenAICompatibleSession.StreamRound Round(string content = "", int tools = 0, string prefix = "c") => new()
    {
        Content = content,
        Reasoning = "",
        ToolCalls = new ValueList<OpenAICompatibleSession.PendingToolCall>(
            Enumerable.Range(0, tools).Select(i => new OpenAICompatibleSession.PendingToolCall($"{prefix}{i}", "run_command", "{}"))),
        RawError = null,
        StatusCode = 200,
    };

    private static async Task Send(TestRuntime runtime, OpenAICompatibleSessionTestSeam session)
    {
        var task = session.Send(MakeTurn());
        runtime.Loop.RunUntil(() => task.IsCompleted);
        await task;
    }

    private static int IndexOfUserNote(OpenAICompatibleSessionTestSeam session, string note) =>
        session.Messages.ToList().FindIndex(message =>
            message["role"]?.JSONValueString == "user" && message["content"]?.JSONValueString == note);

    [Fact]
    [Trait("Req", "PR-HYDRA-016")] // :317-327 budget spent is checked first: the round that finds it spent gets the final note and no wrap-up or pacing note
    public async Task On_the_round_where_the_budget_is_spent_neither_the_wrap_up_nor_the_pacing_note_is_appended()
    {
        using var runtime = new TestRuntime();
        var session = Head(MakeConfig(), MakeSessionConfiguration());
        await session.Start();
        // One round of MaxTools calls: the next iteration finds the budget spent while the wrap-up (>= 120 tools) and the
        // pacing (>= 24 tools without a change) conditions both hold too and have not fired yet.
        session.Rounds.Enqueue(Round(tools: (int)HydraBudget.MaxTools));
        session.Rounds.Enqueue(Round("final report"));
        await Send(runtime, session);

        Assert.True(IndexOfUserNote(session, HydraBudget.FinalNote) >= 0);
        Assert.Equal(-1, IndexOfUserNote(session, HydraBudget.WrapUpNote));
        Assert.Equal(-1, IndexOfUserNote(session, HydraBudget.PacingNote));
        Assert.Equal(2, session.RoundRequests.Count);
        Assert.False(session.RoundRequests[^1].WithTools);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-016")] // :329-339 below the budget: the wrap-up note comes before the pacing note, both before the stream
    public async Task A_round_that_is_due_both_notes_gets_the_wrap_up_note_then_the_pacing_note_then_streams_with_tools()
    {
        using var runtime = new TestRuntime();
        var session = Head(MakeConfig(), MakeSessionConfiguration());
        await session.Start();
        session.Rounds.Enqueue(Round(tools: (int)HydraBudget.WrapUpTools));
        session.Rounds.Enqueue(Round("done"));
        await Send(runtime, session);

        var wrapUp = IndexOfUserNote(session, HydraBudget.WrapUpNote);
        var pacing = IndexOfUserNote(session, HydraBudget.PacingNote);
        Assert.True(wrapUp >= 0);
        Assert.True(pacing > wrapUp);
        Assert.Equal(-1, IndexOfUserNote(session, HydraBudget.FinalNote));
        // Both notes were put in front of the second stream, which still had its tools.
        Assert.Equal(2, session.RoundRequests.Count);
        Assert.True(session.RoundRequests[^1].WithTools);
        Assert.Equal(pacing, session.Messages.ToList().FindLastIndex(message =>
            message["role"]?.JSONValueString == "user") );
    }
}
