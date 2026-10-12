// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-17; DESIGN.md 2.7 "Hook health", "Hold the user's input";
// critiques C2, C3, C17). What the low-level hook callbacks do, pure and allocation-free so the [UnmanagedCallersOnly]
// callback can run it in microseconds: keep which keys and buttons are physically down (a bitset, no history), decide
// whether a borrow swallows the event (pair-aware: an up is swallowed only if its down was; an auto-repeat of a key held
// before the hold asks the borrow to abort), and write one fixed-size record into a lock-free ring for the reader thread.
// A key's code is written only for Esc, a Tactus chord key, a modifier, a swallowed key (for the replay) or a tagged
// injected key (for the borrow's ledger); every other key is recorded as "a key", with no code.
using System.Runtime.InteropServices;

namespace Marrow.Services.Port.PcControl;

/// <summary>One hook event as the callback records it (fixed size; no references).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct PcHookRecord
{
    /// <summary>The event's time as the hook gives it (GetTickCount milliseconds).</summary>
    public uint Time;
    public PcRawKind Kind;
    public bool Down;
    public bool Injected;
    public bool Repeat;
    public bool Swallowed;

    /// <summary>A press, its up or a move while it is held that grabbed the moving pill (swallowed only for the press and its up).</summary>
    public bool Grabbed;

    /// <summary>LLKHF_EXTENDED: the right-hand Ctrl and Alt, the arrows and the editing keys (kept for the replay).</summary>
    public bool Extended;
    public byte Modifiers;
    public byte HeldKeys;
    public byte HeldButtons;
    public ushort Code;
    public ushort Scan;
    public int X;
    public int Y;

    /// <summary>A swallowed move's delta from the last delivered position; a wheel's delta.</summary>
    public int Dx;
    public int Dy;
    public ulong Tag;
}

/// <summary>A single-producer, single-consumer ring of hook records: the hook thread writes, the reader thread reads.</summary>
public sealed class PcInputRing
{
    private readonly PcHookRecord[] items;
    private readonly long mask;
    private long head;
    private long tail;
    private long dropped;

