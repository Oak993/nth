// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-7, ED-NEW-pcctl-17; DESIGN.md 2.7 "Physical input monitor", 2.9,
// critiques C1, C2, C4, C17). The physical input monitor's state machine, pure and on fakes in tests. The hook reader
// hands it every event; it tells the user's input from Tactus's (each borrow's random tag and the exact ledger of what
// that borrow sent; a tagged event not in the ledger, and injected input without a valid tag, count as the user's),
// keeps the times, held state, the pointer's and the keyboard's windows, marks windows dirty, and judges the hooks'
// health against GetLastInputInfo (fail closed: from a missed heartbeat until a fresh event agrees). It never keeps a key
// code: held keys are a count, and chord keys are reported by code only to the chords.
using System.Security.Cryptography;
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl;

/// <summary>
/// The exact events one borrow sends (or replays), under its own random 64-bit tag: a tagged event counts as Tactus's only
/// if it is the next one the ledger expects, inside the borrow's time window.
/// </summary>
public sealed class PcInjectionLedger
{
    /// <summary>How long after the borrow closes its late events still match (the hook reader trails SendInput).</summary>
    public const int GraceMs = 1_000;

    private readonly Lock gate = new();
    private readonly Queue<PcLedgerEvent> expected;

    public PcInjectionLedger(ulong tag, long startMs, IEnumerable<PcLedgerEvent> events, bool replay = false)
    {
        if (tag == 0) throw new ArgumentOutOfRangeException(nameof(tag), "a tag is never 0");
        Tag = tag;
        StartMs = startMs;
        Replay = replay;
        expected = new Queue<PcLedgerEvent>(events);
    }

    public ulong Tag { get; }

    public long StartMs { get; }

    public bool Replay { get; }

    public long? ClosedMs { get; private set; }

    public int Remaining
    {
        get
        {
            lock (gate) return expected.Count;
        }
    }

    /// <summary>A fresh random tag (never 0, never a constant another process could copy).</summary>
    public static ulong NewTag()
    {
        Span<byte> bytes = stackalloc byte[8];
        ulong tag;
        do
        {
            RandomNumberGenerator.Fill(bytes);
            tag = BitConverter.ToUInt64(bytes);
        }
        while (tag == 0);
        return tag;
    }

    /// <summary>Adds events the borrow is about to send (a drag sends in several arrays).</summary>
    public void Expect(IEnumerable<PcLedgerEvent> events)
    {
        lock (gate)
        {
            foreach (var item in events) expected.Enqueue(item);
        }
    }

    internal void Close(long atMs)
    {
        lock (gate) ClosedMs ??= atMs;
    }

    internal bool Expired(long nowMs)
    {
        lock (gate) return ClosedMs is { } closed && nowMs > closed + GraceMs;
    }

    internal bool Accept(PcRawInput input)
    {
        lock (gate)
        {
            if (input.Tag != Tag || input.TimeMs < StartMs) return false;
            if (ClosedMs is { } closed && input.TimeMs > closed + GraceMs) return false;
            if (!expected.TryPeek(out var next) || !next.Matches(input)) return false;
            expected.Dequeue();
            return true;
        }
    }
}

/// <summary>One event a ledger expects: its kind, and for keys and buttons whether it is a down and its code.</summary>
public readonly record struct PcLedgerEvent(PcRawKind Kind, bool Down, ushort Code)
{
    public static PcLedgerEvent Move() => new(PcRawKind.Move, false, 0);

    public static PcLedgerEvent Wheel() => new(PcRawKind.Wheel, false, 0);

    public static PcLedgerEvent Key(ushort vk, bool down) => new(PcRawKind.Key, down, vk);

    public static PcLedgerEvent Button(ushort button, bool down) => new(PcRawKind.Button, down, button);

    internal bool Matches(PcRawInput input) => input.Kind == Kind && (Kind is PcRawKind.Move or PcRawKind.Wheel || (input.Down == Down && input.Code == Code));
}

public sealed class PhysicalInputMonitor : IPhysicalInput
{
    /// <summary>How far GetLastInputInfo may run ahead of the hooks' newest event before they are presumed dead.</summary>
    public const int HealthSlackMs = 50;

    public const ushort Escape = 0x1B;

    private readonly Lock gate = new();
    private readonly IPcClock clock;
    private readonly IForegroundProtection? protection;
    private readonly List<PcInjectionLedger> ledgers = [];
    private readonly Dictionary<nint, long> versions = [];
    private readonly Dictionary<nint, long> presses = [];
    private readonly Dictionary<nint, long> lastIn = [];
    private Func<ushort, bool> chordKey = _ => false;
    private bool installed;
    private bool healthy;
    private long installedMs;
    private long? agreeAt;
    private long? newest;
    private long? lastPointer;
    private long? lastKey;
    private long? lastUnseen;
    private int heldKeys;
    private int heldButtons;
    private PcPoint pointer;
    private nint pointerWindow;
    private nint keyWindow;
    private nint keyFocus;
    private long? keyWindowAt;
    private nint composition;
    private bool imeLanguage;
    private nint pressedWindow;
    private PcPoint pressedPoint;
    private int kept;
    private long? lastClosed;

