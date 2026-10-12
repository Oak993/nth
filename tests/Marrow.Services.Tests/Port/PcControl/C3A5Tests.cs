using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Native;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// ED-NEW-pcctl-cx-5: the trait names the owner decision, as a two-word ED id is not a well-formed trait id (ED-NEW-pcctl-35).
[Trait("Decision", "OD-0144")]
public sealed class C3A5Tests
{
    [Fact]
    public async Task Interrupted_put_back_keeps_the_unsent_keys_exactly_once()
    {
        var rig = new PcBorrowRig().ProveAll();
        var keys = Enumerable.Range(0x41, 20).Select(x => (ushort)x).ToArray();
        var expected = keys.SelectMany(k => new[] { (k, 0u), (k, PcInputPlan.KEY_UP) }).ToArray();
        await C3ReplayRig.Keep(rig, PcBorrowRig.User, PcBorrowRig.UserControl, keys);
        rig.Host.OnSend = _ => { rig.Host.OnSend = null; rig.Host.Foreground = PcBorrowRig.Other; };
        Assert.False(rig.Lease.PutBack());
        Assert.Equal(RealInputLease.ReplayChunk, Assert.Single(rig.Host.Sent).Events.Count);
        Assert.Equal(12, rig.Lease.KeptKeys);
        Assert.Equal(expected.Take(RealInputLease.ReplayChunk), rig.Host.Sent.SelectMany(s => s.Events).Select(e => (e.Vk, e.Flags)));
        rig.Host.Foreground = PcBorrowRig.User;
        Assert.True(rig.Lease.PutBack());
        Assert.Equal(40, rig.Host.Sent.Sum(s => s.Events.Count));
        Assert.Equal(0, rig.Lease.KeptKeys);
        Assert.Equal(expected, rig.Host.Sent.SelectMany(s => s.Events).Select(e => (e.Vk, e.Flags)));
    }
}
