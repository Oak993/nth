using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Runtime;

[Trait("Decision", "ED-NEW-web-nc6-2")]
public sealed class ThreadRuntimeArtifactMixedQueueTests
{
    [Fact]
    public void Dragging_an_artifact_request_cannot_invert_the_accepted_request_order()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.Session.SendScript = _ => new TaskCompletionSource().Task;
        _ = rig.StartTurn("first artifact request");
        rig.H.Main.Loop.RunUntil(() => rig.Session.Inputs.Count == 1, AppModelHarness.Bound);
        rig.H.Main.OnMain(() =>
        {
            rig.Runtime.SendArtifactTurn("second artifact request");
            rig.Runtime.SendArtifactTurn("third artifact request");
            var queued = rig.Runtime.FollowUps;
            Assert.False(rig.Runtime.MoveFollowUp(queued[1].Id, queued[0].Id, placeAfter: false));
            Assert.Equal(queued, rig.Runtime.FollowUps);
        });
        rig.Complete(TurnStatus.Failed);
    }

    [Theory]
    [InlineData(TurnStatus.Failed)]
    [InlineData(TurnStatus.Interrupted)]
    public void A_paused_ordinary_follow_up_does_not_park_later_artifact_requests(TurnStatus outcome)
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.Session.SendScript = _ => new TaskCompletionSource().Task;
        _ = rig.StartTurn("first artifact request");
        rig.H.Main.Loop.RunUntil(() => rig.Session.Inputs.Count == 1, AppModelHarness.Bound);
        rig.H.Main.OnMain(() =>
        {
            rig.Runtime.EnqueueFollowUp("ordinary follow-up", []);
            rig.Runtime.SendArtifactTurn("second artifact request");
            rig.Runtime.SendArtifactTurn("third artifact request");
            rig.Session.Emit(new ProviderEvent.TurnCompleted(outcome, null));
        });
        rig.H.Main.Loop.RunUntil(() => rig.Session.Inputs.Count == 2, TimeSpan.FromSeconds(8));
        Assert.Contains("second artifact request", rig.Session.Inputs[1].Text, StringComparison.Ordinal);
        Assert.Equal(["ordinary follow-up", "third artifact request"], rig.Runtime.FollowUps.Select(p => p.Text));
        rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.TurnCompleted(outcome, null)));
        rig.H.Main.Loop.RunUntil(() => rig.Session.Inputs.Count == 3, TimeSpan.FromSeconds(8));
        Assert.Contains("third artifact request", rig.Session.Inputs[2].Text, StringComparison.Ordinal);
        Assert.Equal("ordinary follow-up", Assert.Single(rig.Runtime.FollowUps).Text);
        rig.Complete(outcome);
        Assert.Equal("ordinary follow-up", Assert.Single(rig.Runtime.FollowUps).Text);
    }
}
