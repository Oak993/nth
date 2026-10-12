using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-cx-14")]
public sealed class C3cV2Tests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public async Task Health_release_that_wins_inside_activation_cannot_leave_late_foreground_or_raise(bool pointer, int restoreFailures)
    {
        var rig = new PcBorrowRig().ProveAll();
        rig.Host.RestoreFailures = restoreFailures;
        using var released = new ManualResetEventSlim();
        rig.Host.AfterSetCursor = released.Set;
        void LoseHealth()
        {
            rig.Monitor.Installed(false);
            Assert.True(released.Wait(TimeSpan.FromSeconds(5)), "health release did not finish");
        }
        if (pointer)
        {
            rig.Host.Under = _ => PcBorrowRig.Other;
            rig.Host.BeforeRaise = LoseHealth;
        }
        else rig.Host.BeforeSetForeground = LoseHealth;
        var result = await rig.Lease.Borrow(pointer ? rig.Click() : rig.Keys(PcBorrowRig.Paint, 0x41), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PcBorrowStatus.NeedsRealInput, result.Status);
        Assert.Equal(PcBorrowRig.User, rig.Host.Foreground);
        if (pointer) Assert.True(rig.Host.Calls.LastIndexOf("zorder") > rig.Host.Calls.LastIndexOf("raise"));
        Assert.Empty(rig.Host.Sent);
        Assert.False(rig.Hold.Swallowing);
    }
}
