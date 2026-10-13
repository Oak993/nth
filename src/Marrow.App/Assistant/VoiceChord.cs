// PORT-ONLY: Dextra (DESIGN.md rev 5, 2.4 "Press Ctrl+Alt+Enter anywhere in Windows (configurable): toggle to talk ... validated by Tactus's ChordValidator.
// It is registered while nth runs and released on exit, and a failed registration shows in Settings with Change"; unit AS-18; ED-NEW-dextra-as18-9). No Swift twin.
// The one global chord that talks to Dextra. It lives on a thread of its own like the stop chord (StopChordThread, IStopChordHost), is checked by
// ChordValidator (not a single key, not a Windows-key chord, types no character on any installed layout, and neither the stop chord nor Tactus's
// pause chord), registered while Dextra runs and released when it stops. RegisterHotKey reports a key-down only, so the chord toggles (the voice
// conversation's Toggle). A chord another app holds is not an error to hide: the status says so, Settings shows it with Change, and a changed
// setting registers it again.
using Marrow.App.Safety;
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.App.Assistant;

/// <summary>Whether the talk chord works now, and the sentence Settings shows when it does not.</summary>
public sealed record VoiceChordStatus(bool Registered, string Chord, bool Taken, PcChordProblem Problem)
{
    /// <summary>True when the chord is registered.</summary>
    public bool Works => Registered && !Taken && Problem == PcChordProblem.None;

    /// <summary>"Ctrl+Alt+Enter is taken by another app. Change it.", or null while the chord works.</summary>
    public string? Line => Works ? null : Problem switch
    {
        PcChordProblem.SingleKey => $"{Chord} is a single key. Pick a chord with Ctrl or Alt. Change it.",
        PcChordProblem.WinKey => $"{Chord} uses the Windows key, which Windows keeps. Change it.",
        PcChordProblem.Clash => $"{Chord} is already the stop or pause chord. Change it.",
        PcChordProblem.TypesCharacter => $"{Chord} types a character on one of your keyboard layouts. Change it.",
        _ => Taken ? $"{Chord} is taken by another app. Change it." : $"{Chord} is not registered yet.",
    };
}

public sealed class VoiceChord : IDisposable
{
    /// <summary>The hot key's id on its own thread.</summary>
    public const int ChordId = 2;

    private readonly IStopChordHost host;
    private readonly Func<string?> setting;
    private readonly Func<IEnumerable<PcHotkey>> others;
    private readonly object gate = new();
    private bool isRegistered;
    private VoiceChordStatus status;
    private bool disposed;

    /// <param name="host">The chord's thread. This owns it and ends it with itself.</param>
    /// <param name="setting">The owner's chord text; null, empty or unreadable text means the default.</param>
    /// <param name="others">The chords this one must differ from (the stop chord, Tactus's pause chord).</param>
    public VoiceChord(IStopChordHost host, Func<string?> setting, Func<IEnumerable<PcHotkey>>? others = null)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        this.setting = setting ?? throw new ArgumentNullException(nameof(setting));
        this.others = others ?? (() => []);
        var chord = Parse(setting());
        status = new VoiceChordStatus(false, chord.Format(), false, PcChordProblem.None);
        host.Pressed += OnPressed;
    }

    /// <summary>The owner pressed the chord (raised on the chord thread: the handler posts to the UI thread and nothing more).</summary>
    public event Action? Pressed;

    /// <summary>The chord registered, or stopped working (any thread).</summary>
    public event Action<VoiceChordStatus>? StatusChanged;

    // ED-NEW-dextra-smooth-7: Settings reads the last completed immutable snapshot without native waits.
    public VoiceChordStatus Status => Volatile.Read(ref status);

    /// <summary>
    /// Registers the chord as the setting has it now: the old registration is released first, so a changed setting takes the new chord at once. Returns
    /// whether it works; the status says why not.
    /// </summary>
    public bool Register() => RegisterSnapshot(null, null);

    // Deferred voice captures settings on main; workers never read the mutable settings store.
    internal bool RegisterSnapshot(string? text, IEnumerable<PcHotkey>? conflicts)
    {
        VoiceChordStatus now;
        lock (gate)
        {
            if (disposed) return false;
            if (isRegistered)
            {
                host.Unregister(ChordId);
                isRegistered = false;
            }
            var chord = Parse(conflicts is null ? setting() : text);
            var problem = ChordValidator.Check(chord, conflicts ?? others(), host.TypesCharacter);
            var ok = problem == PcChordProblem.None && host.Register(ChordId, chord);
            isRegistered = ok;
            now = new VoiceChordStatus(ok, chord.Format(), problem == PcChordProblem.None && !ok, problem);
            Volatile.Write(ref status, now);
        }
        StatusChanged?.Invoke(now);
        return now.Works;
    }

    /// <summary>Releases the chord (Dextra stopped) and ends its thread.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            host.Pressed -= OnPressed;
            if (isRegistered) host.Unregister(ChordId);
            isRegistered = false;
        }
        host.Dispose();
    }

    private void OnPressed(int id)
    {
        if (id == ChordId) Pressed?.Invoke();
    }

    private static PcHotkey Parse(string? text) =>
        PcHotkey.TryParse(text, out var chord) ? chord : PcHotkey.TryParse(DextraSettings.DefaultTalkChord, out var fallback) ? fallback : new PcHotkey(PcHotkeyModifiers.Control | PcHotkeyModifiers.Alt, PcHotkey.Return);
}
