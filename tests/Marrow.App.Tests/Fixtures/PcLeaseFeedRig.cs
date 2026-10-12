using Marrow.App.Port.PcControl;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Native;
using Marrow.Services.Tests.Fixtures;
using Marrow.UI.Port.PcControl;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY: Tactus, the pill's feed over a lease that waits and borrows on fakes (ED-NEW-pcctl-38): the assembly on a
// manual clock (a test acts between the gate's polls), every borrow way proven, the user idle in their own window, the
// feed on a fixed pill clock, and the user's input as the hooks report it (swallowed while a hold swallows). As
// PcBorrowRig, on the assembly's own lease.
public sealed class PcLeaseFeedRig : IDisposable
{
    public const string Paint = "exe:c:\\windows\\mspaint.exe";
    public const string Excel = "exe:c:\\office\\excel.exe";
    public const nint User = 0x100;
    public const nint UserControl = 0x101;
    public const nint Target = 0x200;
    public const nint TargetControl = 0x201;

    public static readonly PcStepWords ClickSave = new("click Save", "clicked Save", "one click");

    public PcLeaseFeedRig(PcForegroundProtection protection = PcForegroundProtection.None)
    {
        Clock.Advance(10_000);
        Protection = new PcFixedProtection(protection);
        Assembly = new PcAssemblyRig(PcSettings.Default with { Enabled = true, RealInput = true }, Clock, Protection);
        Lease = Assembly.Build().Lease;
        foreach (var way in new[] { PcBorrowProofs.Pointer, PcBorrowProofs.Foreground, PcBorrowProofs.Restore })
            Assembly.Build().Capabilities.Passed(PcBorrowProofs.Key(PcBorrowProofs.AnyApp, way));
        Host = (PcFakeRealInputHost)Lease.Host;
        Host.Foreground = User;
        Host.FocusedControl = UserControl;
        Host.Under = _ => Target;
        Host.FocusOf[Target] = TargetControl;
        Host.FocusOf[User] = UserControl;
        Cards = new PillAskBridge(Assembly.Build().Asks);
        Feed = new TactusPillFeed(Assembly.Build(), Cards, () => Now);
    }

    public PcManualClock Clock { get; } = new();

    public PcFixedProtection Protection { get; }

    public PcAssemblyRig Assembly { get; }

    public RealInputLease Lease { get; }

    public PcFakeRealInputHost Host { get; }

    public PillAskBridge Cards { get; }

    public TactusPillFeed Feed { get; }

    /// <summary>The pill's clock, seconds.</summary>
    public double Now { get; set; } = 10;

    public PillView View() => PillStates.Compute(Feed.Facts(), Now);

    public PcBorrowRequest Click(string stream = Paint)
    {
        var point = new PcPoint(300, 200);
        return new PcBorrowRequest(stream, PcBorrowKind.Pointer, Target, "mspaint.exe")
        {
            Chunks = [PcInputPlan.Click(point, Host.Desktop(), PcMouseButton.Left, 1, [], Host.ScanCode)],
            Point = point,
            Detail = "one click in mspaint",
            Words = ClickSave,
        };
    }

    /// <summary>The user's physical event as the hook reports it: delivered, or swallowed by the hold.</summary>
    public void UserInput(PcRawInput raw)
    {
        var hold = Assembly.Hold;
        if (hold.Swallowing && (raw.Kind == PcRawKind.Key || !hold.KeysOnly))
        {
            hold.CountSwallowed();
            Assembly.Input.Record(raw with { Swallowed = true });
            return;
        }
        hold.Deliver();
        Assembly.Input.Record(raw);
    }

    public PcRawInput Key(ushort code, bool down) => new(Clock.NowMs, PcRawKind.Key, down, code, false, 0) { Window = Host.Foreground, Scan = (ushort)(code + 0x100), HeldKeys = down ? 1 : 0 };

    public PcRawInput Move(int dx, int dy) => new(Clock.NowMs, PcRawKind.Move, false, 0, false, 0) { Window = Host.Foreground, Moved = new PcPoint(dx, dy) };

    /// <summary>Advances the clock in poll-sized steps until the task ends (or the step budget runs out).</summary>
    public async Task<T> Drive<T>(Task<T> task, int stepMs = RealInputLease.PollMs, int steps = 400)
    {
        for (var i = 0; i < steps && !task.IsCompleted; i++)
        {
            await PcTestWait.Until(() => task.IsCompleted || Clock.Pending > 0);
            if (task.IsCompleted) break;
            Clock.Advance(stepMs);
            await Task.Yield();
        }
        return await task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        Feed.Dispose();
        Cards.Dispose();
        Assembly.Dispose();
    }
}