    public PcInputRing(int capacityPowerOfTwo = 4096)
    {
        if (capacityPowerOfTwo < 2 || (capacityPowerOfTwo & (capacityPowerOfTwo - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(capacityPowerOfTwo), "the ring's size is a power of two");
        items = new PcHookRecord[capacityPowerOfTwo];
        mask = capacityPowerOfTwo - 1;
    }

    /// <summary>Records the ring could not hold (the reader fell behind); the heartbeat then sees input the hooks missed.</summary>
    public long Dropped => Interlocked.Read(ref dropped);

    public bool Write(in PcHookRecord record)
    {
        var at = Volatile.Read(ref head);
        if (at - Volatile.Read(ref tail) >= items.Length)
        {
            Interlocked.Increment(ref dropped);
            return false;
        }
        items[at & mask] = record;
        Volatile.Write(ref head, at + 1);
        return true;
    }

    public bool TryRead(out PcHookRecord record)
    {
        var at = Volatile.Read(ref tail);
        if (at >= Volatile.Read(ref head))
        {
            record = default;
            return false;
        }
        record = items[at & mask];
        Volatile.Write(ref tail, at + 1);
        return true;
    }
}

/// <summary>
/// The borrow's hold on the user's input (DESIGN.md 2.7 step 4): swallowing on and off, keys only after a failed restore,
/// the counts the re-verify and the catch-up read, and the abort an auto-repeat asks for.
/// </summary>
public interface IPcInputHold
{
    bool Swallowing { get; }

    bool AbortRequested { get; }

    /// <summary>Physical events the hooks let through so far (any change since the gate means input arrived in the gap).</summary>
    long Delivered { get; }

    /// <summary>Events swallowed so far (the borrow waits until it has received them all before it replays).</summary>
    long SwallowedCount { get; }

    void BeginSwallow(bool keysOnly = false);

    void EndSwallow();
}

public sealed class PcHookCore : IPcInputHold
{
    public const ushort Escape = 0x1B;

    private readonly ulong[] watched = new ulong[4];
    private readonly ulong[] down = new ulong[4];
    private readonly ulong[] swallowedKeys = new ulong[4];
    private int buttons;
    private int swallowedButtons;
    private int grabbedButtons;
    private IPcGrabZone? grab;
    private int heldKeys;
    private int swallowing;
    private int abort;
    private long delivered;
    private long swallowedCount;
    private int lastX;
    private int lastY;
    private bool positionKnown;

    /// <summary>Holding the user's input for a borrow (set by the borrow, read by the callback).</summary>
    public bool Swallowing => Volatile.Read(ref swallowing) != 0;

    /// <summary>Holding the user's keys only: the mouse is free (after a failed restore, DESIGN.md 2.7 step 7).</summary>
    public bool KeysOnly => Volatile.Read(ref swallowing) == 2;

    public long Delivered => Interlocked.Read(ref delivered);

    public long SwallowedCount => Interlocked.Read(ref swallowedCount);

    /// <summary>An auto-repeat of a key held before the hold arrived: the borrow must abort and replay.</summary>
    public bool AbortRequested => Volatile.Read(ref abort) != 0;

    /// <summary>
    /// The moving pill's grab zone (DESIGN.md 10.3 "Grab in flight"): outside a borrow, a physical mouse press (not pen or
    /// touch) that lands on it is swallowed with its up, and the records from the press to its up are marked Grabbed.
    /// </summary>
    public IPcGrabZone? Grab
    {
        get => Volatile.Read(ref grab);
        set => Volatile.Write(ref grab, value);
    }

    /// <summary>Marks the Tactus chord keys (their codes are recorded); call from any thread.</summary>
    public void Watch(Func<ushort, bool> isChordKey)
    {
        ArgumentNullException.ThrowIfNull(isChordKey);
        for (var word = 0; word < 4; word++)
        {
            ulong bits = 0;
            for (var bit = 0; bit < 64; bit++)
            {
                if (isChordKey((ushort)((word * 64) + bit))) bits |= 1UL << bit;
            }
            Volatile.Write(ref watched[word], bits);
        }
    }

    /// <summary>
    /// Swallowing on: from now every physical event (or, keys only, every key) is held back until EndSwallow. Switching an
    /// active hold to keys only keeps the keys it holds, so their ups are still swallowed.
    /// </summary>
    public void BeginSwallow(bool keysOnly = false)
    {
        if (!Swallowing)
        {
            Array.Clear(swallowedKeys);
            Volatile.Write(ref swallowedButtons, 0);
            Volatile.Write(ref abort, 0);
        }
        Volatile.Write(ref swallowing, keysOnly ? 2 : 1);
    }

    public void EndSwallow()
    {
        Volatile.Write(ref swallowing, 0);
        Array.Clear(swallowedKeys);
        Volatile.Write(ref swallowedButtons, 0);
        Volatile.Write(ref abort, 0);
    }

    /// <summary>A keyboard event. <paramref name="swallow"/> tells the callback to return 1 (held for the replay).</summary>
    public PcHookRecord Key(uint vk, uint scan, bool isDown, bool injected, ulong extra, uint time, out bool swallow, bool extended = false)
    {
        var key = (int)(vk & 0xFF);
        var word = key >> 6;
        var bit = 1UL << (key & 63);
        var physical = !injected;
        var wasDown = (down[word] & bit) != 0;
        var repeat = isDown && physical && wasDown;
        if (physical)
        {
            if (isDown && !wasDown)
            {
                down[word] |= bit;
                heldKeys++;
            }
            else if (!isDown && wasDown)
            {
                down[word] &= ~bit;
                heldKeys--;
            }
        }
        swallow = false;
        if (Swallowing && (physical || extra == 0))
        {
            var held = (swallowedKeys[word] & bit) != 0;
            if (isDown)
            {
                if (repeat && !held) Volatile.Write(ref abort, 1);
                else
                {
                    swallowedKeys[word] |= bit;
                    swallow = true;
                }
            }
            else if (held)
            {
                swallowedKeys[word] &= ~bit;
                swallow = true;
            }
        }
        Count(physical, swallow);
        var keep = key == Escape || (Volatile.Read(ref watched[word]) & bit) != 0 || Modifier(key) != 0 || swallow || (injected && extra != 0);
        return new PcHookRecord
        {
            Time = time,
            Kind = PcRawKind.Key,
            Down = isDown,
            Injected = injected,
            Repeat = repeat,
            Swallowed = swallow,
            Extended = extended,
            Modifiers = Modifiers(),
            HeldKeys = (byte)Math.Clamp(heldKeys, 0, 255),
            HeldButtons = (byte)buttons,
            Code = keep ? (ushort)key : (ushort)0,
            Scan = swallow ? (ushort)scan : (ushort)0,
            Tag = injected ? extra : 0,
        };
    }

    /// <summary>A mouse event: a move, a button (1 left, 2 right, 3 middle, 4 X1, 5 X2) or a wheel (its delta; button 1 for the horizontal wheel).</summary>
    public PcHookRecord Mouse(PcRawKind kind, ushort button, bool isDown, int x, int y, int wheel, bool injected, ulong extra, uint time, out bool swallow)
    {
        var physical = !injected;
        var mask = kind == PcRawKind.Button && button is >= 1 and <= 5 ? 1 << (button - 1) : 0;
        if (kind == PcRawKind.Button && physical && mask != 0)
            buttons = isDown ? buttons | mask : buttons & ~mask;
        swallow = false;
        var grabbed = false;
        var dx = kind == PcRawKind.Wheel ? wheel : 0;
        var dy = 0;
        if (grabbedButtons != 0 || (physical && kind == PcRawKind.Button && isDown && mask != 0 && !Swallowing && !PcGrabZone.FromPenOrTouch(extra)))
        {
            // The grab in flight: a press on the moving pill, its up, and the moves between them belong to the pill's drag.
            if (kind == PcRawKind.Button && isDown && grabbedButtons == 0 && Grab?.Contains(x, y, time) == true)
            {
                grabbedButtons = mask;
                grabbed = swallow = true;
            }
            else if (kind == PcRawKind.Button && !isDown && (grabbedButtons & mask) != 0)
            {
                grabbedButtons &= ~mask;
                grabbed = swallow = true;
            }
            else if (grabbedButtons != 0 && kind == PcRawKind.Move && physical)
            {
                grabbed = true;
            }
        }
        if (!grabbed && Swallowing && !KeysOnly && (physical || extra == 0))
        {
            switch (kind)
            {
                case PcRawKind.Move:
                    swallow = true;
                    if (positionKnown)
                    {
                        dx = x - lastX;
                        dy = y - lastY;
                    }
                    break;
                case PcRawKind.Wheel:
                    swallow = true;
                    break;
                case PcRawKind.Button when isDown:
                    swallowedButtons |= mask;
                    swallow = true;
                    break;
                case PcRawKind.Button when (swallowedButtons & mask) != 0:
                    swallowedButtons &= ~mask;
                    swallow = true;
                    break;
            }
        }
        Count(physical, swallow && !grabbed);
        if (!swallow)
        {
            // A swallowed move does not move the cursor, so later deltas are measured from the last delivered position.
            lastX = x;
            lastY = y;
            positionKnown = true;
        }
        return new PcHookRecord
        {
            Time = time,
            Kind = kind,
            Down = isDown,
            Injected = injected,
            Swallowed = swallow && !grabbed,
            Grabbed = grabbed,
            Modifiers = Modifiers(),
            HeldKeys = (byte)Math.Clamp(heldKeys, 0, 255),
            HeldButtons = (byte)buttons,
            Code = kind is PcRawKind.Button or PcRawKind.Wheel ? button : (ushort)0,
            X = x,
            Y = y,
            Dx = dx,
            Dy = dy,
            Tag = injected ? extra : 0,
        };
    }

    /// <summary>Ctrl (2), Alt (1), Shift (4) and Windows (8) physically down, as PcHotkeyModifiers bits.</summary>
    public byte Modifiers()
    {
        var bits = 0;
        if (Down(0x11) || Down(0xA2) || Down(0xA3)) bits |= 2;
        if (Down(0x12) || Down(0xA4) || Down(0xA5)) bits |= 1;
        if (Down(0x10) || Down(0xA0) || Down(0xA1)) bits |= 4;
        if (Down(0x5B) || Down(0x5C)) bits |= 8;
        return (byte)bits;
    }

    private void Count(bool physical, bool swallow)
    {
        if (swallow) Interlocked.Increment(ref swallowedCount);
        else if (physical) Interlocked.Increment(ref delivered);
    }

    private bool Down(int key) => (down[key >> 6] & (1UL << (key & 63))) != 0;

    private static int Modifier(int key) => key is 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C ? 1 : 0;
}
