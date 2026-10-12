using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-cx-5")]
public sealed class C3A5Tests
{
    [Fact]
    public async Task Interrupted_put_back_keeps_the_unsent_keys_exactly_once()
    {
        var rig = new PcBorrowRig().ProveAll();
        await C3ReplayRig.Keep(rig, PcBorrowRig.User, PcBorrowRig.UserControl, 0x41, 20);
        rig.Host.OnSend = _ => { rig.Host.OnSend = null; rig.Host.Foreground = PcBorrowRig.Other; };
        Assert.False(rig.Lease.PutBack());
        Assert.Equal(RealInputLease.ReplayChunk, Assert.Single(rig.Host.Sent).Events.Count);
        Assert.Equal(12, rig.Lease.KeptKeys);
        rig.Host.Foreground = PcBorrowRig.User;
        Assert.True(rig.Lease.PutBack());
        Assert.Equal(40, rig.Host.Sent.Sum(s => s.Events.Count));
        Assert.Equal(0, rig.Lease.KeptKeys);
    }
}
