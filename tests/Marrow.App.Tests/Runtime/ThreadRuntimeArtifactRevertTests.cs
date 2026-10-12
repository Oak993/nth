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
        rig.H.Main.OnMain(() =>
        {
            ThreadRuntimeReflection.Set(rig.Runtime, "isReverting", false);
            Assert.True(rig.Runtime.Revert(turn.Id, restoreFiles: false).GetAwaiter().GetResult());
        });
        rig.Session.IsRunning = true;
        rig.Install(rig.Session); // Revert released the session; reinstall the offline seam before pumping.
        rig.H.Main.Loop.RunUntil(() => rig.Session.Inputs.Count == 2, TimeSpan.FromSeconds(8));
        Assert.Contains("accepted before revert", rig.Session.Inputs[1].Text, StringComparison.Ordinal);
        rig.Complete();
    }
}
