using System.Buffers.Binary;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/OpenAICompatibleSession.swift (B3 52bc792), :699-707,
// :817-851, :1035-1088, :1096-1328.
//
// Part 3 of 5 (PR-PROVAPI-137 to 161, excluding 148 which does not exist): condensing and
// trimming history, repairing the tool-call chain, the system prompt (identity, mode lines,
// Hydra/anti-slop policy, refresh-in-place), user-content shaping for images, the assistant-turn
// message shape, output truncation, the six tool-definition schemas, and the approval decision
// and request (RequiresApproval, ApproveIfNeeded). PR-PROVAPI-137 (the streamed-delegation retry
// guard) is exercised through TransportErrorFailureForTests, extracted from PerformStream's real
// stream loop so the decision is testable without a live dropped connection: a genuine URLError
// has no Windows-reproducible Apple localizedDescription and is a separate, pre-existing gap
// (SwiftErrors.LocalizedDescription throws for it, per R-ERR-2, until a per-site decision is
// recorded), so a ToolBoomTestError (ISwiftErrorIdentity) stands in as the transport error. Every
// other record here is exercised through the SetMessagesForTests/CondenseHistoryForTests family
// of test-only seams (InternalsVisibleTo Marrow.Services.Tests) so this part's self-contained
// history and message helpers are testable without a live turn.
[Collection(OpenAICompatibleSessionCollection.Name)]
public sealed class OpenAICompatibleSessionThirdTests
{
    private static OpenAICompatibleSession.Config MakeConfig(Uri? chatUrl = null, Func<string, bool>? isVisionModel = null, bool replaysReasoning = false) => new()
    {
        Kind = ProviderKind.Deepseek,
        Title = "DeepSeek",
        AgentIdentity = model => $"agent ({model})",
        DefaultModel = "deepseek-chat",
        ChatURL = chatUrl ?? new Uri("https://api.deepseek.com/chat/completions"),
        ContextWindow = 64_000,
        ReplaysReasoning = replaysReasoning,
        IsVisionModel = isVisionModel ?? (_ => false),
    };

    private static SessionConfiguration MakeSessionConfiguration(
        RuntimeMode runtimeMode = RuntimeMode.FullAccess,
        InteractionMode interactionMode = InteractionMode.Build,
        HydraLaunch? hydra = null,
        bool antiSlopEnabled = true,
        string? apiKey = "sk-test") => new()
    {
        Provider = ProviderKind.Deepseek,
        WorkingDirectory = SwiftURL.Create("file:///C:/work")!,
        Environment = ValueMap<string, string>.Empty,
        RuntimeMode = runtimeMode,
        InteractionMode = interactionMode,
        ApiKey = apiKey,
        Transcript = ValueList<TranscriptMessage>.Empty,
        Hydra = hydra,
        AntiSlopEnabled = antiSlopEnabled,
    };

    private static TurnInput MakeTurn(
        string text = "hi",
        RuntimeMode runtimeMode = RuntimeMode.FullAccess,
        InteractionMode interactionMode = InteractionMode.Build,
        IReadOnlyList<Attachment>? images = null) => new()
    {
        Text = text,
        Images = new ValueList<Attachment>(images ?? []),
        RuntimeMode = runtimeMode,
        InteractionMode = interactionMode,
    };

    private static OpenAICompatibleSession.StreamRound Round(string content = "") => new() { Content = content };

    private static string SseBody(params string[] dataLines) =>
        string.Join("", dataLines.Select(line => $"data: {line}\n\n")) + "data: [DONE]\n\n";

    private static FakeResponse SseResponse(params string[] dataLines) =>
        new(200, [("Content-Type", "text/event-stream")], [], [SseBody(dataLines)]);

    private static async Task Send(TestRuntime runtime, OpenAICompatibleSessionTestSeam session, TurnInput input)
    {
        var task = session.Send(input);
        runtime.Loop.RunUntil(() => task.IsCompleted);
        await task;
    }

    private static JSONValue Msg(string role, string content) => JSONValue.FromDictionaryLiteral(("role", role), ("content", content));

