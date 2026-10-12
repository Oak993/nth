using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-cx-15")]
public sealed class C3cV7Tests
{
    [Fact]
    public async Task Saved_unknown_focus_replays_into_a_real_control_of_the_same_window()
    {
        var rig = new PcBorrowRig().ProveAll();
        await C3ReplayRig.Keep(rig, PcBorrowRig.User, 0, 0x41);
        rig.Host.FocusedControl = PcBorrowRig.UserControl;
        Assert.True(rig.Lease.PutBack());
        Assert.Equal(0, rig.Lease.KeptKeys);
        Assert.All(Assert.Single(rig.Host.Sent).Events, e => Assert.Equal((ushort)0x41, e.Vk));
        Assert.All(rig.Host.SentWhileFront, front => Assert.Equal(PcBorrowRig.User, front));
    }
}
