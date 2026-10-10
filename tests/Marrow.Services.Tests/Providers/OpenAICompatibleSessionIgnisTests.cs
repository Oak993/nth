using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// PORT-ONLY: Ignis unit U2 (OD-0134, ED-NEW-ignis-providers; work/ignis/PLAN.md). The API session's cache breakers
// (the date line and the history trim), its CacheUsage event, KeepWarm() and Tidy(keepTurns). Every request stops at
// IgnisApiSessionSeam's PerformStreamAttempt, which records the payload and answers a queued round: nothing is sent.
[Collection(OpenAICompatibleSessionCollection.Name)]
public sealed class OpenAICompatibleSessionIgnisTests
{
    private static OpenAICompatibleSession.Config MakeConfig() => new()
    {
        Kind = ProviderKind.Deepseek,
        Title = "DeepSeek",
        AgentIdentity = model => $"agent ({model})",
        DefaultModel = "deepseek-chat",
        ChatURL = new Uri("https://api.deepseek.invalid/chat/completions"),
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
    };

    private static TurnInput Turn(string text) => new()
    {
        Text = text,
        Images = ValueList<Attachment>.Empty,
        RuntimeMode = RuntimeMode.FullAccess,
        InteractionMode = InteractionMode.Build,
    };

    private static OpenAICompatibleSession.StreamRound Answer(string text, long input = 100, long? cached = 60, long output = 5) => new()
    {
        Content = text,
        Usage = new ContextUsage { UsedTokens = input + output, WindowTokens = 64_000 },
        TokenUsage = new OpenAICompatibleSession.ReportedTokenUsage { Total = input + output, Input = input, Output = output, CachedInput = cached },
    };

    private static async Task<IgnisApiSessionSeam> Started()
    {
        var session = new IgnisApiSessionSeam(MakeConfig(), MakeSessionConfiguration()).Wired();
        await session.Start();
        return session;
    }

    private static async Task Send(TestRuntime runtime, IgnisApiSessionSeam session, string text)
    {
        session.Rounds.Enqueue(Answer("answer to " + text));
        var task = session.Send(Turn(text));
        runtime.Loop.RunUntil(() => task.IsCompleted);
        await task;
    }

    private static async Task<bool> Run(TestRuntime runtime, Task<bool> task)
    {
        runtime.Loop.RunUntil(() => task.IsCompleted);
        return await task;
    }

    private static IReadOnlyList<JSONValue> MessagesOf(Dictionary<string, JSONValue> payload) => payload["messages"].JSONValueArray!;

    private static string SystemOf(Dictionary<string, JSONValue> payload) => MessagesOf(payload)[0]["content"]!.JSONValueString!;

    private static string Wire(IEnumerable<JSONValue> messages) => JSONValue.FromArrayLiteral([.. messages]).CompactString;

    private static JSONValue Msg(string role, string content) => JSONValue.FromDictionaryLiteral(("role", role), ("content", content));

