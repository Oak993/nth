using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// ED-NEW-pcctl-cx-1: the trait names the owner decision, as a two-word ED id is not a well-formed trait id (ED-NEW-pcctl-35).
[Trait("Decision", "OD-0144")]
public sealed class C3A1Tests
{
    [Fact]
    public async Task Turning_real_input_off_revokes_a_plan_waiting_for_the_card()
    {
        var rig = new PcBorrowRig().ProveAll();
        var settings = PcSettings.Default with { Enabled = true, RealInput = true };
        var window = new PcWindowInfo(PcBorrowRig.Target, "Paint", "MSPaintApp", 42, "c:\\windows\\mspaint.exe");
        var request = new PcInputRequest(PcInputKind.Click, window) { Point = new PcPoint(300, 200) };
        var plan = Assert.Single(new PcBorrowRung(() => settings, rig.Lease).Offer(request, PcFramework.Win32).Plans);
        rig.CardUp = true;
        var task = plan.Run(CancellationToken.None);
        await PcTestWait.Until(() => rig.Lease.Waits.Count == 1);
        settings = settings with { RealInput = false };
        rig.CardUp = false;
        rig.Clock.Advance(RealInputLease.PollMs);
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PcErrorCode.NeedsRealInput, result.Error);
        Assert.Empty(rig.Host.Sent);
        Assert.Empty(rig.Hold.Log);
    }

    [Fact]
    public async Task Turning_real_input_off_between_text_borrows_stops_the_rest()
    {
        var rig = new PcBorrowRig().ProveAll();
        var settings = PcSettings.Default with { Enabled = true, RealInput = true };
        var window = new PcWindowInfo(PcBorrowRig.Target, "Paint", "MSPaintApp", 42, "c:\\windows\\mspaint.exe");
        rig.Host.OnSend = _ => settings = settings with { RealInput = false };
        var plan = Assert.Single(new PcBorrowRung(() => settings, rig.Lease).Offer(
            new PcInputRequest(PcInputKind.Type, window) { Text = new string('x', 100) }, PcFramework.Win32).Plans);
        var result = await plan.Run(CancellationToken.None);
        Assert.Equal(PcErrorCode.NeedsRealInput, result.Error);
        Assert.Single(rig.Host.Sent);
        Assert.False(rig.Hold.Swallowing);
    }

    [Fact]
    public async Task Turning_real_input_off_inside_a_drag_allows_only_release_and_restore()
    {
        var rig = new PcBorrowRig().ProveAll();
        var settings = PcSettings.Default with { Enabled = true, RealInput = true };
        var window = new PcWindowInfo(PcBorrowRig.Target, "Paint", "MSPaintApp", 42, "c:\\windows\\mspaint.exe");
        rig.Host.OnSend = _ => { rig.Host.OnSend = null; settings = settings with { RealInput = false }; };
        var plan = Assert.Single(new PcBorrowRung(() => settings, rig.Lease).Offer(new PcInputRequest(PcInputKind.Drag, window)
            { Path = [new PcPoint(10, 10), new PcPoint(50, 50)], HoldMs = 100 }, PcFramework.Win32).Plans);
        var task = plan.Run(CancellationToken.None);
        for (var i = 0; i < 40 && !task.IsCompleted; i++)
        {
            await PcTestWait.Until(() => task.IsCompleted || rig.Clock.Pending > 0);
            rig.Clock.Advance(25);
            await Task.Yield();
        }
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PcErrorCode.NeedsRealInput, result.Error);
        Assert.Equal(2, rig.Host.Sent.Count);
        Assert.Equal(Marrow.Services.Port.PcControl.Native.PcInputPlan.LEFTUP, Assert.Single(rig.Host.Sent[1].Events).Flags);
        Assert.False(rig.Hold.Swallowing);
        Assert.Equal(PcBorrowRig.User, rig.Host.Foreground);
    }
}
