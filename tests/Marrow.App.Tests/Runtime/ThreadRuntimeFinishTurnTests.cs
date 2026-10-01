using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Runtime;

// How finishTurn closes a turn and what it starts next: ThreadRuntime.swift:2313-2475 (B3; the records cite the B2 lines
// :2271-2428). Turns run through the real StartTurn over an offline scripted session.
public sealed class ThreadRuntimeFinishTurnTests
{
    private static void Hold(ThreadRuntimeTurnRig rig, TurnStatus? onInterrupt = TurnStatus.Interrupted)
    {
        rig.Session.SendScript = _ => new TaskCompletionSource().Task;
        rig.Session.InterruptScript = () =>
        {
            if (onInterrupt is { } status) rig.Session.Emit(new ProviderEvent.TurnCompleted(status, null));
            return Task.CompletedTask;
        };
    }

    private static void WaitForSends(ThreadRuntimeTurnRig rig, int count) =>
        rig.H.Main.Loop.RunUntil(() => rig.Session.Inputs.Count >= count, AppModelHarness.Bound);

    private static void WaitIdle(ThreadRuntimeTurnRig rig) =>
        rig.H.Main.Loop.RunUntil(() => rig.Runtime.Phase == RuntimePhase.Idle, AppModelHarness.Bound);

    private static Task BeginFinish(ThreadRuntimeTurnRig rig, TurnStatus status, Guid? turnID = null) =>
        rig.H.Main.OnMain(() => ThreadRuntimeReflection.Call<Task>(rig.Runtime, "FinishTurn", status, turnID)!);

    private static void Settle(ThreadRuntimeTurnRig rig)
    {
        for (var round = 0; round < 40; round++)
        {
            rig.H.Main.RunUntilIdle();
            Thread.Sleep(25);
        }
    }

    // ---- the late completion (PR-RUNTIME-112) ----

    [Fact]
    [Trait("Req", "PR-RUNTIME-112")] // :2292-2294 a completion for an older turn leaves the running one alone
    public void A_completion_for_an_older_turn_leaves_the_turn_now_running_untouched()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        Hold(rig);
        _ = rig.StartTurn("work");
        WaitForSends(rig, 1);
        var older = Assert.Single(rig.Runtime.Turns).Id;
        var newer = Guid.NewGuid();
        ThreadRuntimeReflection.Set(rig.Runtime, "currentTurnID", newer);
        // The older turn's command diffs are still on their way: the guard returns before the wait on them is taken up.
        HoldASettle(rig);

        rig.H.Main.Loop.Run(() => ThreadRuntimeReflection.Call<Task>(rig.Runtime, "FinishTurn", TurnStatus.Completed, (Guid?)older)!, AppModelHarness.Bound);

        Assert.Equal(1, PendingSettles(rig));

