using System.Reflection;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.Testing;
using Xunit;

namespace Marrow.App.Tests.Runtime;

public sealed class ThreadRuntimeTests
{
    [Fact]
    [Trait("Req", "PR-RUNTIME-159")] // App/Runtime/ThreadRuntime.swift:3604-3620
    public void Save_now_clears_its_timer_before_skipping_a_clean_document()
    {
        using var harness = new AppModelHarness();
        var fixture = ThreadRuntimePart8Fixtures.Create(harness);
        var saveTask = typeof(ThreadRuntime).GetField("saveTask", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(saveTask);

        harness.Main.OnMain(() =>
        {
            fixture.Runtime.ScheduleSave();
            Assert.NotNull(saveTask.GetValue(fixture.Runtime));
            fixture.Runtime.SaveNow();
            Assert.Null(saveTask.GetValue(fixture.Runtime));
        });
        DiskWriter.Shared.WaitForQueuedWrites();
        Assert.False(File.Exists(Storage.ThreadURL(fixture.Thread.Id)));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-160")] // App/Runtime/ThreadRuntime.swift:3650-3660
    public void Persistence_snapshot_carries_the_runtime_document_fields()
    {
        using var harness = new AppModelHarness();
        var fixture = ThreadRuntimePart8Fixtures.Create(harness);

        harness.Main.OnMain(() => fixture.Runtime.RehearseTurn("A saved request", ["src/answer.cs"]));
        var (snapshot, entries, turns, usage, continuation, followUps, hydraMerges) = harness.Main.OnMain(() =>
        {
            var value = fixture.Runtime.SnapshotForPersistence();
            return (value, fixture.Runtime.Entries.Select(entry => entry.Item).ToArray(), fixture.Runtime.Turns.ToArray(),
                fixture.Runtime.Usage, fixture.Runtime.Continuation, fixture.Runtime.FollowUps.ToArray(), fixture.Runtime.HydraMerges.ToArray());
        });

        Assert.Equal(fixture.Thread.Id, snapshot.ThreadID);
        Assert.Equal(entries, snapshot.Items);
        Assert.Equal(turns, snapshot.Turns);
        Assert.Equal(usage, snapshot.Usage);
        Assert.Equal(continuation, snapshot.Continuation);
        Assert.Equal(followUps, snapshot.FollowUps);
        Assert.Equal(hydraMerges, snapshot.HydraMerges);
        harness.Main.OnMain(fixture.Runtime.CancelPersistence);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-161")] // App/Runtime/ThreadRuntime.swift:557-631
    public void Loading_history_normalizes_reasoning_text_running_turns_and_follow_ups()
    {
        using var harness = new AppModelHarness();
        var project = AppModelSamples.Project("Runtime part 8", harness.NewFolder("runtime-p8"));
        var thread = AppModelSamples.Thread(project);
        harness.WriteLibrary([project], [thread]);
        var turnID = Guid.NewGuid();
        var stored = new ThreadDocument(thread.Id)
        {
            Items = new ValueList<TimelineItem>(
            [
                new TimelineItem(turnID, new TimelineItem.Content.Reasoning(new ReasoningBlock { Text = " \t\n", IsStreaming = true })),
                new TimelineItem(turnID, new TimelineItem.Content.Assistant(new AssistantMessage { Text = "A — B", IsStreaming = true })),
            ]),
            Turns = new ValueList<TurnRecord>(
            [
                new TurnRecord
                {
                    Id = turnID,
                    Index = 0,
                    Status = TurnStatus.Running,
                    TouchedPaths = new ValueList<string>(["src/answer.cs"]),
                },
            ]),
            FollowUps = new ValueList<FollowUpPrompt>(
            [
                new FollowUpPrompt { Text = " \t\n" },
                new FollowUpPrompt { Text = "Keep this prompt" },
            ]),
        };
        var fixture = ThreadRuntimePart8Fixtures.Create(harness, document: stored);

        harness.Main.OnMain(() =>
        {
            var assistant = Assert.Single(fixture.Runtime.Entries, entry => entry.Item.TimelineItemContent is TimelineItem.Content.Assistant);
            var message = Assert.IsType<TimelineItem.Content.Assistant>(assistant.Item.TimelineItemContent).Value;
            Assert.DoesNotContain('\u2014', message.Text);
            Assert.False(message.IsStreaming);
            Assert.DoesNotContain(fixture.Runtime.Entries, entry => entry.Item.TimelineItemContent is TimelineItem.Content.Reasoning);
            Assert.Equal(TurnStatus.Interrupted, Assert.Single(fixture.Runtime.Turns).Status);
            Assert.Equal("Keep this prompt", Assert.Single(fixture.Runtime.FollowUps).Text);

            var end = Assert.IsType<TimelineItem.Content.TurnEnd>(
                Assert.Single(fixture.Runtime.Entries, entry => entry.Item.TimelineItemContent is TimelineItem.Content.TurnEnd)
                    .Item.TimelineItemContent).Value;
            Assert.Equal(TurnStatus.Interrupted, end.Status);
            Assert.Equal(1, end.FilesChanged);
            Assert.Equal(0, end.Additions);
            Assert.Equal(0, end.Deletions);
            Assert.Null(end.Changes);
            fixture.Runtime.CancelPersistence();
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-161")] // App/Runtime/ThreadRuntime.swift:563: continuation = document.continuation
                                      // ?? continuation - the document's continuation wins over an in-memory one.
    // Calls the private Install(ThreadDocument) directly (matching InvokePrivate's pattern elsewhere in this
    // suite): ThreadDocument.Continuation decodes to null and is omitted on encode (Models/Timeline.cs:938,
    // BLOCKED, unrelated STOP note), so a round trip through the real save/load path could not carry a
    // non-null stored continuation and would not exercise this merge at all.
    public void Loading_history_prefers_the_stored_documents_continuation_over_an_in_memory_one()
    {
        using var harness = new AppModelHarness();
        var fixture = ThreadRuntimePart8Fixtures.Create(harness);
        var storedContinuation = new ThreadContinuation(Guid.NewGuid(), "Stored source", "Stored context");
        var inMemoryContinuation = new ThreadContinuation(Guid.NewGuid(), "In-memory source", "In-memory context");
        var document = new ThreadDocument(fixture.Thread.Id) { Continuation = storedContinuation };

        harness.Main.OnMain(() =>
        {
            var continuationField = typeof(ThreadRuntime).GetField("continuation", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(continuationField);
            continuationField.SetValue(fixture.Runtime, inMemoryContinuation);

            var install = typeof(ThreadRuntime).GetMethod("Install", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(install);
            install.Invoke(fixture.Runtime, [document]);

            Assert.Equal(storedContinuation, fixture.Runtime.Continuation);
            fixture.Runtime.CancelPersistence();
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-163")] // App/Runtime/ThreadRuntime.swift:3622-3627
    public void Unsaved_snapshot_is_null_when_clean_and_stays_available_until_written()
    {
        using var harness = new AppModelHarness();
        var fixture = ThreadRuntimePart8Fixtures.Create(harness);

        Assert.Null(harness.Main.OnMain(fixture.Runtime.UnsavedSnapshot));
        harness.Main.OnMain(() => fixture.Runtime.RehearseTurn("Changed after load"));
        var first = harness.Main.OnMain(fixture.Runtime.UnsavedSnapshot);
        var second = harness.Main.OnMain(fixture.Runtime.UnsavedSnapshot);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(fixture.Thread.Id, first!.ThreadID);
        Assert.Equal(fixture.Thread.Id, second!.ThreadID);
        harness.Main.OnMain(fixture.Runtime.CancelPersistence);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-164")] // App/Runtime/ThreadRuntime.swift:3629-3640
    public void A_runtime_holds_only_persisted_state_until_the_composer_has_text()
    {
        using var harness = new AppModelHarness();
        var fixture = ThreadRuntimePart8Fixtures.Create(harness);

        harness.Main.OnMain(() =>
        {
            Assert.True(fixture.Runtime.HoldsOnlyPersistedState);
            fixture.Runtime.Draft = new ComposerDraft { Text = "Unsent text" };
            Assert.False(fixture.Runtime.HoldsOnlyPersistedState);
            fixture.Runtime.Draft = new ComposerDraft();
            Assert.True(fixture.Runtime.HoldsOnlyPersistedState);
            fixture.Runtime.CancelPersistence();
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-165")] // App/Runtime/ThreadRuntime.swift:3642-3648
    public void Cancel_persistence_prevents_a_dirty_scheduled_save_from_recreating_a_deleted_file()
    {
        using var harness = new AppModelHarness();
        var fixture = ThreadRuntimePart8Fixtures.Create(harness);

        harness.Main.OnMain(() =>
        {
            fixture.Runtime.RehearseTurn("Dirty before teardown");
            fixture.Runtime.ScheduleSave();
            fixture.Runtime.CancelPersistence();
        });
        harness.Main.Advance(TimeSpan.FromSeconds(6));
        DiskWriter.Shared.WaitForQueuedWrites();

        Assert.False(File.Exists(Storage.ThreadURL(fixture.Thread.Id)));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-166")] // App/Runtime/ThreadRuntime.swift:1837-1845
    public void Revert_rejects_a_missing_turn_without_changing_the_timeline()
    {
        using var harness = new AppModelHarness();
        var project = AppModelSamples.Project("Revert missing turn", harness.NewFolder("revert-missing-turn"));
        var thread = AppModelSamples.Thread(project) with { Provider = ProviderKind.Cursor };
        harness.WriteLibrary([project], [thread]);
        var turnID = Guid.NewGuid();
        var userItem = new TimelineItem(turnID, new TimelineItem.Content.User(new UserMessage { Text = "Keep this turn" }));
        DiskWriter.Shared.WriteSynchronously(
            new ThreadDocument(thread.Id)
            {
                Turns = new ValueList<TurnRecord>([
                    new TurnRecord
                    {
                        Id = turnID,
                        Index = 0,
                        Status = TurnStatus.Completed,
                        CompletedAt = SwiftDate.Now,
                        UserItemID = userItem.Id,
                    },
                ]),
                Items = new ValueList<TimelineItem>([userItem]),
            },
            Storage.ThreadURL(thread.Id));
        var model = harness.MakeModel();
        var runtime = harness.Main.OnMain(() => model.Runtime(thread.Id));
        harness.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        var (entries, turns) = harness.Main.OnMain(() =>
            (runtime.Entries.ToArray(), runtime.Turns.ToArray()));
        var error = Assert.Throws<ProviderError>(() =>
            harness.Main.Loop.Run(() => runtime.Revert(Guid.NewGuid(), restoreFiles: false), AppModelHarness.Bound));

        Assert.Equal("This message is no longer in the thread.", error.ErrorDescription);
        harness.Main.OnMain(() =>
        {
            Assert.Equal(entries, runtime.Entries);
            Assert.Equal(turns, runtime.Turns);
            runtime.CancelPersistence();
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-166")] // App/Runtime/ThreadRuntime.swift:1837-1845
    public void Revert_rejects_a_running_turn_before_looking_up_the_message()
    {
        using var harness = new AppModelHarness();
        var fixture = ThreadRuntimePart8Fixtures.Create(harness);
        var phase = typeof(ThreadRuntime).GetField("phase", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(phase);
        harness.Main.OnMain(() => phase.SetValue(fixture.Runtime, RuntimePhase.Running));

        var error = Assert.Throws<ProviderError>(() =>
            harness.Main.Loop.Run(() => fixture.Runtime.Revert(Guid.NewGuid(), restoreFiles: false), AppModelHarness.Bound));

        Assert.Equal("Wait for the running turn to finish first.", error.ErrorDescription);
        harness.Main.OnMain(fixture.Runtime.CancelPersistence);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-167")] // App/Runtime/ThreadRuntime.swift:1857-1869
    public void Codex_revert_rejects_a_completed_turn_without_a_provider_turn_id()
    {
        using var harness = new AppModelHarness();
        var project = AppModelSamples.Project("Runtime part 8", harness.NewFolder("runtime-p8"));
        var thread = AppModelSamples.Thread(project);
        harness.WriteLibrary([project], [thread]);
        var turnID = Guid.NewGuid();
        var stored = new ThreadDocument(thread.Id)
        {
            Turns = new ValueList<TurnRecord>([new TurnRecord { Id = turnID, Index = 0, Status = TurnStatus.Completed }]),
        };
        var fixture = ThreadRuntimePart8Fixtures.Create(harness, document: stored);

        var error = Assert.Throws<ProviderError>(() =>
            harness.Main.Loop.Run(() => fixture.Runtime.Revert(turnID, restoreFiles: false), AppModelHarness.Bound));

        Assert.Equal("The selected message has no Codex turn ID, so its history cannot be safely reverted.", error.ErrorDescription);
        harness.Main.OnMain(fixture.Runtime.CancelPersistence);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-168")] // App/Runtime/ThreadRuntime.swift:1870-1876
    public void Copilot_revert_requires_the_original_saved_session()
    {
        using var harness = new AppModelHarness();
        var project = AppModelSamples.Project("Runtime part 8", harness.NewFolder("runtime-p8"));
        var thread = AppModelSamples.Thread(project) with { Provider = ProviderKind.Copilot };
        harness.WriteLibrary([project], [thread]);
        var turnID = Guid.NewGuid();
        var stored = new ThreadDocument(thread.Id)
        {
            Turns = new ValueList<TurnRecord>([new TurnRecord { Id = turnID, Index = 0, Status = TurnStatus.Completed }]),
        };
        var fixture = ThreadRuntimePart8Fixtures.Create(harness, provider: ProviderKind.Copilot, document: stored);

        var error = Assert.Throws<ProviderError>(() =>
            harness.Main.Loop.Run(() => fixture.Runtime.Revert(turnID, restoreFiles: false), AppModelHarness.Bound));

        Assert.Equal("The original Copilot conversation is unavailable.", error.ErrorDescription);
        harness.Main.OnMain(fixture.Runtime.CancelPersistence);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-169")] // App/Runtime/ThreadRuntime.swift:1877-1891
    public void Claude_revert_keeps_the_timeline_when_the_previous_turn_has_no_resume_anchor()
    {
        using var harness = new AppModelHarness();
        var project = AppModelSamples.Project("Runtime part 8", harness.NewFolder("runtime-p8"));
        var thread = AppModelSamples.Thread(project) with { Provider = ProviderKind.Claude };
        harness.WriteLibrary([project], [thread]);
        var firstID = Guid.NewGuid();
        var secondID = Guid.NewGuid();
        var stored = new ThreadDocument(thread.Id)
        {
            Turns = new ValueList<TurnRecord>(
            [
                new TurnRecord { Id = firstID, Index = 0, Status = TurnStatus.Completed },
                new TurnRecord { Id = secondID, Index = 1, Status = TurnStatus.Completed },
            ]),
        };
        var fixture = ThreadRuntimePart8Fixtures.Create(harness, provider: ProviderKind.Claude, document: stored);

        var error = Assert.Throws<ProviderError>(() =>
            harness.Main.Loop.Run(() => fixture.Runtime.Revert(secondID, restoreFiles: false), AppModelHarness.Bound));

        Assert.Equal("Claude has no resume point before this message. The conversation was kept.", error.ErrorDescription);
        harness.Main.OnMain(() =>
        {
            Assert.Equal(new[] { firstID, secondID }, fixture.Runtime.Turns.Select(turn => turn.Id));
            fixture.Runtime.CancelPersistence();
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-171")] // App/Runtime/ThreadRuntime.swift:1851-1856
    public void Restoring_files_requires_a_checkpoint_before_mutating_history()
    {
        using var harness = new AppModelHarness();
        var project = AppModelSamples.Project("Runtime part 8", harness.NewFolder("runtime-p8"));
        var thread = AppModelSamples.Thread(project) with { Provider = ProviderKind.Cursor };
        harness.WriteLibrary([project], [thread]);
        var turnID = Guid.NewGuid();
        var stored = new ThreadDocument(thread.Id)
        {
            Turns = new ValueList<TurnRecord>([new TurnRecord { Id = turnID, Index = 0, Status = TurnStatus.Completed }]),
        };
        var fixture = ThreadRuntimePart8Fixtures.Create(harness, provider: ProviderKind.Cursor, document: stored);

        var error = Assert.Throws<ProviderError>(() =>
            harness.Main.Loop.Run(() => fixture.Runtime.Revert(turnID, restoreFiles: true), AppModelHarness.Bound));

        Assert.Equal("This turn has no file checkpoint. Choose to keep the file changes instead.", error.ErrorDescription);
        harness.Main.OnMain(() =>
        {
            Assert.Equal(turnID, Assert.Single(fixture.Runtime.Turns).Id);
            fixture.Runtime.CancelPersistence();
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-174")] // App/Runtime/ThreadRuntime.swift:1801-1813
    [Trait("Req", "PR-RUNTIME-175")] // App/Runtime/ThreadRuntime.swift:1765-1789
    public void Revert_preview_caches_a_missing_checkpoint_failure_for_ten_seconds()
    {
        using var harness = new AppModelHarness();
        var project = AppModelSamples.Project("Runtime part 8", harness.NewFolder("runtime-p8"));
        var thread = AppModelSamples.Thread(project);
        harness.WriteLibrary([project], [thread]);
        var turnID = Guid.NewGuid();
        var stored = new ThreadDocument(thread.Id)
        {
            Turns = new ValueList<TurnRecord>([new TurnRecord { Id = turnID, Index = 0, Status = TurnStatus.Completed }]),
        };
        var fixture = ThreadRuntimePart8Fixtures.Create(harness, document: stored);
        var first = harness.Main.OnMain(() => fixture.Runtime.RevertPreview(turnID));
        var cached = harness.Main.OnMain(() => fixture.Runtime.RevertPreview(turnID));

        Assert.Same(first, cached);
        var error = Assert.Throws<ProviderError>(() => harness.Main.Loop.Run(() => first.Completion, AppModelHarness.Bound));
        Assert.Equal("No file checkpoint is available for this message. You can still keep the file changes.", error.ErrorDescription);

        harness.Main.Advance(TimeSpan.FromSeconds(10));
        var refreshed = harness.Main.OnMain(() => fixture.Runtime.RevertPreview(turnID));
        Assert.NotSame(first, refreshed);
        harness.Main.OnMain(fixture.Runtime.CancelPersistence);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-172")] // App/Runtime/ThreadRuntime.swift:1915-1935
    [Trait("Req", "PR-RUNTIME-170")] // App/Runtime/ThreadRuntime.swift:1892-1902
    public void Reverting_a_later_turn_removes_its_rows_and_clears_usage()
    {
        using var harness = new AppModelHarness();
        var project = AppModelSamples.Project("Runtime part 8", harness.NewFolder("runtime-p8"));
        var thread = AppModelSamples.Thread(project) with
        {
            Provider = ProviderKind.Cursor,
            ProviderSessionID = "old-session",
            ProviderResumeAt = "old-resume-point",
        };
        harness.WriteLibrary([project], [thread]);
        var firstID = Guid.NewGuid();
        var secondID = Guid.NewGuid();
        var stored = new ThreadDocument(thread.Id)
        {
            Items = new ValueList<TimelineItem>(
            [
                new TimelineItem(firstID, new TimelineItem.Content.Plan(new ProposedPlan { Markdown = "first", ProposedPlanState = ProposedPlan.State.Proposed })),
                new TimelineItem(secondID, new TimelineItem.Content.Plan(new ProposedPlan { Markdown = "second", ProposedPlanState = ProposedPlan.State.Proposed })),
            ]),
            Turns = new ValueList<TurnRecord>(
            [
                new TurnRecord { Id = firstID, Index = 0, Status = TurnStatus.Completed },
                new TurnRecord { Id = secondID, Index = 1, Status = TurnStatus.Completed },
            ]),
            Usage = new ContextUsage { UsedTokens = 25 },
        };
        DiskWriter.Shared.WriteSynchronously(stored, Storage.ThreadURL(thread.Id));
        var model = harness.MakeModel();
        var runtime = harness.Main.OnMain(() => model.Runtime(thread.Id));
        harness.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        var restored = harness.Main.Loop.Run(() => runtime.Revert(secondID, restoreFiles: false), AppModelHarness.Bound);

        Assert.True(restored);
        harness.Main.OnMain(() =>
        {
            Assert.Equal(firstID, Assert.Single(runtime.Turns).Id);
            Assert.DoesNotContain(runtime.Entries, entry => entry.TurnID == secondID);
            Assert.Null(runtime.Usage);
            Assert.Null(model.Thread(thread.Id)!.ProviderSessionID);
            Assert.Null(model.Thread(thread.Id)!.ProviderResumeAt);
            Assert.Equal(TurnStatus.Completed, model.Thread(thread.Id)!.LastStatus);
            runtime.CancelPersistence();
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-176")] // App/Runtime/ThreadRuntime.swift:646-655
    public void Continuation_installs_only_into_a_new_empty_conversation()
    {
        using var harness = new AppModelHarness();
        var fixture = ThreadRuntimePart8Fixtures.Create(harness);
        var continuation = new ThreadContinuation(Guid.NewGuid(), "Earlier", "User: Earlier question");
        var installed = harness.Main.Loop.Run(() => fixture.Runtime.InstallContinuation(continuation), AppModelHarness.Bound);

        Assert.True(installed);
        harness.Main.OnMain(() =>
        {
            Assert.Equal(continuation, fixture.Runtime.Continuation);
            fixture.Runtime.CancelPersistence();
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-173")] // App/Runtime/ThreadRuntime.swift:1820-1831
    public void Resend_keeps_the_edit_in_the_composer_when_file_restore_fails()
    {
        using var harness = new AppModelHarness();
        var repository = AppModelGitRepo.WithCommit(harness.NewFolder("runtime-p8-repo"));
        var project = AppModelSamples.Project("Runtime part 8", repository.Root);
        var thread = AppModelSamples.Thread(project) with { Provider = ProviderKind.Cursor };
        harness.WriteLibrary([project], [thread]);
        var turnID = Guid.NewGuid();
        var stored = new ThreadDocument(thread.Id)
        {
            Turns = new ValueList<TurnRecord>(
            [
                new TurnRecord
                {
                    Id = turnID,
                    Index = 0,
                    Status = TurnStatus.Completed,
                    BaseCheckpoint = repository.Git("rev-parse", "HEAD").Trim(),
                    EndCheckpoint = "missing-end-checkpoint",
                    TouchedPaths = new ValueList<string>(["a.txt"]),
                },
            ]),
        };
        harness.WriteLibrary([project], [thread]);
        DiskWriter.Shared.WriteSynchronously(stored, Storage.ThreadURL(thread.Id));
        var model = harness.MakeModel();
        var runtime = harness.Main.OnMain(() => model.Runtime(thread.Id));
        harness.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);

        harness.Main.Loop.Run(() => runtime.Resend(turnID, "Corrected request", Array.Empty<Attachment>(), restoreFiles: true), AppModelHarness.Bound);

        harness.Main.OnMain(() =>
        {
            Assert.Equal("Corrected request", runtime.Draft.Text);
            Assert.Empty(runtime.Turns);
            Assert.Contains(runtime.Entries, entry => entry.Item.TimelineItemContent is TimelineItem.Content.Notice);
            runtime.CancelPersistence();
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-187")] // App/Runtime/ThreadRuntime.swift:1158-1163
    public void The_local_plan_command_toggles_mode_and_does_not_start_a_turn()
    {
        using var harness = new AppModelHarness();
        var project = AppModelSamples.Project("Runtime part 8", harness.NewFolder("runtime-p8"));
        var thread = AppModelSamples.Thread(project) with { InteractionMode = InteractionMode.Build };
        harness.WriteLibrary([project], [thread]);
        var model = harness.MakeModel();
        var runtime = harness.Main.OnMain(() => model.Runtime(thread.Id));
        harness.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);

        harness.Main.OnMain(() =>
        {
            runtime.Draft = new ComposerDraft { Text = "  /plan \t" };
            runtime.Send();
            Assert.Equal(InteractionMode.Plan, model.Thread(thread.Id)!.InteractionMode);
            Assert.True(runtime.Draft.IsEmpty);
            Assert.Empty(runtime.Turns);

            runtime.Draft = new ComposerDraft { Text = "/plan" };
            runtime.Send();
            Assert.Equal(InteractionMode.Build, model.Thread(thread.Id)!.InteractionMode);
            Assert.Empty(runtime.Turns);
            runtime.CancelPersistence();
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-189")] // App/Runtime/ThreadRuntime.swift:1141-1148
    public void Implement_plan_is_a_no_op_while_a_turn_is_busy()
    {
        using var harness = new AppModelHarness();
        var project = AppModelSamples.Project("Runtime part 8", harness.NewFolder("runtime-p8"));
        var thread = AppModelSamples.Thread(project);
        harness.WriteLibrary([project], [thread]);
        var turnID = Guid.NewGuid();
        var plan = new TimelineItem(turnID, new TimelineItem.Content.Plan(
            new ProposedPlan { Markdown = "Build it", ProposedPlanState = ProposedPlan.State.Proposed }));
        var stored = new ThreadDocument(thread.Id)
        {
            Items = new ValueList<TimelineItem>([plan]),
            Turns = new ValueList<TurnRecord>([new TurnRecord { Id = turnID, Index = 0, Status = TurnStatus.Running }]),
        };
        var fixture = ThreadRuntimePart8Fixtures.Create(harness, document: stored);
        var entryID = harness.Main.OnMain(() => Assert.Single(fixture.Runtime.Entries,
            entry => entry.Item.TimelineItemContent is TimelineItem.Content.Plan).Id);
        var phase = typeof(ThreadRuntime).GetField("phase", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(phase);

        harness.Main.OnMain(() =>
        {
            phase.SetValue(fixture.Runtime, RuntimePhase.Running);
            fixture.Runtime.ImplementPlan(entryID);
            var after = Assert.IsType<TimelineItem.Content.Plan>(fixture.Runtime.Entries.Single(entry => entry.Id == entryID).Item.TimelineItemContent).Value;
            Assert.Equal(ProposedPlan.State.Proposed, after.ProposedPlanState);
            Assert.Single(fixture.Runtime.Turns);
            fixture.Runtime.CancelPersistence();
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-190")] // App/Runtime/ThreadRuntime.swift:1150-1156
    public void Dismissing_a_plan_is_allowed_while_a_turn_is_running_and_schedules_its_save()
    {
        using var harness = new AppModelHarness();
        var project = AppModelSamples.Project("Runtime part 8", harness.NewFolder("runtime-p8"));
        var thread = AppModelSamples.Thread(project);
        harness.WriteLibrary([project], [thread]);
        var turnID = Guid.NewGuid();
        var plan = new TimelineItem(turnID, new TimelineItem.Content.Plan(
            new ProposedPlan { Markdown = "Build it", ProposedPlanState = ProposedPlan.State.Proposed }));
        var stored = new ThreadDocument(thread.Id)
        {
            Items = new ValueList<TimelineItem>([plan]),
            Turns = new ValueList<TurnRecord>([new TurnRecord { Id = turnID, Index = 0, Status = TurnStatus.Running }]),
        };
        var fixture = ThreadRuntimePart8Fixtures.Create(harness, document: stored);
        var entryID = harness.Main.OnMain(() => Assert.Single(fixture.Runtime.Entries,
            entry => entry.Item.TimelineItemContent is TimelineItem.Content.Plan).Id);
        var phase = typeof(ThreadRuntime).GetField("phase", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(phase);
        var saveTask = typeof(ThreadRuntime).GetField("saveTask", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(saveTask);

        harness.Main.OnMain(() =>
        {
            phase.SetValue(fixture.Runtime, RuntimePhase.Running);
            fixture.Runtime.DismissPlan(entryID);
            var after = Assert.IsType<TimelineItem.Content.Plan>(fixture.Runtime.Entries.Single(entry => entry.Id == entryID).Item.TimelineItemContent).Value;
            Assert.Equal(ProposedPlan.State.Dismissed, after.ProposedPlanState);
            Assert.NotNull(saveTask.GetValue(fixture.Runtime));
            fixture.Runtime.CancelPersistence();
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-191")] // App/Runtime/ThreadRuntime.swift:3341-3349
    [Trait("Decision", "ED-060")]
    public void File_watches_classify_the_ruling_write_commands_as_writers()
    {
        Assert.True(ShellCommandKind.MayWriteFiles("find . -fprint results.txt"));
        Assert.True(ShellCommandKind.MayWriteFiles("fd --exec-batch formatter {}"));
        Assert.True(ShellCommandKind.MayWriteFiles("sort --output=sorted.txt input.txt"));
        Assert.True(ShellCommandKind.MayWriteFiles("git config user.email test@example.invalid"));
        Assert.True(ShellCommandKind.MayWriteFiles("git tag v1"));
        Assert.True(ShellCommandKind.MayWriteFiles("git remote add origin https://example.invalid/repo.git"));
        Assert.False(ShellCommandKind.MayWriteFiles("git config --get user.email"));
        Assert.False(ShellCommandKind.MayWriteFiles("git tag --list"));
        Assert.False(ShellCommandKind.MayWriteFiles("git remote -v"));
    }
}
