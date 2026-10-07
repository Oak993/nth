using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Handshake;
using Marrow.Testing;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.App.Tests.Views.MultiAccount.Handshake;

// PORT-ONLY: MA-4-2 live-frame contract (0 B per frame). This isolates the Core projection called by the App host.
// A cached accounts snapshot and a stationary clock exclude adapter/time-fixture allocations from this measurement.
// T15 supplies ReadFrame for the live path; Plan is retained solely as the allocating inspection/oracle API.
public sealed class HandshakeFrameAllocationTests
{
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Warm_live_projection_allocates_zero_bytes_per_frame()
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude); var b = rig.ConnectAnother(ProviderKind.Claude); var rows = new FakeThreadAccountPort(rig, ProviderKind.Claude);
        var snapshot = rows.Accounts; var port = new FakeCardPort(); var clock = new ManualTimeProvider();
        using var model = new HandshakeMotionModel(rows.ThreadId, port, () => snapshot, clock);
        var state = new HandshakeEvent(rows.ThreadId, HandshakeEventKind.PhaseChanged, HandshakePhase.Idle, HandshakePhase.Standby, a.Id.ToString("D"), b.Id.ToString("D"), HandshakeReason.NearLimit, clock.GetUtcNow());
        port.Raise(state); clock.Advance(TimeSpan.FromMilliseconds(600)); port.Raise(state with { To = HandshakePhase.Linking }); clock.Advance(TimeSpan.FromMilliseconds(800));
        for (var i = 0; i < 30; i++) model.ReadFrame(false);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 120; i++) model.ReadFrame(false);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(bytes == 0, $"live Handshake projection allocates {bytes / 120.0:F0} B/frame ({bytes} B/120 frames); required 0 B/frame");
    }
}
