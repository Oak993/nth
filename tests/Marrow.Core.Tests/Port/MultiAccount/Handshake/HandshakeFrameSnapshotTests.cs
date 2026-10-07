using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Handshake;
using Marrow.Testing;
using Marrow.Testing.MultiAccountSettings;
namespace Marrow.Core.Tests.Port.MultiAccount.Handshake;
// PORT-ONLY: MA-4-2 T16 request: sampling frames never invokes the allocating accounts callback/geometry projection.
public sealed class HandshakeFrameSnapshotTests
{
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Snapshot_is_allocation_free_in_every_phase_and_matches_existing_plan()
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude, "Max"); var b = rig.ConnectAnother(ProviderKind.Claude, "Max");
        var account = new FakeThreadAccountPort(rig, ProviderKind.Claude); var port = new FakeCardPort(); var clock = new ManualTimeProvider(); var thread = Guid.NewGuid(); var reads = 0;
        using var model = new HandshakeMotionModel(thread, port, () => { reads++; return account.Accounts.ToArray(); }, clock);
        foreach (var phase in new[] { HandshakePhase.Standby, HandshakePhase.Linking, HandshakePhase.InSync, HandshakePhase.Switch })
        {
            port.Raise(new(thread, phase == HandshakePhase.InSync ? HandshakeEventKind.InSyncReady : HandshakeEventKind.PhaseChanged, HandshakePhase.Idle, phase, a.Id.ToString(), b.Id.ToString(), HandshakeReason.NearLimit, clock.GetUtcNow()));
            clock.Advance(TimeSpan.FromSeconds(2));
            var old = model.Plan(420, 160, true); var frame = model.ReadFrame(true);
            Assert.Equal(old.Status, frame.Status); Assert.Equal(old.Phase, frame.Phase); Assert.Equal(old.VirtualMilliseconds, frame.VirtualMilliseconds);
            Assert.Equal(old.ShowTick, frame.ShowTick); Assert.Equal(old.Progress, frame.Progress);
            for (var n = 0; n < 100; n++) model.ReadFrame(true);
            var beforeReads = reads; var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            for (var n = 0; n < 10000; n++) model.ReadFrame(true);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - beforeBytes); Assert.Equal(beforeReads, reads);
        }
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Snapshot_caches_accounts_on_events_and_explicit_usage_refresh_and_keeps_rm()
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude); var b = rig.ConnectAnother(ProviderKind.Claude);
        var account = new FakeThreadAccountPort(rig, ProviderKind.Claude); var port = new FakeCardPort(); var clock = new ManualTimeProvider(); var thread = Guid.NewGuid();
        using var model = new HandshakeMotionModel(thread, port, () => account.Accounts, clock, reduceMotion: () => true);
        port.Raise(new(thread, HandshakeEventKind.PhaseChanged, HandshakePhase.Idle, HandshakePhase.Standby, a.Id.ToString(), b.Id.ToString(), HandshakeReason.NearLimit, clock.GetUtcNow()));
        var old = model.ReadFrame(true); rig.Usage[b.Id] = new(.99, null, .3, null, false, false); account.Refresh(); model.RefreshAccounts();
        Assert.NotEqual(old.IncomingColor, model.ReadFrame(true).IncomingColor); Assert.True(model.ReadFrame(true).UseCrossfade);
    }
}
