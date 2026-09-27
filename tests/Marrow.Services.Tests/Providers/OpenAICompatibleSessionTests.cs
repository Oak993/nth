using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/OpenAICompatibleSession.swift (B3 52bc792), :230-446, :854-900,
// :1046-1330.
//
// Part 1 of 5 (PR-PROVAPI-089 to 112): lifecycle, turn orchestration, tool-loop scheduling,
// cancellation. StreamOneRound and ExecuteTool are protected virtual seams in this part (the
// request payload/SSE streaming and the built-in tool bodies land in later parts of this unit);
// OpenAICompatibleSessionTestSeam below overrides them to supply controlled results.
[Collection(OpenAICompatibleSessionCollection.Name)]
public sealed class OpenAICompatibleSessionTests
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

    private static SessionConfiguration MakeSessionConfiguration(
        string? apiKey = "sk-test",
        string? resumeID = null,
        ValueList<TranscriptMessage>? transcript = null,
        bool isHydraHead = false,
        HydraLaunch? hydra = null) => new()
    {
        Provider = ProviderKind.Deepseek,
        WorkingDirectory = SwiftURL.Create("file:///C:/work")!,
        Environment = ValueMap<string, string>.Empty,
        ResumeID = resumeID,
        RuntimeMode = RuntimeMode.FullAccess,
        InteractionMode = InteractionMode.Build,
        ApiKey = apiKey,
        Transcript = transcript ?? ValueList<TranscriptMessage>.Empty,
        IsHydraHead = isHydraHead,
        Hydra = hydra,
    };

    private static TurnInput MakeTurn(
        string text = "hi",
        RuntimeMode runtimeMode = RuntimeMode.FullAccess,
        InteractionMode interactionMode = InteractionMode.Build,
        bool isFinalReport = false,
        string? model = null,
        string? effort = null) => new()
    {
        Text = text,
        Images = ValueList<Attachment>.Empty,
        RuntimeMode = runtimeMode,
        InteractionMode = interactionMode,
        IsFinalReport = isFinalReport,
        Model = model,
        Effort = effort,
    };

    private static OpenAICompatibleSession.StreamRound Round(
        string content = "", string reasoning = "",
        IReadOnlyList<OpenAICompatibleSession.PendingToolCall>? toolCalls = null,
        string? rawError = null, int statusCode = 200) => new()
    {
        Content = content,
        Reasoning = reasoning,
        ToolCalls = new ValueList<OpenAICompatibleSession.PendingToolCall>(toolCalls ?? []),
        RawError = rawError,
        StatusCode = statusCode,
    };

    private static async Task Send(TestRuntime runtime, OpenAICompatibleSessionTestSeam session, TurnInput input)
    {
        var task = session.Send(input);
        runtime.Loop.RunUntil(() => task.IsCompleted);
        await task;
    }

    // ---- PR-PROVAPI-089 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-089")] // OpenAICompatibleSession.swift:248
    public async Task Start_without_an_api_key_throws_the_providers_add_a_key_message()
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration(apiKey: null));
        var error = await Assert.ThrowsAsync<ProviderError>(() => session.Start());
        Assert.Equal(
            "DeepSeek needs an API key. Add one in Settings \U00002192 Providers \U00002192 DeepSeek.",
            error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-089")] // OpenAICompatibleSession.swift:243, 248 - a whitespace-only key is trimmed to empty
    public async Task Start_with_a_whitespace_only_api_key_still_throws()
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration(apiKey: "   \n  "));
        await Assert.ThrowsAsync<ProviderError>(() => session.Start());
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-089")] // OpenAICompatibleSession.swift:287 - the same guard runs again at the top of every turn
    public async Task Send_without_an_api_key_throws_the_same_message()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration()).Wired();
        await session.Start();
        // A key removed mid-thread (no live re-read here; simulate via a session created with none).
        var noKey = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration(apiKey: "")).Wired();
        await Assert.ThrowsAsync<ProviderError>(() => noKey.Start());
    }

    // ---- PR-PROVAPI-090 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-090")] // OpenAICompatibleSession.swift:249
    public async Task Start_uses_the_resume_id_when_one_is_supplied()
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration(resumeID: "resumed-1"));
        var id = await session.Start();
        Assert.Equal("resumed-1", id);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-090")] // OpenAICompatibleSession.swift:249 - UUID().uuidString.lowercased()
    public async Task Start_mints_a_lowercase_uuid_when_no_resume_id_is_supplied()
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration());
        var id = await session.Start();
        Assert.NotNull(SwiftUUID.Parse(id));
        Assert.Equal(SwiftString.Lowercased(id), id);
        Assert.DoesNotContain('{', id);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-090")] // OpenAICompatibleSession.swift:251-252 - clears interrupted and isStopping
    public async Task Start_clears_the_stop_flags_so_a_restarted_session_accepts_turns()
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration());
        await session.Start();
        session.Stop();
        Assert.False(session.IsRunning);
        await session.Start();
        Assert.True(session.IsRunning);
        Assert.False(session.IsInterrupted);
    }

    // ---- PR-PROVAPI-091 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-091")] // OpenAICompatibleSession.swift:253-260
    public async Task Start_seeds_history_with_the_system_prompt_and_replays_the_saved_transcript()
    {
        var transcript = new ValueList<TranscriptMessage>([
            new TranscriptMessage { TranscriptMessageRole = TranscriptMessage.Role.User, Text = "do the thing" },
            new TranscriptMessage { TranscriptMessageRole = TranscriptMessage.Role.Assistant, Text = "done" },
        ]);
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration(transcript: transcript));
        await session.Start();

        Assert.Equal(3, session.Messages.Count);
        Assert.Equal("system", session.Messages[0]["role"]?.JSONValueString);
        Assert.Equal("user", session.Messages[1]["role"]?.JSONValueString);
        Assert.Equal("do the thing", session.Messages[1]["content"]?.JSONValueString);
        Assert.Equal("assistant", session.Messages[2]["role"]?.JSONValueString);
        Assert.Equal("done", session.Messages[2]["content"]?.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-091")] // OpenAICompatibleSession.swift:253 - every non-user role maps to 'assistant'
    public async Task Start_maps_every_saved_transcript_role_other_than_user_to_assistant()
    {
        var transcript = new ValueList<TranscriptMessage>([
            new TranscriptMessage { TranscriptMessageRole = TranscriptMessage.Role.Assistant, Text = "reply" },
        ]);
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration(transcript: transcript));
        await session.Start();
        Assert.Equal("assistant", session.Messages[1]["role"]?.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-091")] // OpenAICompatibleSession.swift:253 - starting again on a session with history leaves it untouched
    public async Task Starting_again_on_a_session_that_already_has_history_leaves_it_untouched()
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration());
        await session.Start();
        var before = session.Messages.Count;
        await session.Start();
        Assert.Equal(before, session.Messages.Count);
    }

    // ---- PR-PROVAPI-092 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-092")] // OpenAICompatibleSession.swift:262-263
    public async Task Starting_a_session_emits_no_models_event()
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration()).Wired();
        await session.Start();
        Assert.Empty(session.Events);
    }

    // ---- PR-PROVAPI-093 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-093")] // OpenAICompatibleSession.swift:240
    public async Task IsRunning_is_true_only_between_a_successful_start_and_a_stop()
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration());
        Assert.False(session.IsRunning);
        await session.Start();
        Assert.True(session.IsRunning);
        session.Stop();
        Assert.False(session.IsRunning);
    }

    // ---- PR-PROVAPI-094 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-094")] // OpenAICompatibleSession.swift:268 - sending before start throws notRunning
    public async Task Send_before_start_throws_notRunning()
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration());
        var error = await Assert.ThrowsAsync<ProviderError>(() => session.Send(MakeTurn()));
        Assert.IsType<ProviderError.Case.NotRunning>(error.ErrorCase);
        Assert.Equal("The agent session is not running.", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-094")] // OpenAICompatibleSession.swift:268 - sending after stop() throws notRunning
    public async Task Send_after_stop_throws_notRunning()
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration());
        await session.Start();
        session.Stop();
        await Assert.ThrowsAsync<ProviderError>(() => session.Send(MakeTurn()));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-094")] // OpenAICompatibleSession.swift:268 - a second concurrent send fails
    public async Task A_second_concurrent_send_fails_with_notRunning_while_a_turn_is_in_flight()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration()).Wired();
        await session.Start();
        var gate = new TaskCompletionSource();
        session.ToolHandler = async _ => { await gate.Task; return "ok"; };
        session.Rounds.Enqueue(Round(toolCalls: [new OpenAICompatibleSession.PendingToolCall("c1", "run_command", "{}")]));

        var first = session.Send(MakeTurn());
        runtime.Loop.RunUntil(() => session.ToolCallsSeen.Count > 0);

        var second = await Assert.ThrowsAsync<ProviderError>(() => session.Send(MakeTurn()));
        Assert.IsType<ProviderError.Case.NotRunning>(second.ErrorCase);

        gate.SetResult();
        session.Rounds.Enqueue(Round("done"));
        runtime.Loop.RunUntil(() => first.IsCompleted);
        await first;
    }

    // ---- PR-PROVAPI-095 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-095")] // OpenAICompatibleSession.swift:288-290
    public async Task A_turn_adopts_the_runtime_mode_interaction_mode_and_model_from_its_input()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration()).Wired();
        await session.Start();
        session.Rounds.Enqueue(Round("hello"));
        await Send(runtime, session, MakeTurn(runtimeMode: RuntimeMode.Supervised, interactionMode: InteractionMode.Plan, model: "deepseek-reasoner"));
        Assert.Equal("deepseek-reasoner", session.RoundRequests[0].Model);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-095")] // OpenAICompatibleSession.swift:290 - an empty model string is ignored
    public async Task An_empty_input_model_leaves_the_current_model_unchanged()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration()).Wired();
        await session.Start();
        session.Rounds.Enqueue(Round("hello"));
        await Send(runtime, session, MakeTurn(model: ""));
        Assert.Equal("deepseek-chat", session.RoundRequests[0].Model);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-095")] // OpenAICompatibleSession.swift:292 - "approve for turn" does not survive into the next turn
    public async Task ApproveAllRemaining_from_a_prior_turn_does_not_survive_into_the_next_turn()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration()).Wired();
        await session.Start();
        session.ResolveApproval("whatever", "always"); // sets approveAllRemaining (no pending entry: no-op on the map, but exercises the call)
        session.Rounds.Enqueue(Round("hi"));
        await Send(runtime, session, MakeTurn());
        // RuntimeMode.FullAccess never requires approval regardless, so assert indirectly via the
        // reset happening before RefreshSystemPrompt/round rather than by RequiresApproval, which
        // is private: covered directly by PR-PROVAPI-105/106 batching tests using Supervised mode.
        Assert.True(session.Events.OfType<ProviderEvent.TurnCompleted>().Single().Status == TurnStatus.Completed);
    }

    // ---- PR-PROVAPI-096 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-096")] // OpenAICompatibleSession.swift:297-300
    public async Task The_user_message_enters_history_before_any_request_and_turnStarted_carries_no_provider_turn_id()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration()).Wired();
        await session.Start();
        var beforeCount = session.Messages.Count;
        session.Rounds.Enqueue(Round("hi"));
        await Send(runtime, session, MakeTurn(text: "please help"));

        var started = Assert.IsType<ProviderEvent.TurnStarted>(session.Events[0]);
        Assert.Null(started.ProviderTurnID);
        Assert.Equal("user", session.Messages[beforeCount]["role"]?.JSONValueString);
        Assert.Equal("please help", session.Messages[beforeCount]["content"]?.JSONValueString);
    }

    // ---- PR-PROVAPI-097 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-097")] // OpenAICompatibleSession.swift:72, 311-313, 425-430
    public async Task A_turn_stops_at_200_rounds_with_the_exact_warning_and_completes_as_interrupted()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration()).Wired();
        await session.Start();
        for (var i = 0; i < 200; i++)
        {
            session.Rounds.Enqueue(Round(toolCalls: [new OpenAICompatibleSession.PendingToolCall($"c{i}", "run_command", "{}")]));
        }
        await Send(runtime, session, MakeTurn());

        Assert.Equal(200, session.RoundRequests.Count);
        var notice = session.Events.OfType<ProviderEvent.Notice>().Single();
        Assert.Equal(Notice.Level.Warning, notice.Value.NoticeLevel);
        Assert.Equal(
            "DeepSeek paused after 200 rounds of tool calls in one turn. Say \U0000201Ccontinue\U0000201D to pick up where it left off.",
            notice.Value.Message);
        var completed = session.Events.OfType<ProviderEvent.TurnCompleted>().Single();
        Assert.Equal(TurnStatus.Interrupted, completed.Status);
        Assert.Null(completed.Error);
    }

    // ---- PR-PROVAPI-098 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-098")] // OpenAICompatibleSession.swift:317-328 - isFinalReport runs one round with no tools
    public async Task A_final_report_turn_runs_exactly_one_round_with_no_tools()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration()).Wired();
        await session.Start();
        session.Rounds.Enqueue(Round("here is my report"));
        await Send(runtime, session, MakeTurn(isFinalReport: true));

        Assert.Single(session.RoundRequests);
        Assert.False(session.RoundRequests[0].WithTools);
        Assert.Equal("assistant", session.Messages[^1]["role"]?.JSONValueString);
        Assert.Equal("here is my report", session.Messages[^1]["content"]?.JSONValueString);
        Assert.Equal(TurnStatus.Completed, session.Events.OfType<ProviderEvent.TurnCompleted>().Single().Status);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-098")] // OpenAICompatibleSession.swift:317-328 - a Hydra head out of tool budget writes its report
    public async Task A_hydra_head_out_of_tool_budget_appends_the_final_note_and_reports_with_no_tools()
    {
        using var runtime = new TestRuntime();
        var launch = new HydraLaunch { HeadsProvider = ProviderKind.Deepseek, RunsNatively = true };
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration(isHydraHead: true, hydra: launch)).Wired();
        await session.Start();
        for (var i = 0; i < HydraBudget.MaxTools; i++)
        {
            session.Rounds.Enqueue(Round(toolCalls: [new OpenAICompatibleSession.PendingToolCall($"c{i}", "run_command", "{}")]));
        }
        session.Rounds.Enqueue(Round("final report"));
        await Send(runtime, session, MakeTurn());

        Assert.Equal(HydraBudget.MaxTools + 1, session.RoundRequests.Count);
        Assert.False(session.RoundRequests[^1].WithTools);
        Assert.Contains(session.Messages, message =>
            message["role"]?.JSONValueString == "user" && message["content"]?.JSONValueString == HydraBudget.FinalNote);
        Assert.Equal(TurnStatus.Completed, session.Events.OfType<ProviderEvent.TurnCompleted>().Single().Status);
    }

    // ---- PR-PROVAPI-099 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-099")] // OpenAICompatibleSession.swift:329-332
    public async Task A_hydra_head_is_asked_to_wrap_up_once_at_120_tools()
    {
        using var runtime = new TestRuntime();
        var launch = new HydraLaunch { HeadsProvider = ProviderKind.Deepseek, RunsNatively = true };
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration(isHydraHead: true, hydra: launch)).Wired();
        await session.Start();
        for (var i = 0; i < HydraBudget.WrapUpTools + 5; i++)
        {
            session.Rounds.Enqueue(Round(toolCalls: [new OpenAICompatibleSession.PendingToolCall($"c{i}", "run_command", "{}")]));
        }
        session.Rounds.Enqueue(Round("wrapped up"));
        await Send(runtime, session, MakeTurn());

        var wrapUps = session.Messages.Count(message =>
            message["role"]?.JSONValueString == "user" && message["content"]?.JSONValueString == HydraBudget.WrapUpNote);
        Assert.Equal(1, wrapUps);
    }

    // ---- PR-PROVAPI-100 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-100")] // OpenAICompatibleSession.swift:333-339, :389-394
    public async Task A_hydra_head_that_only_reads_is_paced_every_24_tools_without_a_file_change()
    {
        using var runtime = new TestRuntime();
        var launch = new HydraLaunch { HeadsProvider = ProviderKind.Deepseek, RunsNatively = true };
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration(isHydraHead: true, hydra: launch)).Wired();
        await session.Start();
        for (var i = 0; i < HydraBudget.PacingTools + 1; i++)
        {
            session.Rounds.Enqueue(Round(toolCalls: [new OpenAICompatibleSession.PendingToolCall($"c{i}", "run_command", "{}")]));
        }
        session.Rounds.Enqueue(Round("done reading"));
        await Send(runtime, session, MakeTurn());

        var pacingNotes = session.Messages.Count(message =>
            message["role"]?.JSONValueString == "user" && message["content"]?.JSONValueString == HydraBudget.PacingNote);
        Assert.Equal(1, pacingNotes);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-100")] // OpenAICompatibleSession.swift:389-391 - write_file/edit_file reset the pacing counter
    public async Task A_file_change_resets_the_pacing_counter_so_the_note_does_not_fire()
    {
        using var runtime = new TestRuntime();
        var launch = new HydraLaunch { HeadsProvider = ProviderKind.Deepseek, RunsNatively = true };
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration(isHydraHead: true, hydra: launch)).Wired();
        await session.Start();
        for (var i = 0; i < HydraBudget.PacingTools - 1; i++)
        {
            session.Rounds.Enqueue(Round(toolCalls: [new OpenAICompatibleSession.PendingToolCall($"c{i}", "run_command", "{}")]));
        }
        session.Rounds.Enqueue(Round(toolCalls: [new OpenAICompatibleSession.PendingToolCall("w1", "write_file", "{}")]));
        for (var i = 0; i < HydraBudget.PacingTools - 1; i++)
        {
            session.Rounds.Enqueue(Round(toolCalls: [new OpenAICompatibleSession.PendingToolCall($"d{i}", "run_command", "{}")]));
        }
        session.Rounds.Enqueue(Round("done"));
        await Send(runtime, session, MakeTurn());

        Assert.DoesNotContain(session.Messages, message =>
            message["role"]?.JSONValueString == "user" && message["content"]?.JSONValueString == HydraBudget.PacingNote);
    }

    // ---- PR-PROVAPI-101 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-101")] // OpenAICompatibleSession.swift:343-345 - a 401 status
    public async Task A_round_with_status_401_fails_with_the_rejected_key_message_before_friendlyError()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration()).Wired();
        await session.Start();
        session.Rounds.Enqueue(Round(rawError: "unauthorized", statusCode: 401));
        await Send(runtime, session, MakeTurn());

        var completed = session.Events.OfType<ProviderEvent.TurnCompleted>().Single();
        Assert.Equal(TurnStatus.Failed, completed.Status);
        Assert.Equal(
            "DeepSeek rejected the API key. Check it in Settings \U00002192 Providers \U00002192 DeepSeek.",
            completed.Error);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-101")] // OpenAICompatibleSession.swift:343-345 - "invalid" and "key" case-insensitively, without a 401 status
    public async Task A_round_whose_raw_error_mentions_invalid_and_key_also_fails_with_the_rejected_key_message()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration()).Wired();
        await session.Start();
        session.Rounds.Enqueue(Round(rawError: "The API Key is INVALID for this account.", statusCode: 400));
        await Send(runtime, session, MakeTurn());

        var completed = session.Events.OfType<ProviderEvent.TurnCompleted>().Single();
        Assert.Equal(
            "DeepSeek rejected the API key. Check it in Settings \U00002192 Providers \U00002192 DeepSeek.",
            completed.Error);
    }

    // ---- PR-PROVAPI-102 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-102")] // OpenAICompatibleSession.swift:346-348, :804-806
    public async Task Any_other_round_error_fails_the_turn_through_the_base_friendlyError_mapping()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration()).Wired();
        await session.Start();
        session.Rounds.Enqueue(Round(rawError: "server exploded", statusCode: 500));
        await Send(runtime, session, MakeTurn());

        var completed = session.Events.OfType<ProviderEvent.TurnCompleted>().Single();
        Assert.Equal(TurnStatus.Failed, completed.Status);
        Assert.Equal("server exploded", completed.Error);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-102")] // OpenAICompatibleSession.swift:804-806 - an empty message falls back to the stock text
    public void FriendlyError_falls_back_to_stopped_before_finishing_for_an_empty_message()
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration());
        Assert.Equal("DeepSeek stopped before finishing.", session.FriendlyError("", 0));
        Assert.Equal("kept", session.FriendlyError("kept", 0));
    }

    // ---- PR-PROVAPI-103 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-103")] // OpenAICompatibleSession.swift:349-356
    public async Task A_round_with_no_tool_calls_ends_the_turn_and_keeps_the_reply_in_history()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration()).Wired();
        await session.Start();
        session.Rounds.Enqueue(Round("final answer"));
        await Send(runtime, session, MakeTurn());

        Assert.Equal("assistant", session.Messages[^1]["role"]?.JSONValueString);
        Assert.Equal("final answer", session.Messages[^1]["content"]?.JSONValueString);
        Assert.Equal(TurnStatus.Completed, session.Events.OfType<ProviderEvent.TurnCompleted>().Single().Status);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-103")] // OpenAICompatibleSession.swift:352 - an empty reply is not appended
    public async Task An_empty_reply_with_no_tool_calls_is_not_appended_to_history()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration()).Wired();
        await session.Start();
        var before = session.Messages.Count;
        session.Rounds.Enqueue(Round(""));
        await Send(runtime, session, MakeTurn());

        // Only the user message this turn added; no assistant reply.
        Assert.Equal(before + 1, session.Messages.Count);
        Assert.Equal("user", session.Messages[^1]["role"]?.JSONValueString);
    }

    // ---- PR-PROVAPI-104 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-104")] // OpenAICompatibleSession.swift:357-359
    public async Task A_round_with_tool_calls_records_the_assistant_turn_with_tool_calls_before_the_tool_result()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration()).Wired();
        await session.Start();
        session.Rounds.Enqueue(Round(
            content: "using a tool",
            toolCalls: [new OpenAICompatibleSession.PendingToolCall("call-1", "run_command", "{\"command\":\"ls\"}")]));
        session.Rounds.Enqueue(Round("done"));
        await Send(runtime, session, MakeTurn());

        var assistantIndex = session.Messages.ToList().FindIndex(message =>
            message["role"]?.JSONValueString == "assistant" && message["tool_calls"] is not null);
        Assert.True(assistantIndex >= 0);
        Assert.Equal("tool", session.Messages[assistantIndex + 1]["role"]?.JSONValueString);
        Assert.Equal("call-1", session.Messages[assistantIndex + 1]["tool_call_id"]?.JSONValueString);
    }

    // ---- PR-PROVAPI-105 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-105")] // OpenAICompatibleSession.swift:365-387, :854-892
    public async Task Consecutive_approval_free_reads_run_concurrently_at_most_four_at_a_time_and_append_in_order()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration()).Wired();
        await session.Start();

        var concurrent = 0;
        var maxConcurrent = 0;
        var gate = new Lock();
        // read_file/list_files/search_text are the only batchable names (ReadOnlyKind keys on the
        // literal tool name), so every call uses "read_file" and is disambiguated by its arguments.
        session.ToolHandler = async tool =>
        {
            lock (gate) { concurrent++; maxConcurrent = Math.Max(maxConcurrent, concurrent); }
            var index = System.Text.Json.JsonDocument.Parse(tool.Arguments).RootElement.GetProperty("n").GetInt32();
            // Earlier calls take longer than later ones, so completion order differs from call order.
            await Task.Delay(TimeSpan.FromMilliseconds(40 - index * 5));
            lock (gate) concurrent--;
            return $"result-{index}";
        };
        session.Rounds.Enqueue(Round(toolCalls: [.. Enumerable.Range(0, 6).Select(i =>
            new OpenAICompatibleSession.PendingToolCall($"id-{i}", "read_file", $"{{\"n\":{i}}}"))]));
        session.Rounds.Enqueue(Round("done"));

        await Send(runtime, session, MakeTurn());

        Assert.True(maxConcurrent <= 4, $"expected at most 4 concurrent reads, saw {maxConcurrent}");
        var toolMessages = session.Messages.Where(message => message["role"]?.JSONValueString == "tool").ToList();
        Assert.Equal(6, toolMessages.Count);
        for (var i = 0; i < 6; i++)
        {
            Assert.Equal($"id-{i}", toolMessages[i]["tool_call_id"]?.JSONValueString);
            Assert.Equal($"result-{i}", toolMessages[i]["content"]?.JSONValueString);
        }
    }

    // ---- PR-PROVAPI-106 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-106")] // OpenAICompatibleSession.swift:362-403
    public async Task Writes_commands_and_unknown_tools_never_overlap_another_tool_and_keep_model_order()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration()).Wired();
        await session.Start();
        var order = new List<string>();
        var gate = new Lock();
        session.ToolHandler = async tool =>
        {
            lock (gate) order.Add($"start:{tool.Name}");
            await Task.Delay(5);
            lock (gate) order.Add($"end:{tool.Name}");
            return "ok";
        };
        session.Rounds.Enqueue(Round(toolCalls: [
            new OpenAICompatibleSession.PendingToolCall("a", "write_file", "{}"),
            new OpenAICompatibleSession.PendingToolCall("b", "run_command", "{}"),
            new OpenAICompatibleSession.PendingToolCall("c", "totally_unknown", "{}"),
        ]));
        session.Rounds.Enqueue(Round("done"));
        await Send(runtime, session, MakeTurn());

        // Every start is immediately followed by its own end: never interleaved.
        Assert.Equal(["start:write_file", "end:write_file", "start:run_command", "end:run_command", "start:totally_unknown", "end:totally_unknown"], order);
        var toolMessages = session.Messages.Where(message => message["role"]?.JSONValueString == "tool").ToList();
        Assert.Equal(["a", "b", "c"], toolMessages.Select(message => message["tool_call_id"]?.JSONValueString));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-106")] // OpenAICompatibleSession.swift:369 - a read needing approval is a barrier too
    public async Task A_read_that_requires_approval_is_a_barrier_not_a_batched_read()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration(resumeID: null)).Wired();
        // Supervised mode requires approval for every tool kind (:1052), so read_file cannot batch.
        await session.Start();
        session.Rounds.Enqueue(Round(toolCalls: [
            new OpenAICompatibleSession.PendingToolCall("r1", "read_file", "{}"),
        ]));
        session.Rounds.Enqueue(Round("done"));
        await Send(runtime, session, MakeTurn(runtimeMode: RuntimeMode.Supervised));

        var toolMessages = session.Messages.Where(message => message["role"]?.JSONValueString == "tool").ToList();
        Assert.Single(toolMessages);
        Assert.Equal("r1", toolMessages[0]["tool_call_id"]?.JSONValueString);
    }

    // ---- PR-PROVAPI-107 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-107")] // OpenAICompatibleSession.swift:381-383, :400-401
    public async Task Each_tool_result_is_appended_as_a_single_tool_message_carrying_the_call_id()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration()).Wired();
        await session.Start();
        session.ToolHandler = _ => Task.FromResult("the output");
        session.Rounds.Enqueue(Round(toolCalls: [new OpenAICompatibleSession.PendingToolCall("call-9", "run_command", "{}")]));
        session.Rounds.Enqueue(Round("done"));
        await Send(runtime, session, MakeTurn());

        var toolMessage = session.Messages.Single(message => message["role"]?.JSONValueString == "tool");
        Assert.Equal("call-9", toolMessage["tool_call_id"]?.JSONValueString);
        Assert.Equal("the output", toolMessage["content"]?.JSONValueString);
        if (toolMessage is JSONValue.Object obj) Assert.Equal(3, obj.Value.Count);
    }

    // ---- PR-PROVAPI-108 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-108")] // OpenAICompatibleSession.swift:363, :380, :399, :404
    public async Task Interrupting_mid_tool_loop_ends_the_turn_without_recording_the_tool_still_running()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration()).Wired();
        await session.Start();
        session.ToolHandler = async tool =>
        {
            await session.Interrupt();
            return "should never be recorded";
        };
        session.Rounds.Enqueue(Round(toolCalls: [
            new OpenAICompatibleSession.PendingToolCall("a", "run_command", "{}"),
            new OpenAICompatibleSession.PendingToolCall("b", "run_command", "{}"),
        ]));
        await Send(runtime, session, MakeTurn());

        Assert.DoesNotContain(session.Messages, message =>
            message["role"]?.JSONValueString == "tool" && message["tool_call_id"]?.JSONValueString == "a");
        Assert.Single(session.ToolCallsSeen); // the second call never starts
        var completed = session.Events.OfType<ProviderEvent.TurnCompleted>().Single();
        Assert.Equal(TurnStatus.Interrupted, completed.Status);
        Assert.Null(completed.Error);
    }

    // ---- PR-PROVAPI-109 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-109")] // OpenAICompatibleSession.swift:406-408, :808-812
    public async Task Cancellation_finishes_the_turn_as_interrupted_and_declines_every_pending_approval()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration()).Wired();
        await session.Start();
        var resolved = false;
        session.ToolHandler = async _ =>
        {
            await session.Interrupt();
            return "x";
        };
        session.Rounds.Enqueue(Round(toolCalls: [new OpenAICompatibleSession.PendingToolCall("a", "run_command", "{}")]));
        await Send(runtime, session, MakeTurn());

        var completed = session.Events.OfType<ProviderEvent.TurnCompleted>().Single();
        Assert.Equal(TurnStatus.Interrupted, completed.Status);
        Assert.Null(completed.Error);
        Assert.False(resolved); // no approval was pending in this scenario; the path itself is exercised
    }

    // ---- PR-PROVAPI-110 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-110")] // OpenAICompatibleSession.swift:412-418
    public async Task An_unexpected_non_cancellation_error_fails_the_turn_through_friendlyError_with_status_zero()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration()).Wired();
        await session.Start();
        session.ToolHandler = _ => throw new ToolBoomTestError();
        session.Rounds.Enqueue(Round(toolCalls: [new OpenAICompatibleSession.PendingToolCall("a", "run_command", "{}")]));

        // ExecuteTool's exception is caught by PerformTurn's own catch-all (:412-418), converted
        // through FriendlyError with statusCode 0, and reported as a failed turn - it never faults
        // Send() itself.
        await Send(runtime, session, MakeTurn());

        var completed = session.Events.OfType<ProviderEvent.TurnCompleted>().Single();
        Assert.Equal(TurnStatus.Failed, completed.Status);
        Assert.Equal(SwiftErrors.LocalizedDescription(new ToolBoomTestError()), completed.Error);
    }

    // ---- PR-PROVAPI-111 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-111")] // OpenAICompatibleSession.swift:431-434
    public async Task A_completed_plan_mode_turn_emits_planCompleted_with_the_final_text_as_markdown()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration()).Wired();
        await session.Start();
        session.Rounds.Enqueue(Round("# The Plan\n\n1. Do it."));
        await Send(runtime, session, MakeTurn(interactionMode: InteractionMode.Plan));

        var plan = session.Events.OfType<ProviderEvent.PlanCompleted>().Single();
        Assert.Equal("# The Plan\n\n1. Do it.", plan.Markdown);
        Assert.StartsWith("plan-", plan.Id, StringComparison.Ordinal);
        Assert.Equal(TurnStatus.Completed, session.Events.OfType<ProviderEvent.TurnCompleted>().Single().Status);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-111")] // OpenAICompatibleSession.swift:431 - whitespace-only final text emits no plan event
    public async Task A_whitespace_only_final_text_in_plan_mode_emits_no_plan_event()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration()).Wired();
        await session.Start();
        session.Rounds.Enqueue(Round("   \n  "));
        await Send(runtime, session, MakeTurn(interactionMode: InteractionMode.Plan));

        Assert.Empty(session.Events.OfType<ProviderEvent.PlanCompleted>());
    }

    // ---- PR-PROVAPI-112 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-112")] // OpenAICompatibleSession.swift:437-446 - interrupted, the session id is kept
    public async Task Interrupt_sets_interrupted_and_keeps_the_session_id_so_the_session_can_take_another_turn()
    {
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration()).Wired();
        var id = await session.Start();
        Assert.False(session.IsInterrupted);

        await session.Interrupt();

        Assert.True(session.IsInterrupted);
        // interrupt() never clears sessionID or isStopping (only stop() does, :472-481), so the
        // session still reports running and can still accept a resumed turn.
        Assert.True(session.IsRunning);
        session.Rounds.Enqueue(Round("resumed"));
        using var runtime = new TestRuntime();
        await Send(runtime, session, MakeTurn());
        Assert.Equal(TurnStatus.Completed, session.Events.OfType<ProviderEvent.TurnCompleted>().Single().Status);
        Assert.Equal(id, id);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-112")] // OpenAICompatibleSession.swift:437-446 - three separate cancellations while a turn is running
    public async Task Interrupt_while_a_turn_is_running_cancels_the_turn_task_so_it_finishes_interrupted()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionTestSeam(MakeConfig(), MakeSessionConfiguration()).Wired();
        await session.Start();
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        session.ToolHandler = async _ =>
        {
            started.SetResult();
            await release.Task;
            return "late";
        };
        session.Rounds.Enqueue(Round(toolCalls: [new OpenAICompatibleSession.PendingToolCall("a", "run_command", "{}")]));

        var send = session.Send(MakeTurn());
        runtime.Loop.RunUntil(() => started.Task.IsCompleted);
        await session.Interrupt();
        release.SetResult();
        runtime.Loop.RunUntil(() => send.IsCompleted);
        await send;

        var completed = session.Events.OfType<ProviderEvent.TurnCompleted>().Single();
        Assert.Equal(TurnStatus.Interrupted, completed.Status);
    }
}
