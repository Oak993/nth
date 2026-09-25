using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;
using Marrow.Services.Providers;
using Marrow.Services.Store;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/Runtime/ThreadRuntime.swift (B2 1fbf81e), part 11 of 11.
public sealed class ThreadRuntimePart11Tests
{
    [Fact]
    [Trait("Req", "PR-TERM-030")] // ThreadRuntime.swift:239-244
    public void Terminal_visibility_requests_focus_only_on_a_closed_to_open_transition()
    {
        using var h = new AppModelHarness();
        var (_, runtime, _) = MakeRuntime(h);

        h.Main.OnMain(() =>
        {
            Assert.False(runtime.IsTerminalVisible);
            Assert.False(runtime.TerminalWantsFocus);

            runtime.IsTerminalVisible = true;
            Assert.True(runtime.TerminalWantsFocus);

            runtime.TerminalWantsFocus = false;
            runtime.IsTerminalVisible = true;
            Assert.False(runtime.TerminalWantsFocus);

            runtime.IsTerminalVisible = false;
            runtime.IsTerminalVisible = true;
            Assert.True(runtime.TerminalWantsFocus);
        });
    }

    [Fact]
    [Trait("Req", "PR-TERM-020")] // ThreadRuntime.swift:245-246
    public void Terminal_resizing_flag_tracks_drag_and_release()
    {
        using var h = new AppModelHarness();
        var (_, runtime, _) = MakeRuntime(h);

        h.Main.OnMain(() =>
        {
            Assert.False(runtime.IsTerminalResizing);
            runtime.IsTerminalResizing = true;
            Assert.True(runtime.IsTerminalResizing);
            runtime.IsTerminalResizing = false;
            Assert.False(runtime.IsTerminalResizing);
        });
    }

    [Fact]
    [Trait("Req", "UI-RUNTIME-009")] // ThreadRuntime.swift:3053-3057, 3206-3224
    public void Message_delta_creates_a_streaming_row_only_for_nonempty_text_and_blank_completion_removes_it()
    {
        using var h = new AppModelHarness();
        var (_, runtime, _) = MakeRuntime(h);

        h.Main.OnMain(() =>
        {
            runtime.Rehearse(new ProviderEvent.TurnStarted(null));
            runtime.Rehearse(new ProviderEvent.MessageDelta("message-1", ""));
            Assert.DoesNotContain(runtime.Entries, entry => entry.Id == "message-1");

            runtime.Rehearse(new ProviderEvent.MessageDelta("message-1", "Hello"));
            var row = Assert.Single(runtime.Entries, entry => entry.Id == "message-1");
            Assert.Equal(TimelineEntry.Kind.Assistant, row.TimelineEntryKind);
            var message = Assert.IsType<TimelineItem.Content.Assistant>(row.Item.TimelineItemContent);
            Assert.Equal("Hello", message.Value.Text);
            Assert.True(message.Value.IsStreaming);

            runtime.Rehearse(new ProviderEvent.MessageCompleted("message-1", " \r\n "));
            Assert.DoesNotContain(runtime.Entries, entry => entry.Id == "message-1");
        });
    }

