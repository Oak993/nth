using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Native;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY: Tactus, PC control (ED-NEW-pcctl-18). A borrow on fakes: a healthy monitor with the user idle in their own
// window, a fake host and hold, the capability store (nothing proven until a test proves it), the run control, and a
// manual clock a test drives in poll-sized steps.
public sealed class PcBorrowRig
{
    public const string Paint = "exe:c:\\windows\\mspaint.exe";
    public const string Excel = "exe:c:\\office\\excel.exe";
    public const nint User = 0x100;
    public const nint UserControl = 0x101;
    public const nint Target = 0x200;
    public const nint TargetControl = 0x201;
    public const nint Other = 0x300;

    /// <param name="inlineClock">The clock runs a due delay's continuation inside Advance (<see cref="PcManualClock"/>).</param>
    public PcBorrowRig(PcForegroundProtection protection = PcForegroundProtection.None, bool inlineClock = false)
    {
        Clock = new PcManualClock(inlineClock);
        Clock.Advance(10_000);
        Protection = new PcFixedProtection(protection);
        Monitor = new PhysicalInputMonitor(Clock, Protection);
        Monitor.Installed(true);
        Monitor.LastInputSeen(Clock.NowMs);
        Host = new PcFakeRealInputHost(Monitor, Clock)
        {
            Foreground = User,
            FocusedControl = UserControl,
            Under = _ => Target,
        };
        Host.FocusOf[Target] = TargetControl;
        Host.FocusOf[User] = UserControl;
        Lease = new RealInputLease(Monitor, Hold, Host, Clock, Capabilities, () => CardUp, Control);
    }

    public PcManualClock Clock { get; }

    public PcFixedProtection Protection { get; }

    public PhysicalInputMonitor Monitor { get; }

    public PcFakeInputHold Hold { get; } = new();

    public PcFakeRealInputHost Host { get; }

    public PcCapabilities Capabilities { get; } = new(() => DateTimeOffset.UnixEpoch);

    public PcRunControl Control { get; } = new();

    public RealInputLease Lease { get; }

    public bool CardUp { get; set; }

    /// <summary>Records the NP-4 proof of a way (for every app unless an exe is given).</summary>
    public PcBorrowRig Prove(string method, string exe = PcBorrowProofs.AnyApp)
    {
        Capabilities.Passed(PcBorrowProofs.Key(exe, method));
        return this;
    }

    public PcBorrowRig ProveAll() => Prove(PcBorrowProofs.Pointer).Prove(PcBorrowProofs.Foreground).Prove(PcBorrowProofs.Restore);

    public PcBorrowRequest Click(string stream = Paint, PcPoint? at = null)
    {
        var point = at ?? new PcPoint(300, 200);
        return new PcBorrowRequest(stream, PcBorrowKind.Pointer, Target, "mspaint.exe")
        {
            Chunks = [PcInputPlan.Click(point, Host.Desktop(), PcMouseButton.Left, 1, [], Host.ScanCode)],
            Point = point,
            Detail = "one click in mspaint",
        };
    }

    public PcBorrowRequest Keys(string stream = Paint, params ushort[] keys)
    {
        var events = keys.SelectMany(k => new[] { PcInputEvent.Key(k, Host.ScanCode(k), 0), PcInputEvent.Key(k, Host.ScanCode(k), PcInputPlan.KEY_UP) }).ToList();
        return new PcBorrowRequest(stream, PcBorrowKind.Focus, Target, "mspaint.exe") { Chunks = [events], Detail = "keys in mspaint" };
    }

    /// <summary>The user's physical event as the hook reports it: delivered (counted) or swallowed by the hold.</summary>
    public void UserInput(PcRawInput raw)
    {
        if (Hold.Swallowing && (raw.Kind == PcRawKind.Key || !Hold.KeysOnly))
        {
            Hold.CountSwallowed();
            Monitor.Record(raw with { Swallowed = true });
            return;
        }
        Hold.Deliver();
        Monitor.Record(raw);
    }

    public PcRawInput Key(ushort code, bool down) => new(Clock.NowMs, PcRawKind.Key, down, code, false, 0) { Window = Host.Foreground, Scan = (ushort)(code + 0x100) };

    public PcRawInput Move(int dx, int dy) => new(Clock.NowMs, PcRawKind.Move, false, 0, false, 0) { Window = Host.Foreground, Moved = new PcPoint(dx, dy) };

    /// <summary>Advances the clock in poll-sized steps until the borrow ends (or the step budget runs out).</summary>
    public async Task<PcBorrowOutcome> Drive(Task<PcBorrowOutcome> borrow, int stepMs = RealInputLease.PollMs, int steps = 400, Action? each = null)
    {
        for (var i = 0; i < steps && !borrow.IsCompleted; i++)
        {
            await PcTestWait.Until(() => borrow.IsCompleted || Clock.Pending > 0);
            if (borrow.IsCompleted) break;
            each?.Invoke();
            Clock.Advance(stepMs);
            await Task.Yield();
        }
        return await borrow.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
