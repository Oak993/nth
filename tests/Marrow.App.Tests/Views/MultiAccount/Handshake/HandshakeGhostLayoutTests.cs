using Marrow.App.Port.MultiAccount.Handshake;
using Marrow.Core.Accounts;
using Marrow.Core.Port.MultiAccount.Handshake;
namespace Marrow.App.Tests.Views.MultiAccount.Handshake;
// PORT-ONLY: live ghost's value geometry must remain equal to the locked Core projection.
public sealed class HandshakeGhostLayoutTests
{
    [Theory, Trait("Rule", "R-TEST-4")]
    [InlineData(false)] [InlineData(true)]
    public void Live_ghost_matches_the_existing_oracle_at_each_phase_and_allocates_zero(bool reduced)
    {
        foreach (var t in new double[] { 0, 1000, 1300, 1600, 2000, 2750, 3000, 3800, 4400, 4650, 4900, 5020, 5700 })
        {
            var snapshot = new HandshakeFrameSnapshot(HandshakePhase.InSync, t, 0, "", false, reduced, (240, 105, 90), (58, 214, 168));
            Assert.Equal(HandshakeGeometry.Draw(230, 128, t, false, reduced).Ghost, HandshakeGhostLayout.Read(230, 128, snapshot));
        }
        var frame = new HandshakeFrameSnapshot(HandshakePhase.InSync, 3500, 0, "", false, reduced, (240, 105, 90), (58, 214, 168));
        for (var i = 0; i < 30; i++) HandshakeGhostLayout.Read(230, 128, frame);
        var before = GC.GetAllocatedBytesForCurrentThread(); for (var i = 0; i < 120; i++) HandshakeGhostLayout.Read(230, 128, frame);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
