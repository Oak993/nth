using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Xunit;

namespace Marrow.App.Tests.Port;

[Collection("MCPHub processes")]
public sealed class C7InstallerQueueReviewTests
{
    // ED-NEW-inst-c7-5: provider completion may arrive during a hold; do not remove its waiting delegation.
    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_held_spawn_keeps_the_actual_waiting_head_queue_before_removing_an_entry()
    {
        using var shell = new C7OfflineShell();
        using var rig = new ThreadRuntimeDelegationRig(maxHeads: 2);
        var batch = Guid.NewGuid();
        var waiting = new WaitingTask(new HydraDelegation { Task = "A", Prompt = "Do A" }, batch, SwiftDate.Now);
        rig.SetBatch(batch, []);
        rig.SetWaiting([waiting]);
        rig.OnMain(() =>
        {
            var recorder = new ShellRecorder();
            var host = new NthControlHost(new AppShell(new AppDelegate { Model = rig.App }, recorder.Exit, recorder.AfterWillTerminate),
                processId: 4242, executable: @"C:\Apps\nth\current\nth.exe");
            Assert.Equal(NthControlReply.Ok, host.Answer(NthControlRequest.QuitIfIdle with { Id = Guid.NewGuid().ToString("N") }).Response.Reply);
            using var captured = new CapturedInstallerExecutor(MainActor.Executor.Current);
            using var scope = MainActor.Executor.Override(captured);
            ThreadRuntimeReflection.Call<object>(rig.Runtime, "SpawnWaitingHeads", new object?[] { null });
            Assert.Equal(1, rig.Runtime.HydraWaitingCount);
            Assert.Empty(rig.App.HydraHeads(rig.Lead.Id));
            Assert.Equal(0, captured.QueuedCount);
            Assert.Empty(recorder.Calls);
        });
        Assert.Equal([waiting], rig.Waiting);
    }

    // ED-NEW-inst-c7-5: a clicked helper prompt must not replace an unsent helper draft under the hold.
    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_held_subagent_link_preserves_the_existing_helper_draft_and_records()
    {
        using var shell = new C7OfflineShell();
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("held helper"));
        var parent = AppModelSamples.Thread(project);
        var helper = AppModelSamples.Thread(project) with { ParentThreadID = parent.Id, IsInPanel = true };
        h.WriteLibrary([project], [parent, helper]);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(helper.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            var draft = new ComposerDraft { Text = "Unsent exact helper edit" };
            runtime.Draft = draft;
            var original = model.Thread(helper.Id);
            var recorder = new ShellRecorder();
            var host = new NthControlHost(new AppShell(new AppDelegate { Model = model }, recorder.Exit, recorder.AfterWillTerminate),
                processId: 4242, executable: @"C:\Apps\nth\current\nth.exe");
            Assert.Equal(NthControlReply.Ok, host.Answer(NthControlRequest.QuitIfIdle with { Id = Guid.NewGuid().ToString("N") }).Response.Reply);
            using var captured = new CapturedInstallerExecutor(MainActor.Executor.Current);
            using var scope = MainActor.Executor.Override(captured);
            model.SpawnSubagent(parent.Id, "Review", "Clicked new prompt");
            Assert.Same(draft, runtime.Draft);
            Assert.Equal(original, model.Thread(helper.Id));
            Assert.Equal(2, model.Threads.Count);
            Assert.Equal(0, captured.QueuedCount);
            Assert.Empty(recorder.Calls);
        });
    }
}
