using Marrow.App.Tests.Fixtures;
using Marrow.Core.Support;
using Xunit;

namespace Marrow.App.Tests.Port;

[Collection("MCPHub processes")]
public sealed class C7InstallerConnectionCancellationTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_request_cancelled_while_queued_on_main_never_acquires_a_hold_or_a_quit()
    {
        using var rig = new NthControlRig("Idle");
        using var gone = new CancellationTokenSource();
        var request = NthControlRequest.QuitIfIdle with { Id = Guid.NewGuid().ToString("N") };
        var pending = rig.Host.HandleAsync(request, gone.Token);
        Assert.False(pending.IsCompleted);
        Assert.True(rig.H.Main.Loop.Pending > 0);
        gone.Cancel();
        rig.H.Main.RunUntilIdle();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.False(rig.Model.InstallerHoldsNewTurns);
        Assert.Empty(rig.Recorder.Calls);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Cancellation_after_the_answer_releases_admission_and_makes_its_late_ack_inert()
    {
        using var rig = new NthControlRig("Idle");
        using var gone = new CancellationTokenSource();
        var request = NthControlRequest.QuitIfIdle with { Id = Guid.NewGuid().ToString("N") };
        var answer = rig.H.Main.OnMain(() => rig.Host.Answer(request, gone.Token));
        Assert.True(rig.Model.InstallerHoldsNewTurns);
        gone.Cancel();
        rig.H.Main.RunUntilIdle();
        Assert.False(rig.Model.InstallerHoldsNewTurns);
        rig.Finish(answer);
        Assert.Empty(rig.Recorder.Calls);
    }
}