    private static JSONValue Call(string id, string name) => JSONValue.FromDictionaryLiteral(
        ("role", "assistant"), ("content", ""),
        ("tool_calls", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(
            ("id", id), ("type", "function"),
            ("function", JSONValue.FromDictionaryLiteral(("name", name), ("arguments", "{}")))))));

    private static JSONValue Result(string id, string content) =>
        JSONValue.FromDictionaryLiteral(("role", "tool"), ("tool_call_id", id), ("content", content));

    // ---- the date line ----

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public async Task Two_turns_on_the_same_day_send_a_byte_identical_date_only_system_prompt()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var session = await Started();

        await Send(runtime, session, "first");
        runtime.Advance(TimeSpan.FromMinutes(37));
        await Send(runtime, session, "second");

        Assert.Equal(2, session.Payloads.Count);
        Assert.Equal(SystemOf(session.Payloads[0]), SystemOf(session.Payloads[1]));
        Assert.Contains("- Today's date is 2026-09-22.\n", SystemOf(session.Payloads[0]), StringComparison.Ordinal);
        Assert.DoesNotContain("T12:", SystemOf(session.Payloads[0]), StringComparison.Ordinal);
        // The second request's messages start with the first request's: one stable cached prefix.
        var first = MessagesOf(session.Payloads[0]);
        Assert.Equal(Wire(first), Wire(MessagesOf(session.Payloads[1]).Take(first.Count)));
    }

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public async Task The_date_line_keeps_the_sessions_day_past_midnight_while_the_cache_can_be_warm_and_moves_after_an_idle_hour()
    {
        using var runtime = new TestRuntime(); // 2026-09-22T12:00:00Z
        using var scratch = new StorageScratchFolder();
        var session = await Started();

        runtime.Advance(TimeSpan.FromHours(11) + TimeSpan.FromMinutes(59)); // 23:59
        await Send(runtime, session, "before midnight");
        runtime.Advance(TimeSpan.FromMinutes(2)); // 2026-09-23T00:01, last request two minutes ago
        await Send(runtime, session, "after midnight");
        runtime.Advance(TimeSpan.FromHours(2)); // 02:01, no request for two hours
        await Send(runtime, session, "next morning");

        Assert.Contains("Today's date is 2026-09-22.", SystemOf(session.Payloads[0]), StringComparison.Ordinal);
        Assert.Equal(SystemOf(session.Payloads[0]), SystemOf(session.Payloads[1]));
        Assert.Contains("Today's date is 2026-09-23.", SystemOf(session.Payloads[2]), StringComparison.Ordinal);
    }

    // ---- the history trim ----

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public void Trim_cuts_in_chunks_so_the_kept_prefix_survives_the_next_twenty_messages()
    {
        var session = new IgnisApiSessionSeam(MakeConfig(), MakeSessionConfiguration());
        var messages = new List<JSONValue> { Msg("system", "sys") };
        messages.AddRange(Enumerable.Range(0, 60).Select(i => Msg(i % 2 == 0 ? "user" : "assistant", $"m{i}")));
        session.SetMessagesForTests(messages); // 61: over 60

        session.TrimHistoryForTests();
        Assert.Equal(41, session.Messages.Count);
        var prefix = Wire(session.Messages);

        var grown = session.Messages.ToList();
        grown.AddRange(Enumerable.Range(0, 19).Select(i => Msg(i % 2 == 0 ? "user" : "assistant", $"n{i}")));
        session.SetMessagesForTests(grown); // 60: not over
        session.TrimHistoryForTests();

        Assert.Equal(60, session.Messages.Count);
        Assert.Equal(prefix, Wire(session.Messages.Take(41)));
    }

    // ---- CacheUsage ----

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public void ReportUsage_emits_cache_usage_with_the_cached_part_split_out_of_prompt_tokens()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var session = new IgnisApiSessionSeam(MakeConfig(), MakeSessionConfiguration()).Wired();

        runtime.OnMain(() => session.ReportUsageForTests(Answer("x", input: 1000, cached: 640, output: 12)));

        var usage = Assert.Single(session.Events.OfType<ProviderEvent.CacheUsage>()).Value;
        Assert.Equal(new IgnisTurnUsage(ProviderKind.Deepseek, "deepseek-chat", null, 360, 640, 0, 0, 0, 12, 1012, 64_000, true), usage);
    }

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public void ReportUsage_without_cached_tokens_emits_not_reported_and_without_usage_emits_nothing()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var session = new IgnisApiSessionSeam(MakeConfig(), MakeSessionConfiguration()).Wired();

        runtime.OnMain(() => session.ReportUsageForTests(Answer("x", input: 500, cached: null, output: 7)));
        runtime.OnMain(() => session.ReportUsageForTests(new OpenAICompatibleSession.StreamRound { Content = "no usage" }));

        var usage = Assert.Single(session.Events.OfType<ProviderEvent.CacheUsage>()).Value;
        Assert.False(usage.Reported);
        Assert.Equal(IgnisTurnUsage.NotReported(ProviderKind.Deepseek, "deepseek-chat", null, 507, 64_000), usage);
    }

    // ---- KeepWarm ----

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public async Task KeepWarm_sends_the_next_turns_payload_plus_one_ping_capped_at_one_token_and_leaves_history_alone()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var session = await Started();
        await Send(runtime, session, "first");
        var history = Wire(session.Messages);
        session.Events.Clear();

        session.Rounds.Enqueue(Answer("ok", input: 300, cached: 280, output: 1));
        Assert.True(await Run(runtime, session.KeepWarm()));

        var turn = session.Payloads[0];
        var warm = session.Payloads[1];
        var sent = MessagesOf(warm);
        Assert.Equal(history, Wire(sent.Take(sent.Count - 1)));
        Assert.Equal(Wire([Msg("user", IgnisWords.PingText)]), Wire([sent[^1]]));
        Assert.Equal(SystemOf(turn), SystemOf(warm));
        Assert.Equal(turn["model"].CompactString, warm["model"].CompactString);
        Assert.Equal(turn["tools"].CompactString, warm["tools"].CompactString);
        Assert.Equal(1, warm["max_tokens"].JSONValueInt);
        Assert.False(turn.ContainsKey("max_tokens"));
        // Not added to history, no turn or notice events; the request's cache usage is reported.
        Assert.Equal(history, Wire(session.Messages));
        Assert.DoesNotContain(session.Events, e => e is ProviderEvent.TurnStarted or ProviderEvent.TurnCompleted or ProviderEvent.Notice
            or ProviderEvent.MessageDelta or ProviderEvent.MessageCompleted);
        var usage = Assert.Single(session.Events.OfType<ProviderEvent.CacheUsage>()).Value;
        Assert.Equal(280, usage.CacheRead);
        Assert.Equal(20, usage.UncachedInput);
    }

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public async Task KeepWarm_sends_nothing_before_start_or_while_a_turn_runs_and_reports_false_on_an_error()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var idle = new IgnisApiSessionSeam(MakeConfig(), MakeSessionConfiguration()).Wired();
        Assert.False(await idle.KeepWarm()); // not started
        Assert.Empty(idle.Payloads);

        var session = await Started();
        Assert.False(await session.KeepWarm()); // only the system prompt: nothing to keep warm yet
        await Send(runtime, session, "first");
        var history = Wire(session.Messages);
        session.Rounds.Enqueue(new OpenAICompatibleSession.StreamRound { RawError = "overloaded", StatusCode = 529 });
        Assert.False(await Run(runtime, session.KeepWarm()));
        Assert.Equal(history, Wire(session.Messages));
        Assert.Equal(2, session.Payloads.Count);
    }

    // ---- Tidy ----

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public async Task Tidy_summarises_the_older_turns_on_the_chats_model_and_keeps_the_last_turns_word_for_word()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var session = await Started();
        var system = session.Messages[0];
        JSONValue[] turn1 = [Msg("user", "one"), Call("c1", "read_file"), Result("c1", "file one"), Msg("assistant", "done one")];
        JSONValue[] turn2 = [Msg("user", "two"), Msg("assistant", "done two")];
        JSONValue[] turn3 = [Msg("user", "three"), Call("c3", "list_files"), Result("c3", "a\nb"), Msg("assistant", "done three")];
        JSONValue[] turn4 = [Msg("user", "four"), Call("c4", "read_file"), Result("c4", "file four"), Msg("assistant", "done four")];
        session.SetMessagesForTests([system, .. turn1, .. turn2, .. turn3, .. turn4]);

        session.Rounds.Enqueue(Answer("The user asked for one and two; both are done."));
        Assert.True(await Run(runtime, session.Tidy(2)));

        // One summary request: the history before the kept turns (the cached prefix), then the ask, with tools listed
        // (the same prefix) but not to be called.
        var request = Assert.Single(session.Payloads);
        Assert.Equal(Wire([system, .. turn1, .. turn2, Msg("user", OpenAICompatibleSession.IgnisTidySummaryRequest)]), Wire(MessagesOf(request)));
        Assert.Equal("deepseek-chat", request["model"].JSONValueString);
        Assert.Equal("none", request["tool_choice"].JSONValueString);
        Assert.True(request.ContainsKey("tools"));

        // The new history: system, the summary, an acknowledgement, then turns 3 and 4 exactly, tool calls intact.
        var after = session.Messages;
        Assert.Equal(Wire([system]), Wire([after[0]]));
        Assert.Equal("user", after[1]["role"]!.JSONValueString);
        Assert.Equal(OpenAICompatibleSession.IgnisTidySummaryHeader + "The user asked for one and two; both are done.", after[1]["content"]!.JSONValueString);
        Assert.Equal("assistant", after[2]["role"]!.JSONValueString);
        Assert.Equal(OpenAICompatibleSession.IgnisTidySummaryAcknowledgement, after[2]["content"]!.JSONValueString);
        Assert.Equal(Wire([.. turn3, .. turn4]), Wire(after.Skip(3)));
        // Silent: no "Compacting context." or "Context compacted." notices.
        Assert.DoesNotContain(session.Events, e => e is ProviderEvent.Notice or ProviderEvent.TurnStarted or ProviderEvent.TurnCompleted);
    }

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public async Task Tidy_with_no_more_turns_than_it_keeps_sends_nothing_and_a_failed_summary_leaves_the_history_unchanged()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var session = await Started();
        var system = session.Messages[0];
        session.SetMessagesForTests([system, Msg("user", "one"), Msg("assistant", "a"), Msg("user", "two"), Msg("assistant", "b")]);
        var history = Wire(session.Messages);

        Assert.False(await session.Tidy(2));
        Assert.Empty(session.Payloads);

        session.Rounds.Enqueue(new OpenAICompatibleSession.StreamRound { RawError = "bad request", StatusCode = 400 });
        Assert.False(await Run(runtime, session.Tidy(1)));
        session.Rounds.Enqueue(Answer("   "));
        Assert.False(await Run(runtime, session.Tidy(1)));
        Assert.Equal(history, Wire(session.Messages));
        Assert.Equal(2, session.Payloads.Count);
    }
}