    [Fact]
    [Trait("Req", "UI-RUNTIME-010")] // ThreadRuntime.swift:3058-3062, 3226-3242
    public void Reasoning_delta_creates_a_streaming_row_only_for_nonempty_text_and_blank_completion_removes_it()
    {
        using var h = new AppModelHarness();
        var (_, runtime, _) = MakeRuntime(h);

        h.Main.OnMain(() =>
        {
            runtime.Rehearse(new ProviderEvent.TurnStarted(null));
            runtime.Rehearse(new ProviderEvent.ReasoningDelta("reasoning-1", ""));
            Assert.DoesNotContain(runtime.Entries, entry => entry.Id == "reasoning-1");

            runtime.Rehearse(new ProviderEvent.ReasoningDelta("reasoning-1", "Checking the result"));
            var row = Assert.Single(runtime.Entries, entry => entry.Id == "reasoning-1");
            Assert.Equal(TimelineEntry.Kind.Reasoning, row.TimelineEntryKind);
            var reasoning = Assert.IsType<TimelineItem.Content.Reasoning>(row.Item.TimelineItemContent);
            Assert.Equal("Checking the result", reasoning.Value.Text);
            Assert.True(reasoning.Value.IsStreaming);

            runtime.Rehearse(new ProviderEvent.ReasoningCompleted("reasoning-1", " \t "));
            Assert.DoesNotContain(runtime.Entries, entry => entry.Id == "reasoning-1");
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-251")] // ThreadRuntime.swift:2172-2176
    [Trait("Req", "PR-RUNTIME-252")] // ThreadRuntime.swift:2172-2176 precedes the duplicate arm at :2228-2237
    public void Token_spends_append_to_the_current_turn_without_filtering_merging_or_agent_routing()
    {
        using var h = new AppModelHarness();
        var (_, runtime, _) = MakeRuntime(h);
        var first = new TokenSpend(ProviderKind.Codex, 12, inputTokens: 9, outputTokens: 3, model: "model-a", agentID: "head-1");
        var duplicateIdentity = new TokenSpend(ProviderKind.Codex, 0, inputTokens: 0, outputTokens: 0, model: "model-a", agentID: "head-1");

        h.Main.OnMain(() =>
        {
            runtime.RehearseTurn("Token spend test");
            runtime.Rehearse(new ProviderEvent.TurnStarted(null));
            runtime.Rehearse(new ProviderEvent.TokenSpend(first));
            runtime.Rehearse(new ProviderEvent.TokenSpend(duplicateIdentity));

            var turn = Assert.Single(runtime.Turns);
            Assert.NotNull(turn.TokenSpends);
            Assert.Equal(new[] { first, duplicateIdentity }, turn.TokenSpends!.ToArray());
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-251")] // ThreadRuntime.swift:2172-2176
    public void Token_spend_without_a_current_turn_is_dropped()
    {
        using var h = new AppModelHarness();
        var (_, runtime, _) = MakeRuntime(h);

        h.Main.OnMain(() =>
        {
            runtime.Rehearse(new ProviderEvent.TokenSpend(new TokenSpend(ProviderKind.Codex, 5, agentID: "head-1")));
            Assert.Empty(runtime.Turns);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-255")] // ThreadRuntime.swift:3259, 3428-3431
    [Trait("Req", "PR-RUNTIME-256")] // ThreadRuntime.swift:1205, 2377-2381
    public void Tool_edits_are_checkout_relative_and_finished_paths_remain_until_the_next_turn()
    {
        using var h = new AppModelHarness();
        var (_, runtime, projectRoot) = MakeRuntime(h);
        var outsideRoot = h.NewFolder("outside-runtime-root");
        var call = new ToolCall
        {
            ToolCallKind = ToolCall.Kind.Edit,
            Title = "Edit files",
            Edits = new ValueList<FileEdit>([
                new(Path.Combine(projectRoot, "src", "changed.cs")),
                new(Path.Combine(outsideRoot, "ignored.cs")),
            ]),
        };

        h.Main.OnMain(() =>
        {
            runtime.RehearseTurn("Edit test");
            runtime.Rehearse(new ProviderEvent.TurnStarted(null));
            runtime.Rehearse(new ProviderEvent.ToolStarted("edit-1", call));
            Assert.Equal(new[] { "src/changed.cs" }, runtime.HydraCurrentTurnTouchedPaths.OrderBy(path => path, StringComparer.Ordinal));

            runtime.Rehearse(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
        });
        h.Main.Loop.RunUntil(
            () => runtime.Phase == RuntimePhase.Idle
                && runtime.Turns.LastOrDefault()?.Status == TurnStatus.Completed,
            AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            Assert.Equal(new[] { "src/changed.cs" }, runtime.HydraCurrentTurnTouchedPaths.OrderBy(path => path, StringComparer.Ordinal));

            runtime.RehearseTurn("Next edit test turn");
            Assert.Empty(runtime.HydraCurrentTurnTouchedPaths);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-256")] // ThreadRuntime.swift:3715-3717
    public void Rehearsed_turn_sets_exact_touched_paths_and_a_new_turn_clears_them()
    {
        using var h = new AppModelHarness();
        var (_, runtime, _) = MakeRuntime(h);

        h.Main.OnMain(() =>
        {
            runtime.RehearseTurn("Test turn", touchedPaths: ["src/a.cs", "docs/readme.md", "src/a.cs"]);
            Assert.Equal(new[] { "docs/readme.md", "src/a.cs" },
                runtime.HydraCurrentTurnTouchedPaths.OrderBy(path => path, StringComparer.Ordinal));

            runtime.RehearseTurn("Next turn");
            Assert.Empty(runtime.HydraCurrentTurnTouchedPaths);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-262")] // ThreadRuntime.swift:2485-2490
    public void Hydra_merge_readiness_requires_a_completed_turn_and_is_cleared_by_a_new_turn()
    {
        using var h = new AppModelHarness();
        var (_, runtime, _) = MakeRuntime(h);

        h.Main.OnMain(() =>
        {
            Assert.False(runtime.IsReadyForHydraMerge);

            runtime.RehearseTurn("Merge readiness test");
            runtime.Rehearse(new ProviderEvent.TurnStarted(null));
            runtime.Rehearse(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
        });
        h.Main.Loop.RunUntil(
            () => runtime.Phase == RuntimePhase.Idle
                && runtime.Turns.LastOrDefault()?.Status == TurnStatus.Completed,
            AppModelHarness.Bound);

        h.Main.OnMain(() => Assert.True(runtime.IsReadyForHydraMerge));
        h.Main.OnMain(() =>
        {

            runtime.RehearseTurn("Next turn");
            Assert.False(runtime.IsReadyForHydraMerge);
        });
    }

    [Fact]
    [Trait("Req", "PR-USAGE-082")] // ThreadRuntime.swift:2177-2184
    public void Usage_event_replaces_the_runtime_usage()
    {
        using var h = new AppModelHarness();
        var (_, runtime, _) = MakeRuntime(h);
        var usage = new ContextUsage { UsedTokens = 73, WindowTokens = 100 };

        h.Main.OnMain(() =>
        {
            runtime.Rehearse(new ProviderEvent.Usage(usage));
            Assert.Equal(usage, runtime.Usage);
        });
    }

    [Fact]
    [Trait("Req", "PR-STORE-028")] // ThreadRuntime.swift:3604-3620
    public void Save_now_does_not_create_a_document_when_the_revision_is_already_saved()
    {
        using var h = new AppModelHarness();
        var (_, runtime, _) = MakeRuntime(h);
        var documentPath = Storage.ThreadURL(runtime.ThreadID);
        Assert.False(File.Exists(documentPath));

        h.Main.OnMain(runtime.SaveNow);
        DiskWriter.Shared.WaitForQueuedWrites();

        Assert.False(File.Exists(documentPath));
    }

    [Fact]
    [Trait("Decision", "ED-164")] // ThreadRuntime.swift:1950, 2384; FileChangeSummary+Diff.swift:5-17
    public void File_change_summaries_are_created_through_the_diff_factory()
    {
        var diff = new DiffFile("src/change.cs", DiffFile.Change.Modified, false, ValueList<DiffHunk>.Empty,
            reportedAdditions: 4, reportedDeletions: 2);

        var summary = FileChangeSummaryDiff.Create(new ValueList<DiffFile>([diff]));

        Assert.Equal(new FileChangeSummary.File("src/change.cs", 4, 2, false), Assert.Single(summary.Files));
        Assert.Equal(4L, summary.Additions);
        Assert.Equal(2L, summary.Deletions);
    }

    private static (AppModel Model, ThreadRuntime Runtime, string ProjectRoot) MakeRuntime(AppModelHarness h)
    {
        var projectRoot = h.NewFolder("thread-runtime-p11");
        var project = AppModelSamples.Project("Runtime part 11", projectRoot);
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        return (model, runtime, projectRoot);
    }
}