    public PhysicalInputMonitor(IPcClock clock, IForegroundProtection? protection = null)
    {
        this.clock = clock;
        this.protection = protection;
    }

    public event Action<PcHookKeyDown>? KeyDown;

    public event Action<nint>? Dirty;

    public event Action<bool>? HealthChanged;

    public event Action<PcRawInput>? Swallowed;

    public event Action<PcRawInput>? Grabbed;

    /// <summary>
    /// Each of the user's pointer events (a move, a button, the wheel), swallowed and grabbed ones too (their flags say so),
    /// once the monitor has kept it; on the hook reader's thread. The pill's pointer state reads it: where the pointer is
    /// and whether a press that began in another app still holds (ED-NEW-pcctl-34). Tactus's own input never raises it.
    /// </summary>
    public event Action<PcRawInput>? UserPointer;

    /// <summary>The hooks should be installed (true) or removed (false): someone keeps the monitor, or no one does.</summary>
    public event Action<bool>? KeepChanged;

    /// <summary>The chord keys changed: the hook core records these keys' codes.</summary>
    public event Action<Func<ushort, bool>>? ChordsWatched;

    public Func<ushort, bool> ChordKey
    {
        get
        {
            lock (gate) return chordKey;
        }
    }

    public bool Kept
    {
        get
        {
            lock (gate) return kept > 0;
        }
    }

    public PcPhysicalSnapshot Snapshot()
    {
        lock (gate)
        {
            return new PcPhysicalSnapshot
            {
                Healthy = installed && healthy,
                NewestMs = newest,
                LastPointerMs = lastPointer,
                LastKeyMs = lastKey,
                LastUnseenMs = lastUnseen,
                HeldKeys = heldKeys,
                HeldButtons = heldButtons,
                Pointer = pointer,
                PointerWindow = pointerWindow,
                KeyWindow = keyWindow,
                KeyFocus = keyFocus,
                KeyWindowMs = keyWindowAt,
                Composition = composition,
                ImeLanguage = imeLanguage,
                PressedWindow = pressedWindow,
                PressedPoint = pressedPoint,
            };
        }
    }

    public long Version(nint window)
    {
        lock (gate) return versions.GetValueOrDefault(window);
    }

    public long Presses(nint window)
    {
        lock (gate) return presses.GetValueOrDefault(window);
    }

    public long? LastIn(nint window)
    {
        lock (gate) return lastIn.TryGetValue(window, out var at) ? at : null;
    }

    public IDisposable Keep()
    {
        bool first;
        lock (gate) first = kept++ == 0;
        if (first) KeepChanged?.Invoke(true);
        return new Keeper(this);
    }

    public void WatchChords(Func<ushort, bool> isChordKey)
    {
        ArgumentNullException.ThrowIfNull(isChordKey);
        lock (gate) chordKey = isChordKey;
        ChordsWatched?.Invoke(isChordKey);
    }

    public PcForegroundProtection Protection() => protection?.Read() ?? PcForegroundProtection.None;

    public void Open(PcInjectionLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        lock (gate)
        {
            ledgers.RemoveAll(l => l.Expired(clock.NowMs));
            ledgers.Add(ledger);
        }
    }

    public void Close(PcInjectionLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        var now = clock.NowMs;
        ledger.Close(now);
        lock (gate) lastClosed = lastClosed is { } before ? Math.Max(before, now) : now;
    }

    /// <summary>The hooks went in (fresh: healthy at the first heartbeat that shows nothing they missed) or came out.</summary>
    public void Installed(bool on)
    {
        bool? changed = null;
        lock (gate)
        {
            var was = installed && healthy;
            installed = on;
            installedMs = clock.NowMs;
            healthy = false;
            agreeAt = null;
            if (!on) heldKeys = heldButtons = 0;
            if (was) changed = false;
        }
        if (changed is { } now) HealthChanged?.Invoke(now);
    }

    /// <summary>
    /// The heartbeat (every 250 ms) and every gate check: the newest GetLastInputInfo tick. A tick newer than the hooks'
    /// newest event (50 ms slack) is the user's input the hooks did not see: it counts as activity at that tick, and the
    /// hooks are presumed dead. Returns true when they should be reinstalled at once.
    /// </summary>
    public bool LastInputSeen(long tickMs)
    {
        bool? changed = null;
        bool reinstall;
        lock (gate)
        {
            if (!installed) return false;
            var seen = Math.Max(newest ?? installedMs, installedMs);
            // Tactus's own input moves GetLastInputInfo too, and the reader hands its events on a few milliseconds late:
            // while a borrow's ledger is open, and up to the moment the last one closed, the tick is Tactus's.
            if (ledgers.Any(l => l.ClosedMs is null)) seen = Math.Max(seen, clock.NowMs);
            else if (lastClosed is { } closed) seen = Math.Max(seen, closed);
            reinstall = tickMs > seen + HealthSlackMs;
            if (reinstall)
            {
                lastUnseen = lastUnseen is { } before ? Math.Max(before, tickMs) : tickMs;
                agreeAt = tickMs;
                if (healthy) changed = false;
                healthy = false;
            }
            else if (!healthy && agreeAt is null)
            {
                // A fresh install with nothing missed since it went in.
                healthy = true;
                changed = true;
            }
        }
        if (changed is { } now) HealthChanged?.Invoke(now);
        return reinstall;
    }

