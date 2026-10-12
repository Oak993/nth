using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Xunit;

namespace Marrow.App.Tests.Port;

[Collection("MCPHub processes")]
public sealed class C7InstallerAdmissionTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void Idle_quit_holds_the_actual_send_paths_without_mutating_the_draft_or_queue()
    {
        using var rig = new NthControlRig("Saved draft");
        var runtime = rig.Runtimes[0];
        var draft = new ComposerDraft { Text = "Keep this draft" };
        rig.H.Main.OnMain(() => runtime.Draft = draft);
        var answer = rig.Ask(NthControlRequest.QuitIfIdle);
        Assert.Equal(NthControlReply.Ok, answer.Response.Reply);
        rig.H.Main.OnMain(() =>
        {
            Assert.True(rig.Model.InstallerHoldsNewTurns);
            Assert.Equal("nth is closing to update", rig.Model.InstallerClosingMessage);
            runtime.Send();
            runtime.InterruptAndSend();
            runtime.SendHydraBrief("New head", []);
            Assert.Same(draft, runtime.Draft);
            Assert.Empty(runtime.FollowUps);
            Assert.False(runtime.IsRunning);
        });
        answer.Abort!();
        rig.H.Main.RunUntilIdle();
        Assert.False(rig.Model.InstallerHoldsNewTurns);
        Assert.Null(rig.Model.InstallerClosingMessage);
        Assert.Empty(rig.Recorder.Calls);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_captured_turn_is_busy_before_its_post_runs_and_retains_its_typed_intent()
    {
        using var rig = new NthControlRig("Captured send");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = false;
        try
        {
            rig.H.Main.OnMain(() =>
            {
                rig.Runtimes[0].SpawnInstallerTurn(async () => { entered = true; await release.Task; });
                var answer = rig.Host.Answer(NthControlRequest.QuitIfIdle with { Id = Guid.NewGuid().ToString("N") });
                Assert.Equal(NthControlReply.Busy, answer.Response.Reply);
                Assert.Equal("Captured send", Assert.Single(answer.Response.Work).Name);
                Assert.False(rig.Model.InstallerHoldsNewTurns);
            });
            rig.H.Main.RunUntilIdle();
            Assert.True(entered);
        }
        finally
        {
            release.TrySetResult();
            rig.H.Main.Loop.RunUntil(() => !rig.Model.PendingInstallerTurns.Any(), AppModelHarness.Bound);
        }
        Assert.Empty(rig.Recorder.Calls);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_busy_recheck_releases_the_hold_and_reports_the_work_without_an_exit()
    {
        using var rig = new NthControlRig("Late work");
        var answer = rig.Ask(NthControlRequest.QuitIfIdle);
        Assert.True(rig.Model.InstallerHoldsNewTurns);
        rig.SetRunning(0, TimeSpan.FromSeconds(2));
        var final = rig.Finish(answer);
        Assert.Equal(NthControlReply.Busy, final.Reply);
        Assert.Equal("Late work", Assert.Single(final.Work).Name);
        Assert.False(rig.Model.InstallerHoldsNewTurns);
        Assert.Empty(rig.Recorder.Calls);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Work_started_after_prepare_but_before_final_commit_is_returned_as_busy()
    {
        using var rig = new NthControlRig("Final acknowledgement race");
        var answer = rig.Ask(NthControlRequest.QuitIfIdle);
        NthControlAnswer prepared = null!;
        rig.H.Main.Loop.Run(async () => { prepared = await answer.PrepareCommit!(CancellationToken.None); }, AppModelHarness.Bound);
        Assert.Equal(NthControlReply.Ok, prepared.Response.Reply);
        rig.SetRunning(0, TimeSpan.FromSeconds(1));
        NthControlResponse? outcome = null;
        rig.H.Main.Loop.Run(async () => { outcome = await prepared.Commit!(CancellationToken.None); }, AppModelHarness.Bound);
        Assert.Equal(NthControlReply.Busy, outcome!.Reply);
        Assert.Equal("Final acknowledgement race", Assert.Single(outcome.Work).Name);
        Assert.False(rig.Model.InstallerHoldsNewTurns);
        Assert.Empty(rig.Recorder.Calls);
    }
}
