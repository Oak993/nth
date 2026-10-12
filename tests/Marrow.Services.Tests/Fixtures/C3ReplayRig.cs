using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Tests.Fixtures;

public static class C3ReplayRig
{
    public static Task Keep(PcBorrowRig rig, nint window, nint focus, ushort key, int count = 1) =>
        Keep(rig, window, focus, Enumerable.Repeat(key, count).ToArray());

    public static async Task Keep(PcBorrowRig rig, nint window, nint focus, IReadOnlyList<ushort> keys)
    {
        rig.Host.Foreground = window;
        rig.Host.FocusedControl = focus;
        rig.Host.RestoreFailures = int.MaxValue;
        rig.Host.OnSend = _ =>
        {
            rig.Host.OnSend = null;
            foreach (var key in keys)
            {
                rig.UserInput(rig.Key(key, true));
                rig.UserInput(rig.Key(key, false));
            }
        };
        await rig.Drive(rig.Lease.Borrow(rig.Click(), CancellationToken.None));
        rig.Host.Foreground = window;
        rig.Host.FocusedControl = focus;
        await PcTestWait.Until(() => rig.Clock.Pending > 0);
        rig.Clock.Advance(RealInputLease.KeyHoldPollMs);
        await PcTestWait.Until(() => !rig.Lease.HoldingKeys);
        rig.Host.Sent.Clear();
        rig.Host.SentWhileFront.Clear();
    }
}
