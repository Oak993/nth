using System.Reflection;
using System.Runtime.CompilerServices;
using Marrow.App;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Xunit;

namespace Marrow.App.Tests.Runtime;

public sealed class ThreadRuntimePart9Tests
{
    private const string DelegationBlock = "\u0060\u0060\u0060hydra\n[]\n\u0060\u0060\u0060";

    [Fact]
    [Trait("Req", "PR-RUNTIME-198")] // ThreadRuntime.swift:3700-3723
    public void Rehearsing_a_text_turn_records_its_diff_and_touched_paths_without_a_session()
    {
        using var h = new AppModelHarness();
        var (_, thread, model, runtime) = CreateRuntime(h);

        h.Main.OnMain(() => runtime.RehearseTurn("Inspect these changes.", ["src/a.cs", "README.md"], "provider diff"));

        h.Main.OnMain(() =>
        {
            var turn = Assert.Single(runtime.Turns);
            Assert.Equal(TurnStatus.Running, turn.Status);
            Assert.Equal("provider diff", turn.ProviderDiff);
            Assert.Equal(new[] { "src/a.cs", "README.md" }, turn.TouchedPaths!.ToArray());
            Assert.Equal(RuntimePhase.Running, runtime.Phase);
            Assert.Equal(TurnStatus.Running, model.Thread(thread.Id)!.LastStatus);
            var userEntry = Assert.Single(runtime.Entries);
            Assert.Equal(TimelineEntry.Kind.User, userEntry.TimelineEntryKind);
            Assert.Equal(turn.UserItemID, userEntry.Id);
            var user = Assert.IsType<TimelineItem.Content.User>(userEntry.Item.TimelineItemContent);
            Assert.Equal("Inspect these changes.", user.Value.Text);
            Assert.Equal(
                new ValueSet<string>(["src/a.cs", "README.md"], SwiftStringComparer.Instance),
                runtime.HydraCurrentTurnTouchedPaths);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-198")] // ThreadRuntime.swift:3710-3719
    public void Rehearsing_an_empty_turn_keeps_no_user_row_and_replaces_touched_paths_with_empty()
    {
        using var h = new AppModelHarness();
        var (_, _, _, runtime) = CreateRuntime(h);

        h.Main.OnMain(() =>
        {
            SetPrivateProperty(runtime, nameof(ThreadRuntime.HydraCurrentTurnTouchedPaths),
                new ValueSet<string>(["stale.txt"], SwiftStringComparer.Instance));
            runtime.RehearseTurn(null, [], "provider diff");

            var turn = Assert.Single(runtime.Turns);
            Assert.Null(turn.UserItemID);
            Assert.Null(turn.TouchedPaths);
            Assert.Equal("provider diff", turn.ProviderDiff);
            Assert.Empty(runtime.Entries);
            Assert.Empty(runtime.HydraCurrentTurnTouchedPaths);
            Assert.Equal(RuntimePhase.Running, runtime.Phase);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-199")] // ThreadRuntime.swift:3727-3736
    public void A_late_brief_is_inserted_before_existing_rows_and_only_once()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("rehearsed brief"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        AppModelHarness.WriteDocument(thread.Id);

        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);

        var priorEntryIDs = h.Main.OnMain(() =>
        {
            runtime.Rehearse(new ProviderEvent.MessageDelta("earlier-reply", "Earlier reply"));
            runtime.RehearseTurn(null);
            return runtime.Entries.Select(entry => entry.Id).ToArray();
        });

        h.Main.OnMain(() =>
        {
            runtime.RehearseBrief("The provider's brief.");
            runtime.RehearseBrief("This duplicate must be ignored.");

            Assert.Equal(priorEntryIDs.Length + 1, runtime.Entries.Count);
            Assert.Equal(priorEntryIDs, runtime.Entries.Skip(1).Select(entry => entry.Id));
            var briefEntry = runtime.Entries[0];
            Assert.Equal(TimelineEntry.Kind.User, briefEntry.TimelineEntryKind);
            Assert.Equal(runtime.Turns.Last().UserItemID, briefEntry.Id);
            Assert.Equal(
                "The provider's brief.",
                Assert.IsType<TimelineItem.Content.User>(briefEntry.Item.TimelineItemContent).Value.Text);

            var earlierEntry = Assert.Single(runtime.Entries, entry => entry.Id == "earlier-reply");
            var earlierReply = Assert.IsType<TimelineItem.Content.Assistant>(earlierEntry.Item.TimelineItemContent);
            Assert.Equal("Earlier reply", earlierReply.Value.Text);
            Assert.True(earlierReply.Value.IsStreaming);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-200")] // ThreadRuntime.swift:3751-3757
    [Trait("Decision", "ED-164")]
    public void A_legacy_change_summary_is_written_to_its_turn_end_once()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("legacy change summary"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        var turnID = Guid.NewGuid();
        DiskWriter.Shared.WriteSynchronously(
            new ThreadDocument(thread.Id)
            {
                Turns = new ValueList<TurnRecord>([new TurnRecord { Id = turnID, Index = 0, Status = TurnStatus.Completed }]),
                Items = new ValueList<TimelineItem>(
                [
                    new TimelineItem(turnID, new TimelineItem.Content.User(new UserMessage { Text = "Legacy turn" })),
                ]),
            },
            Storage.ThreadURL(thread.Id));
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);

        var first = new FileChangeSummary(
            new ValueList<FileChangeSummary.File>([new("first.cs", 1, 0, false)]),
            1,
            0);
        var later = new FileChangeSummary(
            new ValueList<FileChangeSummary.File>([new("later.cs", 0, 1, false)]),
            0,
            1);

        var turnEnd = h.Main.OnMain(() => Assert.Single(runtime.Entries, entry =>
            entry.TurnID == turnID && entry.TimelineEntryKind == TimelineEntry.Kind.TurnEnd));

        h.Main.OnMain(() =>
        {
            runtime.StoreLegacyChanges(first, turnID);
            Assert.Equal(first, Assert.IsType<TimelineItem.Content.TurnEnd>(turnEnd.Item.TimelineItemContent).Value.Changes);

            runtime.StoreLegacyChanges(later, turnID);
            Assert.Equal(first, Assert.IsType<TimelineItem.Content.TurnEnd>(turnEnd.Item.TimelineItemContent).Value.Changes);
            runtime.CancelPersistence();
        });
    }

    [Theory]
    [InlineData(HydraHeadInfo.Status.Completed, ToolCall.Status.Completed)]
    [InlineData(HydraHeadInfo.Status.Stopped, ToolCall.Status.Failed)]
    [Trait("Req", "PR-RUNTIME-196")] // ThreadRuntime.swift:2660-2669
    public void A_native_head_finishes_its_running_tool_row_with_the_two_way_status_map(
        HydraHeadInfo.Status headStatus,
        ToolCall.Status expectedStatus)
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("native head"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);

        const string toolID = "native-tool";
        var headID = Guid.NewGuid();
        var info = new HydraHeadInfo(1, "Run the build", HydraHeadInfo.Kind.Native, HydraHeadInfo.Origin.Sent)
        {
            ToolUseID = toolID,
        };

        h.Main.OnMain(() =>
        {
            runtime.RehearseTurn("Run the build");
            runtime.Rehearse(new ProviderEvent.ToolStarted(toolID, new ToolCall
            {
                ToolCallKind = ToolCall.Kind.Command,
                Title = "Build",
                ToolCallStatus = ToolCall.Status.Running,
            }));
            runtime.HydraHeadFinished(headID, info, headStatus, "Build output");

            var entry = Assert.Single(runtime.Entries, candidate => candidate.Id == toolID);
            var call = Assert.IsType<TimelineItem.Content.Tool>(entry.Item.TimelineItemContent).Value;
            Assert.Equal(expectedStatus, call.ToolCallStatus);
            Assert.Equal("Build output", call.Output);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-201")] // ThreadRuntime.swift:2650-2654
    public async Task Stopping_a_native_head_without_a_session_returns_false()
    {
        using var h = new AppModelHarness();
        var (_, _, _, runtime) = CreateRuntime(h);
        var result = h.Main.OnMain(() => runtime.StopNativeHead("native-agent-id"));

        Assert.False(await result);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-195")] // ThreadRuntime.swift:3279-3289
    public void A_non_head_ignores_head_activity()
    {
        using var h = new AppModelHarness();
        var (_, _, _, runtime) = CreateRuntime(h);
        var oldEvent = new DateTimeOffset(2001, 2, 3, 4, 5, 6, TimeSpan.Zero);

        h.Main.OnMain(() =>
        {
            runtime.HydraLastEventAt = oldEvent;
            runtime.HydraEditCount = 4;
            runtime.HydraToolCalls = 2;
            InvokePrivate(runtime, "NoteHydraHeadEvent", 3L, true);

            Assert.Equal(oldEvent, runtime.HydraLastEventAt);
            Assert.Equal(4L, runtime.HydraEditCount);
            Assert.Equal(2L, runtime.HydraToolCalls);
        });
    }

    [Theory]
    [InlineData(HydraHeadInfo.Kind.Droppy, 3)]
    [InlineData(HydraHeadInfo.Kind.Native, 2)]
    [Trait("Req", "PR-RUNTIME-195")] // ThreadRuntime.swift:3279-3289
    public void A_head_counts_its_edits_but_counts_tools_only_for_a_droppy_head(
        HydraHeadInfo.Kind kind,
        long expectedToolCalls)
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("head activity"));
        var thread = AppModelSamples.Thread(project) with
        {
            Hydra = new HydraHeadInfo(1, "Head", kind, HydraHeadInfo.Origin.Delegated),
        };
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        var oldEvent = new DateTimeOffset(2001, 2, 3, 4, 5, 6, TimeSpan.Zero);

        h.Main.OnMain(() =>
        {
            runtime.HydraLastEventAt = oldEvent;
            runtime.HydraEditCount = 4;
            runtime.HydraToolCalls = 2;
            InvokePrivate(runtime, "NoteHydraHeadEvent", 3L, true);

            Assert.True(runtime.HydraLastEventAt > oldEvent);
            Assert.Equal(7L, runtime.HydraEditCount);
            Assert.Equal(expectedToolCalls, runtime.HydraToolCalls);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-195")] // ThreadRuntime.swift:342
    public void Hydra_idle_seconds_is_zero_without_an_event_and_tracks_elapsed_wall_time()
    {
        using var h = new AppModelHarness();
        var (_, _, _, runtime) = CreateRuntime(h);

        h.Main.OnMain(() =>
        {
            runtime.HydraLastEventAt = null;
            Assert.Equal(0d, runtime.HydraIdleSeconds);

            runtime.HydraLastEventAt = h.Main.Time.GetUtcNow();
            Assert.Equal(0d, runtime.HydraIdleSeconds);
        });

        h.Main.Advance(TimeSpan.FromSeconds(3));
        h.Main.OnMain(() => Assert.InRange(runtime.HydraIdleSeconds, 2.999999, 3.000001));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-203")] // ThreadRuntime.swift:2717-2723
    public void Flushable_reports_sort_every_waiting_report_by_head_index()
    {
        var runtime = (ThreadRuntime)RuntimeHelpers.GetUninitializedObject(typeof(ThreadRuntime));
        var reports = new[]
        {
            Report(3),
            Report(1),
            Report(2),
        };
        SetPrivateSequence(runtime, "hydraPendingReports", reports);

        var result = Assert.IsAssignableFrom<IEnumerable<HydraReport>>(InvokePrivate(runtime, "FlushableHydraReports"));
        Assert.Equal(new long[] { 1, 2, 3 }, result.Select(report => report.HeadIndex));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-204")] // ThreadRuntime.swift:2725-2732
    public async Task A_blocked_heads_report_turn_clears_its_flag_and_keeps_reports()
    {
        var runtime = (ThreadRuntime)RuntimeHelpers.GetUninitializedObject(typeof(ThreadRuntime));
        var reports = new[] { Report(2), Report(1) };
        SetPrivateSequence(runtime, "hydraPendingReports", reports);
        SetPrivateField(runtime, "hydraFlushScheduled", true);
        SetPrivateField(runtime, "phase", RuntimePhase.Running);

        await (Task)InvokePrivate(runtime, "StartHydraReportTurn");

        Assert.False((bool)GetPrivateField(runtime, "hydraFlushScheduled"));
        var pending = Assert.IsAssignableFrom<IEnumerable<HydraReport>>(GetPrivateField(runtime, "hydraPendingReports"));
        Assert.Equal(new long[] { 2, 1 }, pending.Select(report => report.HeadIndex));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-213")] // ThreadRuntime.swift:2786-2789
    public void Delegation_block_membership_is_assistant_only()
    {
        var turnID = Guid.NewGuid();
        var plainUser = new TimelineEntry(new TimelineItem(
            turnID,
            new TimelineItem.Content.User(new UserMessage { Text = DelegationBlock })));
        var plainAssistant = new TimelineEntry(new TimelineItem(
            turnID,
            new TimelineItem.Content.Assistant(new AssistantMessage { Text = "ordinary answer" })));
        var assistantWithBlock = new TimelineEntry(new TimelineItem(
            turnID,
            new TimelineItem.Content.Assistant(new AssistantMessage { Text = DelegationBlock })));

        Assert.False((bool)InvokePrivateStatic("HoldsDelegationBlock", plainUser));
        Assert.False((bool)InvokePrivateStatic("HoldsDelegationBlock", plainAssistant));
        Assert.True((bool)InvokePrivateStatic("HoldsDelegationBlock", assistantWithBlock));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-226")] // ThreadRuntime.swift:2943-2973
    public void A_streamed_delegation_is_queued_once_when_the_pair_cap_is_full()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("streamed delegation"));
        var lead = AppModelSamples.Thread(project) with { HydraEnabled = true };
        var busyHead = AppModelSamples.Thread(project) with
        {
            ParentThreadID = lead.Id,
            IsInPanel = true,
            Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Running),
        };
        h.WriteLibrary([project], [lead, busyHead]);
        var model = h.MakeModel();
        h.Main.OnMain(() =>
        {
            model.Settings.HydraEnabled = true;
            model.Settings.HydraMaxHeads = 1;
            model.UpdateThread(busyHead.Id, current => current with
            {
                Hydra = current.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running },
            });
        });
        var runtime = h.Main.OnMain(() => model.Runtime(lead.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            Assert.Equal(1, model.RunningDroppyHeads(lead.Id));
            Assert.False(model.HydraLaunch(model.Thread(lead.Id)!)!.HasRoom(model.RunningDroppyHeads(lead.Id)));
            runtime.RehearseTurn("Delegate the parser change.");
            var block = "Lead reply\n```hydra\n[{\"task\":\"Update parser\",\"prompt\":\"Make the parser change.\"}]\n```";
            runtime.Rehearse(new ProviderEvent.MessageDelta("reply-1", block));
            InvokePrivate(runtime, "FlushDeltas", false);
            var turnID = runtime.Turns.Last().Id;
            Assert.Equal(1, model.HydraLaunch(model.Thread(lead.Id)!)!.MaxHeads);
            Assert.Equal(1, model.RunningDroppyHeads(lead.Id));
            var parser = new HydraDelegationStreamParser(turnID, "reply-1");
            parser.Ingest(block);
            Assert.Single(parser.Delegations);
            Assert.Equal(turnID, Assert.Single(runtime.Entries, entry => entry.Id == "reply-1").TurnID);

            Assert.NotNull(GetPrivateField(runtime, "hydraStream"));
            Assert.Equal(1L, GetPrivateField(runtime, "hydraStream").GetType().GetField("Item4")!.GetValue(GetPrivateField(runtime, "hydraStream")));
            Assert.Equal(1, ((HydraDelegationBudget)GetPrivateField(runtime, "hydraBudget")).Rounds);
            Assert.Equal(1, runtime.HydraWaitingCount);
            Assert.Equal(1, model.RunningDroppyHeads(lead.Id));
            var queued = Assert.IsAssignableFrom<System.Collections.IEnumerable>(GetPrivateField(runtime, "hydraWaitingStorage"));
            Assert.Single(queued.Cast<object>());
            var stream = GetPrivateField(runtime, "hydraStream");
            Assert.NotNull(stream);
            Assert.Equal(turnID, stream.GetType().GetField("Item1")!.GetValue(stream));
            Assert.Equal("reply-1", stream.GetType().GetField("Item2")!.GetValue(stream));
            Assert.Equal(1L, stream.GetType().GetField("Item4")!.GetValue(stream));
            Assert.Single(Assert.IsAssignableFrom<System.Collections.IDictionary>(GetPrivateField(runtime, "hydraBatches")).Keys.Cast<object>());
            Assert.Equal(1, ((HydraDelegationBudget)GetPrivateField(runtime, "hydraBudget")).Rounds);
        });
    }
    [Fact]
    [Trait("Req", "PR-RUNTIME-207")] // ThreadRuntime.swift:2757-2890
    public void A_completed_streamed_block_marks_the_reply_sent_and_holds_the_lead_for_its_head()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("completed delegation"));
        var lead = AppModelSamples.Thread(project) with { HydraEnabled = true };
        var busyHead = AppModelSamples.Thread(project) with
        {
            ParentThreadID = lead.Id,
            IsInPanel = true,
            Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Running),
        };
        h.WriteLibrary([project], [lead, busyHead]);
        var model = h.MakeModel();
        h.Main.OnMain(() =>
        {
            model.Settings.HydraEnabled = true;
            model.Settings.HydraMaxHeads = 1;
            model.UpdateThread(busyHead.Id, current => current with
            {
                Hydra = current.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running },
            });
        });
        var runtime = h.Main.OnMain(() => model.Runtime(lead.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            runtime.RehearseTurn("Delegate the parser change.");
            var block = "Lead reply\n```hydra\n[{\"task\":\"Update parser\",\"prompt\":\"Make the parser change.\"}]\n```";
            runtime.Rehearse(new ProviderEvent.MessageDelta("reply-1", block));
            InvokePrivate(runtime, "FlushDeltas", false);
            var outcome = InvokePrivate(runtime, "SpawnDelegatedHeads", runtime.Turns.Last().Id);

            Assert.Equal("HeadsOut", outcome.ToString());
            Assert.Null(GetPrivateField(runtime, "hydraStream"));
            Assert.Equal(1, runtime.HydraWaitingCount);
            var reply = Assert.IsType<TimelineItem.Content.Assistant>(
                Assert.Single(runtime.Entries, entry => entry.Id == "reply-1").Item.TimelineItemContent);
            Assert.True(HydraPrompts.HasSentDelegationBlock(reply.Value.Text));
            Assert.Equal(1, ((HydraDelegationBudget)GetPrivateField(runtime, "hydraBudget")).Rounds);
        });
    }
    [Fact]
    [Trait("Req", "PR-RUNTIME-206")] // ThreadRuntime.swift:2727-2741
    public void A_heads_report_turn_consumes_reports_and_starts_with_the_sorted_head_indexes()
    {
        using var h = new AppModelHarness();
        var (project, thread, model, runtime) = CreateRuntime(h,
            project => AppModelSamples.Thread(project) with { Model = "thread-runtime-test" });
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        var reports = new[] { Report(3), Report(1), Report(2) };

        h.Main.OnMain(() =>
        {
            var signature = InvokePrivateStatic("MakeSessionSignature", model.Thread(thread.Id)!, project.Path,
                model.Settings.AntiSlopEnabled, model.HydraLaunch(model.Thread(thread.Id)!));
            SetPrivateField(runtime, "session", new TestProviderSession());
            SetPrivateField(runtime, "sessionSignature", signature);
            SetPrivateSequence(runtime, "hydraPendingReports", reports);
            SetPrivateField(runtime, "hydraFlushScheduled", true);
        });

        h.Main.Loop.Run(() => (Task)InvokePrivate(runtime, "StartHydraReportTurn"), AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            Assert.False((bool)GetPrivateField(runtime, "hydraFlushScheduled"));
            Assert.Empty(Assert.IsAssignableFrom<IEnumerable<HydraReport>>(
                GetPrivateField(runtime, "hydraPendingReports")));
            Assert.Equal(RuntimePhase.Running, runtime.Phase);
            var user = Assert.IsType<TimelineItem.Content.User>(
                Assert.Single(runtime.Entries, entry => entry.TimelineEntryKind == TimelineEntry.Kind.User)
                    .Item.TimelineItemContent);
            Assert.Equal(new long[] { 1, 2, 3 }, user.Value.HydraHeads);
        });
    }

    [Fact]
    public void StartTurn_uses_the_Win32_exception_message_in_its_error_notice()
    {
        using var h = new AppModelHarness();
        var (project, thread, model, runtime) = CreateRuntime(h,
            project => AppModelSamples.Thread(project) with { Model = "thread-runtime-test" });
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        var failure = new System.ComponentModel.Win32Exception(5);

        h.Main.OnMain(() =>
        {
            var signature = InvokePrivateStatic("MakeSessionSignature", model.Thread(thread.Id)!, project.Path,
                model.Settings.AntiSlopEnabled, model.HydraLaunch(model.Thread(thread.Id)!));
            SetPrivateField(runtime, "session", new Win32FailingProviderSession(failure));
            SetPrivateField(runtime, "sessionSignature", signature);
        });

        h.Main.Loop.Run(() => (Task)InvokePrivate(runtime, "StartTurn", "Start request.",
            Array.Empty<Attachment>(), null, false), AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            Assert.Equal(RuntimePhase.Idle, runtime.Phase);
            var notice = Assert.IsType<TimelineItem.Content.Notice>(Assert.Single(runtime.Entries,
                entry => entry.Item.TimelineItemContent is TimelineItem.Content.Notice value
                    && value.Value.NoticeLevel == Notice.Level.Error).Item.TimelineItemContent).Value;
            Assert.Equal(Notice.Level.Error, notice.NoticeLevel);
            Assert.Equal(failure.Message, notice.Message);
        });
    }

    private static (Project Project, ChatThread Thread, AppModel Model, ThreadRuntime Runtime) CreateRuntime(
        AppModelHarness h,
        Func<Project, ChatThread>? makeThread = null)
    {
        var project = AppModelSamples.Project("P", h.NewFolder("thread runtime"));
        var thread = makeThread?.Invoke(project) ?? AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        return (project, thread, model, runtime);
    }

    private static HydraReport Report(long index) => new()
    {
        HeadIndex = index,
        Task = "task " + index,
        Origin = HydraHeadInfo.Origin.Queued,
        Status = HydraHeadInfo.Status.Completed,
        Text = "report " + index,
    };

    private static object InvokePrivate(ThreadRuntime runtime, string method, params object?[] arguments)
    {
        var target = typeof(ThreadRuntime).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(target);
        return target.Invoke(runtime, arguments)!;
    }

    private static object InvokePrivateStatic(string method, params object?[] arguments)
    {
        var target = typeof(ThreadRuntime).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(target);
        return target.Invoke(null, arguments)!;
    }

    private static void SetPrivateField(ThreadRuntime runtime, string fieldName, object value)
    {
        var field = typeof(ThreadRuntime).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        Assert.True(field.FieldType.IsInstanceOfType(value), $"Unexpected type for {fieldName}: {field.FieldType}");
        field.SetValue(runtime, value);
    }

    private static object GetPrivateField(ThreadRuntime runtime, string fieldName)
    {
        var field = typeof(ThreadRuntime).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return field.GetValue(runtime)!;
    }

    private static void SetPrivateProperty(ThreadRuntime runtime, string propertyName, object value)
    {
        var property = typeof(ThreadRuntime).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        Assert.True(property.PropertyType.IsInstanceOfType(value), $"Unexpected type for {propertyName}: {property.PropertyType}");
        property.SetValue(runtime, value);
    }

    private static void SetPrivateSequence(ThreadRuntime runtime, string fieldName, IEnumerable<HydraReport> values)
    {
        var field = typeof(ThreadRuntime).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);

        var fieldType = field.FieldType;
        if (fieldType.IsInstanceOfType(values))
        {
            field.SetValue(runtime, values);
            return;
        }

        if (fieldType.IsArray)
        {
            field.SetValue(runtime, values.ToArray());
            return;
        }

        var elementType = fieldType.GetGenericArguments().Single();
        var elements = values.Cast<object>().ToArray();
        var array = Array.CreateInstance(elementType, elements.Length);
        for (var index = 0; index < elements.Length; index++) array.SetValue(elements[index], index);
        var constructor = fieldType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(candidate =>
            {
                var parameters = candidate.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(array);
            });
        if (constructor is not null)
        {
            field.SetValue(runtime, constructor.Invoke([array]));
            return;
        }

        var listType = typeof(List<>).MakeGenericType(elementType);
        var list = (System.Collections.IList)Activator.CreateInstance(listType)!;
        foreach (var element in elements) list.Add(element);
        Assert.True(fieldType.IsInstanceOfType(list), $"Cannot build collection for {fieldName}: {fieldType}");
        field.SetValue(runtime, list);
    }

    private sealed class Win32FailingProviderSession(System.ComponentModel.Win32Exception failure) : ProviderSession
    {
        public Action<ProviderEvent>? OnEvent { get; set; }
        public bool IsRunning => true;
        public Task<string> Start() => Task.FromResult("session");
        public Task Send(TurnInput input) => Task.FromException(failure);
        public Task Interrupt() => Task.CompletedTask;
        public void ResolveApproval(string requestID, string optionID) { }
        public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers) { }
        public Task Compact() => Task.CompletedTask;
        public void Stop() { }
    }
}
