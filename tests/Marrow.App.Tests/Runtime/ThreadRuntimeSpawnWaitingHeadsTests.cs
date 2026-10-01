using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Runtime;

// spawnWaitingHeads(launch:) (App/Runtime/ThreadRuntime.swift:3017-3040, B3): delegated tasks wait for the pair's cap, and go
// out in block order as it makes room, each pinned to the roster name the lead announced. The lead is a Hydra lead with no
// pair unless a test adds one; heads are made as real head records (their sessions do not start: the loop is not pumped).
public sealed class ThreadRuntimeSpawnWaitingHeadsTests
{
    private static HydraDelegation Delegation(string task, string? name = null) =>
        new() { Task = task, Prompt = "Prompt of " + task, Name = name };

    private static WaitingTask Waiting(Guid batch, DateTimeOffset at, string task, string? name = null) =>
        new(Delegation(task, name), batch, at);

    private static HydraPair UnavailablePair() =>
        new HydraPair(ProviderKind.Codex) with { WorkerProvider = ProviderKind.Claude };

    private static string FallbackNote(ThreadRuntimeDelegationRig rig, HydraPair pair) =>
        rig.OnMain(() => rig.App.HydraHeadsFallbackNote(pair))!;

    [Fact]
    [Trait("Req", "PR-RUNTIME-233")] // :3030-3040 a cap of two with four waiting tasks sends two and leaves two, in block order
    [Trait("Req", "PR-RUNTIME-258")] // :3039 the new head's record gets the queuedAt the task was stamped with, not the time it went out
    public void A_cap_of_two_with_four_waiting_tasks_sends_the_first_two_in_order_and_stamps_their_queued_time()
    {
        using var rig = new ThreadRuntimeDelegationRig(maxHeads: 2);
        var batch = Guid.NewGuid();
        var queued = rig.OnMain(() => SwiftDate.Now);
        rig.SetBatch(batch, []);
        rig.SetWaiting(Enumerable.Range(1, 4).Select(index => Waiting(batch, queued.AddSeconds(index), "Task " + index)));
        rig.H.Main.Advance(TimeSpan.FromMinutes(5)); // long after: the head must carry the queue time, not now

        rig.SpawnWaitingHeads();

        var heads = rig.Heads;
        Assert.Equal(["Task 1", "Task 2"], heads.Select(head => head.Hydra!.Task));
        Assert.All(heads, head => Assert.Equal(HydraHeadInfo.Origin.Delegated, head.Hydra!.HydraHeadInfoOrigin));
        Assert.All(heads, head => Assert.Equal(batch, head.Hydra!.BatchID));
        Assert.Equal(queued.AddSeconds(1), heads[0].Hydra!.QueuedAt);
        Assert.Equal(queued.AddSeconds(2), heads[1].Hydra!.QueuedAt);
        Assert.Equal(["Task 3", "Task 4"], rig.Waiting.Select(task => task.Delegation.Task));
        Assert.Equal(2, rig.OnMain(() => rig.Runtime.HydraWaitingCount));
        Assert.Equal(heads.Select(head => head.Id).OrderBy(id => id), Assert.Single(rig.Batches).Pending.OrderBy(id => id));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-233")] // :3030 launch?.hasRoom(...) ?? true: a thread with no launch at all sends every waiting task, with no cap
    public void With_no_launch_at_all_every_waiting_task_goes_out_regardless_of_the_cap()
    {
        using var rig = new ThreadRuntimeDelegationRig(maxHeads: 1, busyHeads: 1);
        rig.SetLeadHydra(false); // no Hydra launch for this chat
        var batch = Guid.NewGuid();
        var now = rig.OnMain(() => SwiftDate.Now);
        rig.SetWaiting(Enumerable.Range(1, 3).Select(index => Waiting(batch, now, "Task " + index)));

        rig.SpawnWaitingHeads();

        Assert.Equal(["Task 1", "Task 2", "Task 3"], rig.Heads.Select(head => head.Hydra!.Task));
        Assert.Empty(rig.Waiting);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-233")] // :3030 the room test: with no room nothing leaves the queue
    public void A_full_pair_leaves_every_task_waiting()
    {
        using var rig = new ThreadRuntimeDelegationRig(maxHeads: 1, busyHeads: 1);
        var batch = Guid.NewGuid();
        var now = rig.OnMain(() => SwiftDate.Now);
        rig.SetWaiting(Enumerable.Range(1, 3).Select(index => Waiting(batch, now, "Task " + index)));

        rig.SpawnWaitingHeads();

        Assert.Empty(rig.Heads);
        Assert.Equal(3, rig.Waiting.Length);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-233")] // :3034-3036 the lead's announced name pins the head to that roster slot; an unknown or taken name does not
    public void An_announced_roster_name_pins_the_head_to_its_slot_and_an_unknown_or_taken_name_does_not()
    {
        using var rig = new ThreadRuntimeDelegationRig();
        var batch = Guid.NewGuid();
        var now = rig.OnMain(() => SwiftDate.Now);
        rig.SetWaiting([
            Waiting(batch, now, "Pinned", "Ada"),
            Waiting(batch, now, "Taken", "Ada"),
            Waiting(batch, now, "Unknown", "Nobody"),
            Waiting(batch, now, "Unnamed"),
        ]);

        rig.SpawnWaitingHeads();

        var heads = rig.Heads.ToDictionary(head => head.Hydra!.Task);
        Assert.Equal(HydraRoster.Index("Ada"), heads["Pinned"].Hydra!.Index);
        Assert.NotEqual(HydraRoster.Index("Ada"), heads["Taken"].Hydra!.Index);
        Assert.NotEqual(HydraRoster.Index("Ada"), heads["Unknown"].Hydra!.Index);
        Assert.NotEqual(HydraRoster.Index("Ada"), heads["Unnamed"].Hydra!.Index);
        Assert.Equal(4, heads.Values.Select(head => head.Hydra!.Index).Distinct().Count());
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-233")] // :3034-3039 the brief closure builds HydraPrompts.delegatedHeadPrompt for that task's delegation
    public void A_spawned_head_is_briefed_with_its_own_tasks_prompt()
    {
        using var rig = new ThreadRuntimeDelegationRig(maxHeads: 2);
        var batch = Guid.NewGuid();
        var now = rig.OnMain(() => SwiftDate.Now);
        rig.SetWaiting([Waiting(batch, now, "Alpha"), Waiting(batch, now, "Beta")]);

        rig.SpawnWaitingHeads();

        foreach (var head in rig.Heads)
        {
            var brief = rig.Brief(head);
            Assert.Contains("Prompt of " + head.Hydra!.Task, brief, StringComparison.Ordinal);
            Assert.DoesNotContain("Prompt of " + (head.Hydra.Task == "Alpha" ? "Beta" : "Alpha"), brief, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-233")] // :3019 it returns at once on an empty queue
    [Trait("Req", "PR-RUNTIME-232")] // :3019-3023 and the fallback note is not written for an empty queue
    public void An_empty_queue_does_nothing_not_even_the_fallback_note()
    {
        using var rig = new ThreadRuntimeDelegationRig();
        rig.UsePair(UnavailablePair());

        rig.SpawnWaitingHeads();

        Assert.Empty(rig.Notes());
        Assert.Empty(rig.Heads);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-232")] // :3023-3027 the note is "Hydra: " + the pair's note, once per pair, before the loop and whether or not any head goes out
    [Trait("Req", "UI-RUNTIME-025")] // :3023-3027 exactly "Hydra: " followed by hydraHeadsFallbackNote
    public void A_pair_whose_heads_cannot_run_on_its_own_provider_says_so_once_per_pair()
    {
        using var rig = new ThreadRuntimeDelegationRig(maxHeads: 1, busyHeads: 1); // no room: no head goes out at all
        var pair = UnavailablePair();
        rig.UsePair(pair);
        var note = FallbackNote(rig, pair);
        Assert.Contains("Claude", note, StringComparison.Ordinal);
        var batch = Guid.NewGuid();
        var now = rig.OnMain(() => SwiftDate.Now);
        rig.SetWaiting([Waiting(batch, now, "Task 1")]);

        rig.SpawnWaitingHeads();
        rig.SpawnWaitingHeads(); // a second round on the same pair

        Assert.Equal("Hydra: " + note, Assert.Single(rig.Notes()));
        Assert.Equal(pair.Id, rig.OnMain(() => ThreadRuntimeReflection.Get<Guid?>(rig.Runtime, "hydraFallbackNoted")));
        Assert.Empty(rig.Heads);

        var other = UnavailablePair(); // switching the chat to a different pair arms it again
        rig.UsePair(other);
        rig.SpawnWaitingHeads();

        Assert.Equal(["Hydra: " + note, "Hydra: " + FallbackNote(rig, other)], rig.Notes());
        Assert.Equal(other.Id, rig.OnMain(() => ThreadRuntimeReflection.Get<Guid?>(rig.Runtime, "hydraFallbackNoted")));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-232")] // :3023 no pair, or a pair whose heads run where it says, writes nothing
    public void A_chat_with_no_pair_writes_no_fallback_note()
    {
        using var rig = new ThreadRuntimeDelegationRig(maxHeads: 1, busyHeads: 1);
        var batch = Guid.NewGuid();
        rig.SetWaiting([Waiting(batch, rig.OnMain(() => SwiftDate.Now), "Task 1")]);

        rig.SpawnWaitingHeads();

        Assert.Empty(rig.Notes());
    }
}
