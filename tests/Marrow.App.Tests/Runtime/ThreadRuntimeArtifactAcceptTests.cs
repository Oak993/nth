using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Artifacts;
using Marrow.Core.Models;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Runtime;

/// <summary>
/// ED-NEW-web-nc6l-1: an accepted Ask to change / Ask to fix is "queued" only when something is ahead of it. The
/// first request on an idle chat is the pending one from the moment it is accepted (the strip pulses "v5", the toast
/// is "Asked the chat to fix it"), even though the follow-up queue starts it one main-loop hop later.
/// </summary>
[Trait("Decision", "ED-NEW-web-nc6l-1")]
public sealed class ThreadRuntimeArtifactAcceptTests
{
    [Fact]
    public void The_first_request_on_an_idle_chat_is_pending_at_once_and_only_the_next_one_is_queued()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.Session.SendScript = _ => new TaskCompletionSource().Task;
        var state = TabulaPane.StateOf(rig.Runtime);
        var first = new TabulaPendingAsk("orbit", 4, "first", rig.H.Main.Time.GetUtcNow());
        var second = new TabulaPendingAsk("orbit", 4, "second", first.At);
        rig.H.Main.OnMain(() =>
        {
            Assert.True(rig.Runtime.TrySendArtifactAsk(first, "id orbit v4 first"));
            // The turn starts a main-loop hop later; until then the request is the pending one, not a queued one.
            Assert.Equal(first.RequestId, state.PendingAsk!.RequestId);
            Assert.Empty(state.QueuedAsks);
            Assert.True(rig.Runtime.TrySendArtifactAsk(second, "id orbit v4 second"));
            Assert.Equal(first.RequestId, state.PendingAsk!.RequestId);
            var queued = Assert.Single(state.QueuedAsks);
            Assert.Equal(second.RequestId, queued.RequestId);
            Assert.Equal(6, queued.Number);
        });
        rig.H.Main.Loop.RunUntil(() => rig.Session.Inputs.Count == 1, AppModelHarness.Bound);
        rig.H.Main.OnMain(() =>
        {
            Assert.Equal(first.RequestId, state.PendingAsk!.RequestId);
            Assert.Equal(second.RequestId, Assert.Single(state.QueuedAsks).RequestId);
        });
        rig.Complete();
    }

    [Fact]
    public void A_request_made_while_the_chat_runs_another_turn_is_queued_until_that_turn_ends()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.Session.SendScript = _ => new TaskCompletionSource().Task;
        _ = rig.StartTurn("Earlier request");
        rig.H.Main.Loop.RunUntil(() => rig.Session.Inputs.Count == 1, AppModelHarness.Bound);
        var state = TabulaPane.StateOf(rig.Runtime);
        var ask = new TabulaPendingAsk("orbit", 4, "change", rig.H.Main.Time.GetUtcNow());
        rig.H.Main.OnMain(() =>
        {
            Assert.True(rig.Runtime.TrySendArtifactAsk(ask, "id orbit v4 change"));
            Assert.Null(state.PendingAsk);
            Assert.Equal(ask.RequestId, Assert.Single(state.QueuedAsks).RequestId);
            rig.Session.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
        });
        rig.H.Main.Loop.RunUntil(() => rig.Session.Inputs.Count == 2, AppModelHarness.Bound);
        rig.H.Main.OnMain(() =>
        {
            Assert.Equal(ask.RequestId, state.PendingAsk!.RequestId);
            Assert.Empty(state.QueuedAsks);
        });
        rig.Complete();
    }
}