        Assert.Equal(RuntimePhase.Running, rig.Runtime.Phase);
        Assert.Equal(newer, ThreadRuntimeReflection.Get<Guid?>(rig.Runtime, "currentTurnID"));
        Assert.Equal(TurnStatus.Running, Assert.Single(rig.Runtime.Turns).Status);
        Assert.DoesNotContain(rig.Runtime.Entries, entry => entry.Item.TimelineItemContent is TimelineItem.Content.TurnEnd);
    }

    // ---- waiting out the command diffs (PR-RUNTIME-114) ----

    private static TaskCompletionSource HoldASettle(ThreadRuntimeTurnRig rig)
    {
        var gate = new TaskCompletionSource();
        rig.H.Main.OnMain(() => ThreadRuntimeReflection.Get<Dictionary<string, TaskHandle>>(rig.Runtime, "commandSettles")!["slow"] =
            MainActor.Spawn(async () => await gate.Task));
        return gate;
    }

    private static int PendingSettles(ThreadRuntimeTurnRig rig) =>
        ThreadRuntimeReflection.Get<Dictionary<string, TaskHandle>>(rig.Runtime, "commandSettles")!.Count;

    [Fact]
    [Trait("Req", "PR-RUNTIME-114")] // :2295-2310 a completed turn waits for every settle, however long, and empties the map first
    public void A_completed_turn_waits_for_its_command_diffs_however_long_they_take()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        Hold(rig);
        _ = rig.StartTurn("work");
        WaitForSends(rig, 1);
        var gate = HoldASettle(rig);

        var finish = BeginFinish(rig, TurnStatus.Completed);
        Settle(rig);
        rig.H.Main.Advance(TimeSpan.FromSeconds(600));
        Settle(rig);

        Assert.False(finish.IsCompleted);
        Assert.Equal(RuntimePhase.Running, rig.Runtime.Phase);
        Assert.Equal(0, PendingSettles(rig));

        gate.SetResult();
        rig.H.Main.Loop.RunUntil(() => finish.IsCompleted, AppModelHarness.Bound);
        Assert.Equal(RuntimePhase.Idle, rig.Runtime.Phase);
    }

    [Theory]
    [Trait("Req", "PR-RUNTIME-114")] // :2300-2309 any other status races the settles against a three-second sleep
    [InlineData(TurnStatus.Interrupted)]
    [InlineData(TurnStatus.Failed)]
    public void A_stopped_or_failed_turn_gives_its_command_diffs_three_seconds(TurnStatus status)
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        Hold(rig);
        _ = rig.StartTurn("work");
        WaitForSends(rig, 1);
        HoldASettle(rig);

        var finish = BeginFinish(rig, status);
        rig.H.Main.Advance(TimeSpan.FromSeconds(2));
        Settle(rig);
        Assert.False(finish.IsCompleted);
        Assert.Equal(0, PendingSettles(rig));

        rig.H.Main.Advance(TimeSpan.FromSeconds(1));
        rig.H.Main.Loop.RunUntil(() => finish.IsCompleted, AppModelHarness.Bound);

        Assert.Equal(RuntimePhase.Idle, rig.Runtime.Phase);
        Assert.Equal(status, Assert.Single(rig.Runtime.Turns).Status);
    }

    // ---- settling the outstanding commands first (PR-RUNTIME-111) ----

    [Fact]
    [Trait("Req", "PR-RUNTIME-111")] // :2287-2291 every outstanding command is settled before the turn's files are counted
    public void A_command_still_open_when_the_turn_ends_has_its_changes_counted_in_the_turn()
    {
        AppModelGitRepo? repo = null;
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude,
            projectFactory: h => (repo = AppModelGitRepo.WithCommit(h.NewFolder("repo"))).Root);
        rig.Send("append a line");
        var committedTree = repo!.Git("rev-parse", "HEAD^{tree}").Trim();
        rig.H.Main.OnMain(() =>
        {
            // treeBeforeCommand takes the last snapshot when none is settled (ThreadRuntime.swift:3399-3404).
            ThreadRuntimeReflection.Set(rig.Runtime, "latestTree", committedTree);
            rig.Runtime.Rehearse(new ProviderEvent.ToolStarted("command-1", new ToolCall
            {
                ToolCallKind = ToolCall.Kind.Command,
                Title = "printf 'after\\n' >> a.txt",
            }));
        });
        File.WriteAllText(Path.Combine(repo.Root, "a.txt"), "a\nafter\n");

        rig.Complete();

        Assert.Equal(["a.txt"], Assert.Single(rig.Runtime.Turns).TouchedPaths!);
    }

    // ---- what continues after a turn (PR-RUNTIME-124) ----

    [Theory]
    [Trait("Req", "PR-RUNTIME-124")] // :2407-2411 branch 1: a Return-captured pending send jumps the queue, whatever the status
    [InlineData(TurnStatus.Interrupted)]
    [InlineData(TurnStatus.Completed)]
    public void A_message_captured_by_return_is_sent_before_anything_queued(TurnStatus status)
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        Hold(rig, onInterrupt: status);
        _ = rig.StartTurn("work");
        WaitForSends(rig, 1);
        rig.H.Main.OnMain(() =>
        {
            rig.Runtime.EnqueueFollowUp("queued", []);
            rig.Runtime.Draft = new ComposerDraft { Text = "now" };
            rig.Session.SendScript = null;
            rig.Runtime.InterruptAndSend();
        });

        WaitForSends(rig, 2);

        Assert.Equal("now", rig.Session.Inputs[1].Text);
        Assert.Equal("queued", Assert.Single(rig.Runtime.FollowUps).Text);
        Assert.Equal(2, rig.Session.Inputs.Count);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-124")] // :2424-2427 branch 4: a completed turn drains the follow-up queue
    public void A_completed_turn_goes_on_to_the_next_queued_message()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        rig.Send("work");
        rig.H.Main.OnMain(() => rig.Runtime.EnqueueFollowUp("queued", []));

        rig.Complete();
        WaitForSends(rig, 2);

        Assert.Equal("queued", rig.Session.Inputs[1].Text);
        Assert.Empty(rig.Runtime.FollowUps);
    }

    [Theory]
    [Trait("Req", "PR-RUNTIME-124")] // :2427 drainFollowUps only ever starts a turn after a completed turn: a stopped or failed one leaves the queue for the user
    [InlineData(TurnStatus.Interrupted)]
    [InlineData(TurnStatus.Failed)]
    public void A_turn_that_did_not_complete_leaves_the_queued_messages_for_the_user(TurnStatus status)
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        rig.Send("work");
        rig.H.Main.OnMain(() => rig.Runtime.EnqueueFollowUp("queued", []));

        rig.Complete(status);
        Settle(rig);

        Assert.Single(rig.Session.Inputs);
        Assert.Equal("queued", Assert.Single(rig.Runtime.FollowUps).Text);
        Assert.Equal(RuntimePhase.Idle, rig.Runtime.Phase);
    }

    private static ThreadRuntimeTurnRig HydraLead() =>
        new(ProviderKind.Claude, hydraOn: true, settings: app =>
        {
            app.Settings.HydraIsolateHeads = false;
            app.Settings.HydraAlwaysHeads = false;
            app.Settings.HydraQueueHeads = false;
        });

    [Fact]
    [Trait("Req", "PR-RUNTIME-124")] // :2412-2414 branch 2: a completed reply with a delegation block is dealt with before the queue is drained
    public void A_completed_reply_with_an_unreadable_delegation_block_is_answered_before_the_queue()
    {
        using var rig = HydraLead();
        rig.Send("work");
        Guid queuedID = default;
        rig.H.Main.OnMain(() =>
        {
            rig.Runtime.Rehearse(new ProviderEvent.MessageCompleted("reply-1", "Plan.\n\n```hydra\nthis is not json\n```"));
            rig.Runtime.EnqueueFollowUp("queued", []);
            queuedID = rig.Runtime.FollowUps.Single().Id;
        });

        rig.Complete();
        WaitForSends(rig, 2);

        Assert.Contains("could not be read", rig.Session.Inputs[1].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("queued", rig.Session.Inputs[1].Text, StringComparison.Ordinal);
        // Untouched: a prompt that had been taken and put back would carry a new id.
        Assert.Equal(queuedID, Assert.Single(rig.Runtime.FollowUps).Id);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-124")] // :2415-2417 branch 3: a completed turn with head reports waiting sends them before the queue
    public void A_completed_turn_sends_waiting_head_reports_before_the_queue()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        rig.Send("work");
        rig.H.Main.OnMain(() =>
        {
            ThreadRuntimeReflection.Get<List<HydraReport>>(rig.Runtime, "hydraPendingReports")!.Add(new HydraReport
            {
                HeadIndex = 1,
                Task = "the task",
                Origin = HydraHeadInfo.Origin.Delegated,
                Status = HydraHeadInfo.Status.Completed,
                Text = "head says it is done",
            });
            rig.Runtime.EnqueueFollowUp("queued", []);
        });

        rig.Complete();
        WaitForSends(rig, 2);

        Assert.Contains("head says it is done", rig.Session.Inputs[1].Text, StringComparison.Ordinal);
        Assert.Equal("queued", Assert.Single(rig.Runtime.FollowUps).Text);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-124")] // :2418-2427 branch 4: a turn that did not complete drops the delegation block from its reply, then leaves the queue
    public void A_stopped_turn_drops_its_delegation_block_and_leaves_the_queue_alone()
    {
        using var rig = HydraLead();
        rig.Send("work");
        rig.H.Main.OnMain(() =>
        {
            rig.Runtime.Rehearse(new ProviderEvent.MessageCompleted("reply-1", "Plan.\n\n```hydra\n[{\"task\": \"a\"}]\n```"));
            rig.Runtime.EnqueueFollowUp("queued", []);
        });

        rig.Complete(TurnStatus.Interrupted);
        Settle(rig);

        var reply = rig.Runtime.Entries.Select(entry => entry.Item.TimelineItemContent).OfType<TimelineItem.Content.Assistant>().Single().Value.Text;
        Assert.DoesNotContain("```hydra", reply, StringComparison.Ordinal);
        Assert.Contains(rig.Runtime.Entries.Select(entry => entry.Item.TimelineItemContent).OfType<TimelineItem.Content.User>(),
            user => user.Value.Text.Contains("Hydra sent no heads: the turn was stopped before they could go out.", StringComparison.Ordinal));
        Assert.Single(rig.Session.Inputs);
        Assert.Equal("queued", Assert.Single(rig.Runtime.FollowUps).Text);
    }
}
