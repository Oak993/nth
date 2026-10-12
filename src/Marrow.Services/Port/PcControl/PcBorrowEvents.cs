// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-18; DESIGN.md 2.7 steps 4 to 7, critiques C3, C17). The pure parts
// of a borrow's input: the hook events each SendInput entry produces (the ledger the monitor matches Tactus's events
// against), the ups a borrow still owes, the user's input held during a borrow (moves summed, everything else in order),
// and that input turned back into SendInput entries for the replay (keys with their own codes, so an IME composes them as
// typed; buttons and wheels at the user's cursor).
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl.Native;
using NativeKind = Marrow.Services.Port.PcControl.Native.PcInputKind;

namespace Marrow.Services.Port.PcControl;

public static class PcBorrowEvents
{
    /// <summary>VK_PACKET: what the keyboard hook reports for a KEYEVENTF_UNICODE character.</summary>
    public const ushort Packet = 0xE7;

    public const uint XDOWN = 0x0080;
    public const uint XUP = 0x0100;

    /// <summary>The hook events one SendInput entry produces, in order.</summary>
    public static IEnumerable<PcLedgerEvent> Ledger(PcInputEvent entry)
    {
        if (entry.Kind == NativeKind.Key)
        {
            var vk = (entry.Flags & PcInputPlan.KEY_UNICODE) != 0 ? Packet : (ushort)(entry.Vk & 0xFF);
            yield return PcLedgerEvent.Key(vk, (entry.Flags & PcInputPlan.KEY_UP) == 0);
            yield break;
        }
        var flags = entry.Flags;
        if ((flags & PcInputPlan.MOVE) != 0) yield return PcLedgerEvent.Move();
        if ((flags & PcInputPlan.LEFTDOWN) != 0) yield return PcLedgerEvent.Button(1, true);
        if ((flags & PcInputPlan.LEFTUP) != 0) yield return PcLedgerEvent.Button(1, false);
        if ((flags & PcInputPlan.RIGHTDOWN) != 0) yield return PcLedgerEvent.Button(2, true);
        if ((flags & PcInputPlan.RIGHTUP) != 0) yield return PcLedgerEvent.Button(2, false);
        if ((flags & PcInputPlan.MIDDLEDOWN) != 0) yield return PcLedgerEvent.Button(3, true);
        if ((flags & PcInputPlan.MIDDLEUP) != 0) yield return PcLedgerEvent.Button(3, false);
        var x = (ushort)(entry.MouseData == 2 ? 5 : 4);
        if ((flags & XDOWN) != 0) yield return PcLedgerEvent.Button(x, true);
        if ((flags & XUP) != 0) yield return PcLedgerEvent.Button(x, false);
        if ((flags & (PcInputPlan.WHEEL | PcInputPlan.HWHEEL)) != 0) yield return PcLedgerEvent.Wheel();
    }

    public static List<PcLedgerEvent> Ledger(IEnumerable<IReadOnlyList<PcInputEvent>> arrays) => arrays.SelectMany(a => a.SelectMany(Ledger)).ToList();

    /// <summary>One held user event as a SendInput entry for the replay; null for a move (the cursor carries moves).</summary>
    public static PcInputEvent? Replay(PcRawInput input, Func<ushort, ushort> scan)
    {
        switch (input.Kind)
        {
            case PcRawKind.Key:
                if (input.Code == 0) return null;
                var flags = (input.Extended ? PcInputPlan.KEY_EXTENDED : 0) | (input.Down ? 0 : PcInputPlan.KEY_UP);
                return PcInputEvent.Key(input.Code, input.Scan != 0 ? input.Scan : scan(input.Code), flags);
            case PcRawKind.Button:
                return input.Code switch
                {
                    1 => PcInputEvent.Mouse(0, 0, 0, input.Down ? PcInputPlan.LEFTDOWN : PcInputPlan.LEFTUP),
                    2 => PcInputEvent.Mouse(0, 0, 0, input.Down ? PcInputPlan.RIGHTDOWN : PcInputPlan.RIGHTUP),
                    3 => PcInputEvent.Mouse(0, 0, 0, input.Down ? PcInputPlan.MIDDLEDOWN : PcInputPlan.MIDDLEUP),
                    4 => PcInputEvent.Mouse(0, 0, 1, input.Down ? XDOWN : XUP),
                    5 => PcInputEvent.Mouse(0, 0, 2, input.Down ? XDOWN : XUP),
                    _ => null,
                };
            case PcRawKind.Wheel:
                return PcInputEvent.Mouse(0, 0, input.Delta, input.Code == 1 ? PcInputPlan.HWHEEL : PcInputPlan.WHEEL);
            default:
                return null;
        }
    }
}

