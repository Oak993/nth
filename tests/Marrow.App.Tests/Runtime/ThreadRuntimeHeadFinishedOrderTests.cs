using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Runtime;

// A Droppy-run head finishes (App/Runtime/ThreadRuntime.swift:2711-2735, hydraHeadFinished, B3) and the batches and report
// turn it leads to (settleBatches :2737-2743, startHydraReportTurn :2772-2788). Heads finish through the app's own
// finishHydraHead, which records the finish and tells the lead runtime. The lead is a Hydra lead with no pair.
public sealed class ThreadRuntimeHeadFinishedOrderTests
{
    private const string Prompt = "Rename the helper in Parser.cs and update every one of its callers across the whole solution";

    private static HydraDelegation Delegation(string task) => new() { Task = task, Prompt = "Prompt of " + task };

    private static long Token(ThreadRuntimeDelegationRig rig) => rig.OnMain(() => rig.Runtime.AutoStartedTurnToken);

    private static bool FlushScheduled(ThreadRuntimeDelegationRig rig) =>
        rig.OnMain(() => ThreadRuntimeReflection.Get<bool>(rig.Runtime, "hydraFlushScheduled"));

    /// <summary>What the lead runtime held the moment hydraHeadFinished returned, before anything it posted could run (the
    /// report turn and the follow-up turn are posted tasks, and the test loop drains those when the call that made them ends).</summary>

    private static HeadFinishSnapshot Finish(ThreadRuntimeDelegationRig rig, ChatThread head, TurnStatus status = TurnStatus.Completed) =>
        rig.OnMain(() =>
        {
            rig.App.FinishHydraHead(head.Id, status, "all done");
            return new HeadFinishSnapshot(
                ThreadRuntimeReflection.Get<bool>(rig.Runtime, "hydraFlushScheduled"),
                ThreadRuntimeReflection.Get<bool>(rig.Runtime, "followUpFlushScheduled"),
                rig.Runtime.AutoStartedTurnToken,
                ThreadRuntimeReflection.Get<List<HydraReport>>(rig.Runtime, "hydraPendingReports")!.Count,
                ((System.Collections.IDictionary)ThreadRuntimeReflection.Get<object>(rig.Runtime, "hydraBatches")!).Count);
        });

    // ---- PR-RUNTIME-202: settleBatches ----

    [Fact]
    [Trait("Req", "PR-RUNTIME-202")] // :2737-2743 a batch settles only with no head pending AND nothing of it waiting; reports go over sorted by headIndex
    public void A_batch_hands_its_reports_over_only_when_every_head_is_back_and_nothing_of_it_still_waits()
    {
        using var rig = new ThreadRuntimeDelegationRig();
        var (back, queued, working, alsoBack) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        rig.SetBatch(back, [], [ThreadRuntimeDelegationRig.Report(3), ThreadRuntimeDelegationRig.Report(1), ThreadRuntimeDelegationRig.Report(2)]);
        rig.SetBatch(queued, [], [ThreadRuntimeDelegationRig.Report(5)]); // every head back, but a task of it still waits
        rig.SetBatch(working, [Guid.NewGuid()], [ThreadRuntimeDelegationRig.Report(6)]); // a head still out
        rig.SetBatch(alsoBack, [], [ThreadRuntimeDelegationRig.Report(9), ThreadRuntimeDelegationRig.Report(4)]);
        rig.SetWaiting([new WaitingTask(Delegation("tail"), queued, rig.OnMain(() => SwiftDate.Now))]);

        rig.OnMain(() => ThreadRuntimeReflection.Call<object>(rig.Runtime, "SettleBatches"));

        // Several batches settle in one call; the order within one batch is by headIndex ascending.
        Assert.Equal(new HashSet<Guid> { queued, working }, rig.Batches.Select(batch => batch.Id).ToHashSet());
        var handed = rig.PendingReports.Select(report => report.HeadIndex).ToArray();
        Assert.Equal(5, handed.Length);
        Assert.Equal([1L, 2L, 3L], handed.Where(index => index <= 3).ToArray());
        Assert.Equal([4L, 9L], handed.Where(index => index is 4 or 9).ToArray());
        Assert.DoesNotContain(5L, handed);
        Assert.DoesNotContain(6L, handed);

        // Once the tail goes out the same batch settles.
        rig.SetWaiting([]);
        rig.OnMain(() => ThreadRuntimeReflection.Call<object>(rig.Runtime, "SettleBatches"));
        Assert.Equal([working], rig.Batches.Select(batch => batch.Id));
        Assert.Contains(5L, rig.PendingReports.Select(report => report.HeadIndex));
    }

    // ---- PR-RUNTIME-205: the heads-report turn's gates ----

