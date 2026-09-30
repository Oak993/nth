using Marrow.App;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.Testing;
using Xunit;

namespace Marrow.App.Tests.Runtime;

public sealed class ThreadRuntimePart6Tests
{
    private const string ProviderPatch =
        "diff --git a/provider-only.cs b/provider-only.cs\n" +
        "new file mode 100644\n" +
        "--- /dev/null\n" +
        "+++ b/provider-only.cs\n" +
        "@@ -0,0 +1,2 @@\n" +
        "+first\n" +
        "+second\n";

    [Fact]
    [Trait("Req", "PR-RUNTIME-110")] // App/Runtime/ThreadRuntime.swift:2282-2286
    public void A_completion_followed_by_exit_writes_one_turn_end_row()
    {
        using var h = new AppModelHarness();
        var context = CreateRuntime(h);

        h.Main.OnMain(() =>
        {
            context.Runtime.RehearseTurn("The user asked for a change.");
            context.Runtime.Rehearse(new ProviderEvent.TurnStarted("provider-turn"));
            context.Runtime.Rehearse(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
            context.Runtime.Rehearse(new ProviderEvent.Exited(null));
        });
        WaitForEndedTurn(h.Main, context.Runtime);

        var turn = Assert.Single(context.Runtime.Turns);
        Assert.Equal(TurnStatus.Completed, turn.Status);
        Assert.Single(context.Runtime.Entries, entry =>
            entry.Item.TimelineItemContent is TimelineItem.Content.TurnEnd);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-115")] // App/Runtime/ThreadRuntime.swift:2311-2314
    [Trait("Req", "PR-RUNTIME-117")] // App/Runtime/ThreadRuntime.swift:2328-2337
    [Trait("Req", "PR-RUNTIME-122")] // App/Runtime/ThreadRuntime.swift:2396-2401
    public void Finishing_a_turn_clears_requests_records_the_provider_patch_and_saves_the_end_row()
    {
        using var h = new AppModelHarness();
        var context = CreateRuntime(h);
        var approval = new ApprovalRequest(
            "approval",
            ApprovalRequest.Kind.Command,
            "Run command",
            ValueList<ApprovalRequest.Option>.Empty);
        var question = new QuestionRequest(
            "question",
            new ValueList<QuestionRequest.Question>([
                new("choice", "Mode", "Choose a mode", ValueList<QuestionRequest.Choice>.Empty,
                    AllowsMultiple: false, AllowsOther: false, IsSecret: false),
            ]));

        h.Main.OnMain(() =>
        {
            context.Runtime.RehearseTurn("The user asked for a change.");
            context.Runtime.Rehearse(new ProviderEvent.TurnStarted("provider-turn"));
            context.Runtime.Rehearse(new ProviderEvent.Approval(approval));
            context.Runtime.Rehearse(new ProviderEvent.Question(question));
            context.Runtime.Rehearse(new ProviderEvent.Diff(ProviderPatch));
            context.Runtime.Rehearse(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
        });
        WaitForEndedTurn(h.Main, context.Runtime);

        Assert.Empty(context.Runtime.Approvals);
        Assert.Empty(context.Runtime.Questions);
        Assert.Equal(RuntimePhase.Idle, context.Runtime.Phase);
        Assert.Null(context.Runtime.TurnStartedAt);

        var turn = Assert.Single(context.Runtime.Turns);
        Assert.Equal(TurnStatus.Completed, turn.Status);
        Assert.NotNull(turn.CompletedAt);
        Assert.Equal(ProviderPatch, turn.ProviderDiff);

        DiskWriter.Shared.WaitForQueuedWrites();
        var saved = Storage.ReadDocument(context.Thread.Id)!.Value.Document;
        Assert.Equal(TurnStatus.Completed, Assert.Single(saved.Turns).Status);
        Assert.Contains(saved.Items, item => item.TimelineItemContent is TimelineItem.Content.TurnEnd);
    }

    [Theory]
    [InlineData(TurnStatus.Completed, ToolCall.Status.Completed)]
    [InlineData(TurnStatus.Interrupted, ToolCall.Status.Failed)]
    [InlineData(TurnStatus.Failed, ToolCall.Status.Failed)]
    [Trait("Req", "PR-RUNTIME-116")] // App/Runtime/ThreadRuntime.swift:2315-2327
    [Trait("Req", "PR-RUNTIME-120")] // App/Runtime/ThreadRuntime.swift:2354-2376
    public void Finishing_closes_running_tools_and_records_sorted_in_checkout_edits(
        TurnStatus turnStatus,
        ToolCall.Status expectedToolStatus)
    {
        using var h = new AppModelHarness();
        var context = CreateRuntime(h);
        var outsidePath = Path.Combine(Path.GetDirectoryName(context.Root)!, "outside.txt");
        var activeTool = new ToolCall
        {
            ToolCallKind = ToolCall.Kind.Command,
            Title = "Active command",
            Edits = new ValueList<FileEdit>([
                new(Path.Combine(context.Root, "z.cs"), null, Additions: 3, Deletions: 0),
                new(outsidePath, null, Additions: 7, Deletions: 1),
            ]),
        };
        var finishedTool = new ToolCall
        {
            ToolCallKind = ToolCall.Kind.Edit,
            Title = "Earlier edit",
            ToolCallStatus = ToolCall.Status.Completed,
            FinishedAt = h.Main.Time.GetUtcNow(),
            Edits = new ValueList<FileEdit>([
                new(Path.Combine(context.Root, "a.cs"), null, Additions: 2, Deletions: 1),
            ]),
        };

        h.Main.OnMain(() =>
        {
            context.Runtime.RehearseTurn("The user asked for a change.");
            context.Runtime.Rehearse(new ProviderEvent.TurnStarted("provider-turn"));
            context.Runtime.Rehearse(new ProviderEvent.ToolStarted("active", activeTool));
            context.Runtime.Rehearse(new ProviderEvent.ToolStarted("finished", finishedTool));
            context.Runtime.Rehearse(new ProviderEvent.TurnCompleted(turnStatus, null));
        });
        WaitForEndedTurn(h.Main, context.Runtime);

        var rows = context.Runtime.Entries
            .Where(entry => entry.Item.TimelineItemContent is TimelineItem.Content.Tool)
            .ToArray();
        Assert.Equal(2, rows.Length);
        var active = Assert.IsType<TimelineItem.Content.Tool>(rows[0].Item.TimelineItemContent).Value;
        var finished = Assert.IsType<TimelineItem.Content.Tool>(rows[1].Item.TimelineItemContent).Value;
        Assert.Equal(expectedToolStatus, active.ToolCallStatus);
        Assert.NotNull(active.FinishedAt);
        Assert.Equal(ToolCall.Status.Completed, finished.ToolCallStatus);

        var turn = Assert.Single(context.Runtime.Turns);
        Assert.Equal(new[] { "a.cs", "z.cs" }, turn.TouchedPaths);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-119")] // App/Runtime/ThreadRuntime.swift:2342-2353
    [Trait("Req", "PR-RUNTIME-120")] // App/Runtime/ThreadRuntime.swift:2354-2376
    [Trait("Req", "PR-RUNTIME-121")] // App/Runtime/ThreadRuntime.swift:2383-2395
    [Trait("Req", "PR-RUNTIME-122")] // App/Runtime/ThreadRuntime.swift:2396-2401
    [Trait("Decision", "ED-164")]
    public void Provider_patch_builds_the_turn_summary_without_becoming_a_touched_path()
    {
        using var h = new AppModelHarness();
        var context = CreateRuntime(h);

        h.Main.OnMain(() =>
        {
            context.Runtime.RehearseTurn("The user asked for a change.");
            context.Runtime.Rehearse(new ProviderEvent.TurnStarted("provider-turn"));
            context.Runtime.Rehearse(new ProviderEvent.Diff(ProviderPatch));
            context.Runtime.Rehearse(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
        });
        WaitForEndedTurn(h.Main, context.Runtime);

        var turn = Assert.Single(context.Runtime.Turns);
        Assert.Null(turn.BaseCheckpoint);
        Assert.Null(turn.EndCheckpoint);
        Assert.Equal(ProviderPatch, turn.ProviderDiff);
        Assert.Empty(turn.TouchedPaths!);

        var endEntry = Assert.Single(context.Runtime.Entries, entry =>
            entry.Item.TimelineItemContent is TimelineItem.Content.TurnEnd);
        var end = Assert.IsType<TimelineItem.Content.TurnEnd>(endEntry.Item.TimelineItemContent);
        Assert.Equal(TurnStatus.Completed, end.Value.Status);
        Assert.Equal(1, end.Value.FilesChanged);
        Assert.Equal(2, end.Value.Additions);
        Assert.Equal(0, end.Value.Deletions);
        Assert.Equal("provider-only.cs", Assert.Single(end.Value.Changes!.Files).Path);

        DiskWriter.Shared.WaitForQueuedWrites();
        var saved = Storage.ReadDocument(context.Thread.Id)!.Value.Document;
        var savedEndItem = Assert.Single(saved.Items, item =>
            item.TimelineItemContent is TimelineItem.Content.TurnEnd);
        var savedEnd = Assert.IsType<TimelineItem.Content.TurnEnd>(savedEndItem.TimelineItemContent);
        Assert.Equal(end.Value, savedEnd.Value);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-123")] // App/Runtime/ThreadRuntime.swift:2402-2406
    public void Archiving_during_a_turn_keeps_the_follow_up_queue_from_continuing()
    {
        using var h = new AppModelHarness();
        var context = CreateRuntime(h);
        h.Main.OnMain(() =>
        {
            context.Runtime.RehearseTurn("The user asked for a change.");
            context.Runtime.EnqueueFollowUp("Keep queued", Array.Empty<Attachment>());
            context.Runtime.Rehearse(new ProviderEvent.TurnStarted("provider-turn"));
            context.Model.UpdateThread(context.Thread.Id, value => value with { IsArchived = true });
            context.Runtime.Rehearse(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
        });
        WaitForEndedTurn(h.Main, context.Runtime);

        Assert.Equal("Keep queued", Assert.Single(context.Runtime.FollowUps).Text);
        Assert.Single(context.Runtime.Turns);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-129")] // App/Runtime/ThreadRuntime.swift:1691-1695
    public void Interrupt_on_an_idle_runtime_leaves_it_idle_without_a_turn()
    {
        using var h = new AppModelHarness();
        var context = CreateRuntime(h);

        h.Main.OnMain(context.Runtime.Interrupt);

        Assert.Equal(RuntimePhase.Idle, context.Runtime.Phase);
        Assert.Empty(context.Runtime.Turns);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-131")] // App/Runtime/ThreadRuntime.swift:1705-1712
    public void Interrupting_a_running_turn_without_a_session_finishes_it_as_interrupted()
    {
        using var h = new AppModelHarness();
        var context = CreateRuntime(h);
        h.Main.OnMain(() =>
        {
            context.Runtime.RehearseTurn("The user asked for a change.");
            context.Runtime.Rehearse(new ProviderEvent.TurnStarted("provider-turn"));
            context.Runtime.Interrupt();
        });
        WaitForEndedTurn(h.Main, context.Runtime);

        Assert.Equal(RuntimePhase.Idle, context.Runtime.Phase);
        Assert.Equal(TurnStatus.Interrupted, Assert.Single(context.Runtime.Turns).Status);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-133")] // App/Runtime/ThreadRuntime.swift:690-722
    public void Take_back_with_text_already_in_the_composer_only_stops_the_turn()
    {
        using var h = new AppModelHarness();
        var context = CreateRuntime(h);
        h.Main.OnMain(() =>
        {
            context.Runtime.RehearseTurn("The user asked for a change.");
            context.Runtime.Rehearse(new ProviderEvent.TurnStarted("provider-turn"));
            context.Runtime.Draft = new ComposerDraft { Text = "Keep my new draft" };
            context.Runtime.TakeBackRunningTurn();
        });
        WaitForEndedTurn(h.Main, context.Runtime);

        Assert.Equal("Keep my new draft", context.Runtime.Draft.Text);
        Assert.Equal(TurnStatus.Interrupted, Assert.Single(context.Runtime.Turns).Status);
    }

    private static (AppModel Model, ChatThread Thread, string Root, ThreadRuntime Runtime) CreateRuntime(
        AppModelHarness harness)
    {
        var project = AppModelSamples.Project("Part 6", harness.NewFolder("part6-project"));
        var thread = AppModelSamples.Thread(project);
        harness.WriteLibrary([project], [thread]);
        var model = harness.MakeModel();
        // These tests need a stored follow-up, not a Hydra head dispatched from the queue (ThreadRuntime.swift:999-1013; Swift
        // WebsiteCaptures.swift:107 turns hydraQueueHeads off for the same reason).
        harness.Main.OnMain(() =>
        {
            model.Settings.HydraQueueHeads = false;
            model.Settings.HydraAlwaysHeads = false;
        });
        var runtime = harness.Main.OnMain(() => model.Runtime(thread.Id));
        harness.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        return (model, thread, project.Path, runtime);
    }

    private static void WaitForEndedTurn(TestRuntime main, ThreadRuntime runtime) =>
        main.Loop.RunUntil(
            () => runtime.Phase == RuntimePhase.Idle && runtime.Turns.Any(turn =>
                turn.Status != TurnStatus.Running && turn.CompletedAt is not null),
            AppModelHarness.Bound);
}
