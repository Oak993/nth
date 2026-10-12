using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Native;
using NativeKind = Marrow.Services.Port.PcControl.Native.PcInputKind;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY: Tactus, PC control (ED-NEW-pcctl-18). The Windows side of a borrow, played by a test: a foreground and focus
// it moves the way Windows would (a click activates the window under it, SetForegroundWindow and the restore move it when
// allowed), every SendInput array recorded with its tag and handed to the monitor as the hook would see it. Nothing here
// touches Windows.
public sealed class PcFakeRealInputHost(PhysicalInputMonitor monitor, IPcClock clock) : IRealInputHost
{
    public nint Foreground { get; set; }

    public nint FocusedControl { get; set; }

    public PcPoint Cursor { get; set; } = new(500, 500);

    public Func<PcPoint, nint> Under { get; set; } = _ => 0;

    public PcPoint? CaptionPoint { get; set; }

    public bool KeysDown { get; set; }

    public long? LastInput { get; set; }

    public bool ForegroundAllowed { get; set; } = true;

    /// <summary>How many restores fail before one works (int.MaxValue: they never do).</summary>
    public int RestoreFailures { get; set; }

    /// <summary>The control that takes focus when a window comes in front by SetForegroundWindow.</summary>
    public Dictionary<nint, nint> FocusOf { get; } = [];

    public List<(IReadOnlyList<PcInputEvent> Events, ulong Tag)> Sent { get; } = [];

    /// <summary>The window in front when each array was sent.</summary>
    public List<nint> SentWhileFront { get; } = [];

    public List<string> Calls { get; } = [];

    /// <summary>Runs after each array is sent (a test's user acts during the borrow).</summary>
    public Action<IReadOnlyList<PcInputEvent>>? OnSend { get; set; }

    public PcBorrowSaved? Saved { get; private set; }

    public PcRect Desktop() => new(0, 0, 1920, 1080);

    public ushort ScanCode(ushort vk) => (ushort)(vk + 0x100);

    public PcBorrowSaved Save(nint target)
    {
        Calls.Add("save");
        Saved = new PcBorrowSaved(Cursor, Foreground, FocusedControl) { Neighbour = 0x900, Above = [0x900, 0x901] };
        return Saved;
    }

    public (nint Foreground, nint Focus) Focus() => (Foreground, FocusedControl);

    public bool Held() => KeysDown;

    public long? LastInputMs() => LastInput;

    public nint WindowAt(PcPoint point) => Under(point);

    public PcPoint? Caption(nint target) => CaptionPoint;

    public void Raise(nint target) => Calls.Add("raise");

    public bool SetForeground(nint window)
    {
        Calls.Add("set-foreground");
        if (!ForegroundAllowed) return false;
        Foreground = window;
        FocusedControl = FocusOf.GetValueOrDefault(window);
        return true;
    }

    public int Send(IReadOnlyList<PcInputEvent> events, ulong tag)
    {
        Sent.Add((events.ToList(), tag));
        SentWhileFront.Add(Foreground);
        foreach (var e in events)
        {
            if (e.Kind == NativeKind.Mouse && (e.Flags & PcInputPlan.MOVE) != 0)
                Cursor = PcPointFromAbsolute(e);
            if (e.Kind == NativeKind.Mouse && (e.Flags & (PcInputPlan.LEFTDOWN | PcInputPlan.RIGHTDOWN)) != 0 && Under(Cursor) is var hit && hit != 0)
            {
                Foreground = hit;
                FocusedControl = FocusOf.GetValueOrDefault(hit);
            }
            foreach (var hook in PcBorrowEvents.Ledger(e))
                monitor.Record(new PcRawInput(clock.NowMs, hook.Kind, hook.Down, hook.Code, true, tag) { Window = Foreground });
        }
        OnSend?.Invoke(events);
        return events.Count;
    }

    public bool Restore(nint window, nint focus)
    {
        Calls.Add("restore");
        if (RestoreFailures > 0)
        {
            RestoreFailures--;
            return false;
        }
        Foreground = window;
        FocusedControl = focus;
        return true;
    }

    public bool RestoreZOrder(nint target, PcBorrowSaved saved)
    {
        Calls.Add("zorder");
        return true;
    }

    public void RestorePlacement(nint target, PcBorrowSaved saved) => Calls.Add("placement");

    public PcPoint Clamp(PcPoint point) => new(Math.Clamp(point.X, 0, 1919), Math.Clamp(point.Y, 0, 1079));

    public void SetCursor(PcPoint point)
    {
        Calls.Add("cursor");
        Cursor = point;
    }

    /// <summary>The tags of the arrays sent, in order.</summary>
    public IEnumerable<ulong> Tags => Sent.Select(s => s.Tag);

    // Absolute coordinates back to the pixel Windows would put the cursor on.
    private PcPoint PcPointFromAbsolute(PcInputEvent e) => CoordinateMapper.FromAbsolute(e.Dx, e.Dy, Desktop());
}