    // Called from inside the main loop (Loop.Run), as the posted task is.
    private static Task StartReportTurn(ThreadRuntimeDelegationRig rig) =>
        ThreadRuntimeReflection.Call<Task>(rig.Runtime, "StartHydraReportTurn")!;

    [Fact]
    [Trait("Req", "PR-RUNTIME-205")] // :2774 the lead is not idle (the user got a word in first): no turn, the reports wait and the flag is cleared
    public void A_lead_that_is_not_idle_keeps_the_reports_and_starts_no_turn()
    {
        using var rig = new ThreadRuntimeDelegationRig();
        rig.BeginTurn(); // the user's turn is running
        rig.SetPendingReports([ThreadRuntimeDelegationRig.Report(1)]);
        rig.OnMain(() => ThreadRuntimeReflection.Set(rig.Runtime, "hydraFlushScheduled", true));
        var turns = rig.OnMain(() => rig.Runtime.Turns.Count);

        rig.H.Main.Loop.Run(() => StartReportTurn(rig), AppModelHarness.Bound);

        Assert.False(FlushScheduled(rig)); // cleared first, so the next head to finish can ask again
        Assert.Single(rig.PendingReports);
        Assert.Equal(turns, rig.OnMain(() => rig.Runtime.Turns.Count));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-205")] // :2774 a lead that is reverting hears nothing yet
    public void A_lead_that_is_reverting_keeps_the_reports_and_starts_no_turn()
    {
        using var rig = new ThreadRuntimeDelegationRig();
        rig.SetPendingReports([ThreadRuntimeDelegationRig.Report(1)]);
        rig.OnMain(() => ThreadRuntimeReflection.Set(rig.Runtime, "isReverting", true));
        rig.OnMain(() => ThreadRuntimeReflection.Set(rig.Runtime, "hydraFlushScheduled", true));

        rig.H.Main.Loop.Run(() => StartReportTurn(rig), AppModelHarness.Bound);

        Assert.False(FlushScheduled(rig));
        Assert.Single(rig.PendingReports);
        Assert.Empty(rig.OnMain(() => rig.Runtime.Turns));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-205")] // :2775-2776 with nothing to report no turn starts
    public void With_no_report_waiting_no_turn_starts()
    {
        using var rig = new ThreadRuntimeDelegationRig();
        rig.OnMain(() => ThreadRuntimeReflection.Set(rig.Runtime, "hydraFlushScheduled", true));

        rig.H.Main.Loop.Run(() => StartReportTurn(rig), AppModelHarness.Bound);

        Assert.False(FlushScheduled(rig));
        Assert.Empty(rig.OnMain(() => rig.Runtime.Turns));
        Assert.Empty(rig.OnMain(() => rig.Runtime.Entries));
    }

    // ---- PR-RUNTIME-236: what a finished head does, in order ----

    [Fact]
    [Trait("Req", "PR-RUNTIME-236")] // :2723-2725 step 1: a waiting task takes the freed slot BEFORE a queued user follow-up does
    public void A_task_waiting_in_a_batch_takes_the_freed_slot_before_a_queued_follow_up()
    {
        using var rig = new ThreadRuntimeDelegationRig(maxHeads: 1);
        var batch = Guid.NewGuid();
        var working = rig.OnMain(() => rig.App.SpawnDroppyHead(rig.Lead.Id, "Working", HydraHeadInfo.Origin.Delegated, [], batch,
            null, null, null, (persona, workplace) => "brief"))!;
        rig.SetBatch(batch, [working.Id]);
        rig.SetWaiting([new WaitingTask(Delegation("Waiting"), batch, rig.OnMain(() => SwiftDate.Now))]);
        rig.BeginTurn(); // the lead works, so the user's message is queued behind it
        rig.OnMain(() => rig.Runtime.EnqueueFollowUp(Prompt, []));
        Assert.Equal(Prompt, Assert.Single(rig.OnMain(() => rig.Runtime.FollowUps)).Text); // the pair is full: it stays a follow-up

        Finish(rig, working);

        Assert.Equal(["Working", "Waiting"], rig.Heads.Select(head => head.Hydra!.Task));
        Assert.Empty(rig.Waiting);
        Assert.Equal(Prompt, Assert.Single(rig.OnMain(() => rig.Runtime.FollowUps)).Text); // no room was left for it
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-236")] // :2726 step 2: dispatchQueuedFollowUps hands a queued follow-up to the freed slot while the lead works
    public void A_queued_follow_up_takes_the_freed_slot_while_the_lead_is_working()
    {
        using var rig = new ThreadRuntimeDelegationRig(maxHeads: 1);
        var working = rig.OnMain(() => rig.App.SpawnDroppyHead(rig.Lead.Id, "Working", HydraHeadInfo.Origin.Sent, [], null,
            null, null, null, (persona, workplace) => "brief"))!;
        rig.BeginTurn();
        rig.OnMain(() => rig.Runtime.EnqueueFollowUp(Prompt, []));
        Assert.Single(rig.OnMain(() => rig.Runtime.FollowUps));

        Finish(rig, working);

        Assert.Single(rig.Heads, head => head.Hydra!.HydraHeadInfoOrigin == HydraHeadInfo.Origin.Queued);
        Assert.Empty(rig.OnMain(() => rig.Runtime.FollowUps));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-236")] // :2728 step 3 then 4: a batch whose last head is back settles BEFORE the report flush, so the reports go out in the same breath
    public void A_batch_that_settles_is_flushed_to_an_idle_lead_in_the_same_call()
    {
        using var rig = new ThreadRuntimeDelegationRig();
        var batch = Guid.NewGuid();
        var head = rig.OnMain(() => rig.App.SpawnDroppyHead(rig.Lead.Id, "Only", HydraHeadInfo.Origin.Delegated, [], batch,
            null, null, null, (persona, workplace) => "brief"))!;
        rig.SetBatch(batch, [head.Id]);
        var token = Token(rig);

        var after = Finish(rig, head);

        Assert.Equal(0, after.Batches); // settled
        Assert.Equal(1, after.PendingReports); // its report handed over...
        Assert.True(after.FlushScheduled); // ...and the idle lead was asked to hear it in the same call
        Assert.Equal(token + 1, after.Token);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-236")] // :2730 step 4: an idle lead the user did not stop hears at once
    public void An_idle_lead_hears_a_head_at_once_unless_the_user_stopped_it_or_it_is_working()
    {
        // Idle and not stopped: flushed.
        using var idle = new ThreadRuntimeDelegationRig();
        var head = idle.OnMain(() => idle.App.SpawnDroppyHead(idle.Lead.Id, "A", HydraHeadInfo.Origin.Sent, [], null,
            null, null, null, (persona, workplace) => "brief"))!;
        var token = Token(idle);
        var flushed = Finish(idle, head);
        Assert.True(flushed.FlushScheduled);
        Assert.Equal(token + 1, flushed.Token);

        // The user stopped the lead: the report waits for their next word.
        using var stopped = new ThreadRuntimeDelegationRig();
        stopped.OnMain(() => stopped.App.UpdateThread(stopped.Lead.Id, thread => thread with { LastStatus = TurnStatus.Interrupted }));
        head = stopped.OnMain(() => stopped.App.SpawnDroppyHead(stopped.Lead.Id, "A", HydraHeadInfo.Origin.Sent, [], null,
            null, null, null, (persona, workplace) => "brief"))!;
        token = Token(stopped);
        var waited = Finish(stopped, head);
        Assert.False(waited.FlushScheduled);
        Assert.Equal(token, waited.Token);
        Assert.Equal(1, waited.PendingReports);

        // The lead is still working: the report waits for the end of that turn.
        using var working = new ThreadRuntimeDelegationRig();
        head = working.OnMain(() => working.App.SpawnDroppyHead(working.Lead.Id, "A", HydraHeadInfo.Origin.Sent, [], null,
            null, null, null, (persona, workplace) => "brief"))!;
        working.BeginTurn();
        token = Token(working);
        var busy = Finish(working, head);
        Assert.False(busy.FlushScheduled);
        Assert.Equal(token, busy.Token);
        Assert.Equal(1, busy.PendingReports);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-236")] // :2733 step 5 after step 4: with reports and a follow-up both waiting behind an idle lead the reports go first
    public void Reports_go_to_an_idle_lead_before_a_follow_up_that_waits_behind_it()
    {
        using var rig = new ThreadRuntimeDelegationRig();
        var head = rig.OnMain(() => rig.App.SpawnDroppyHead(rig.Lead.Id, "A", HydraHeadInfo.Origin.Sent, [], null,
            null, null, null, (persona, workplace) => "brief"))!;
        // Queued behind the idle lead directly: enqueueFollowUp would take it at once (an idle lead never waits on its queue).
        rig.OnMain(() => typeof(ThreadRuntime).GetProperty("FollowUps")!.SetValue(rig.Runtime,
            new ValueList<FollowUpPrompt>([new FollowUpPrompt { Text = "A message the user queued" }])));

        var after = Finish(rig, head);
        Assert.True(after.FlushScheduled);
        Assert.True(after.FollowUpFlushScheduled); // step 5 ran too, behind step 4

        var firstUser = rig.OnMain(() => rig.Runtime.Entries.Select(entry => entry.Item.TimelineItemContent).OfType<TimelineItem.Content.User>().First().Value);
        Assert.NotNull(firstUser.HydraHeads);
        Assert.NotEmpty(firstUser.HydraHeads!); // the first turn the lead took was the heads' report
        Assert.NotEqual("A message the user queued", firstUser.Text);
    }
}
