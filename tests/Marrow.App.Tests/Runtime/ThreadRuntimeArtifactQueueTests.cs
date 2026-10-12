using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Services.Providers;
using Marrow.App.Views.Artifacts;

namespace Marrow.App.Tests.Runtime;

[Trait("Decision", "ED-NEW-web-nc6-1")]
public sealed class ThreadRuntimeArtifactQueueTests
{
    [Theory]
    [InlineData(TurnStatus.Completed)]
    [InlineData(TurnStatus.Failed)]
    [InlineData(TurnStatus.Interrupted)]
    public void Runtime_owned_requests_complete_and_promote_without_a_pane_or_selected_artifact(TurnStatus outcome)
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.Session.SendScript = _ => new TaskCompletionSource().Task;
        var state = TabulaPane.StateOf(rig.Runtime);
        var first = new TabulaPendingAsk("orbit", 4, "first", rig.H.Main.Time.GetUtcNow());
        var second = new TabulaPendingAsk("orbit", 4, "second", first.At);
        rig.H.Main.OnMain(() => Assert.True(rig.Runtime.TrySendArtifactAsk(first, "id orbit v4 first")));
        rig.H.Main.Loop.RunUntil(() => rig.Session.Inputs.Count == 1, AppModelHarness.Bound);
        rig.H.Main.OnMain(() =>
        {
            Assert.Equal(first.RequestId, state.PendingAsk!.RequestId);
            Assert.True(rig.Runtime.TrySendArtifactAsk(second, "id orbit v4 second"));
            Assert.Equal(second.RequestId, Assert.Single(state.QueuedAsks).RequestId);
            state.Select(new TabulaRef(rig.InitialThread.Id, "another-artifact"));
            rig.Session.Emit(new ProviderEvent.TurnCompleted(outcome, null));
        });
        rig.H.Main.Loop.RunUntil(() => rig.Session.Inputs.Count == 2, AppModelHarness.Bound);
        Assert.Equal(second.RequestId, state.PendingAsk!.RequestId);
        Assert.Empty(state.QueuedAsks);
        rig.Complete();
        Assert.Empty(state.Asks);
    }

    [Fact]
    public void Artifact_requests_cannot_be_bundled_or_sent_now_out_of_order_and_removal_clears_the_node()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.Session.SendScript = _ => new TaskCompletionSource().Task;
        _ = rig.StartTurn("Earlier request");
        rig.H.Main.Loop.RunUntil(() => rig.Session.Inputs.Count == 1, AppModelHarness.Bound);
        var state = TabulaPane.StateOf(rig.Runtime);
        var one = new TabulaPendingAsk("orbit", 4, "one", rig.H.Main.Time.GetUtcNow());
        var two = new TabulaPendingAsk("orbit", 4, "two", one.At);
        rig.H.Main.OnMain(() =>
        {
            Assert.True(rig.Runtime.TrySendArtifactAsk(one, "first"));
            Assert.True(rig.Runtime.TrySendArtifactAsk(two, "second"));
            rig.Runtime.BundleFollowUp(two.RequestId, one.RequestId);
            Assert.All(rig.Runtime.FollowUps, p => Assert.Null(p.BundleID));
            rig.Runtime.SendFollowUpNow(two.RequestId);
            Assert.Equal(2, rig.Runtime.FollowUps.Count);
            Assert.Equal(0, rig.Session.InterruptCalls);
            rig.Runtime.RemoveFollowUp(two.RequestId);
            Assert.Equal(one.RequestId, Assert.Single(state.QueuedAsks).RequestId);
        });
        rig.Complete();
    }

    [Fact]
    public void Two_idle_requests_are_accepted_into_the_same_queue_before_startup_can_yield()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.Session.SendScript = _ => new TaskCompletionSource().Task;
        rig.H.Main.OnMain(() =>
        {
            rig.Runtime.SendArtifactTurn("first artifact request");
            rig.Runtime.SendArtifactTurn("second artifact request");
            Assert.Equal(["first artifact request", "second artifact request"], rig.Runtime.FollowUps.Select(p => p.Text));
        });
        rig.H.Main.Loop.RunUntil(() => rig.Session.Inputs.Count == 1, AppModelHarness.Bound);
        Assert.Contains("first artifact request", rig.Session.Inputs[0].Text, StringComparison.Ordinal);
        rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null)));
        rig.H.Main.Loop.RunUntil(() => rig.Session.Inputs.Count == 2, AppModelHarness.Bound);
        Assert.Contains("second artifact request", rig.Session.Inputs[1].Text, StringComparison.Ordinal);
        rig.Complete();
    }

    [Fact]
    public void Artifact_requests_stay_on_the_lead_when_Hydra_has_room()
    {
        using var rig = new ThreadRuntimeTurnRig(hydraOn: true, settings: app =>
        {
            app.Settings.HydraEnabled = true;
            app.Settings.HydraQueueHeads = true;
            app.Settings.HydraIsolateHeads = false;
            app.Settings.HydraMaxHeads = 2;
        });
        rig.Session.SendScript = _ => new TaskCompletionSource().Task;
        _ = rig.StartTurn("Earlier request");
        rig.H.Main.Loop.RunUntil(() => rig.Session.Inputs.Count == 1, AppModelHarness.Bound);
        rig.H.Main.OnMain(() =>
        {
            rig.Runtime.SendArtifactTurn("Change the Tabula artifact \"Orbit\" (id orbit, v4, Markdown): second");
            Assert.Single(rig.Runtime.FollowUps);
            Assert.Empty(rig.App.HydraHeads(rig.InitialThread.Id));
        });
        rig.Complete();
    }

    [Theory]
    [InlineData(TurnStatus.Completed)]
    [InlineData(TurnStatus.Failed)]
    [InlineData(TurnStatus.Interrupted)]
    public void Artifact_follow_ups_run_in_order_after_any_previous_outcome(TurnStatus outcome)
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.Session.SendScript = _ => new TaskCompletionSource().Task;
        _ = rig.StartTurn("Earlier request");
        rig.H.Main.Loop.RunUntil(() => rig.Session.Inputs.Count == 1, AppModelHarness.Bound);
        rig.H.Main.OnMain(() =>
        {
            rig.Runtime.SendArtifactTurn("Change the Tabula artifact \"Orbit\" (id orbit, v4, Markdown): second");
            rig.Runtime.SendArtifactTurn("Change the Tabula artifact \"Orbit\" (id orbit, v4, Markdown): third");
            Assert.Equal(2, rig.Runtime.FollowUps.Count);
        });
        rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.TurnCompleted(outcome, null)));
        rig.H.Main.Loop.RunUntil(() => rig.Session.Inputs.Count == 2, AppModelHarness.Bound);
        Assert.Contains("second", rig.Session.Inputs[1].Text, StringComparison.Ordinal);
        Assert.Single(rig.Runtime.FollowUps);
        rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null)));
        rig.H.Main.Loop.RunUntil(() => rig.Session.Inputs.Count == 3, AppModelHarness.Bound);
        Assert.Contains("third", rig.Session.Inputs[2].Text, StringComparison.Ordinal);
        Assert.Empty(rig.Runtime.FollowUps);
        rig.Complete();
    }
}
