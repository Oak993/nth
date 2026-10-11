// PORT-ONLY: Tactus, PC control (OD-0145, ED-NEW-pcctl-12 items 1 and 2, ED-NEW-pcctl-15; DESIGN.md 4.4, 10.1, 10.2). The
// Tactus chords and Esc three times, on a thread of their own (never the UI thread, so a busy UI never delays them). They
// are registered only while a stream is working or paused: the stop chord (fails closed: no stream starts without it), the
// pause chord (fails open: Pause still works from the pill, the chat and the tray, and the pill says once that the key is
// taken) and "Answer Tactus" when the user set one. The pause chord ignores injected presses and presses under 400 ms
// apart, and resumes only when every live app is paused. Esc three times within 1 s, the first in a window Tactus is
// using, stops all; Escs an IME consumes, auto-repeats and injected Escs do not count. Every handler only sets flags.
using Marrow.Core.Port.PcControl;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Native;

namespace Marrow.App.Port.PcControl;

/// <summary>
/// The thread the chords live on (its own message loop) and what it can ask Windows. Its events are raised on that thread.
/// The app binds <see cref="HotkeyThread"/>; tests bind a fake.
/// </summary>
public interface ITactusHotkeyHost : IDisposable
{
    /// <summary>RegisterHotKey with MOD_NOREPEAT on the host's thread; false when another app holds the chord.</summary>
    bool Register(int id, PcHotkey chord);

    void Unregister(int id);

    /// <summary>Installs (or removes) the low-level keyboard hook on the host's thread.</summary>
    void Watch(bool on, Func<ushort, bool> isChordKey);

    /// <summary>The window in front now.</summary>
    nint Foreground();

    /// <summary>Whether the window's IME is open, so an Esc there may be the IME's.</summary>
    bool ImeOpen(nint window);

    /// <summary>Whether the chord types a printable character on any installed keyboard layout.</summary>
    bool TypesCharacter(PcHotkey chord);

    event Action<int>? Pressed;

    event Action<PcHookKeyDown>? KeyDown;
}

public sealed class TactusHotkeys : IPcStopKey, IDisposable
{
    public const int StopId = 1;
    public const int PauseId = 2;
    public const int AnswerId = 3;

    // gate guards the fields the host's thread reads; arming holds armGate only, so a host call never waits under gate
    // while the host's thread waits for gate in a key handler.
    private readonly Lock gate = new();
    private readonly Lock armGate = new();
    private readonly ITactusHotkeyHost host;
    private readonly Func<PcSettings> settings;
    private readonly PcStopControl stop;
    private readonly PcPauseControl pause;
    private readonly PcLanes lanes;
    private readonly Func<nint, bool> isTactusWindow;
    private readonly Func<long> nowMs;
    private readonly Action? answer;
    private readonly ChordFilter filter = new();
    private readonly EscTripleCounter escapes = new();
    private PcHotkey stopChord = PcHotkey.StopDefault;
    private PcHotkey? pauseChord;
    private PcHotkey? answerChord;
    private bool armed;
    private bool stopWorks;
    private bool pauseInjected;

    public TactusHotkeys(ITactusHotkeyHost host, Func<PcSettings> settings, PcStopControl stop, PcPauseControl pause, PcLanes lanes,
        Func<nint, bool> isTactusWindow, Func<long> nowMs, Action? answer = null)
    {
        this.host = host;
        this.settings = settings;
        this.stop = stop;
        this.pause = pause;
        this.lanes = lanes;
        this.isTactusWindow = isTactusWindow;
        this.nowMs = nowMs;
        this.answer = answer;
        host.Pressed += OnPressed;
        host.KeyDown += OnKeyDown;
        lanes.ActiveChanged += OnActiveChanged;
        pause.Changed += OnPauseChanged;
    }

    /// <summary>Raised once when the pause chord could not be registered or failed its checks (the pill says "Pause key is taken").</summary>
    public event Action<PcChordProblem>? PauseKeyProblem;

    /// <summary>Why the pause chord is not working; null while it works or nothing is armed.</summary>
    public PcChordProblem? PauseProblem { get; private set; }

    public string Chord
    {
        get
        {
            lock (gate) return (armed ? stopChord : Parse(settings().StopChord, PcHotkey.StopDefault)).Format();
        }
    }

    /// <summary>Whether the chords are registered now.</summary>
    public bool Armed
    {
        get
        {
            lock (gate) return armed;
        }
    }

