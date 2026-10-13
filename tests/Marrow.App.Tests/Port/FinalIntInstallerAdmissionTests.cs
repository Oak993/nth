using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.App.Views.Artifacts;
using Marrow.Core.Models;

namespace Marrow.App.Tests.Port;

[Collection("MCPHub processes")]
[Trait("Decision", "ED-NEW-finalint-4")]
public sealed class FinalIntInstallerAdmissionTests
{
    [Theory]
    [InlineData("mission")]
    [InlineData("side")]
    [InlineData("app")]
    public void A_held_send_refuses_before_clearing_a_draft_or_posting(string path)
    {
        using var rig = new NthControlRig("Held new entry point");
        rig.H.Main.OnMain(() =>
        {
            var runtime = rig.Runtimes[0];
            var draft = new ComposerDraft { Text = "Keep this exact draft" };
            runtime.Draft = draft;
            var answer = rig.Host.Answer(NthControlRequest.QuitIfIdle with { Id = Guid.NewGuid().ToString("N") });
            Assert.Equal(NthControlReply.Ok, answer.Response.Reply);
            using var captured = new CapturedInstallerExecutor(MainActor.Executor.Current);
            using var scope = MainActor.Executor.Override(captured);
            Assert.False(Send(runtime, path));
            Assert.Same(draft, runtime.Draft);
            Assert.Equal(0, captured.QueuedCount);
            Assert.Empty(rig.Recorder.Calls);
        });
    }

    [Theory]
    [InlineData("mission")]
    [InlineData("side")]
    [InlineData("app")]
    [InlineData("live")]
    public void An_accepted_send_is_busy_before_its_actor_post_runs(string path)
    {
        using var rig = new NthControlRig("Captured new entry point");
        rig.H.Main.OnMain(() =>
        {
            using var captured = new CapturedInstallerExecutor(MainActor.Executor.Current);
            using var scope = MainActor.Executor.Override(captured);
            Assert.True(Send(rig.Runtimes[0], path));
            Assert.Equal(1, captured.QueuedCount);
            Assert.False(rig.Runtimes[0].IsRunning);
            var answer = rig.Host.Answer(NthControlRequest.QuitIfIdle with { Id = Guid.NewGuid().ToString("N") });
            Assert.Equal(NthControlReply.Busy, answer.Response.Reply);
            Assert.Equal("Captured new entry point", Assert.Single(answer.Response.Work).Name);
            Assert.Empty(rig.Recorder.Calls);
        });
    }

    [Fact]
    public void An_artifact_dispatch_reserves_its_second_hop_before_its_drain_lease_ends()
    {
        using var rig = new NthControlRig("Captured artifact change");
        rig.H.Main.Loop.Run(() => rig.Runtimes[0].EnsureLoaded(), AppModelHarness.Bound);
        rig.H.Main.OnMain(() =>
        {
            using var captured = new CapturedInstallerExecutor(MainActor.Executor.Current);
            using var scope = MainActor.Executor.Override(captured);
            var runtime = rig.Runtimes[0];
            runtime.SendArtifactTurn("Keep this artifact change");
            Assert.Single(runtime.FollowUps);
            captured.RunUntil(() => runtime.FollowUps.Count == 0);
            Assert.Empty(runtime.FollowUps);
            Assert.Single(rig.Model.PendingInstallerTurns);
            Assert.True(captured.QueuedCount > 0);
            Assert.Empty(rig.Recorder.Calls);
        });
    }

    private static bool Send(ThreadRuntime runtime, string path)
    {
        switch (path)
        {
            case "mission": return runtime.SendMissionMessage("Mission brief");
            case "side": return runtime.SendSideChat("Side question");
            case "app": return runtime.PostAppMessage("offline-app", "Attributed app message") is null;
            case "live": runtime.SendLiveText("Reader action"); return true;
            default: throw new ArgumentOutOfRangeException(nameof(path));
        }
    }

