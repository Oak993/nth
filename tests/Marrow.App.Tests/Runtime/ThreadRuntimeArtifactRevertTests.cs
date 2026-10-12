using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;

namespace Marrow.App.Tests.Runtime;

[Trait("Decision", "ED-NEW-web-nc6-2")]
public sealed class ThreadRuntimeArtifactRevertTests
{
    [Fact]
    public void A_request_deferred_during_revert_resumes_when_revert_settles()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.Send("first turn");
        rig.Complete();
        var turn = Assert.Single(rig.Runtime.Turns);
        rig.H.Main.OnMain(() =>
        {
            rig.Runtime.SendArtifactTurn("accepted before revert");
            ThreadRuntimeReflection.Set(rig.Runtime, "isReverting", true);
        });
        // The controlled main loop holds the revert boundary before queued startup.
        rig.H.Main.RunUntilIdle();
        Assert.Equal("accepted before revert", Assert.Single(rig.Runtime.FollowUps).Text);
        var next = new ThreadRuntimeScriptedSession { SendScript = _ => new TaskCompletionSource().Task };
        rig.H.Main.OnMain(() =>
        {
            ThreadRuntimeReflection.Set(rig.Runtime, "isReverting", false);
            Assert.True(rig.Runtime.Revert(turn.Id, restoreFiles: false).GetAwaiter().GetResult());
            // OnMain runs everything Revert posted (the resumed startup) before it returns, so the offline seam goes in
            // here, in the same action: Revert released the old session, and a startup without a seam would try to
            // launch the real provider (ED-NEW-web-nc6l-2).
            rig.Install(next);
        });
        rig.H.Main.Loop.RunUntil(() => next.Inputs.Count == 1, TimeSpan.FromSeconds(8));
        Assert.Contains("accepted before revert", next.Inputs[0].Text, StringComparison.Ordinal);
        rig.Complete();
    }
}