    /// <summary>
    /// Registers the chords when the first stream starts (cheap afterwards). True while the stop chord works; false keeps
    /// every stream from starting (fail closed). The pause and answer chords fail open.
    /// </summary>
    public bool Arm()
    {
        PcChordProblem? problem = null;
        lock (armGate)
        {
            lock (gate)
            {
                if (armed) return stopWorks;
            }
            var current = settings();
            var stopKey = Parse(current.StopChord, PcHotkey.StopDefault);
            if (ChordValidator.Check(stopKey, [], _ => false) != PcChordProblem.None || !host.Register(StopId, stopKey)) return false;
            PcHotkey? pauseKey = Parse(current.PauseChord, PcHotkey.PauseDefault);
            var check = ChordValidator.Check(pauseKey.Value, [stopKey], host.TypesCharacter);
            if (check == PcChordProblem.None && !host.Register(PauseId, pauseKey.Value)) check = PcChordProblem.Clash;
            if (check != PcChordProblem.None)
            {
                problem = check;
                pauseKey = null;
            }
            PcHotkey? answerKey = PcHotkey.TryParse(current.AnswerChord, out var set)
                && ChordValidator.Check(set, pauseKey is { } p ? [stopKey, p] : [stopKey], host.TypesCharacter) == PcChordProblem.None
                && host.Register(AnswerId, set) ? set : null;
            lock (gate)
            {
                stopChord = stopKey;
                pauseChord = pauseKey;
                answerChord = answerKey;
                stopWorks = true;
                armed = true;
                PauseProblem = problem;
            }
            host.Watch(true, IsChordKey);
        }
        if (problem is { } said) PauseKeyProblem?.Invoke(said);
        return true;
    }

    /// <summary>Unregisters everything (no stream working or paused), so other apps get the chords back.</summary>
    public void Disarm()
    {
        lock (armGate)
        {
            bool hadPause;
            bool hadAnswer;
            lock (gate)
            {
                if (!armed) return;
                hadPause = pauseChord is not null;
                hadAnswer = answerChord is not null;
                armed = false;
                stopWorks = false;
                pauseChord = null;
                answerChord = null;
                PauseProblem = null;
            }
            host.Watch(false, IsChordKey);
            host.Unregister(StopId);
            if (hadPause) host.Unregister(PauseId);
            if (hadAnswer) host.Unregister(AnswerId);
        }
    }

    public void Dispose()
    {
        host.Pressed -= OnPressed;
        host.KeyDown -= OnKeyDown;
        lanes.ActiveChanged -= OnActiveChanged;
        pause.Changed -= OnPauseChanged;
        Disarm();
    }

    private static PcHotkey Parse(string text, PcHotkey fallback) => PcHotkey.TryParse(text, out var chord) ? chord : fallback;

    private bool IsChordKey(ushort key)
    {
        lock (gate) return key == stopChord.Key || key == pauseChord?.Key || key == answerChord?.Key;
    }

    // On the host's thread. Nothing here waits on a worker or the UI.
    private void OnPressed(int id)
    {
        switch (id)
        {
            case StopId:
                stop.StopKey();
                break;
            case PauseId:
                bool injected;
                lock (gate) injected = pauseInjected;
                if (filter.Accept(nowMs(), repeat: false, injected)) pause.Chord();
                break;
            case AnswerId:
                answer?.Invoke();
                break;
        }
    }

    private void OnKeyDown(PcHookKeyDown key)
    {
        if (key.Kind == PcHookKey.Chord)
        {
            lock (gate)
            {
                if (key.Key == pauseChord?.Key) pauseInjected = key.Injected;
            }
        }
        var inTactusWindow = false;
        var ime = false;
        if (key.Kind == PcHookKey.Escape && !key.Repeat && !key.Injected)
        {
            var front = host.Foreground();
            inTactusWindow = isTactusWindow(front);
            ime = host.ImeOpen(front);
        }
        if (escapes.Key(nowMs(), new PcKeyDown(key.Kind == PcHookKey.Escape, key.Repeat, key.Injected, ime, inTactusWindow))) stop.EscapeThreeTimes();
    }

    private void OnActiveChanged(IReadOnlyList<string> active) => DisarmWhenIdle();

    private void OnPauseChanged() => DisarmWhenIdle();

    // The chords are held only while a stream is working or paused: from a chat's first Tactus call to the end of its turn
    // (so the stop chord also stops the turn between two steps), and while any app is paused or Pause all is on.
    private void DisarmWhenIdle()
    {
        if (lanes.Active.Count == 0 && !pause.InTurn && pause.PausedApps.Count == 0 && !pause.Latched) Disarm();
    }
}