    [Fact]
    public void The_artifact_child_keeps_the_original_request_when_a_hold_begins_after_the_drain()
    {
        using var rig = new ThreadRuntimeTurnRig();
        var quit = new object();
        rig.H.Main.OnMain(() =>
        {
            using var captured = new CapturedInstallerExecutor(MainActor.Executor.Current);
            using var scope = MainActor.Executor.Override(captured);
            var request = new TabulaPendingAsk("offline-artifact", 1, "change", rig.H.Main.Time.GetUtcNow());
            Assert.True(rig.Runtime.TrySendArtifactAsk(request, "Keep this exact artifact change"));
            captured.RunUntil(() => rig.Runtime.FollowUps.Count == 0); // Stop after the drain, before its child.
            Assert.Empty(rig.Runtime.FollowUps);
            rig.App.HoldInstallerTurns(quit);
            try
            {
                captured.RunUntil(() => rig.Runtime.Turns.Count > 0 || captured.QueuedCount == 0);
                var user = Assert.IsType<TimelineItem.Content.User>(Assert.Single(rig.Runtime.Entries).Item.TimelineItemContent).Value;
                Assert.Equal("Keep this exact artifact change", user.Text);
                var map = ThreadRuntimeReflection.Get<Dictionary<Guid, Guid>>(rig.Runtime, "artifactRequestTurnMap")!;
                Assert.Equal(request.RequestId, Assert.Single(map).Value);
                Assert.Equal(request.RequestId, TabulaPane.StateOf(rig.Runtime).PendingAsk!.RequestId);
            }
            finally { rig.App.ReleaseInstallerTurns(quit); }
        });
    }

    [Fact]
    public void A_refused_artifact_child_restores_the_original_queue_identity_once()
    {
        using var rig = new ThreadRuntimeTurnRig();
        var quit = new object();
        rig.H.Main.OnMain(() =>
        {
            using var captured = new CapturedInstallerExecutor(MainActor.Executor.Current);
            using var scope = MainActor.Executor.Override(captured);
            var request = new TabulaPendingAsk("offline-artifact", 1, "change", rig.H.Main.Time.GetUtcNow());
            Assert.True(rig.Runtime.TrySendArtifactAsk(request, "Keep this exact artifact change"));
            var prompt = ThreadRuntimeReflection.Call<FollowUpPrompt>(rig.Runtime, "TakeBundle", 0)!;
            var queuedBefore = captured.QueuedCount;
            rig.App.HoldInstallerTurns(quit);
            try
            {
                var revision = ThreadRuntimeReflection.Get<long>(rig.Runtime, "saveRevision");
                ThreadRuntimeReflection.Call<object>(rig.Runtime, "StartArtifactFollowUp", prompt);
                var restoredSave = ThreadRuntimeReflection.Get<TaskHandle>(rig.Runtime, "saveTask");
                Assert.NotNull(restoredSave);
                ThreadRuntimeReflection.Call<object>(rig.Runtime, "StartArtifactFollowUp", prompt);
                Assert.Same(restoredSave, ThreadRuntimeReflection.Get<TaskHandle>(rig.Runtime, "saveTask"));
                Assert.Equal(revision + 1, ThreadRuntimeReflection.Get<long>(rig.Runtime, "saveRevision"));
                Assert.Equal(request.RequestId, Assert.Single(rig.Runtime.FollowUps).Id);
                Assert.Equal("Keep this exact artifact change", rig.Runtime.FollowUps[0].Text);
                Assert.Null(ThreadRuntimeReflection.Get<Guid?>(rig.Runtime, "artifactDispatchPending"));
                Assert.Single(TabulaPane.StateOf(rig.Runtime).Asks);
                Assert.Equal(queuedBefore + 1, captured.QueuedCount); // Exactly one restored save, no refused child callback.
            }
            finally { rig.App.ReleaseInstallerTurns(quit); }
        });
    }
}
