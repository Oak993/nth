using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppModel+HydraMerge.swift (B3 52bc792), :80-117,414-423.
public sealed class AppModelHydraMergeGuardTests
{
    [Theory]
    [InlineData("setting")]
    [InlineData("thread")]
    [InlineData("hydra")]
    [InlineData("runtime")]
    [InlineData("merging")]
    [InlineData("queued")]
    [InlineData("ready")]
    [Trait("Req", "PR-HYDRAMERGE-010")] // App/AppModel+HydraMerge.swift:80-82
    public void Every_pre_guard_refuses_silently_without_creating_a_runtime(string guard)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project");
        var lead = f.AddLead(p, "Lead", ["a.txt"]);
        f.Start(lead);
        var runtime = f.Runtime(lead);
        f.H.Main.OnMain(() =>
        {
            switch (guard)
            {
                case "setting": f.Model.Settings.HydraAutoMerge = false; break;
                case "thread": f.RemoveThread(lead); break;
                case "hydra": f.Model.UpdateThread(lead.Id, t => t with { HydraEnabled = false }); break;
                case "runtime": f.RemoveRuntime(lead); break;
                case "merging": runtime.IsHydraMerging = true; break;
                case "queued": runtime.IsHydraMergeQueued = true; break;
                case "ready": runtime.HasHydraMergeRetryPending = true; typeof(ThreadRuntime).GetProperty("IsLoadingHistory")!.SetValue(runtime, true); break;
            }
        });
        var calls = new List<ShellInvocation>();
        f.Run(lead, calls.Add);
        Assert.Empty(calls);
        Assert.Empty(runtime.HydraMerges);
        Assert.Empty(runtime.Entries);
        Assert.False(runtime.Turns.Single().HydraMerged);
        if (guard == "runtime") Assert.Null(f.Model.ExistingRuntime(lead.Id));
    }

    [Theory]
    [InlineData("setting")]
    [InlineData("thread")]
    [InlineData("hydra")]
    [InlineData("runtime")]
    [InlineData("merging")]
    [InlineData("queued")]
    [InlineData("ready")]
    [Trait("Req", "PR-HYDRAMERGE-010")] // App/AppModel+HydraMerge.swift:80-95: the pre-guard decides before any wait, so a refused job never queues behind another chat's merge
    [Trait("Rule", "R-TEST-4")] // The pre-guard clauses while another merge runs; the guards without one are proved by Every_pre_guard_refuses_silently_without_creating_a_runtime and the post-wait guards below.
    public void Every_pre_guard_refuses_at_once_without_queueing_even_while_another_chat_merges(string guard)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project");
        var lead = f.AddLead(p, "Lead", ["a.txt"]);
        var busy = f.AddLead(p, "Busy", []);
        f.Start(lead, busy);
        var runtime = f.Runtime(lead);
        f.H.Main.OnMain(() =>
        {
            f.Runtime(busy).IsHydraMerging = true;
            switch (guard)
            {
                case "setting": f.Model.Settings.HydraAutoMerge = false; break;
                case "thread": f.RemoveThread(lead); break;
                case "hydra": f.Model.UpdateThread(lead.Id, t => t with { HydraEnabled = false }); break;
                case "runtime": f.RemoveRuntime(lead); break;
                case "merging": runtime.IsHydraMerging = true; break;
                case "queued": runtime.IsHydraMergeQueued = true; break;
                case "ready": typeof(ThreadRuntime).GetProperty("IsLoadingHistory")!.SetValue(runtime, true); break;
            }
        });
        var timers = f.H.Main.Time.ActiveTimers;
        Task call = null!;
        f.H.Main.OnMain(() => call = f.Model.AutoMergeHydraWork(lead.Id));
        Assert.True(call.IsCompletedSuccessfully);
        Assert.Equal(timers, f.H.Main.Time.ActiveTimers);
        Assert.Equal(guard == "queued", runtime.IsHydraMergeQueued); // the flag is left exactly as it was found
        Assert.Equal(guard == "merging", runtime.IsHydraMerging);
        Assert.Empty(runtime.HydraMerges); Assert.Empty(runtime.Entries);
        if (guard == "runtime") Assert.Null(f.Model.ExistingRuntime(lead.Id));
    }

    [Theory]
    [InlineData("setting")]
    [InlineData("thread")]
    [InlineData("hydra")]
    [InlineData("project")]
    [InlineData("runtime")]
    [InlineData("ready")]
    [Trait("Req", "PR-HYDRAMERGE-010")] // App/AppModel+HydraMerge.swift:88-100
    [Trait("Req", "PR-HYDRAMERGE-122")] // App/AppModel+HydraMerge.swift:89-95
    public void A_queued_second_trigger_returns_and_each_condition_is_read_again_after_the_400ms_poll(string guard)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project");
        var lead = f.AddLead(p, "Lead", ["a.txt"]);
        var busy = f.AddLead(p, "Busy", []);
        f.Start(lead, busy);
        var runtime = f.Runtime(lead);
        Task wait = null!, again = null!;
        f.H.Main.OnMain(() => { f.Runtime(busy).IsHydraMerging = true; wait = f.Model.AutoMergeHydraWork(lead.Id); });
        Assert.True(runtime.IsHydraMergeQueued);
        Assert.False(wait.IsCompleted);
        var timers = f.H.Main.Time.ActiveTimers;
        f.H.Main.OnMain(() => again = f.Model.AutoMergeHydraWork(lead.Id));
        Assert.True(again.IsCompletedSuccessfully);
        Assert.Equal(timers, f.H.Main.Time.ActiveTimers);
        Assert.Empty(runtime.HydraMerges); Assert.Empty(f.Notes(lead));
        f.H.Main.OnMain(() =>
        {
            switch (guard)
            {
                case "setting": f.Model.Settings.HydraAutoMerge = false; break;
                case "thread": f.RemoveThread(lead); break;
                case "hydra": f.Model.UpdateThread(lead.Id, t => t with { HydraEnabled = false }); break;
                case "project": f.Model.UpdateThread(lead.Id, t => t with { ProjectID = Guid.NewGuid() }); break;
                case "runtime": f.RemoveRuntime(lead); break;
                case "ready": typeof(ThreadRuntime).GetProperty("IsLoadingHistory")!.SetValue(runtime, true); break;
            }
            f.Runtime(busy).IsHydraMerging = false;
        });
        f.H.Main.AdvanceMilliseconds(399);
        Assert.True(runtime.IsHydraMergeQueued);
        Assert.False(wait.IsCompleted);
        f.H.Main.AdvanceMilliseconds(1);
        Assert.True(wait.IsCompletedSuccessfully);
        Assert.False(runtime.IsHydraMergeQueued);
        Assert.False(runtime.IsHydraMerging);
        Assert.Empty(runtime.HydraMerges);
        Assert.False(runtime.Turns.Single().HydraMerged);
        if (guard == "runtime") Assert.Null(f.Model.ExistingRuntime(lead.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-HYDRAMERGE-116")] // App/AppModel+HydraMerge.swift:296-304
    [Trait("Req", "PR-HYDRAMERGE-124")] // App/AppModel+HydraMerge.swift:246-248,304
    [Trait("Req", "PR-HYDRAMERGE-033")] // App/AppModel+HydraMerge.swift:296-308
    public void Resuming_during_tree_capture_defers_before_any_commit_reference_push_or_bookkeeping(bool duringCapture)
    {
        using var f = new HydraMergeReviewFixture();
        f.Edit();
        var calls = new List<ShellInvocation>();
        f.Run(call =>
        {
            calls.Add(call);
            if (HydraMergeReviewFixture.IsGit(call, duringCapture ? "write-tree" : "add"))
                typeof(ThreadRuntime).GetProperty("IsLoadingHistory")!.SetValue(f.Runtime, true);
        });
        Assert.Contains(calls, c => HydraMergeReviewFixture.IsGit(c, "write-tree"));
        Assert.DoesNotContain(calls, c => HydraMergeReviewFixture.IsGit(c, "commit-tree") || HydraMergeReviewFixture.IsGit(c, "update-ref") || HydraMergeReviewFixture.IsGit(c, "push"));
        Assert.Empty(f.Runtime.HydraMerges); Assert.Empty(f.Notes);
        Assert.False(f.Runtime.HasHydraMergeRetryPending);
        Assert.False(f.Runtime.Turns.Single().HydraMerged);
        Assert.False(f.Runtime.IsHydraMerging);
        Assert.Null(f.Runtime.HydraMergeStage); Assert.Null(f.Runtime.HydraMergeStep);
        Assert.Null(f.Runtime.HydraMergeStartedAt); Assert.Null(f.Runtime.HydraMergeNoteID);
    }

    [Theory]
    [InlineData("runtime")]
    [InlineData("queued")]
    [InlineData("ready")]
    [InlineData("busy")]
    [Trait("Req", "PR-HYDRAMERGE-126")] // App/AppModel+HydraMerge.swift:415-421
    public void Retry_is_one_per_live_chat_and_rechecks_the_current_state_after_exactly_sixty_seconds(string state)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project", origin: false);
        var lead = f.AddLead(p, "Lead", ["a.txt"]);
        f.Start(lead);
        var runtime = f.Runtime(lead);
        f.H.Main.OnMain(() =>
        {
            HydraMergeProofFixture.Call("ScheduleHydraMergeRetry", f.Model, lead.Id);
            HydraMergeProofFixture.Call("ScheduleHydraMergeRetry", f.Model, lead.Id);
            HydraMergeProofFixture.Call("ScheduleHydraMergeRetry", f.Model, Guid.NewGuid());
        });
        Assert.True(runtime.HasHydraMergeRetryPending);
        var timers = f.H.Main.Time.ActiveTimers;
        Assert.Equal(1, timers);
        f.H.Main.AdvanceMilliseconds(59_999);
        Assert.True(runtime.HasHydraMergeRetryPending);
        Assert.Empty(runtime.HydraMerges);
        f.H.Main.OnMain(() =>
        {
            switch (state)
            {
                case "runtime": f.RemoveRuntime(lead); break;
                case "queued": runtime.IsHydraMergeQueued = true; break;
                case "ready": typeof(ThreadRuntime).GetProperty("IsLoadingHistory")!.SetValue(runtime, true); break;
                case "busy": runtime.IsHydraMerging = true; break;
            }
        });
        f.H.Main.AdvanceMilliseconds(1);
        if (state != "runtime") Assert.False(runtime.HasHydraMergeRetryPending);
        else Assert.Null(f.Model.ExistingRuntime(lead.Id));
        Assert.Empty(runtime.HydraMerges);
        Assert.False(runtime.Turns.Single().HydraMerged);
        Assert.Equal(0, f.H.Main.Time.ActiveTimers);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-010")] // App/AppModel+HydraMerge.swift:96-101
    [Trait("Req", "PR-HYDRAMERGE-122")] // App/AppModel+HydraMerge.swift:88-95
    public void Of_two_waiters_only_one_starts_at_a_poll_and_the_other_waits_for_its_release()
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project", origin: false);
        File.WriteAllText(Path.Combine(p.Path, "b.txt"), "b\n");
        var busy = f.AddLead(p, "Busy", []);
        var first = f.AddLead(p, "First", ["a.txt"]);
        var second = f.AddLead(p, "Second", ["b.txt"]);
        f.Start(busy, first, second);
        var landing = new TaskCompletionSource();
        Task firstTask = null!, secondTask = null!;
        f.H.Main.OnMain(() =>
        {
            f.Runtime(busy).IsHydraMerging = true;
            f.Runtime(first).HydraLanding = MainActor.Spawn(() => landing.Task);
            firstTask = f.Model.AutoMergeHydraWork(first.Id);
            secondTask = f.Model.AutoMergeHydraWork(second.Id);
        });
        Assert.True(f.Runtime(first).IsHydraMergeQueued);
        Assert.True(f.Runtime(second).IsHydraMergeQueued);
        f.H.Main.OnMain(() => f.Runtime(busy).IsHydraMerging = false);
        f.H.Main.AdvanceMilliseconds(400);
        Assert.True(f.Runtime(first).IsHydraMerging);
        Assert.False(f.Runtime(first).IsHydraMergeQueued);
        Assert.False(f.Runtime(second).IsHydraMerging);
        Assert.True(f.Runtime(second).IsHydraMergeQueued);
        Assert.Empty(f.Runtime(first).HydraMerges);
        f.H.Main.OnMain(() => landing.SetResult());
        f.H.Main.Loop.RunUntil(() => firstTask.IsCompleted, AppModelHarness.Bound);
        Assert.False(f.Runtime(first).IsHydraMerging);
        Assert.True(f.Runtime(second).IsHydraMergeQueued);
        f.H.Main.AdvanceMilliseconds(399);
        Assert.True(f.Runtime(second).IsHydraMergeQueued);
        f.H.Main.AdvanceMilliseconds(1);
        f.H.Main.Loop.RunUntil(() => secondTask.IsCompleted, AppModelHarness.Bound);
        Assert.False(f.Runtime(second).IsHydraMergeQueued);
        Assert.False(f.Runtime(second).IsHydraMerging);
        Assert.Single(f.Runtime(first).HydraMerges);
        Assert.Single(f.Runtime(second).HydraMerges);
    }
}
