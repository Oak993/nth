using System.Reflection;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Testing;

namespace Marrow.App.Tests.Runtime;

// ThreadRuntime.dispatchQueuedFollowUps() (App/Runtime/ThreadRuntime.swift:1041-1053), called when a Droppy head reports
// (:2727-2728): follow-ups queued while every head was busy go out as queued heads once one frees up, while the lead works.
public sealed class ThreadRuntimeQueuedFollowUpTests
{
    private const string Prompt = "Rename the helper in Parser.cs and update every one of its callers across the whole solution";

    private static ThreadRuntimeDispatchHeadRig MakeRig()
    {
        var harness = new AppModelHarness();
        var project = AppModelSamples.Project("Project", harness.NewFolder("project"));
        var lead = AppModelSamples.Thread(project) with { HydraEnabled = true };
        harness.WriteLibrary([project], [lead]);
        var model = harness.MakeModel();
        harness.Main.OnMain(() =>
        {
            model.Settings.HydraEnabled = true;
            model.Settings.HydraIsolateHeads = false;
            model.Settings.HydraQueueHeads = true;
            model.Settings.HydraMaxHeads = 1;
        });
        harness.PrepareBootstrap(model);
        var runtime = harness.Main.OnMain(() => model.Runtime(lead.Id));
        harness.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        return new ThreadRuntimeDispatchHeadRig(harness, model, runtime, lead);
    }

    // A working lead whose one head slot is taken, with the prompt queued behind it.
    private static ChatThread QueueBehindABusyHead(ThreadRuntimeDispatchHeadRig rig)
    {
        rig.SetPhase(RuntimePhase.Running);
        Assert.True(rig.Dispatch(new FollowUpPrompt { Text = "First task" }, HydraHeadInfo.Origin.Queued));
        rig.Harness.Main.OnMain(() => rig.Runtime.EnqueueFollowUp(Prompt, []));
        rig.Harness.Main.OnMain(() => Assert.Equal(Prompt, Assert.Single(rig.Runtime.FollowUps).Text));
        return Assert.Single(rig.Heads);
    }

    private static void DispatchQueued(ThreadRuntimeDispatchHeadRig rig) => rig.Harness.Main.OnMain(() =>
        typeof(ThreadRuntime).GetMethod("DispatchQueuedFollowUps", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(rig.Runtime, []));

    [Fact]
    [Trait("Req", "PR-RUNTIME-238")] // ThreadRuntime.swift:2725-2728, 1043-1052
    public void When_a_head_reports_to_a_working_lead_the_queued_follow_up_goes_out_as_a_queued_head()
    {
        using var rig = MakeRig();
        var first = QueueBehindABusyHead(rig);
        rig.Harness.Main.OnMain(() => rig.Model.FinishHydraHead(first.Id, TurnStatus.Completed, "Done."));
        rig.Harness.Main.OnMain(() =>
        {
            Assert.Empty(rig.Runtime.FollowUps); // :1048 takeBundle removed it
            Assert.Equal(RuntimePhase.Running, rig.Runtime.Phase); // the lead keeps working; it does not take the prompt
        });
        var second = Assert.Single(rig.Heads, head => head.Id != first.Id);
        Assert.Equal(HydraHeadInfo.Origin.Queued, second.Hydra!.HydraHeadInfoOrigin); // :1281 dispatchQueuedHead
        Assert.Contains("## Your task\n" + Prompt + "\n", rig.Brief(second), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-238")] // ThreadRuntime.swift:1047: guard dispatchQueuedHead(merged) else { break }
    public void With_every_head_still_busy_the_follow_up_stays_queued()
    {
        using var rig = MakeRig();
        QueueBehindABusyHead(rig);
        DispatchQueued(rig);
        rig.Harness.Main.OnMain(() => Assert.Equal(Prompt, Assert.Single(rig.Runtime.FollowUps).Text));
        Assert.Single(rig.Heads);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-238")] // ThreadRuntime.swift:1044: guard phase != .idle else { return }
    public void An_idle_lead_keeps_its_queue_for_itself_even_with_a_head_slot_free()
    {
        using var rig = MakeRig();
        var first = QueueBehindABusyHead(rig);
        // Free the slot without the report path, then idle the lead: the queue is the lead's to take, in order.
        rig.Harness.Main.OnMain(() => rig.Model.UpdateThread(first.Id, thread => thread with
        {
            Hydra = thread.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Completed },
        }));
        rig.SetPhase(RuntimePhase.Idle);
        DispatchQueued(rig);
        rig.Harness.Main.OnMain(() => Assert.Equal(Prompt, Assert.Single(rig.Runtime.FollowUps).Text));
        Assert.Single(rig.Heads);
    }
}
