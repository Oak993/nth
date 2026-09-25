using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.App.Tests.Runtime;

public sealed class ThreadRuntimePart7Tests
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [Fact]
    [Trait("Req", "PR-RUNTIME-134")] // ThreadRuntime.swift:804-830
    public void Return_does_nothing_when_the_runtime_is_idle()
    {
        using var test = new TestRuntime();
        test.OnMain(() =>
        {
            var runtime = CreateRuntime();
            runtime.Draft = new ComposerDraft { Text = "send me" };

            runtime.InterruptAndSend();

            Assert.Equal(RuntimePhase.Idle, runtime.Phase);
            Assert.Equal("send me", runtime.Draft.Text);
            Assert.Null(ReadField(runtime, "pendingSend"));
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-135")] // ThreadRuntime.swift:1114-1139
    public void Follow_ups_wait_after_failed_or_interrupted_turns()
    {
        using var test = new TestRuntime();
        test.OnMain(() =>
        {
            var prompt = new FollowUpPrompt { Text = "keep queued" };
            var runtime = CreateRuntime();
            SetField(runtime, "followUps", new ValueList<FollowUpPrompt>([prompt]));

            Assert.False(Invoke<bool>(runtime, "DrainFollowUps", TurnStatus.Failed));
            Assert.False(Invoke<bool>(runtime, "DrainFollowUps", TurnStatus.Interrupted));
            Assert.Equal([prompt], runtime.FollowUps);
            Assert.Equal(0, runtime.AutoStartedTurnToken);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-136")] // ThreadRuntime.swift:947-990
    public void Send_now_for_an_unknown_queue_item_is_a_no_op()
    {
        using var test = new TestRuntime();
        test.OnMain(() =>
        {
            var prompt = new FollowUpPrompt { Text = "still queued" };
            var runtime = CreateRuntime();
            SetField(runtime, "followUps", new ValueList<FollowUpPrompt>([prompt]));

            runtime.SendFollowUpNow(Guid.NewGuid());

            Assert.Equal([prompt], runtime.FollowUps);
            Assert.Equal(RuntimePhase.Idle, runtime.Phase);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-137")] // ThreadRuntime.swift:2464-2479
    public void Drain_pending_send_consumes_an_empty_message_without_starting_a_turn()
    {
        using var test = new TestRuntime();
        test.OnMain(() =>
        {
            var runtime = CreateRuntime();
            var pendingType = typeof(ThreadRuntime).GetNestedType("PendingSend", BindingFlags.NonPublic);
            Assert.NotNull(pendingType);
            var pending = RuntimeHelpers.GetUninitializedObject(pendingType!);
            SetProperty(pending, "Text", "  \t\r\n ");
            SetProperty(pending, "Attachments", ValueList<Attachment>.Empty);
            SetField(runtime, "pendingSend", pending);

            Assert.False(Invoke<bool>(runtime, "DrainPendingSend"));

            Assert.Null(ReadField(runtime, "pendingSend"));
            Assert.Equal(RuntimePhase.Idle, runtime.Phase);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-138")] // ThreadRuntime.swift:1721-1731
    public void Resolving_a_plan_approval_updates_the_plan_before_answering_the_session()
    {
        using var test = new TestRuntime();
        test.OnMain(() =>
        {
            var runtime = CreateRuntime();
            var entry = AddEntry(runtime, "plan-row", new TimelineItem.Content.Plan(new ProposedPlan
            {
                Markdown = "Plan",
                ProposedPlanState = ProposedPlan.State.Drafting,
            }));
            var request = new ApprovalRequest(
                "approval-1", ApprovalRequest.Kind.Plan, "Plan?", ValueList<ApprovalRequest.Option>.Empty,
                ToolItemID: entry.Id);
            SetField(runtime, "approvals", new ValueList<ApprovalRequest>([request]));
            var session = new RecordingSession();
            session.OnResolve = (_, _) =>
            {
                Assert.Empty(runtime.Approvals);
                Assert.Equal(ProposedPlan.State.Accepted,
                    Assert.IsType<TimelineItem.Content.Plan>(entry.Item.TimelineItemContent).Value.ProposedPlanState);
            };
            SetField(runtime, "session", session);

            runtime.Resolve(request, new ApprovalRequest.Option("allow", "Allow", ApprovalRequest.Option.Role.Approve));

            Assert.Equal(("approval-1", "allow"), session.Resolved);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-138")] // ThreadRuntime.swift:1721-1731
    public void Approve_always_dismisses_a_plan_and_still_resolves_the_card()
    {
        using var test = new TestRuntime();
        test.OnMain(() =>
        {
            var runtime = CreateRuntime();
            var entry = AddEntry(runtime, "plan-row", new TimelineItem.Content.Plan(new ProposedPlan
            {
                Markdown = "Plan",
                ProposedPlanState = ProposedPlan.State.Drafting,
            }));
            var request = new ApprovalRequest(
                "approval-2", ApprovalRequest.Kind.Plan, "Plan?", ValueList<ApprovalRequest.Option>.Empty,
                ToolItemID: entry.Id);
            SetField(runtime, "approvals", new ValueList<ApprovalRequest>([request]));
            var session = new RecordingSession();
            session.OnResolve = (_, _) => Assert.Empty(runtime.Approvals);
            SetField(runtime, "session", session);

            runtime.Resolve(request, new ApprovalRequest.Option("always", "Always", ApprovalRequest.Option.Role.ApproveAlways));

            Assert.Equal(ProposedPlan.State.Dismissed,
                Assert.IsType<TimelineItem.Content.Plan>(entry.Item.TimelineItemContent).Value.ProposedPlanState);
            Assert.Equal(("approval-2", "always"), session.Resolved);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-139")] // ThreadRuntime.swift:1733-1736
    public void Answers_are_forwarded_unchanged_after_the_question_is_removed()
    {
        using var test = new TestRuntime();
        test.OnMain(() =>
        {
            var runtime = CreateRuntime();
            var question = new QuestionRequest("question-1", new ValueList<QuestionRequest.Question>([
                new("secret", "Password", "Type it", ValueList<QuestionRequest.Choice>.Empty,
                    AllowsMultiple: false, AllowsOther: true, IsSecret: true),
            ]));
            SetField(runtime, "questions", new ValueList<QuestionRequest>([question]));
            var answers = new ValueMap<string, ValueList<string>>(
                [new KeyValuePair<string, ValueList<string>>("secret", new ValueList<string>(["  keep spaces  "]))]);
            var session = new RecordingSession();
            session.OnAnswer = (_, forwarded) => Assert.Empty(runtime.Questions);
            SetField(runtime, "session", session);

            runtime.Answer(question, answers);

            Assert.Same(answers, session.Answered);
            Assert.Equal("  keep spaces  ", session.Answered!["secret"][0]);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-140")] // ThreadRuntime.swift:3050-3068
    public void Unknown_delta_ids_create_rows_only_for_the_supported_first_delta_kinds()
    {
        using var test = new TestRuntime();
        test.OnMain(() =>
        {
            var turnID = Guid.NewGuid();
            var runtime = CreateRuntime(turnID);

            QueueDelta(runtime, "empty-message", ThreadRuntime.DeltaKind.Message, "");
            QueueDelta(runtime, "empty-reasoning", ThreadRuntime.DeltaKind.Reasoning, "");
            QueueDelta(runtime, "empty-plan", ThreadRuntime.DeltaKind.Plan, "");
            QueueDelta(runtime, "tool-output", ThreadRuntime.DeltaKind.ToolOutput, "discard");

            Assert.DoesNotContain(runtime.Entries, entry => entry.Id == "empty-message");
            Assert.DoesNotContain(runtime.Entries, entry => entry.Id == "empty-reasoning");
            var plan = Assert.Single(runtime.Entries, entry => entry.Id == "empty-plan");
            Assert.Equal(turnID, plan.TurnID);
            Assert.Equal(ProposedPlan.State.Drafting,
                Assert.IsType<TimelineItem.Content.Plan>(plan.Item.TimelineItemContent).Value.ProposedPlanState);
            Assert.DoesNotContain(runtime.Entries, entry => entry.Id == "tool-output");
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-141")] // ThreadRuntime.swift:3069-3073
    [Trait("Req", "PR-RUNTIME-145")] // ThreadRuntime.swift:3179-3204
    [Trait("Req", "PR-RUNTIME-146")] // ThreadRuntime.swift:3663-3692
    public void Streamed_deltas_flatten_prose_and_keep_the_first_kind_for_each_row_id()
    {
        using var test = new TestRuntime();
        test.OnMain(() =>
        {
            var runtime = CreateRuntime(Guid.NewGuid());
            var entry = AddEntry(runtime, "assistant", new TimelineItem.Content.Assistant(new AssistantMessage
            {
                Text = "",
                IsStreaming = true,
            }));
            DelayNextFlush(runtime);

            QueueDelta(runtime, "assistant", ThreadRuntime.DeltaKind.Message, "first—");
            QueueDelta(runtime, "assistant", ThreadRuntime.DeltaKind.Reasoning, "second");

            var pending = GetDictionary(runtime, "pendingDeltas");
            var delta = pending["assistant"]!;
            Assert.Equal(ThreadRuntime.DeltaKind.Message, GetProperty<ThreadRuntime.DeltaKind>(delta, "Kind"));
            Assert.Equal("first-second", GetProperty<string>(delta, "Text"));

            Invoke(runtime, "FlushDeltas", false);

            Assert.Equal("first-second", Assert.IsType<TimelineItem.Content.Assistant>(entry.Item.TimelineItemContent).Value.Text);
            Assert.Empty(GetDictionary(runtime, "pendingDeltas"));
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-141")] // ThreadRuntime.swift:3069-3073
    [Trait("Req", "PR-RUNTIME-146")] // ThreadRuntime.swift:3663-3692
    public void Tool_output_keeps_em_dashes_and_mismatched_stream_kinds_are_dropped()
    {
        using var test = new TestRuntime();
        test.OnMain(() =>
        {
            var runtime = CreateRuntime(Guid.NewGuid());
            var tool = AddEntry(runtime, "tool", new TimelineItem.Content.Tool(new ToolCall
            {
                ToolCallKind = ToolCall.Kind.Command,
                Title = "Run",
            }));
            DelayNextFlush(runtime);
            QueueDelta(runtime, "tool", ThreadRuntime.DeltaKind.ToolOutput, "left—right");
            Assert.Equal("left—right", GetProperty<string>(GetDictionary(runtime, "pendingDeltas")["tool"]!, "Text"));
            Invoke(runtime, "FlushDeltas", false);
            Assert.Equal("left—right", Assert.IsType<TimelineItem.Content.Tool>(tool.Item.TimelineItemContent).Value.Output);

            var assistant = AddEntry(runtime, "assistant", new TimelineItem.Content.Assistant(new AssistantMessage { Text = "keep" }));
            var revision = GetField<long>(runtime, "saveRevision");
            PutDelta(runtime, "assistant", ThreadRuntime.DeltaKind.Reasoning, "wrong row kind");
            Invoke(runtime, "FlushDeltas", false);

            Assert.Equal("keep", Assert.IsType<TimelineItem.Content.Assistant>(assistant.Item.TimelineItemContent).Value.Text);
            Assert.Equal(revision, GetField<long>(runtime, "saveRevision"));
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-143")] // ThreadRuntime.swift:3101-3136
    public void Flush_delay_uses_exact_length_tiers_and_scroll_floor()
    {
        using var test = new TestRuntime();
        test.OnMain(() =>
        {
            Assert.Equal(45L, FlushDelay(null, scrolling: false));
            Assert.Equal(45L, FlushDelay(4_095, scrolling: false));
            Assert.Equal(90L, FlushDelay(4_096, scrolling: false));
            Assert.Equal(90L, FlushDelay(32_767, scrolling: false));
            Assert.Equal(150L, FlushDelay(32_768, scrolling: false));
            Assert.Equal(160L, FlushDelay(4_095, scrolling: true));
            Assert.Equal(160L, FlushDelay(4_096, scrolling: true));
            Assert.Equal(160L, FlushDelay(32_768, scrolling: true));
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-142")] // ThreadRuntime.swift:3074-3096,3138
    public void First_delta_after_a_lull_flushes_now_and_later_deltas_share_one_pending_flush()
    {
        using var test = new TestRuntime();
        test.OnMain(() =>
        {
            var runtime = CreateRuntime();
            var first = AddEntry(runtime, "first", new TimelineItem.Content.Assistant(new AssistantMessage { Text = "" }));
            SetField(runtime, "lastFlushAt", SwiftClock.Now - TimeSpan.FromSeconds(1));

            QueueDelta(runtime, "first", ThreadRuntime.DeltaKind.Message, "immediate");

            Assert.Equal("immediate", Assert.IsType<TimelineItem.Content.Assistant>(first.Item.TimelineItemContent).Value.Text);
            Assert.Empty(GetDictionary(runtime, "pendingDeltas"));
            Assert.Null(ReadField(runtime, "flushTask"));

            SetField(runtime, "lastFlushAt", SwiftClock.Now);
            QueueDelta(runtime, "queued-one", ThreadRuntime.DeltaKind.Plan, "one");
            var scheduled = ReadField(runtime, "flushTask");
            Assert.NotNull(scheduled);
            QueueDelta(runtime, "queued-two", ThreadRuntime.DeltaKind.Plan, "two");
            Assert.Same(scheduled, ReadField(runtime, "flushTask"));
            Assert.Equal(2, GetDictionary(runtime, "pendingDeltas").Count);
            Invoke(runtime, "FlushDeltas", false);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-144")] // ThreadRuntime.swift:3146-3177
    public void Streaming_heads_share_a_budget_and_forget_removes_one_runtime()
    {
        using var test = new TestRuntime();
        test.OnMain(() =>
        {
            var streamLoad = ThreadRuntime.StreamLoad.Shared;
            var first = CreateRuntime();
            var second = CreateRuntime();
            var baseline = streamLoad.StreamingCount;
            try
            {
                streamLoad.NoteStreaming(first);
                streamLoad.NoteStreaming(second);
                Assert.Equal(baseline + 2, streamLoad.StreamingCount);
                Assert.Equal(Math.Max(45, streamLoad.StreamingCount * 70), streamLoad.HeadFlushDelayMilliseconds);
                Assert.Equal(TimeSpan.FromMilliseconds(streamLoad.HeadFlushDelayMilliseconds), streamLoad.HeadFlushWindow);

                streamLoad.Forget(first);
                Assert.Equal(baseline + 1, streamLoad.StreamingCount);
                streamLoad.Forget(second);
                Assert.Equal(baseline, streamLoad.StreamingCount);
            }
            finally
            {
                streamLoad.Forget(first);
                streamLoad.Forget(second);
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-147")] // ThreadRuntime.swift:3020-3026
    [Trait("Req", "PR-RUNTIME-149")] // ThreadRuntime.swift:3034-3048
    public void Appending_a_row_closes_only_the_last_six_streaming_rows()
    {
        using var test = new TestRuntime();
        test.OnMain(() =>
        {
            var runtime = CreateRuntime();
            var rows = new List<TimelineEntry>();
            for (var index = 0; index < 7; index++)
            {
                TimelineItem.Content content = index == 1
                    ? new TimelineItem.Content.Plan(new ProposedPlan { Markdown = "draft", ProposedPlanState = ProposedPlan.State.Drafting })
                    : new TimelineItem.Content.Assistant(new AssistantMessage { Text = $"row-{index}", IsStreaming = true });
                var item = new TimelineItem(Guid.NewGuid(), content, $"row-{index}",
                    new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero));
                var entry = new TimelineEntry(item);
                rows.Add(entry);
                GetDictionary(runtime, "entryIndex")[entry.Id] = entry;
            }
            SetField(runtime, "entries", new ValueList<TimelineEntry>(rows));

            AddEntry(runtime, "appended", new TimelineItem.Content.Notice(new Notice
            {
                NoticeLevel = Notice.Level.Info,
                Message = "new row",
            }));

            Assert.True(Assert.IsType<TimelineItem.Content.Assistant>(rows[0].Item.TimelineItemContent).Value.IsStreaming);
            for (var index = 2; index < rows.Count; index++)
                Assert.False(Assert.IsType<TimelineItem.Content.Assistant>(rows[index].Item.TimelineItemContent).Value.IsStreaming);
            Assert.Equal(ProposedPlan.State.Proposed,
                Assert.IsType<TimelineItem.Content.Plan>(rows[1].Item.TimelineItemContent).Value.ProposedPlanState);
            Assert.Equal(8, runtime.Entries.Count);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-148")] // ThreadRuntime.swift:3028-3032
    public void Removing_a_row_updates_the_ordered_rows_index_and_revision()
    {
        using var test = new TestRuntime();
        test.OnMain(() =>
        {
            var runtime = CreateRuntime();
            AddEntry(runtime, "remove-me", new TimelineItem.Content.Assistant(new AssistantMessage { Text = "x" }));
            var revision = GetField<long>(runtime, "saveRevision");

            Invoke(runtime, "Remove", "remove-me");

            Assert.DoesNotContain(runtime.Entries, entry => entry.Id == "remove-me");
            Assert.False(GetDictionary(runtime, "entryIndex").Contains("remove-me"));
            Assert.Equal(revision + 1, GetField<long>(runtime, "saveRevision"));
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-151")] // ThreadRuntime.swift:3257-3277
    public void Tool_upsert_keeps_missing_metadata_and_replaces_edits_without_reopening_terminal_rows()
    {
        using var test = new TestRuntime();
        test.OnMain(() =>
        {
            var runtime = CreateRuntime();
            var original = AddEntry(runtime, "tool-row", new TimelineItem.Content.Tool(new ToolCall
            {
                ToolCallKind = ToolCall.Kind.Read,
                Title = "Read file",
                Detail = "original detail",
                ToolCallStatus = ToolCall.Status.Completed,
                Edits = new ValueList<FileEdit>([new("old.cs", "old", 1, 0)]),
            }));
            var incoming = new ToolCall
            {
                ToolCallKind = ToolCall.Kind.Edit,
                Title = "",
                Detail = null,
                ToolCallStatus = ToolCall.Status.Running,
                Edits = new ValueList<FileEdit>([new("new.cs", "new", 2, 1)]),
            };

            Invoke(runtime, "UpsertTool", "tool-row", incoming);

            var merged = Assert.IsType<TimelineItem.Content.Tool>(original.Item.TimelineItemContent).Value;
            Assert.Equal("Read file", merged.Title);
            Assert.Equal("original detail", merged.Detail);
            Assert.Equal(ToolCall.Kind.Edit, merged.ToolCallKind);
            Assert.Equal(ToolCall.Status.Completed, merged.ToolCallStatus);
            Assert.Equal([new FileEdit("new.cs", "new", 2, 1)], merged.Edits);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-152")] // ThreadRuntime.swift:3300-3324
    public void Tool_update_creates_a_fallback_row_and_replaces_output_and_status()
    {
        using var test = new TestRuntime();
        test.OnMain(() =>
        {
            var runtime = CreateRuntime();
            var update = new ToolUpdate { Output = "new output", Status = ToolCall.Status.Completed };

            Invoke(runtime, "ApplyToolUpdate", "missing-tool", update);

            var row = Assert.Single(runtime.Entries);
            var created = Assert.IsType<TimelineItem.Content.Tool>(row.Item.TimelineItemContent).Value;
            Assert.Equal("Tool", created.Title);
            Assert.Equal(ToolCall.Kind.Other, created.ToolCallKind);
            Assert.Equal("new output", created.Output);
            Assert.Equal(ToolCall.Status.Completed, created.ToolCallStatus);
            Assert.True(GetDictionary(runtime, "entryIndex").Contains("missing-tool"));
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-152")] // ThreadRuntime.swift:3300-3324
    public void A_running_tool_update_can_reopen_a_finished_tool_row()
    {
        using var test = new TestRuntime();
        test.OnMain(() =>
        {
            var runtime = CreateRuntime();
            var row = AddEntry(runtime, "tool-row", new TimelineItem.Content.Tool(new ToolCall
            {
                ToolCallKind = ToolCall.Kind.Command,
                Title = "Command",
                Output = "old",
                ToolCallStatus = ToolCall.Status.Failed,
            }));

            Invoke(runtime, "ApplyToolUpdate", "tool-row", new ToolUpdate
            {
                Output = "replacement",
                Status = ToolCall.Status.Running,
            });

            var call = Assert.IsType<TimelineItem.Content.Tool>(row.Item.TimelineItemContent).Value;
            Assert.Equal("replacement", call.Output);
            Assert.Equal(ToolCall.Status.Running, call.ToolCallStatus);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-153")] // ThreadRuntime.swift:3291-3298
    public void Only_write_capable_tool_kinds_invalidate_the_working_tree_snapshot()
    {
        using var test = new TestRuntime();
        test.OnMain(() =>
        {
            var runtime = CreateRuntime();
            var snapshot = GetFieldInfo(runtime, "settledTree");
            var epoch = GetFieldInfo(runtime, "treeEpoch");
            var marker = CreateMarker(snapshot.FieldType);
            snapshot.SetValue(runtime, marker);
            var initialEpoch = Convert.ToInt64(epoch.GetValue(runtime), System.Globalization.CultureInfo.InvariantCulture);
            Invoke(runtime, "NoteToolMayWrite", [null]);
            Assert.Equal(marker, snapshot.GetValue(runtime));
            Assert.Equal(initialEpoch, Convert.ToInt64(epoch.GetValue(runtime), System.Globalization.CultureInfo.InvariantCulture));

            foreach (var kind in new[] { ToolCall.Kind.Read, ToolCall.Kind.Search, ToolCall.Kind.Web, ToolCall.Kind.Command })
            {
                snapshot.SetValue(runtime, marker);
                var before = Convert.ToInt64(epoch.GetValue(runtime), System.Globalization.CultureInfo.InvariantCulture);
                Invoke(runtime, "NoteToolMayWrite", kind);
                Assert.Equal(marker, snapshot.GetValue(runtime));
                Assert.Equal(before, epoch.GetValue(runtime));
            }

            foreach (var kind in new[] { ToolCall.Kind.Edit, ToolCall.Kind.Mcp, ToolCall.Kind.Agent, ToolCall.Kind.Other })
            {
                snapshot.SetValue(runtime, marker);
                var before = Convert.ToInt64(epoch.GetValue(runtime), System.Globalization.CultureInfo.InvariantCulture);
                Invoke(runtime, "NoteToolMayWrite", kind);
                Assert.Null(snapshot.GetValue(runtime));
                Assert.Equal(before + 1, Convert.ToInt64(epoch.GetValue(runtime), System.Globalization.CultureInfo.InvariantCulture));
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-154")] // ThreadRuntime.swift:3484-3498
    public void Edit_diff_cap_uses_utf8_bytes_and_preserves_statistics()
    {
        var exact = new FileEdit("exact.cs", new string('x', 200_000), 17, 9);
        var overByUtf8Bytes = new FileEdit("large.cs", new string('é', 100_001), 23, 11);

        var capped = InvokeStatic<ValueList<FileEdit>>("Capped", new ValueList<FileEdit>([exact, overByUtf8Bytes]));

        Assert.Equal(new string('x', 200_000), capped[0].Diff);
        Assert.Null(capped[1].Diff);
        Assert.Equal((23L, 11L), (capped[1].Additions, capped[1].Deletions));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-155")] // ThreadRuntime.swift:1738-1757
    public void Compact_without_a_running_session_appends_the_exact_info_notice()
    {
        using var test = new TestRuntime();
        test.OnMain(() =>
        {
            var runtime = CreateRuntime();

            runtime.Compact();

            var notice = Assert.IsType<TimelineItem.Content.Notice>(Assert.Single(runtime.Entries).Item.TimelineItemContent).Value;
            Assert.Equal(Notice.Level.Info, notice.NoticeLevel);
            Assert.Equal("Nothing to compact yet.", notice.Message);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-155")] // ThreadRuntime.swift:1738-1757
    public void Compact_is_silent_while_a_turn_runs()
    {
        using var test = new TestRuntime();
        test.OnMain(() =>
        {
            var runtime = CreateRuntime();
            SetField(runtime, "phase", RuntimePhase.Running);

            runtime.Compact();

            Assert.Empty(runtime.Entries);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-156")] // ThreadRuntime.swift:3014-3018
    public void Hydra_note_is_a_turnless_user_row_with_an_empty_head_list()
    {
        using var test = new TestRuntime();
        test.OnMain(() =>
        {
            var runtime = CreateRuntime();
            runtime.AppendHydraNote("Merged 2 files\nDetails", "hydra-note-id");

            var entry = Assert.Single(runtime.Entries);
            var user = Assert.IsType<TimelineItem.Content.User>(entry.Item.TimelineItemContent).Value;
            Assert.Equal("hydra-note-id", entry.Id);
            Assert.Null(entry.TurnID);
            Assert.NotNull(user.HydraHeads);
            Assert.Empty(user.HydraHeads!);
            Assert.Equal("Merged 2 files\nDetails", user.Text);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-157")] // ThreadRuntime.swift:3554-3558
    public void Updating_an_unknown_turn_is_a_silent_no_op_and_a_known_turn_is_dirty()
    {
        using var test = new TestRuntime();
        test.OnMain(() =>
        {
            var runtime = CreateRuntime();
            var turn = new TurnRecord { Index = 0 };
            SetField(runtime, "turns", new ValueList<TurnRecord>([turn]));
            var revision = GetField<long>(runtime, "saveRevision");

            InvokeTurnUpdate(runtime, Guid.NewGuid());
            Assert.Equal(revision, GetField<long>(runtime, "saveRevision"));

            InvokeTurnUpdate(runtime, turn.Id);
            Assert.Equal(revision + 1, GetField<long>(runtime, "saveRevision"));
            Assert.Single(runtime.Turns);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-158")] // ThreadRuntime.swift:3581-3602
    public void Idle_saves_rearm_the_eight_hundred_millisecond_debounce()
    {
        using var test = new TestRuntime();
        var runtime = test.OnMain(() =>
        {
            var runtime = CreateRuntime();
            SetField(runtime, "persistenceEnabled", true);
            runtime.ScheduleSave();
            return runtime;
        });
        test.AdvanceMilliseconds(400);
        test.OnMain(runtime.ScheduleSave);
        test.AdvanceMilliseconds(799);
        test.OnMain(() => Assert.NotNull(ReadField(runtime, "saveTask")));
        test.AdvanceMilliseconds(1);
        test.OnMain(() => Assert.Null(ReadField(runtime, "saveTask")));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-158")] // ThreadRuntime.swift:3581-3602
    public void Running_saves_share_one_five_second_timer()
    {
        using var test = new TestRuntime();
        var runtime = test.OnMain(() =>
        {
            var runtime = CreateRuntime();
            SetField(runtime, "persistenceEnabled", true);
            SetField(runtime, "phase", RuntimePhase.Running);
            runtime.ScheduleSave();
            return runtime;
        });
        var firstTask = test.OnMain(() => ReadField(runtime, "saveTask"));
        test.AdvanceMilliseconds(2_000);
        test.OnMain(runtime.ScheduleSave);
        test.OnMain(() => Assert.Same(firstTask, ReadField(runtime, "saveTask")));
        test.AdvanceMilliseconds(2_999);
        test.OnMain(() => Assert.NotNull(ReadField(runtime, "saveTask")));
        test.AdvanceMilliseconds(1);
        test.OnMain(() => Assert.Null(ReadField(runtime, "saveTask")));
    }

    private static ThreadRuntime CreateRuntime(Guid? currentTurnID = null)
    {
        var runtime = (ThreadRuntime)RuntimeHelpers.GetUninitializedObject(typeof(ThreadRuntime));
        SetField(runtime, "entries", ValueList<TimelineEntry>.Empty);
        SetField(runtime, "turns", ValueList<TurnRecord>.Empty);
        SetField(runtime, "followUps", ValueList<FollowUpPrompt>.Empty);
        SetField(runtime, "approvals", ValueList<ApprovalRequest>.Empty);
        SetField(runtime, "questions", ValueList<QuestionRequest>.Empty);
        SetField(runtime, "entryIndex", new Dictionary<string, TimelineEntry>(SwiftStringComparer.Instance));
        SetField(runtime, "phase", RuntimePhase.Idle);
        SetField(runtime, "saveRevision", 0L);
        SetField(runtime, "persistenceEnabled", false);
        SetField(runtime, "currentTurnID", currentTurnID);

        var pendingDeltaType = typeof(ThreadRuntime).GetNestedType("PendingDelta", BindingFlags.NonPublic);
        Assert.NotNull(pendingDeltaType);
        var dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(string), pendingDeltaType!);
        SetField(runtime, "pendingDeltas", Activator.CreateInstance(dictionaryType)!);
        return runtime;
    }

    private static TimelineEntry AddEntry(ThreadRuntime runtime, string id, TimelineItem.Content content)
    {
        var item = new TimelineItem(Guid.NewGuid(), content, id, new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero));
        Invoke(runtime, "Append", item);
        return Assert.Single(runtime.Entries, entry => entry.Id == id);
    }

    private static void QueueDelta(ThreadRuntime runtime, string id, ThreadRuntime.DeltaKind kind, string text) =>
        Invoke(runtime, "QueueDelta", id, kind, text);

    private static void DelayNextFlush(ThreadRuntime runtime) =>
        SetField(runtime, "lastFlushAt", SwiftClock.Now + TimeSpan.FromSeconds(1));

    private static void PutDelta(ThreadRuntime runtime, string id, ThreadRuntime.DeltaKind kind, string text)
    {
        var type = typeof(ThreadRuntime).GetNestedType("PendingDelta", BindingFlags.NonPublic)!;
        var delta = RuntimeHelpers.GetUninitializedObject(type);
        SetProperty(delta, "Kind", kind);
        SetProperty(delta, "Text", text);
        GetDictionary(runtime, "pendingDeltas")[id] = delta;
    }

    private static long FlushDelay(long? length, bool scrolling) =>
        InvokeStatic<long>("FlushDelay", length, scrolling);

    private static void InvokeTurnUpdate(ThreadRuntime runtime, Guid id)
    {
        var method = typeof(ThreadRuntime).GetMethod("UpdateTurn", InstanceMembers);
        Assert.NotNull(method);
        var delegateType = method!.GetParameters()[1].ParameterType;
        var invoke = delegateType.GetMethod("Invoke");
        Assert.NotNull(invoke);
        var parameters = invoke!.GetParameters().Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();
        Expression body = invoke.ReturnType == typeof(void)
            ? Expression.Empty()
            : parameters.Length > 0 && invoke.ReturnType.IsAssignableFrom(parameters[^1].Type)
                ? parameters[^1]
                : Expression.Default(invoke.ReturnType);
        var change = Expression.Lambda(delegateType, body, parameters).Compile();
        method.Invoke(runtime, [id, change]);
    }

    private static void Invoke(object target, string name, params object?[] arguments)
    {
        var method = target.GetType().GetMethod(name, InstanceMembers);
        Assert.NotNull(method);
        method!.Invoke(target, arguments);
    }

    private static T Invoke<T>(object target, string name, params object?[] arguments)
    {
        var method = target.GetType().GetMethod(name, InstanceMembers);
        Assert.NotNull(method);
        return Assert.IsType<T>(method!.Invoke(target, arguments));
    }

    private static T InvokeStatic<T>(string name, params object?[] arguments)
    {
        var method = typeof(ThreadRuntime).GetMethod(name, StaticMembers);
        Assert.NotNull(method);
        return Assert.IsType<T>(method!.Invoke(null, arguments));
    }

    private static IDictionary GetDictionary(object target, string name) =>
        Assert.IsAssignableFrom<IDictionary>(ReadField(target, name));

    private static object? ReadField(object target, string name) => GetFieldInfo(target, name).GetValue(target);

    private static T GetField<T>(object target, string name) => Assert.IsType<T>(ReadField(target, name));

    private static FieldInfo GetFieldInfo(object target, string name)
    {
        var field = target.GetType().GetField(name, InstanceMembers);
        Assert.NotNull(field);
        return field!;
    }

    private static void SetField(object target, string name, object? value) => GetFieldInfo(target, name).SetValue(target, value);

    private static T GetProperty<T>(object target, string name)
    {
        var property = target.GetType().GetProperty(name, InstanceMembers);
        Assert.NotNull(property);
        return Assert.IsType<T>(property!.GetValue(target));
    }

    private static void SetProperty(object target, string name, object? value)
    {
        var property = target.GetType().GetProperty(name, InstanceMembers);
        Assert.NotNull(property);
        property!.SetValue(target, value);
    }

    private static object? CreateMarker(Type declaredType)
    {
        var type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        if (type == typeof(string)) return "snapshot";
        if (type.IsValueType) return Activator.CreateInstance(type);
        return RuntimeHelpers.GetUninitializedObject(type);
    }

}