    private static string TempDir() =>
        Directory.CreateDirectory(Path.Combine(Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR")!, Guid.NewGuid().ToString("N"))).FullName;

    private static void WriteBmp(string path, int width, int height)
    {
        var stride = checked((width * 3 + 3) & ~3);
        var pixels = checked(stride * height);
        var bytes = new byte[checked(54 + pixels)];
        bytes[0] = (byte)'B'; bytes[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2), bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), height);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(28), 24);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var i = 54 + y * stride + x * 3;
                bytes[i] = (byte)(17 + x % 64); bytes[i + 1] = (byte)(47 + y % 64); bytes[i + 2] = 91;
            }
        File.WriteAllBytes(path, bytes);
    }

    // ---- PR-PROVAPI-137 ----
    //
    // Exercised through TransportErrorFailureForTests, the extracted branch PerformStream's real
    // stream loop calls (Swift :694-707): a real dropped connection's URLError has no
    // Windows-reproducible Apple localizedDescription text and is a separate, pre-existing gap
    // (SwiftErrors.LocalizedDescription throws for any unmapped Foundation-style error per R-ERR-2,
    // "each such site needs a recorded decision"; URLError itself implements neither ILocalizedError
    // nor ISwiftErrorIdentity). ToolBoomTestError (ISwiftErrorIdentity, from
    // OpenAICompatibleSessionTestSeams.cs) stands in as the transport error so this record's own
    // decision - retry versus fail outright, and the exact suffix - is provable without that gap.

    [Fact]
    [Trait("Req", "PR-PROVAPI-137")] // OpenAICompatibleSession.swift:699-707
    public void A_dropped_connection_after_a_streamed_hydra_delegation_is_not_retried()
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration());
        var content = "```hydra\n{\"task\":\"a\",\"prompt\":\"b\"}\n";

        var failure = session.TransportErrorFailureForTests(new ToolBoomTestError(), streamError: null, content: content);

        var providerError = Assert.IsType<ProviderError>(failure);
        var failed = Assert.IsType<ProviderError.Case.Failed>(providerError.ErrorCase);
        Assert.Equal(
            "The operation couldn’t be completed. (Test.ToolBoom error 1.) This response was not retried because it had already streamed a head delegation.",
            failed.Message); // U+2019 RIGHT SINGLE QUOTATION MARK
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-137")] // OpenAICompatibleSession.swift:701-702, :707 - no delegation streamed: not this record's guard
    public void A_dropped_connection_with_no_streamed_delegation_is_a_retryable_StreamAttemptFailure()
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration());
        var underlying = new ToolBoomTestError();

        var failure = session.TransportErrorFailureForTests(underlying, streamError: null, content: "plain text, no hydra fence", sawContent: true);

        var attempt = Assert.IsType<OpenAICompatibleSession.StreamAttemptFailure>(failure);
        Assert.Same(underlying, attempt.Underlying);
        Assert.True(attempt.EmittedOutput);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-137")] // OpenAICompatibleSession.swift:696-697 - an in-stream error takes priority over the delegation guard
    public void An_in_stream_error_takes_priority_over_the_delegation_guard()
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration());
        var content = "```hydra\n{\"task\":\"a\",\"prompt\":\"b\"}\n";

        var failure = session.TransportErrorFailureForTests(new ToolBoomTestError(), streamError: "server said no", content: content);

        var providerError = Assert.IsType<ProviderError>(failure);
        var failed = Assert.IsType<ProviderError.Case.Failed>(providerError.ErrorCase);
        Assert.DoesNotContain("head delegation", failed.Message, StringComparison.Ordinal);
    }

    // ---- PR-PROVAPI-138 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-138")] // OpenAICompatibleSession.swift:77-80, :1096-1114
    public void CondenseHistory_shortens_older_tool_results_once_they_total_more_than_160000_bytes()
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration());
        // 12 tool messages, each 15000 bytes: total 180000 > 160000. The last 10 are kept whole; of
        // the first 2 candidates, condensing them (each drops well below its own 15000 bytes) is
        // enough to bring the total back under 100000.
        var big = new string('x', 15_000);
        var messages = Enumerable.Range(0, 12).Select(i => Msg("tool", big)).ToList();
        session.SetMessagesForTests(messages);

        session.CondenseHistoryForTests();

        var result = session.Messages;
        var condensed0 = result[0]["content"]!.JSONValueString!;
        Assert.StartsWith(big[..400], condensed0, StringComparison.Ordinal);
        Assert.Contains("\u2026[Marrow condensed this output (15000 bytes); call the tool again if you need it]", condensed0, StringComparison.Ordinal);
        // The 10 most recent (indices 2..11) are never condensed.
        for (var i = 2; i < 12; i++) Assert.Equal(big, result[i]["content"]!.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-138")] // OpenAICompatibleSession.swift:1103
    public void CondenseHistory_does_nothing_when_total_tool_content_is_160000_bytes_or_less()
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration());
        var messages = Enumerable.Range(0, 2).Select(_ => Msg("tool", new string('y', 5_000))).ToList();
        session.SetMessagesForTests(messages);

        session.CondenseHistoryForTests();

        Assert.All(session.Messages, m => Assert.Equal(5_000, m["content"]!.JSONValueString!.Length));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-138")] // OpenAICompatibleSession.swift:1106-1107 - a short or already-condensed result is skipped
    public void CondenseHistory_skips_a_short_result_and_one_already_condensed()
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration());
        var big = new string('z', 15_000);
        var already = new string('a', 300) + "\n\u2026[Marrow condensed this output (99999 bytes); call the tool again if you need it]";
        var messages = new List<JSONValue> { Msg("tool", already), Msg("tool", "short") };
        messages.AddRange(Enumerable.Range(0, 11).Select(_ => Msg("tool", big))); // pushes total > 160000
        session.SetMessagesForTests(messages);

        session.CondenseHistoryForTests();

        Assert.Equal(already, session.Messages[0]["content"]!.JSONValueString); // already condensed: skipped
        Assert.Equal("short", session.Messages[1]["content"]!.JSONValueString); // 1200 bytes or fewer: skipped
    }

    // ---- PR-PROVAPI-139 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-139")] // OpenAICompatibleSession.swift:1245-1258
    public void TrimHistory_does_nothing_at_60_messages_or_fewer()
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration());
        var messages = new List<JSONValue> { Msg("system", "sys") };
        messages.AddRange(Enumerable.Range(0, 59).Select(i => Msg(i % 2 == 0 ? "user" : "assistant", $"m{i}")));
        session.SetMessagesForTests(messages);

        session.TrimHistoryForTests();

        Assert.Equal(60, session.Messages.Count);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-139")] // OpenAICompatibleSession.swift:1251-1257
    // DEVIATION (OD-0134 Ignis): the body is cut to 40, not Swift's 50, so the cached prefix holds for 20 messages.
    public void TrimHistory_keeps_the_system_message_the_whole_running_turn_and_earlier_up_to_40()
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration());
        var messages = new List<JSONValue> { Msg("system", "sys") };
        // 55 earlier messages, then the running turn: a user message and 20 tool/assistant messages
        // after it (76 body messages total, 77 with the system message: over the 60 threshold).
        messages.AddRange(Enumerable.Range(0, 55).Select(i => Msg("assistant", $"old{i}")));
        messages.Add(Msg("user", "current task"));
        messages.AddRange(Enumerable.Range(0, 20).Select(i => Msg("tool", $"t{i}")));
        session.SetMessagesForTests(messages);

        session.TrimHistoryForTests();

        var result = session.Messages;
        Assert.Equal("system", result[0]["role"]!.JSONValueString);
        // The running turn (21 messages: the user message plus its 20 tool/assistant messages) is
        // never cut, however long it is.
        var currentIndex = result.ToList().FindIndex(m => m["content"]?.JSONValueString == "current task");
        Assert.True(currentIndex >= 0);
        Assert.Equal(21, result.Count - currentIndex);
        // Earlier messages are capped so the body (earlier + current) totals 40.
        Assert.Equal(1 + 40, result.Count); // system + 40-message body
    }

    // ---- PR-PROVAPI-140 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-140")] // OpenAICompatibleSession.swift:1266-1322 - an orphaned tool message is dropped
    public void SanitizeHistory_drops_a_tool_message_whose_assistant_call_is_gone()
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration());
        session.SetMessagesForTests(
        [
            Msg("system", "sys"),
            Msg("user", "hi"),
            JSONValue.FromDictionaryLiteral(("role", "tool"), ("tool_call_id", "orphan"), ("content", "result")),
            Msg("assistant", "ok"),
        ]);

        session.SanitizeHistoryForTests();

        Assert.DoesNotContain(session.Messages, m => m["role"]?.JSONValueString == "tool");
        Assert.Equal(3, session.Messages.Count);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-140")] // OpenAICompatibleSession.swift:1282-1289 - an unanswered call is stripped; an emptied assistant message is dropped
    public void SanitizeHistory_strips_an_unanswered_tool_call_and_drops_the_assistant_message_if_left_empty()
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration());
        var assistantWithTwoCalls = JSONValue.FromDictionaryLiteral(
            ("role", "assistant"),
            ("content", ""),
            ("tool_calls", JSONValue.FromArrayLiteral(
                JSONValue.FromDictionaryLiteral(("id", "answered"), ("type", "function"), ("function", JSONValue.FromDictionaryLiteral(("name", "read_file"), ("arguments", "{}")))),
                JSONValue.FromDictionaryLiteral(("id", "unanswered"), ("type", "function"), ("function", JSONValue.FromDictionaryLiteral(("name", "read_file"), ("arguments", "{}")))))));
        session.SetMessagesForTests(
        [
            Msg("system", "sys"),
            Msg("user", "hi"),
            assistantWithTwoCalls,
            JSONValue.FromDictionaryLiteral(("role", "tool"), ("tool_call_id", "answered"), ("content", "result")),
            // history ends here: "unanswered" never got a tool message, e.g. cut by a trim.
        ]);

        session.SanitizeHistoryForTests();

        var assistant = session.Messages.Single(m => m["role"]?.JSONValueString == "assistant");
        var calls = assistant["tool_calls"]!.JSONValueArray!;
        Assert.Single(calls);
        Assert.Equal("answered", calls[0]["id"]!.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-140")] // OpenAICompatibleSession.swift:1282-1287 - all calls stripped and no text: the assistant message itself is dropped
    public void SanitizeHistory_drops_an_assistant_message_left_with_no_calls_and_no_text()
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration());
        var assistantWithOneCall = JSONValue.FromDictionaryLiteral(
            ("role", "assistant"),
            ("content", "   "),
            ("tool_calls", JSONValue.FromArrayLiteral(
                JSONValue.FromDictionaryLiteral(("id", "unanswered"), ("type", "function"), ("function", JSONValue.FromDictionaryLiteral(("name", "read_file"), ("arguments", "{}")))))));
        session.SetMessagesForTests(
        [
            Msg("system", "sys"),
            Msg("user", "hi"),
            assistantWithOneCall,
        ]);

        session.SanitizeHistoryForTests();

        Assert.DoesNotContain(session.Messages, m => m["role"]?.JSONValueString == "assistant");
        Assert.Equal(2, session.Messages.Count);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-140")] // OpenAICompatibleSession.swift:1298-1307 - a real chain survives untouched
    public void SanitizeHistory_keeps_an_answered_tool_chain_intact()
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration());
        var assistant = JSONValue.FromDictionaryLiteral(
            ("role", "assistant"),
            ("content", ""),
            ("tool_calls", JSONValue.FromArrayLiteral(
                JSONValue.FromDictionaryLiteral(("id", "a"), ("type", "function"), ("function", JSONValue.FromDictionaryLiteral(("name", "read_file"), ("arguments", "{}")))))));
        var seed = new List<JSONValue>
        {
            Msg("system", "sys"),
            Msg("user", "hi"),
            assistant,
            JSONValue.FromDictionaryLiteral(("role", "tool"), ("tool_call_id", "a"), ("content", "result")),
            Msg("assistant", "done"),
        };
        session.SetMessagesForTests(seed);

        session.SanitizeHistoryForTests();

        Assert.Equal(seed.Count, session.Messages.Count);
    }

    // ---- PR-PROVAPI-141, 142, 143, 144, 145 ----

    [Theory]
    [Trait("Req", "PR-PROVAPI-141")] // OpenAICompatibleSession.swift:1200 - DEVIATION OD-0012 F1
    [InlineData(InteractionMode.Plan, RuntimeMode.FullAccess)]
    [InlineData(InteractionMode.Build, RuntimeMode.Supervised)]
    public void SystemPrompt_identity_line_names_Marrow_the_agent_identity_the_directory_and_Windows(InteractionMode interactionMode, RuntimeMode runtimeMode)
    {
        var config = MakeConfig();
        var session = new OpenAICompatibleSessionTestSeam(config, MakeSessionConfiguration(runtimeMode, interactionMode));

        var prompt = session.SystemPromptForTests();

        // WorkingDirectory is SwiftURL.Create("file:///C:/work")!.Path, which (SwiftURL.cs:121-130,
        // 151-155, citing URL_Swift.swift:616, 621-624 - posix style, compatibility false) is the
        // decoded absolute path with no drive-letter translation: "/C:/work".
        Assert.StartsWith("You are Marrow's agent (deepseek-chat), working inside /C:/work on Windows.\n", prompt, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-142")] // OpenAICompatibleSession.swift:1177-1178
    public void SystemPrompt_plan_mode_line_wins_regardless_of_runtime_mode()
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration(RuntimeMode.FullAccess, InteractionMode.Plan));

        var prompt = session.SystemPromptForTests();

        Assert.Contains(
            "You are in PLAN mode: research the project, then write a clear step-by-step plan. Do not edit files or run commands that change anything; reads, lists and searches are fine.\n",
            prompt, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-143")] // OpenAICompatibleSession.swift:1180 - DEVIATION OD-0012 F1
    public void SystemPrompt_supervised_mode_line_names_Marrow()
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration(RuntimeMode.Supervised, InteractionMode.Build));

        var prompt = session.SystemPromptForTests();

        Assert.Contains(
            "You are in BUILD mode with supervised permissions: use tools freely but the user approves each one in Marrow.\n",
            prompt, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "PR-PROVAPI-144")] // OpenAICompatibleSession.swift:1182 - DEVIATION OD-0012 F1
    [InlineData(RuntimeMode.AutoAcceptEdits)]
    [InlineData(RuntimeMode.Auto)]
    public void SystemPrompt_autoAcceptEdits_and_auto_share_one_line_naming_Marrow(RuntimeMode runtimeMode)
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration(runtimeMode, InteractionMode.Build));

        var prompt = session.SystemPromptForTests();

        Assert.Contains(
            "You are in BUILD mode: read and edit files freely; the user approves shell commands in Marrow.\n",
            prompt, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-145")] // OpenAICompatibleSession.swift:1184
    public void SystemPrompt_full_access_mode_line_asks_nothing()
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration(RuntimeMode.FullAccess, InteractionMode.Build));

        var prompt = session.SystemPromptForTests();

        Assert.Contains("You are in BUILD mode with full access: use tools freely without asking.\n", prompt, StringComparison.Ordinal);
    }

    // ---- PR-PROVAPI-146 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-146")] // OpenAICompatibleSession.swift:1186-1198 - anti-slop always present, Hydra policy only with a launch
    public void SystemPrompt_always_carries_the_anti_slop_notice_and_the_Hydra_policy_only_when_launched()
    {
        var withoutHydra = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration(hydra: null, antiSlopEnabled: false));
        var withoutHydraPrompt = withoutHydra.SystemPromptForTests();
        Assert.Contains(AntiSlopPolicy.DisabledInstructions, withoutHydraPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(AntiSlopPolicy.EnabledInstructions, withoutHydraPrompt, StringComparison.Ordinal);

        var hydra = new HydraLaunch { HeadsProvider = ProviderKind.Claude, RunsNatively = true };
        var withHydra = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration(hydra: hydra, antiSlopEnabled: true));
        var withHydraPrompt = withHydra.SystemPromptForTests();
        var fallback = HydraPrompts.FallbackPolicy(hydra);
        Assert.Contains(fallback, withHydraPrompt, StringComparison.Ordinal);
        Assert.Contains(AntiSlopPolicy.EnabledInstructions, withHydraPrompt, StringComparison.Ordinal);
        // The Hydra policy comes before the anti-slop text, both before it.
        Assert.True(withHydraPrompt.IndexOf(fallback, StringComparison.Ordinal) < withHydraPrompt.IndexOf(AntiSlopPolicy.EnabledInstructions, StringComparison.Ordinal));
    }

    // ---- PR-PROVAPI-147 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-147")] // OpenAICompatibleSession.swift:1170-1173 - only index 0 is rebuilt, in place
    public async Task RefreshSystemPrompt_rewrites_only_the_first_message_each_turn()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration(RuntimeMode.FullAccess, InteractionMode.Build)).Wired();
        await session.Start();
        session.Rounds.Enqueue(Round("first"));
        await Send(runtime, session, MakeTurn(runtimeMode: RuntimeMode.FullAccess, interactionMode: InteractionMode.Build));
        Assert.Contains("full access", session.Messages[0]["content"]!.JSONValueString!, StringComparison.Ordinal);

        session.Rounds.Enqueue(Round("second"));
        await Send(runtime, session, MakeTurn(runtimeMode: RuntimeMode.Supervised, interactionMode: InteractionMode.Build));

        // The second turn's mode line replaces the first turn's, and only index 0 changed: every
        // later message from the first turn is still exactly where it was.
        Assert.Contains("supervised permissions", session.Messages[0]["content"]!.JSONValueString!, StringComparison.Ordinal);
        Assert.DoesNotContain("full access", session.Messages[0]["content"]!.JSONValueString!, StringComparison.Ordinal);
        Assert.Equal("system", session.Messages[0]["role"]!.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-147")] // OpenAICompatibleSession.swift:1171 - a history not starting with system is left alone entirely
    public void RefreshSystemPrompt_does_nothing_when_the_first_message_is_not_system()
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration());
        session.SetMessagesForTests([Msg("user", "hi")]);

        session.RefreshSystemPromptForTests();

        Assert.Equal("user", session.Messages[0]["role"]!.JSONValueString);
        Assert.Equal("hi", session.Messages[0]["content"]!.JSONValueString);
    }

    // ---- PR-PROVAPI-149, 150, 151 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-149")] // OpenAICompatibleSession.swift:1215-1218 - no usable images: a plain string
    public async Task UserContentValue_with_no_images_is_a_plain_string()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration()).Wired();
        await session.Start();
        session.Rounds.Enqueue(Round("ok"));

        await Send(runtime, session, MakeTurn("hello there"));

        var last = session.Messages[^2]; // the user message precedes the assistant turn appended after the round
        Assert.Equal("user", last["role"]!.JSONValueString);
        Assert.IsType<JSONValue.String>(last["content"]);
        Assert.Equal("hello there", last["content"]!.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-150")] // OpenAICompatibleSession.swift:1217-1221 - images with a non-vision model: named in the text
    public async Task UserContentValue_with_images_and_a_non_vision_model_names_them_in_the_text()
    {
        var path = Path.Combine(TempDir(), "a.bmp");
        WriteBmp(path, 4, 2);
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(isVisionModel: _ => false), MakeSessionConfiguration()).Wired();
        await session.Start();
        session.Rounds.Enqueue(Round("ok"));

        var images = new List<Attachment>
        {
            new() { Name = "a.png", Path = path, MimeType = "image/bmp" },
            new() { Name = "b.jpg", Path = path, MimeType = "image/bmp" },
        };
        await Send(runtime, session, MakeTurn("look", images: images));

        var last = session.Messages[^2];
        Assert.Equal("look\n\n[Attached images (model cannot see them): a.png, b.jpg]", last["content"]!.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-151")] // OpenAICompatibleSession.swift:1222-1227 - a vision model gets a text part then image_url parts, capped at four
    public async Task UserContentValue_with_a_vision_model_sends_text_then_up_to_four_image_url_parts()
    {
        var path = Path.Combine(TempDir(), "a.bmp");
        WriteBmp(path, 4, 2);
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(isVisionModel: _ => true), MakeSessionConfiguration()).Wired();
        await session.Start();
        session.Rounds.Enqueue(Round("ok"));

        var images = Enumerable.Range(0, 5).Select(i => new Attachment { Name = $"img{i}", Path = path, MimeType = "image/bmp" }).ToList();
        await Send(runtime, session, MakeTurn("see this", images: images));

        var last = session.Messages[^2];
        var parts = last["content"]!.JSONValueArray!;
        Assert.Equal(5, parts.Count); // text + at most 4 images (a 5th is dropped before anything is read)
        Assert.Equal("text", parts[0]["type"]!.JSONValueString);
        Assert.Equal("see this", parts[0]["text"]!.JSONValueString);
        for (var i = 1; i < 5; i++)
        {
            Assert.Equal("image_url", parts[i]["type"]!.JSONValueString);
            var url = parts[i]["image_url"]!["url"]!.JSONValueString!;
            Assert.StartsWith("data:image/bmp;base64,", url, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-151")] // OpenAICompatibleSession.swift:1223-1224 - a downscaled image's data URL carries its own (jpeg) mime type
    public async Task UserContentValue_data_URL_mime_type_follows_the_downscaled_image_when_over_2000px()
    {
        var path = Path.Combine(TempDir(), "big.bmp");
        WriteBmp(path, 2500, 500);
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(isVisionModel: _ => true), MakeSessionConfiguration()).Wired();
        await session.Start();
        session.Rounds.Enqueue(Round("ok"));

        await Send(runtime, session, MakeTurn("see", images: [new Attachment { Name = "big", Path = path, MimeType = "image/bmp" }]));

        var last = session.Messages[^2];
        var parts = last["content"]!.JSONValueArray!;
        var url = parts[1]["image_url"]!["url"]!.JSONValueString!;
        Assert.StartsWith("data:image/jpeg;base64,", url, StringComparison.Ordinal); // :108 mimeType becomes image/jpeg once downscaled
    }

    // ---- PR-PROVAPI-152 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-152")] // OpenAICompatibleSession.swift:1234-1243 - reasoning_content present (even empty) only when the provider replays it; tool_calls only when there are any
    public async Task AssistantTurn_carries_reasoning_content_only_when_configured_and_tool_calls_only_when_present()
    {
        using var runtime = new TestRuntime();
        var replays = new OpenAICompatibleSessionTestSeam(MakeConfig(replaysReasoning: true), MakeSessionConfiguration()).Wired();
        await replays.Start();
        replays.Rounds.Enqueue(Round("hi")); // no reasoning text, no tool calls
        await Send(runtime, replays, MakeTurn());
        var withReplay = replays.Messages[^1];
        Assert.Equal("assistant", withReplay["role"]!.JSONValueString);
        Assert.Equal("", withReplay["reasoning_content"]!.JSONValueString); // empty string is acceptable, field still present
        Assert.Null(withReplay["tool_calls"]);

        using var runtime2 = new TestRuntime();
        var noReplay = new OpenAICompatibleSessionTestSeam(MakeConfig(replaysReasoning: false), MakeSessionConfiguration()).Wired();
        await noReplay.Start();
        noReplay.Rounds.Enqueue(new OpenAICompatibleSession.StreamRound
        {
            Content = "",
            ToolCalls = new ValueList<OpenAICompatibleSession.PendingToolCall>([new OpenAICompatibleSession.PendingToolCall("call-1", "read_file", "{\"path\":\"a\"}")]),
        });
        noReplay.ToolHandler = _ => Task.FromResult("done");
        noReplay.Rounds.Enqueue(Round("wrapped up"));
        await Send(runtime2, noReplay, MakeTurn());
        var toolTurn = noReplay.Messages.First(m => m["role"]?.JSONValueString == "assistant" && m["tool_calls"] is not null);
        Assert.Null(toolTurn["reasoning_content"]); // field left off entirely when reasoning is not replayed
        var calls = toolTurn["tool_calls"]!.JSONValueArray!;
        Assert.Single(calls);
        Assert.Equal("call-1", calls[0]["id"]!.JSONValueString);
        Assert.Equal("function", calls[0]["type"]!.JSONValueString);
        Assert.Equal("read_file", calls[0]["function"]!["name"]!.JSONValueString);
        Assert.Equal("{\"path\":\"a\"}", calls[0]["function"]!["arguments"]!.JSONValueString); // raw accumulated string, not re-serialized
    }

    // ---- PR-PROVAPI-153 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-153")] // OpenAICompatibleSession.swift:1324-1328
    public void Summary_leaves_short_text_unchanged_and_truncates_long_text_with_the_marker()
    {
        Assert.Equal("short", OpenAICompatibleSession.SummaryForTests("short", 4000));
        var exact = new string('a', 10);
        Assert.Equal(exact, OpenAICompatibleSession.SummaryForTests(exact, 10)); // exactly at the limit: unchanged
        var over = new string('a', 11);
        Assert.Equal("aaaaaaaaaa\n\u2026(truncated)", OpenAICompatibleSession.SummaryForTests(over, 10));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-153")] // OpenAICompatibleSession.swift:1326 - a text whose byte count exceeds the limit but whose character count does not is left whole
    public void Summary_leaves_text_whole_when_only_its_byte_count_exceeds_the_limit()
    {
        var text = new string('\u00e9', 6); // each is 2 UTF-8 bytes: 6 chars, 12 bytes
        Assert.Equal(6, SwiftString.Count(text));
        Assert.Equal(12, SwiftString.Utf8Count(text));
        Assert.Equal(text, OpenAICompatibleSession.SummaryForTests(text, 10)); // 12 > 10 but 6 <= 10: unchanged
    }

    // ---- PR-PROVAPI-154 to 159 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-154")] // OpenAICompatibleSession.swift:818-826
    [Trait("Req", "PR-PROVAPI-155")] // OpenAICompatibleSession.swift:827-831
    [Trait("Req", "PR-PROVAPI-156")] // OpenAICompatibleSession.swift:832-836
    [Trait("Req", "PR-PROVAPI-157")] // OpenAICompatibleSession.swift:837-841
    [Trait("Req", "PR-PROVAPI-158")] // OpenAICompatibleSession.swift:842-846
    [Trait("Req", "PR-PROVAPI-159")] // OpenAICompatibleSession.swift:847-851
    public async Task The_six_tool_definitions_match_their_descriptions_and_schemas_byte_for_byte()
    {
        using var scratch = new StorageScratchFolder(); // MCPHub.Tools() reads Storage.Locations even with no MCP servers configured
        FakeRequest? seen = null;
        await using var server = new FakeUpstreamServer { Handler = r => { seen = r; return SseResponse("""{"choices":[{"delta":{"content":"ok"}}]}"""); } };
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionCapturingSeam(MakeConfig(new Uri($"{server.BaseUrl}/v1/chat/completions")), MakeSessionConfiguration()).Wired();
        await session.Start();
        var task = session.Send(new TurnInput { Text = "go", Images = ValueList<Attachment>.Empty, RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build });
        runtime.Loop.RunUntil(() => task.IsCompleted);
        await task;

        var tools = JSONValue.Parse(seen!.Body)!["tools"]!.JSONValueArray!;
        var expected = JSONValue.Parse("""
        [
          {"type":"function","function":{"name":"read_file","description":"Read a file inside the project. Path is relative to the project root. The whole file by default; for a big file (hundreds of lines) pass start_line and line_count to read just the part you need, and the result says how many lines the file has.","parameters":{"type":"object","properties":{"path":{"type":"string","description":"Relative file path"},"start_line":{"type":"integer","description":"First line to read, counted from 1"},"line_count":{"type":"integer","description":"How many lines to read from start_line"}},"required":["path"]}}},
          {"type":"function","function":{"name":"list_files","description":"List files in a directory inside the project.","parameters":{"type":"object","properties":{"path":{"type":"string","description":"Relative directory, empty for the root"},"recursive":{"type":"boolean"}}}}},
          {"type":"function","function":{"name":"search_text","description":"Search file contents for a fixed string or regex.","parameters":{"type":"object","properties":{"pattern":{"type":"string"},"path":{"type":"string","description":"Relative directory or file"},"regex":{"type":"boolean"}},"required":["pattern"]}}},
          {"type":"function","function":{"name":"write_file","description":"Create or overwrite a file inside the project with the full new content.","parameters":{"type":"object","properties":{"path":{"type":"string"},"content":{"type":"string"}},"required":["path","content"]}}},
          {"type":"function","function":{"name":"edit_file","description":"Replace one exact block of text in a file with new text. old_string must appear exactly once.","parameters":{"type":"object","properties":{"path":{"type":"string"},"old_string":{"type":"string"},"new_string":{"type":"string"}},"required":["path","old_string","new_string"]}}},
          {"type":"function","function":{"name":"run_command","description":"Run a shell command in the project root and capture its output.","parameters":{"type":"object","properties":{"command":{"type":"string"}},"required":["command"]}}}
        ]
        """)!;
        Assert.Equal(expected.CompactString, new JSONValue.Array(new ValueList<JSONValue>(tools)).CompactString);
    }

    // ---- PR-PROVAPI-160 ----

    [Theory]
    [Trait("Req", "PR-PROVAPI-160")] // OpenAICompatibleSession.swift:1047-1056
    [InlineData(InteractionMode.Build, RuntimeMode.Supervised, ToolCall.Kind.Read, true)]
    [InlineData(InteractionMode.Build, RuntimeMode.Supervised, ToolCall.Kind.Edit, true)]
    [InlineData(InteractionMode.Build, RuntimeMode.Supervised, ToolCall.Kind.Command, true)]
    [InlineData(InteractionMode.Build, RuntimeMode.AutoAcceptEdits, ToolCall.Kind.Read, false)]
    [InlineData(InteractionMode.Build, RuntimeMode.AutoAcceptEdits, ToolCall.Kind.Edit, false)]
    [InlineData(InteractionMode.Build, RuntimeMode.AutoAcceptEdits, ToolCall.Kind.Command, true)]
    [InlineData(InteractionMode.Build, RuntimeMode.Auto, ToolCall.Kind.Command, true)]
    [InlineData(InteractionMode.Build, RuntimeMode.FullAccess, ToolCall.Kind.Command, false)]
    [InlineData(InteractionMode.Build, RuntimeMode.FullAccess, ToolCall.Kind.Edit, false)]
    public void RequiresApproval_by_interaction_then_runtime_mode(InteractionMode interactionMode, RuntimeMode runtimeMode, ToolCall.Kind kind, bool expected)
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration(runtimeMode, interactionMode));

        Assert.Equal(expected, session.RequiresApprovalForTests(kind));
    }

    [Theory]
    [Trait("Req", "PR-PROVAPI-160")] // OpenAICompatibleSession.swift:1048-1050 - plan mode blocks edits and commands even after "approve for turn"
    [InlineData(ToolCall.Kind.Edit)]
    [InlineData(ToolCall.Kind.Command)]
    public void RequiresApproval_plan_mode_still_asks_for_edits_and_commands_even_after_approve_for_turn(ToolCall.Kind kind)
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration(RuntimeMode.FullAccess, InteractionMode.Plan));
        session.SetApproveAllRemainingForTests(true);

        Assert.True(session.RequiresApprovalForTests(kind));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-160")] // OpenAICompatibleSession.swift:1049 - "approve for turn" suppresses everything plan does not block
    public void RequiresApproval_approve_for_turn_suppresses_a_read_in_supervised_mode()
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration(RuntimeMode.Supervised, InteractionMode.Build));
        session.SetApproveAllRemainingForTests(true);

        Assert.False(session.RequiresApprovalForTests(ToolCall.Kind.Read));
        Assert.False(session.RequiresApprovalForTests(ToolCall.Kind.Command));
    }

    // ---- PR-PROVAPI-161 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-161")] // OpenAICompatibleSession.swift:1060 - an interrupted turn declines immediately, before anything else
    public async Task ApproveIfNeeded_on_an_interrupted_turn_declines_without_asking()
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration(RuntimeMode.Supervised, InteractionMode.Build)).Wired();
        session.SetInterruptedForTests(true);

        var approved = await session.ApproveIfNeededForTests(ToolCall.Kind.Command, "run it", null, null);

        Assert.False(approved);
        Assert.Empty(session.Events.OfType<ProviderEvent.Approval>());
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-161")] // OpenAICompatibleSession.swift:1061 - no approval needed: true, no request raised
    public async Task ApproveIfNeeded_returns_true_without_a_request_when_the_kind_needs_no_approval()
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration(RuntimeMode.FullAccess, InteractionMode.Build)).Wired();

        var approved = await session.ApproveIfNeededForTests(ToolCall.Kind.Edit, "edit it", null, null);

        Assert.True(approved);
        Assert.Empty(session.Events.OfType<ProviderEvent.Approval>());
    }

    [Theory]
    [Trait("Req", "PR-PROVAPI-161")] // OpenAICompatibleSession.swift:1063-1067 - the approval kind by tool kind
    [InlineData(ToolCall.Kind.Command, ApprovalRequest.Kind.Command)]
    [InlineData(ToolCall.Kind.Edit, ApprovalRequest.Kind.FileChange)]
    [InlineData(ToolCall.Kind.Read, ApprovalRequest.Kind.Tool)]
    [InlineData(ToolCall.Kind.Search, ApprovalRequest.Kind.Tool)]
    public async Task ApproveIfNeeded_raises_one_request_with_the_kind_mapped_from_the_tool_kind(ToolCall.Kind toolKind, ApprovalRequest.Kind expectedApprovalKind)
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration(RuntimeMode.Supervised, InteractionMode.Build)).Wired();
        // Started through runtime.OnMain, not called directly on the xUnit test thread: see the comment on
        // ApproveIfNeeded_resolves_true_on_allow_and_false_on_decline below for why a direct call captures
        // xUnit's own SynchronizationContext instead of runtime.Loop's, racing ResolveApproval against the
        // continuation that registers the pending approval. This test hit that same race under the parallel
        // suite (a recurring provider test-host abort with zero assertion failures, blaming this theory).
        var task = runtime.OnMain(() => session.ApproveIfNeededForTests(toolKind, "do it", "detail text", "item-1"));
        runtime.Loop.RunUntilIdle();

        var request = session.Events.OfType<ProviderEvent.Approval>().Single().Value;
        Assert.Equal(expectedApprovalKind, request.RequestKind);
        Assert.Equal("do it", request.Title);
        Assert.Equal("detail text", request.Detail);
        Assert.Equal("item-1", request.ToolItemID);
        Assert.Equal(3, request.Options.Count);
        Assert.Equal(("allow", "Approve", ApprovalRequest.Option.Role.Approve), (request.Options[0].Id, request.Options[0].Title, request.Options[0].OptionRole));
        Assert.Equal(("always", "Approve for turn", ApprovalRequest.Option.Role.ApproveAlways), (request.Options[1].Id, request.Options[1].Title, request.Options[1].OptionRole));
        Assert.Equal(("decline", "Decline", ApprovalRequest.Option.Role.Decline), (request.Options[2].Id, request.Options[2].Title, request.Options[2].OptionRole));

        session.ResolveApproval(request.Id, "decline");
        runtime.Loop.RunUntilIdle();
        Assert.False(await task);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-161")] // OpenAICompatibleSession.swift:1076-1087 - approving resolves true, declining resolves false
    public async Task ApproveIfNeeded_resolves_true_on_allow_and_false_on_decline()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration(RuntimeMode.Supervised, InteractionMode.Build)).Wired();

        // Started through runtime.OnMain, consistent with how this suite drives every async provider
        // call (ManualMainLoop.cs:6-9: "every await in main-actor code resumes back into the loop").
        // Before B3 removed the `MainActor.Run` wrapper around the approval event post (:1073),
        // ApproveIfNeeded's first suspension was that hop, and an await captures whatever
        // SynchronizationContext.Current is at the call site: called directly (not through
        // runtime.OnMain), that was xUnit's own AsyncTestSyncContext, not runtime.Loop's - so the
        // continuation that registers the pending approval (:1080, `pendingApprovals[id] = continuation`)
        // resumed on an arbitrary thread pool thread, racing the very next line here (ResolveApproval)
        // instead of being ordered by runtime.Loop.RunUntilIdle(). Losing that race dropped
        // ResolveApproval before the continuation was registered, so nothing ever resumed it and
        // `await allowTask` hung forever - reproduced deterministically (5/5 isolated runs) before the
        // fix that added runtime.OnMain here. B3's direct event post removes that race outright: the
        // event fires and the continuation registers synchronously, with no suspension in between, so
        // calling this seam directly would no longer hang either. runtime.OnMain is kept regardless,
        // since it remains correct and this test's job is the allow/decline outcome, not the dispatch
        // mechanism.
        var allowTask = runtime.OnMain(() => session.ApproveIfNeededForTests(ToolCall.Kind.Command, "run", null, null));
        var allowId = session.Events.OfType<ProviderEvent.Approval>().Single().Value.Id;
        session.ResolveApproval(allowId, "allow");
        runtime.Loop.RunUntilIdle();
        Assert.True(await allowTask);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-161")] // OpenAICompatibleSession.swift:1082-1086 - cancelling the wait resolves false and removes the pending entry
    public async Task ApproveIfNeeded_interrupting_the_turn_while_a_request_is_pending_resolves_false()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration(RuntimeMode.Supervised, InteractionMode.Build)).Wired();

        // Same reason as the theory above and ApproveIfNeeded_resolves_true_on_allow_and_false_on_decline: start
        // through runtime.OnMain so the approval-registering continuation resumes on runtime.Loop, not on an
        // arbitrary thread pool thread racing Interrupt() below.
        var task = runtime.OnMain(() => session.ApproveIfNeededForTests(ToolCall.Kind.Command, "run", null, null));
        runtime.Loop.RunUntilIdle();
        Assert.Single(session.Events.OfType<ProviderEvent.Approval>());

        await session.Interrupt(); // resolves every pending approval with false and clears the map
        runtime.Loop.RunUntilIdle();

        Assert.False(await task);
    }
}
