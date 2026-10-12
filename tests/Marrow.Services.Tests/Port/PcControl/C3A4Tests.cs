using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-cx-4")]
public sealed class C3A4Tests
{
    [Fact]
    public async Task Kept_keys_from_two_windows_return_only_to_their_own_origin()
    {
        var rig = new PcBorrowRig().ProveAll();
        await C3ReplayRig.Keep(rig, PcBorrowRig.User, PcBorrowRig.UserControl, 0x41);
        await C3ReplayRig.Keep(rig, PcBorrowRig.Other, 0x301, 0x42);
        Assert.True(rig.Lease.PutBack());
        Assert.Equal(new ushort[] { 0x42, 0x42 }, rig.Host.Sent.SelectMany(s => s.Events).Select(e => e.Vk));
        Assert.Equal(1, rig.Lease.KeptKeys);
        rig.Host.Foreground = PcBorrowRig.User;
        rig.Host.FocusedControl = PcBorrowRig.UserControl;
        Assert.True(rig.Lease.PutBack());
        Assert.Equal(new ushort[] { 0x41, 0x41 }, rig.Host.Sent[^1].Events.Select(e => e.Vk));
        Assert.Equal(0, rig.Lease.KeptKeys);
    }

    [Fact]
    public async Task Kept_keys_require_the_original_control_as_well_as_the_window()
    {
        var rig = new PcBorrowRig().ProveAll();
        await C3ReplayRig.Keep(rig, PcBorrowRig.User, PcBorrowRig.UserControl, 0x41);
        rig.Host.FocusedControl = 0x102;
        Assert.False(rig.Lease.PutBack());
        Assert.Empty(rig.Host.Sent);
        Assert.Equal(1, rig.Lease.KeptKeys);
        rig.Host.FocusedControl = PcBorrowRig.UserControl;
        Assert.True(rig.Lease.PutBack());
        Assert.Equal(0, rig.Lease.KeptKeys);
    }
}
