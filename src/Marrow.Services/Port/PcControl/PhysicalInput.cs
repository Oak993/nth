// PORT-ONLY: Tactus, PC control (OD-0144, OD-0145, ED-NEW-pcctl-7, ED-NEW-pcctl-17; DESIGN.md 2.7, 2.9, 10.1, 10.2). What
// the physical input monitor reads and keeps. One input event as the hook reader hands it on (with its window resolved),
// what a key-down tells the chords, the state the gate and the same-window rules read, and the seam the engine, the
// borrow and the chords use. Outside a borrow nothing here keeps a key code beyond "is it held right now".
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl;

public enum PcRawKind : byte
{
    Move,
    Button,
    Wheel,
    Key,
}

/// <summary>
/// One input event, as the hook reader hands it on: its time on the monitor's clock, kind, down or up, the key or button
/// code (0 for a key Tactus does not watch, outside a borrow), the injected flag and dwExtraInfo, and the window it went to
/// (the root under the pointer for the mouse, the foreground root for a key) with the key's focused control.
/// </summary>
public readonly record struct PcRawInput(long TimeMs, PcRawKind Kind, bool Down, ushort Code, bool Injected, ulong Tag)
{
    public PcPoint Point { get; init; }

    public nint Window { get; init; }

    public nint Focus { get; init; }

    /// <summary>The hook held this event back (a borrow is swallowing); it reaches the user's window only by the replay.</summary>
    public bool Swallowed { get; init; }

    /// <summary>A key-down of a key already held (auto-repeat).</summary>
    public bool Repeat { get; init; }

    /// <summary>The scan code, kept only for a swallowed key (for the replay).</summary>
    public ushort Scan { get; init; }

    /// <summary>The key's extended flag (right Ctrl and Alt, arrows, editing keys), for the replay.</summary>
    public bool Extended { get; init; }

    /// <summary>A move's delta from the last position the cursor reached (a swallowed move does not move it).</summary>
    public PcPoint Moved { get; init; }

    /// <summary>A wheel's delta; its code is 1 for the horizontal wheel.</summary>
    public int Delta { get; init; }

    /// <summary>Ctrl, Alt, Shift and Windows physically down at this event (PcHotkeyModifiers bits).</summary>
    public int Modifiers { get; init; }

    /// <summary>How many keys were physically down after this event.</summary>
    public int HeldKeys { get; init; }

    /// <summary>Mouse buttons physically down after this event (bit 1 left, 2 right, 4 middle, 8 X1, 16 X2).</summary>
    public int HeldButtons { get; init; }
}

/// <summary>What a key-down tells the chords and the Esc counter.</summary>
public enum PcHookKey
{
    Escape,
    Chord,
    Other,
}

/// <summary>One key-down: its kind, the key code for Esc and chord keys (0 for any other key), and its repeat and injected flags.</summary>
public readonly record struct PcHookKeyDown(PcHookKey Kind, ushort Key, bool Repeat, bool Injected)
{
    /// <summary>The hook held it back for a borrow (a stop key Windows' hotkeys never see).</summary>
    public bool Swallowed { get; init; }

    /// <summary>Ctrl, Alt, Shift and Windows physically down (PcHotkeyModifiers bits).</summary>
    public int Modifiers { get; init; }
}

/// <summary>Why the user's foreground may not be borrowed (DESIGN.md 2.7, critique C1).</summary>
public enum PcForegroundProtection
{
    None,
    Elevated,
    UiAccess,
    SecureDesktop,
    Locked,
    Fullscreen,
    Presentation,
    Busy,
}

/// <summary>Reads whether the foreground may be borrowed now (integrity, UIAccess, the secure desktop, the notification state).</summary>
public interface IForegroundProtection
{
    PcForegroundProtection Read();
}