    void IPhysicalInput.LastInput(long tickMs) => LastInputSeen(tickMs);

    /// <summary>An IME composition opened or closed in a window (WinEvents, TextEditPattern); closing window 0 closes any.</summary>
    public void Composition(nint window, bool open)
    {
        lock (gate)
        {
            if (open) composition = window;
            else if (window == 0 || composition == window) composition = 0;
        }
    }

    /// <summary>The foreground's input language is served by an IME with no composition signal: keys need 2 s of stillness.</summary>
    public void ImeLanguage(bool ime)
    {
        lock (gate) imeLanguage = ime;
    }

    /// <summary>One event from the hook reader.</summary>
    public void Record(PcRawInput input)
    {
        var origin = PcInputOrigin.User;
        nint dirty = 0;
        var pressed = false;
        bool? changed = null;
        Func<ushort, bool> chords;
        lock (gate)
        {
            chords = chordKey;
            if (input.Injected && input.Tag != 0) origin = Classify(input);
            newest = newest is { } n ? Math.Max(n, input.TimeMs) : input.TimeMs;
            heldKeys = input.HeldKeys;
            heldButtons = input.HeldButtons;
            if (heldButtons == 0) pressedWindow = 0;
            if (origin == PcInputOrigin.User)
            {
                // A swallowed event is the user's activity, but it never reached the window in front (the borrow's
                // target): it goes back to the user's own window by the replay, so it marks no window and moves nothing.
                if (input.Kind == PcRawKind.Key)
                {
                    lastKey = input.TimeMs;
                    if (!input.Swallowed)
                    {
                        keyWindow = input.Window;
                        keyFocus = input.Focus;
                        keyWindowAt = input.TimeMs;
                        dirty = input.Window;
                        if (input.Down) pressed = true;
                    }
                }
                else
                {
                    lastPointer = input.TimeMs;
                    // A grab in flight is the user's activity (the gate sees a busy hand) but went to the pill, not to a window.
                    if (!input.Swallowed && !input.Grabbed)
                    {
                        pointer = input.Point;
                        pointerWindow = input.Window;
                        dirty = input.Window;
                        if (input.Kind == PcRawKind.Button && input.Down)
                        {
                            pressedWindow = input.Window;
                            pressedPoint = input.Point;
                            pressed = true;
                        }
                    }
                }
                if (dirty != 0)
                {
                    versions[dirty] = versions.GetValueOrDefault(dirty) + 1;
                    lastIn[dirty] = Math.Max(lastIn.GetValueOrDefault(dirty), input.TimeMs);
                    if (pressed) presses[dirty] = presses.GetValueOrDefault(dirty) + 1;
                }
            }
            if (installed && !healthy && agreeAt is { } tick && input.TimeMs >= tick - HealthSlackMs)
            {
                healthy = true;
                agreeAt = null;
                changed = true;
            }
        }
        if (changed is { } now) HealthChanged?.Invoke(now);
        if (dirty != 0) Dirty?.Invoke(dirty);
        if (input.Swallowed) Swallowed?.Invoke(input);
        if (input.Grabbed) Grabbed?.Invoke(input);
        if (origin == PcInputOrigin.User && input.Kind != PcRawKind.Key) UserPointer?.Invoke(input);
        if (origin == PcInputOrigin.User && input.Kind == PcRawKind.Key && input.Down)
        {
            var kind = input.Code == Escape ? PcHookKey.Escape : input.Code != 0 && chords(input.Code) ? PcHookKey.Chord : PcHookKey.Other;
            KeyDown?.Invoke(new PcHookKeyDown(kind, kind == PcHookKey.Other ? (ushort)0 : input.Code, input.Repeat, input.Injected)
            {
                Swallowed = input.Swallowed,
                Modifiers = input.Modifiers,
            });
        }
    }

    // Under the lock: the first open ledger that expects this tagged event.
    private PcInputOrigin Classify(PcRawInput input)
    {
        ledgers.RemoveAll(l => l.Expired(input.TimeMs));
        foreach (var ledger in ledgers)
        {
            if (ledger.Accept(input)) return ledger.Replay ? PcInputOrigin.Replay : PcInputOrigin.Tactus;
        }
        return PcInputOrigin.User;
    }

    private void Release()
    {
        bool last;
        lock (gate) last = --kept == 0;
        if (last) KeepChanged?.Invoke(false);
    }

    private sealed class Keeper(PhysicalInputMonitor owner) : IDisposable
    {
        private int done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref done, 1) == 0) owner.Release();
        }
    }
}

/// <summary>Whose an input event is: the user's, Tactus's own (a borrow's ledger), or the replay of the user's held input.</summary>
public enum PcInputOrigin
{
    User,
    Tactus,
    Replay,
}
