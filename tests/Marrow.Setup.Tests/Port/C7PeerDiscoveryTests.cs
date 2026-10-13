using Marrow.Setup.Support;
using Marrow.Setup.Tests.Fakes;

namespace Marrow.Setup.Tests.Port;

public sealed class C7PeerDiscoveryTests
{
    // ED-NEW-inst-c7-4: a queued open check can be canceled without waiting for the executing discovery.
    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_canceled_open_check_does_not_wait_behind_a_paused_discovery()
    {
        var rig = new HandshakeRig();
        using var processes = new PausedNthDiscovery(rig.Nth.Main);
        var handshake = new RunningNthHandshake(HandshakeRig.Root, rig.Nth, processes, rig.Time);
        using var cancellation = new CancellationTokenSource();
        Task<bool>? first = null;
        Task<bool>? second = null;
        try
        {
            first = handshake.IsNthOpenAsync();
            await processes.FirstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            second = handshake.IsNthOpenAsync(cancellation.Token);
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { _ = await second.WaitAsync(TimeSpan.FromSeconds(5)); });
            Assert.False(first.IsCompleted);
        }
        finally
        {
            processes.ReleaseFirst();
            if (first is not null) _ = await first;
            if (second is not null)
                try { _ = await second; } catch (OperationCanceledException) { }
        }
    }

    // ED-NEW-inst-c7-4: a stale open-check cannot overwrite a newer operation's known live lifetime.
    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_paused_open_check_cannot_drop_the_process_guard_seeded_by_a_concurrent_ask()
    {
        var rig = new HandshakeRig();
        rig.Nth.ExitDelay = null;
        using var processes = new PausedNthDiscovery(rig.Nth.Main);
        var handshake = new RunningNthHandshake(HandshakeRig.Root, rig.Nth, processes, rig.Time);
        Task<bool>? open = null;
        Task<RunningNthStatus>? ask = null;
        try
        {
            open = handshake.IsNthOpenAsync();
            await processes.FirstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            ask = handshake.AskAsync();
            try
            {
                // With no refresh gate, seed the newer guard before releasing the stale discovery.
                // A serialized implementation cannot enter the second discovery until the first is released.
                var overlapping = true;
                try
                {
                    await processes.LaterEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
                }
                catch (TimeoutException)
                {
                    overlapping = false; // Releasing the first lets the serialized ask discover the live process.
                }
                if (overlapping) Assert.Equal(RunningNthState.NotResponding, (await rig.Time.RunAsync(ask)).State);
            }
            finally
            {
                processes.ReleaseFirst();
            }
            _ = await open;
            Assert.Equal(RunningNthState.NotResponding, (await rig.Time.RunAsync(ask)).State);

            var result = await rig.Time.RunAsync(handshake.KeepWaitingAsync());

            Assert.False(result.CanProceed);
            Assert.Equal(RunningNthState.NotResponding, result.State);
            Assert.True(processes.IsRunning(rig.Nth.Main));
            Assert.Empty(processes.FindInside(HandshakeRig.Root));
        }
        finally
        {
            processes.ReleaseFirst();
            if (open is not null) _ = await open;
            if (ask is not null) _ = await rig.Time.RunAsync(ask);
        }
    }

    // ED-NEW-inst-c7-4: discovery absence cannot replace proof that a known lifetime exited.
    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_failed_end_and_unavailable_discovery_do_not_clear_a_known_live_process()
    {
        var rig = new HandshakeRig();
        rig.Nth.ExitDelay = null;
        var processes = new UndescribedNthProcesses(rig.Nth.Main);
        var handshake = new RunningNthHandshake(HandshakeRig.Root, rig.Nth, processes, rig.Time);
        var before = await rig.Time.RunAsync(handshake.AskAsync());
        Assert.Equal(RunningNthState.NotResponding, before.State);

        var result = await rig.Time.RunAsync(handshake.EndItAsync());

        Assert.False(result.CanProceed);
        Assert.Equal(RunningNthState.Refused, result.State);
        Assert.Equal("could-not-end", result.Reason);
        Assert.Equal([rig.Nth.Main], processes.EndAttempts);
        Assert.True(processes.IsRunning(rig.Nth.Main));
        Assert.Empty(processes.FindInside(HandshakeRig.Root));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_recycled_peer_cannot_clear_the_old_snapshot_while_the_new_in_root_process_still_runs()
    {
        var rig = new HandshakeRig();
        rig.Nth.ExitDelay = null;
        rig.Nth.Work = _ =>
        {
            rig.Nth.InRoot = [rig.Nth.Main with { StartedAtTicks = 2000 }]; // A exited; Windows reused its ID for B.
            return [];
        };
        var processes = new LifetimeNthProcesses(rig.Nth);
        var handshake = new RunningNthHandshake(HandshakeRig.Root, rig.Nth, processes, rig.Time);

        var result = await rig.Time.RunAsync(handshake.AskAsync());

        Assert.False(result.CanProceed);
        Assert.Equal(RunningNthState.NotResponding, result.State);
        Assert.True(rig.Nth.Running);
        Assert.False(processes.IsRunning(rig.Nth.Main)); // old lifetime is gone, but its replacement is still in root.
        Assert.Empty(rig.Nth.Ended);
    }
}