/// <summary>The monitor's state at one moment (DESIGN.md 2.7 "State kept").</summary>
public sealed record PcPhysicalSnapshot
{
    /// <summary>The hooks are installed, alive and agree with GetLastInputInfo (fail closed otherwise).</summary>
    public bool Healthy { get; init; }

    /// <summary>The newest event the hooks delivered (physical or Tactus's), on the monitor's clock; null before any.</summary>
    public long? NewestMs { get; init; }

    public long? LastPointerMs { get; init; }

    public long? LastKeyMs { get; init; }

    /// <summary>A GetLastInputInfo tick newer than any event the hooks saw (input to an elevated window): user activity then.</summary>
    public long? LastUnseenMs { get; init; }

    public int HeldKeys { get; init; }

    public int HeldButtons { get; init; }

    public PcPoint Pointer { get; init; }

    /// <summary>The root window under the last physical pointer event.</summary>
    public nint PointerWindow { get; init; }

    /// <summary>The foreground root at the last physical key.</summary>
    public nint KeyWindow { get; init; }

    /// <summary>The focused control at the last physical key.</summary>
    public nint KeyFocus { get; init; }

    /// <summary>When the last physical key reached <see cref="KeyWindow"/> (a swallowed key reached no window).</summary>
    public long? KeyWindowMs { get; init; }

    /// <summary>The window whose IME composition is open now; 0 when none is.</summary>
    public nint Composition { get; init; }

    /// <summary>The foreground thread's input language is served by an IME or a TSF text service with no composition signal.</summary>
    public bool ImeLanguage { get; init; }

    /// <summary>The last physical button press: its window and point, while the button is still down.</summary>
    public nint PressedWindow { get; init; }

    public PcPoint PressedPoint { get; init; }

    /// <summary>The newest physical (user's) event of any kind; null before any.</summary>
    public long? LastUserMs => Max(Max(LastPointerMs, LastKeyMs), LastUnseenMs);

    private static long? Max(long? a, long? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);
}

/// <summary>
/// The physical input monitor as the engine, the borrow and the chords use it. Thread-safe. Its hooks are installed only
/// while someone keeps it (a stream working or paused, the pill visible); while not installed it reads unhealthy.
/// </summary>
public interface IPhysicalInput
{
    PcPhysicalSnapshot Snapshot();

    /// <summary>Counts physical input into the window (mouse over it, keys while it is in front): a step compares it with the count it saw.</summary>
    long Version(nint window);

    /// <summary>
    /// Counts the physical key-downs and button presses into the window: input that can change a control's value, state or
    /// focus (a pointer passing over it cannot).
    /// </summary>
    long Presses(nint window);

    /// <summary>When physical input last went into the window (a key while it was in front, a pointer event over it); null if never.</summary>
    long? LastIn(nint window);

    /// <summary>Keeps the hooks installed until disposed.</summary>
    IDisposable Keep();

    /// <summary>Which key codes count as Tactus chord keys (reported by code; any other key is "another key").</summary>
    void WatchChords(Func<ushort, bool> isChordKey);

    /// <summary>Whether the foreground may be borrowed now.</summary>
    PcForegroundProtection Protection();

    /// <summary>Raised for every key-down (not a Tactus-tagged one): Esc, a chord key, or another key with no code.</summary>
    event Action<PcHookKeyDown>? KeyDown;

    /// <summary>Raised when physical input marks a window dirty.</summary>
    event Action<nint>? Dirty;

    event Action<bool>? HealthChanged;

    /// <summary>A swallowed event, for the borrow that holds the user's input.</summary>
    event Action<PcRawInput>? Swallowed;

    /// <summary>Starts matching Tactus-tagged events against a borrow's ledger.</summary>
    void Open(PcInjectionLedger ledger);

    /// <summary>Stops matching (late events still match for a short grace).</summary>
    void Close(PcInjectionLedger ledger);

    /// <summary>The newest GetLastInputInfo tick, on the monitor's clock: any tick newer than the hooks' newest event is the user's.</summary>
    void LastInput(long tickMs);
}
