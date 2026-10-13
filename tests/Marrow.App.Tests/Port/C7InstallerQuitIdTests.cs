using Marrow.App.Tests.Fixtures;
using Marrow.App.Port;
using Marrow.Core.Support;
using Xunit;

namespace Marrow.App.Tests.Port;

[Collection("MCPHub processes")]
public sealed class C7InstallerQuitIdTests
{
    private static NthControlRequest Quit(string id) => NthControlRequest.QuitIfIdle with { Id = id };
    private static NthControlRequest Withdraw(string id) => NthControlRequest.Withdraw with { Id = id };

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Withdrawal_before_ack_retires_only_its_id_and_cannot_release_a_new_quit()
    {
        using var rig = new NthControlRig("Idle");
        var a = Guid.NewGuid().ToString("N"); var b = Guid.NewGuid().ToString("N");
        var old = rig.Ask(Quit(a));
        rig.Ask(Withdraw(a));
        Assert.False(rig.Model.InstallerHoldsNewTurns);
        var current = rig.Ask(Quit(b));
        rig.Ask(Withdraw(a));
        rig.Finish(old);
        Assert.True(rig.Model.InstallerHoldsNewTurns);
        Assert.Empty(rig.Recorder.Calls);
        rig.Finish(current);
        Assert.Equal(["exit"], rig.Recorder.Calls);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_unrelated_withdraw_does_not_cancel_the_held_quit()
    {
        using var rig = new NthControlRig("Idle");
        var answer = rig.Ask(Quit(Guid.NewGuid().ToString("N")));
        rig.Ask(Withdraw(Guid.NewGuid().ToString("N")));
        Assert.True(rig.Model.InstallerHoldsNewTurns);
        rig.Finish(answer);
        Assert.Equal(["exit"], rig.Recorder.Calls);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_second_quit_that_cannot_own_pending_termination_releases_its_hold()
    {
        using var rig = new NthControlRig("Merge");
        var a = Guid.NewGuid().ToString("N"); var b = Guid.NewGuid().ToString("N");
        rig.SetMerging(0, true);
        rig.Finish(rig.Ask(NthControlRequest.QuitAndStop with { Id = a }));
        rig.SetMerging(0, false); // polling has not observed merge completion yet
        var second = rig.Ask(Quit(b));
        var refusal = rig.Finish(second);
        Assert.Equal(NthControlReply.Refused, refusal.Reply);
        Assert.Equal("pending-quit", refusal.Reason);
        Assert.True(rig.Model.InstallerHoldsNewTurns); // only committed Stop A retains its own hold
        rig.Ask(Withdraw(a));
        rig.H.Main.AdvanceMilliseconds(250);
        Assert.Empty(rig.Recorder.Calls);
        Assert.False(rig.Model.InstallerHoldsNewTurns);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_retired_callback_stays_inert_after_the_lookup_tombstone_is_pruned()
    {
        using var rig = new NthControlRig("Idle");
        var id = Guid.NewGuid().ToString("N");
        var answer = rig.Ask(Quit(id));
        rig.Ask(Withdraw(id));
        for (var i = 0; i < 1025; i++) rig.Ask(Withdraw(Guid.NewGuid().ToString("N")));
        rig.Finish(answer);
        Assert.Empty(rig.Recorder.Calls);
        Assert.False(rig.Model.InstallerHoldsNewTurns);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_quit_without_an_id_is_refused_before_acquiring_admission()
    {
        using var rig = new NthControlRig();
        var answer = rig.H.Main.OnMain(() => rig.Host.Answer(NthControlRequest.QuitIfIdle));
        Assert.Equal(NthControlReply.Refused, answer.Response.Reply);
        Assert.Equal("missing-id", answer.Response.Reason);
        Assert.False(rig.Model.InstallerHoldsNewTurns);
        Assert.Empty(rig.Recorder.Calls);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_old_merge_sender_cannot_resolve_a_new_generation_even_when_its_wire_id_is_reused()
    {
        using var rig = new NthControlRig("Merge");
        var id = Guid.NewGuid().ToString("N");
        var oldGeneration = new object(); var currentGeneration = new object();
        rig.SetMerging(0, true);
        Assert.True(rig.H.Main.OnMain(() => rig.Shell.TerminateForInstaller(id, oldGeneration)));
        Assert.True(rig.H.Main.OnMain(() => rig.Shell.WithdrawInstallerQuit(id)));
        for (var i = 0; i < 1025; i++) rig.Ask(Withdraw(Guid.NewGuid().ToString("N")));
        Assert.True(rig.H.Main.OnMain(() => rig.Shell.TerminateForInstaller(id, currentGeneration)));
        rig.H.Main.OnMain(() => new InstallerTerminationSender(rig.Shell, id, oldGeneration).Reply(true));
        Assert.Empty(rig.Recorder.Calls);
        rig.H.Main.OnMain(() => new InstallerTerminationSender(rig.Shell, id, currentGeneration).Reply(true));
        Assert.Equal(["exit"], rig.Recorder.Calls);
    }
}
