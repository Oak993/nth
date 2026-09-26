using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/OpenAICompatibleSession.swift (B3 52bc792), :425-429, :457-468,
// :547-549, :898-917, :1027-1032.
//
// Part 5 of 5 (UI-PROVAPI-021 to 025): the timeline-row and notice records for MCP/unknown tool rows,
// the lost-connection retry warning, the round-ceiling warning, and the compaction notices. The
// underlying orchestration these records ride on (transport retries PR-PROVAPI-121/122, the round
// ceiling PR-PROVAPI-097, compact PR-PROVAPI-116, MCP dispatch PR-PROVAPI-164, the unknown-tool
// default case PR-PROVAPI-172) is proven in parts 1, 2 and 4; this part's tests are new and prove the
// UI records specifically (R-TEST-1: one Req trait per record it proves).
[Collection(OpenAICompatibleSessionCollection.Name)]
public sealed class OpenAICompatibleSessionFifthTests
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

    private static SwiftURL WorkingDirectoryURL(string root) => SwiftURL.Create("file:" + root.Replace('\\', '/'))!;

    private static SessionConfiguration MakeSessionConfiguration(string root,
        RuntimeMode runtimeMode = RuntimeMode.FullAccess, InteractionMode interactionMode = InteractionMode.Build) => new()
    {
        Provider = ProviderKind.Deepseek,
        WorkingDirectory = WorkingDirectoryURL(root),
        Environment = ValueMap<string, string>.Empty,
        RuntimeMode = runtimeMode,
        InteractionMode = interactionMode,
        ApiKey = "sk-test",
    };

    private static (OpenAICompatibleSession Session, List<ProviderEvent> Events) WiredSession(string root,
        RuntimeMode runtimeMode = RuntimeMode.FullAccess, InteractionMode interactionMode = InteractionMode.Build)
    {
        var session = new OpenAICompatibleSession(MakeConfig(), MakeSessionConfiguration(root, runtimeMode, interactionMode));
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        return (session, events);
    }

    private static OpenAICompatibleSession.PendingToolCall Call(string name, string argumentsJson) =>
        new("call-1", name, argumentsJson);

    private static MCPServerSpec.Stdio StdioFixture(string script) =>
        new("powershell", new ValueList<string>(["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script]),
            ValueMap<string, string>.Empty);

    private static string FixtureScript([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.Join(Path.GetDirectoryName(path)!, "..", "Fixtures", "mcp-stdio-fixture.ps1");

    private static async Task SeedRealServer(OpenAICompatibleSession session, string serverId, string toolName)
    {
        await MCPHub.Shared.Seed(new Dictionary<string, MCPServerSpec>(SwiftStringComparer.Instance) { [serverId] = StdioFixture(FixtureScript()) });
        session.SeedMcpToolsForTests([new MCPHub.Tool(serverId, toolName, "", new JSONValue.Null())]);
    }

    // ---- UI-PROVAPI-021: MCP tool row (title, detail, and its states) ----

    [Fact]
    [Trait("Req", "UI-PROVAPI-021")] // OpenAICompatibleSession.swift:898-917
    public async Task An_mcp_row_starts_running_with_a_middle_dot_title_and_pretty_printed_detail_then_completes_truncated_at_12000()
    {
        using var folder = new ScratchFolder();
        var (session, events) = WiredSession(folder.Root);
        const string serverId = "srv-021-a";
        try
        {
            await SeedRealServer(session, serverId, "echo");

            // Running: the row starts with kind mcp, "<server> \u00b7 <tool>" and the pretty-printed
            // arguments as Detail (:898-899).
            var args = "{\"text\":\"hi\"}";
            var result = await session.ExecuteToolForTests(Call($"mcp__{serverId}__echo", args));
            var started = events.OfType<ProviderEvent.ToolStarted>().Single().Call;
            Assert.Equal(ToolCall.Kind.Mcp, started.ToolCallKind);
            Assert.Equal($"{serverId} \U000000B7 echo", started.Title); // U+00B7 MIDDLE DOT, :899
            Assert.Equal((JSONValue.Parse(args)!).PrettyString, started.Detail); // :899 detail: args.prettyString

            // Completed, truncated to 12000: the row's Output is Summary(result.Text, 12_000), :910,
            // while the string handed back to the model (the return value) is the untruncated text.
            events.Clear();
            var bigArgs = "{\"text\":\"" + new string('x', 13_000) + "\"}";
            var bigResult = await session.ExecuteToolForTests(Call($"mcp__{serverId}__echo", bigArgs));
            var expectedRowOutput = OpenAICompatibleSession.SummaryForTests(bigResult, 12_000);
            Assert.True(bigResult.Length > 12_000);
            Assert.NotEqual(bigResult, expectedRowOutput); // truncation actually happened
            Assert.EndsWith("\n\U00002026(truncated)", expectedRowOutput, StringComparison.Ordinal); // U+2026 HORIZONTAL ELLIPSIS, :1327
            var updated = events.OfType<ProviderEvent.ToolUpdated>().Single().Update;
            Assert.Equal(expectedRowOutput, updated.Output);
            Assert.Equal(ToolCall.Status.Completed, updated.Status);
        }
        finally
        {
            await MCPHub.Shared.Reload();
        }
    }

    [Fact]
    [Trait("Req", "UI-PROVAPI-021")] // OpenAICompatibleSession.swift:899 - args.isNull -> nil detail
    public async Task An_mcp_rows_detail_is_absent_when_the_arguments_are_null()
    {
        using var folder = new ScratchFolder();
        var (session, events) = WiredSession(folder.Root);
        const string serverId = "srv-021-b";
        try
        {
            await SeedRealServer(session, serverId, "echo");
            await session.ExecuteToolForTests(Call($"mcp__{serverId}__echo", "null"));
            var started = events.OfType<ProviderEvent.ToolStarted>().Single().Call;
            Assert.Null(started.Detail);
        }
        finally
        {
            await MCPHub.Shared.Reload();
        }
    }

    [Fact]
    [Trait("Req", "UI-PROVAPI-021")] // OpenAICompatibleSession.swift:906-908 - failed with the MCP tool's own error text
    public async Task An_mcp_rows_failed_state_carries_the_mcp_tools_own_error_text()
    {
        using var folder = new ScratchFolder();
        var (session, events) = WiredSession(folder.Root);
        const string serverId = "srv-021-c";
        try
        {
            await SeedRealServer(session, serverId, "error-tool");
            var result = await session.ExecuteToolForTests(Call($"mcp__{serverId}__error-tool", "{}"));

            Assert.Equal("failed", result); // the fixture's isError text, echoed back to the model too
            var updated = events.OfType<ProviderEvent.ToolUpdated>().Single().Update;
            Assert.Equal("failed", updated.Output);
            Assert.Equal(ToolCall.Status.Failed, updated.Status);
        }
        finally
        {
            await MCPHub.Shared.Reload();
        }
    }

    [Fact]
    [Trait("Req", "UI-PROVAPI-021")] // OpenAICompatibleSession.swift:912-915 - failed with the thrown error's description
    public async Task An_mcp_rows_failed_state_carries_a_thrown_errors_localized_description()
    {
        using var folder = new ScratchFolder();
        var (session, events) = WiredSession(folder.Root);
        // An empty seed (never a server named this) so MCPHub.Shared.Call's specs lookup misses
        // without falling through to ReadSpecs's on-disk config file (which needs Storage bound):
        // it throws MCPHubError.UnknownTool, whose ErrorDescription is "Unknown tool: <name>."
        // (MCPHub.cs :30, MCPHub.swift :10-11).
        await MCPHub.Shared.Seed(new Dictionary<string, MCPServerSpec>(SwiftStringComparer.Instance));
        var callName = "mcp__no-such-server-021__whatever";
        var result = await session.ExecuteToolForTests(Call(callName, "{}"));

        Assert.Equal($"Error: Unknown tool: {callName}.", result); // :915 "Error: \(error.localizedDescription)"
        var updated = events.OfType<ProviderEvent.ToolUpdated>().Single().Update;
        Assert.Equal($"Unknown tool: {callName}.", updated.Output); // :914 output: error.localizedDescription
        Assert.Equal(ToolCall.Status.Failed, updated.Status);
    }

    [Fact]
    [Trait("Req", "UI-PROVAPI-021")] // OpenAICompatibleSession.swift:900-902 - declined
    public async Task An_mcp_rows_declined_state_matches_the_shared_declined_wording()
    {
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        var (session, events) = WiredSession(folder.Root, RuntimeMode.Supervised);
        var task = session.ExecuteToolForTests(Call("mcp__srv-021-d__echo", "{}"));
        runtime.Loop.RunUntilIdle();
        var approval = events.OfType<ProviderEvent.Approval>().Single().Value;
        await Task.Delay(300); // the pending-approval continuation resumes asynchronously
        session.ResolveApproval(approval.Id, "decline");
        runtime.Loop.RunUntilIdle();
        var result = await task;

        Assert.Equal("The user declined this action. Ask how to proceed instead of retrying it.", result); // :1037
        var updated = events.OfType<ProviderEvent.ToolUpdated>().Single().Update;
        Assert.Equal("The user declined this action.", updated.Output); // :1036
        Assert.Equal(ToolCall.Status.Declined, updated.Status);
    }

    // ---- UI-PROVAPI-022: unknown tool row ----

    [Fact]
    [Trait("Req", "UI-PROVAPI-022")] // OpenAICompatibleSession.swift:1027-1032
    public async Task An_unknown_tools_row_shows_the_raw_name_and_says_Unknown_tool_while_the_model_hears_a_different_sentence()
    {
        using var folder = new ScratchFolder();
        var (session, events) = WiredSession(folder.Root, RuntimeMode.Supervised); // no approval requested either way
        var result = await session.ExecuteToolForTests(Call("frobnicate_022", "{}"));

        Assert.DoesNotContain(events, e => e is ProviderEvent.Approval); // "there is no running state worth seeing" is immediate
        var started = events.OfType<ProviderEvent.ToolStarted>().Single().Call;
        Assert.Equal(ToolCall.Kind.Other, started.ToolCallKind);
        Assert.Equal("frobnicate_022", started.Title); // :1028 title: tool.name, the raw name
        var updated = events.OfType<ProviderEvent.ToolUpdated>().Single().Update;
        Assert.Equal("Unknown tool.", updated.Output); // :1030
        Assert.Equal(ToolCall.Status.Failed, updated.Status);
        Assert.Equal("Error: unknown tool frobnicate_022.", result); // :1031, the row text and the model's text differ
    }

    // ---- UI-PROVAPI-023: lost-connection warning during a retry ----

    private static OpenAICompatibleSessionRetryLadderSeam MakeRetrySession() =>
        new OpenAICompatibleSessionRetryLadderSeam(MakeConfig(), MakeSessionConfiguration(Path.GetTempPath())).Wired();

    [Fact]
    [Trait("Req", "UI-PROVAPI-023")] // OpenAICompatibleSession.swift:547-549 - the exact sentence, level warning
    public async Task The_lost_connection_notice_is_warning_level_with_the_providers_title_interpolated()
    {
        using var runtime = new TestRuntime();
        var session = MakeRetrySession();
        session.Results.Enqueue(() => throw new OpenAICompatibleSession.StreamAttemptFailure(new URLError(URLError.Code.NetworkConnectionLost), true));
        session.Results.Enqueue(() => new OpenAICompatibleSession.StreamRound { Content = "resumed" });

        Task<OpenAICompatibleSession.StreamRound>? task = null;
        runtime.Loop.Enqueue(() => task = session.StreamOneRoundForTests("deepseek-chat", null, false));
        runtime.Loop.RunUntilIdle();
        runtime.Advance(TimeSpan.FromSeconds(1));

        var notice = Assert.Single(session.Events.OfType<ProviderEvent.Notice>());
        Assert.Equal(Notice.Level.Warning, notice.Value.NoticeLevel);
        Assert.Equal(
            "DeepSeek lost the connection while answering. That reply was cut off, and this response is restarting.",
            notice.Value.Message);
        Assert.Equal("resumed", (await task!).Content);
    }

    [Fact]
    [Trait("Req", "UI-PROVAPI-023")] // OpenAICompatibleSession.swift:547 - emitted only when the attempt had streamed output
    public void The_lost_connection_notice_is_withheld_when_nothing_had_streamed_yet()
    {
        using var runtime = new TestRuntime();
        var session = MakeRetrySession();
        session.Results.Enqueue(() => throw new OpenAICompatibleSession.StreamAttemptFailure(new URLError(URLError.Code.TimedOut), false));
        session.Results.Enqueue(() => new OpenAICompatibleSession.StreamRound { Content = "ok" });

        Task<OpenAICompatibleSession.StreamRound>? task = null;
        runtime.Loop.Enqueue(() => task = session.StreamOneRoundForTests("deepseek-chat", null, false));
        runtime.Loop.RunUntilIdle();
        runtime.Advance(TimeSpan.FromSeconds(1));

        Assert.Empty(session.Events.OfType<ProviderEvent.Notice>());
        Assert.True(task!.IsCompletedSuccessfully);
    }

    [Fact]
    [Trait("Req", "UI-PROVAPI-023")] // OpenAICompatibleSession.swift:82-84, :541-551 - at most twice per round (maxTransportRetries = 2)
    public void The_lost_connection_notice_never_fires_more_than_twice_in_one_round()
    {
        using var runtime = new TestRuntime();
        var session = MakeRetrySession();
        session.Results.Enqueue(() => throw new OpenAICompatibleSession.StreamAttemptFailure(new URLError(URLError.Code.NetworkConnectionLost), true));
        session.Results.Enqueue(() => throw new OpenAICompatibleSession.StreamAttemptFailure(new URLError(URLError.Code.NetworkConnectionLost), true));
        session.Results.Enqueue(() => new OpenAICompatibleSession.StreamRound { Content = "ok" });

        Task<OpenAICompatibleSession.StreamRound>? task = null;
        runtime.Loop.Enqueue(() => task = session.StreamOneRoundForTests("deepseek-chat", null, false));
        runtime.Loop.RunUntilIdle();
        runtime.Advance(TimeSpan.FromSeconds(1)); // after attempt 1's failure
        runtime.Advance(TimeSpan.FromSeconds(2)); // after attempt 2's failure

        Assert.Equal(2, session.Events.OfType<ProviderEvent.Notice>().Count()); // never a third, since maxTransportRetries = 2
        Assert.True(task!.IsCompletedSuccessfully);
    }

    // ---- UI-PROVAPI-024: round-ceiling warning ----

    private static SessionConfiguration MakeTurnSessionConfiguration() => new()
    {
        Provider = ProviderKind.Deepseek,
        WorkingDirectory = SwiftURL.Create("file:///C:/work")!,
        Environment = ValueMap<string, string>.Empty,
        RuntimeMode = RuntimeMode.FullAccess,
        InteractionMode = InteractionMode.Build,
        ApiKey = "sk-test",
    };

    private static TurnInput MakeTurn() => new()
    {
        Text = "hi",
        Images = ValueList<Attachment>.Empty,
        RuntimeMode = RuntimeMode.FullAccess,
        InteractionMode = InteractionMode.Build,
        IsFinalReport = false,
    };

    private static OpenAICompatibleSession.StreamRound RoundWithTool(string id) => new()
    {
        ToolCalls = new ValueList<OpenAICompatibleSession.PendingToolCall>(
            [new OpenAICompatibleSession.PendingToolCall(id, "run_command", "{}")]),
    };

    [Fact]
    [Trait("Req", "UI-PROVAPI-024")] // OpenAICompatibleSession.swift:425-429, :72
    public async Task The_round_ceiling_notice_fires_exactly_once_with_the_curly_quoted_continue_and_a_warning_level()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeTurnSessionConfiguration()).Wired();
        session.ToolHandler = _ => Task.FromResult("ok");
        await session.Start();
        for (var i = 0; i < 200; i++) session.Rounds.Enqueue(RoundWithTool($"c{i}"));

        var task = session.Send(MakeTurn());
        runtime.Loop.RunUntil(() => task.IsCompleted);
        await task;

        var notices = session.Events.OfType<ProviderEvent.Notice>().ToList();
        var notice = Assert.Single(notices); // emitted once, :425-429
        Assert.Equal(Notice.Level.Warning, notice.Value.NoticeLevel);
        Assert.Equal(
            "DeepSeek paused after 200 rounds of tool calls in one turn. Say \U0000201Ccontinue\U0000201D to pick up where it left off.",
            notice.Value.Message); // U+201C/U+201D LEFT/RIGHT DOUBLE QUOTATION MARK

        // "immediately before the turn completes": the notice is the event right before TurnCompleted.
        var noticeIndex = session.Events.IndexOf(notice);
        var completedIndex = session.Events.FindIndex(e => e is ProviderEvent.TurnCompleted);
        Assert.Equal(noticeIndex + 1, completedIndex);
        var completed = Assert.IsType<ProviderEvent.TurnCompleted>(session.Events[completedIndex]);
        Assert.Equal(TurnStatus.Interrupted, completed.Status);
    }

    // ---- UI-PROVAPI-025: compaction notices ----

    [Fact]
    [Trait("Req", "UI-PROVAPI-025")] // OpenAICompatibleSession.swift:457-462 - 12 or fewer messages
    public async Task Compacting_12_or_fewer_messages_says_nothing_to_compact_yet_at_info_level()
    {
        var session = new OpenAICompatibleSessionCapturingSeam(MakeConfig(), MakeTurnSessionConfiguration()).Wired();
        session.SetMessagesForTests(Enumerable.Range(0, 12)
            .Select(i => JSONValue.FromDictionaryLiteral(("role", "user"), ("content", $"m{i}"))));

        await session.Compact();

        var notice = Assert.Single(session.Events.OfType<ProviderEvent.Notice>());
        Assert.Equal(Notice.Level.Info, notice.Value.NoticeLevel); // not warning, :460
        Assert.Equal("Nothing to compact yet.", notice.Value.Message);
        Assert.EndsWith(".", notice.Value.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "UI-PROVAPI-025")] // OpenAICompatibleSession.swift:463-468 - more than 12 messages
    public async Task Compacting_more_than_12_messages_says_context_compacted_at_info_level()
    {
        var session = new OpenAICompatibleSessionCapturingSeam(MakeConfig(), MakeTurnSessionConfiguration()).Wired();
        var system = JSONValue.FromDictionaryLiteral(("role", "system"), ("content", "sys"));
        var body = Enumerable.Range(0, 15).Select(i => JSONValue.FromDictionaryLiteral(("role", "user"), ("content", $"m{i}")));
        session.SetMessagesForTests(new[] { system }.Concat(body));

        await session.Compact();

        var notice = Assert.Single(session.Events.OfType<ProviderEvent.Notice>());
        Assert.Equal(Notice.Level.Info, notice.Value.NoticeLevel); // not warning, :468
        Assert.Equal("Context compacted.", notice.Value.Message);
        Assert.EndsWith(".", notice.Value.Message, StringComparison.Ordinal);
    }
}