/// <summary>What a borrow pressed and has not let go: the ups it owes, sent on every path out (DESIGN.md 2.7 step 6).</summary>
public sealed class PcPressed
{
    private readonly Lock gate = new();
    private readonly List<(ushort Vk, ushort Scan, uint Extended)> keys = [];
    private readonly List<(uint Up, int Data)> buttons = [];

    public bool Any
    {
        get
        {
            lock (gate) return keys.Count > 0 || buttons.Count > 0;
        }
    }

    public void Track(IReadOnlyList<PcInputEvent> events)
    {
        lock (gate)
        {
            foreach (var e in events)
            {
                if (e.Kind == NativeKind.Key)
                {
                    if ((e.Flags & PcInputPlan.KEY_UNICODE) != 0) continue;
                    var up = (e.Flags & PcInputPlan.KEY_UP) != 0;
                    var index = keys.FindIndex(k => k.Vk == e.Vk);
                    if (!up && index < 0) keys.Add((e.Vk, e.Scan, e.Flags & PcInputPlan.KEY_EXTENDED));
                    else if (up && index >= 0) keys.RemoveAt(index);
                    continue;
                }
                Button(e.Flags, PcInputPlan.LEFTDOWN, PcInputPlan.LEFTUP, 0);
                Button(e.Flags, PcInputPlan.RIGHTDOWN, PcInputPlan.RIGHTUP, 0);
                Button(e.Flags, PcInputPlan.MIDDLEDOWN, PcInputPlan.MIDDLEUP, 0);
                Button(e.Flags, PcBorrowEvents.XDOWN, PcBorrowEvents.XUP, e.MouseData);
            }
        }
    }

    /// <summary>The ups still owed, buttons first, keys in the reverse of their order down; clears them.</summary>
    public List<PcInputEvent> TakeUps()
    {
        lock (gate)
        {
            var ups = buttons.Select(b => PcInputEvent.Mouse(0, 0, b.Data, b.Up)).ToList();
            for (var i = keys.Count - 1; i >= 0; i--) ups.Add(PcInputEvent.Key(keys[i].Vk, keys[i].Scan, keys[i].Extended | PcInputPlan.KEY_UP));
            keys.Clear();
            buttons.Clear();
            return ups;
        }
    }

    private void Button(uint flags, uint down, uint up, int data)
    {
        if ((flags & down) != 0 && !buttons.Contains((up, data))) buttons.Add((up, data));
        if ((flags & up) != 0) buttons.Remove((up, data));
    }
}

/// <summary>
/// The user's input held while a borrow swallows it: moves summed into one delta, every other event in arrival order. It
/// lives in memory only for the replay, is zeroed right after, and is never logged (critique C17).
/// </summary>
public sealed class PcHeldInput
{
    private readonly Lock gate = new();
    private readonly List<PcRawInput> events = [];
    private int dx;
    private int dy;
    private long received;

    /// <summary>How many swallowed events arrived (moves included), for the catch-up with the hook's count.</summary>
    public long Received => Interlocked.Read(ref received);

    public PcPoint Delta
    {
        get
        {
            lock (gate) return new PcPoint(dx, dy);
        }
    }

    public int Keys
    {
        get
        {
            lock (gate) return events.Count(e => e.Kind == PcRawKind.Key && e.Down);
        }
    }

    public void Add(PcRawInput input)
    {
        lock (gate)
        {
            if (input.Kind == PcRawKind.Move)
            {
                dx += input.Moved.X;
                dy += input.Moved.Y;
            }
            else
            {
                events.Add(input);
            }
        }
        Interlocked.Increment(ref received);
    }

    /// <summary>Counts a swallowed event that is not replayed (Esc in a borrow, a Tactus chord).</summary>
    public void Drop() => Interlocked.Increment(ref received);

    /// <summary>The held events (not moves) in order, taken out of the queue.</summary>
    public List<PcRawInput> Take()
    {
        lock (gate)
        {
            var taken = events.ToList();
            events.Clear();
            return taken;
        }
    }

    /// <summary>Takes the summed move (the cursor goes to the saved point plus this).</summary>
    public PcPoint TakeDelta()
    {
        lock (gate)
        {
            var delta = new PcPoint(dx, dy);
            dx = dy = 0;
            return delta;
        }
    }

    /// <summary>Overwrites and drops everything held.</summary>
    public void Zero()
    {
        lock (gate)
        {
            for (var i = 0; i < events.Count; i++) events[i] = default;
            events.Clear();
            dx = dy = 0;
        }
    }
}
