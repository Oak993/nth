using Marrow.UI.Port;
using Marrow.Core.Port;

namespace Marrow.UI.Sound;

// Source: DroppyCode/UI/Sound/SettleChime.swift (B3 52bc792), :1-36. Record PR-THEME-082.

/// <summary>
/// The small sound of a thread settling: two soft wooden notes stepping down, the way a checkbox ticks
/// and the row comes to rest. Quieter and shorter than the finish chime, and the setting can silence it.
/// </summary>
// :6-7. `@MainActor enum SettleChime` with no cases -> a static class (:3-5 convention). This type has no
// mutable state of its own beyond the lazily built voice: SwiftSound itself locks around Play/Stop, so
// nothing here needs a per-call main-actor check (R-ISO-1).
public static class SettleChime
{
    /// <summary>Barely there: a tick under the sidebar's motion, not a notification.</summary>
    // :8-9 `private static let volume: Float = 0.18`.
    private static readonly float Volume = 0.18f;

    /// <summary>
    /// `private static let sound: NSSound? = { ... }()`. A Swift static let initializes lazily, exactly
    /// once, on first access; that first access is inside <see cref="Play"/> (ED-151, F.Sound).
    /// </summary>
    // :11-15.
    private static readonly Lazy<SwiftSound?> Sound = new(CreateSound, LazyThreadSafetyMode.ExecutionAndPublication);
    // :11-15 PORT-ONLY scoped voice seam (Ambient, ED-065); default retains the original once-only static lazy.
    private static readonly Ambient<Lazy<SwiftSound?>> Voice = new(() => Sound);

    private static SwiftSound? CreateSound()
    {
        var voice = SwiftSound.Create(ToneSynth.Wave(Notes, Partials, 0.32)); // :12
        if (voice is not null) voice.Volume = Volume; // :13
        return voice;
    }

    /// <summary>`static func play()`.</summary>
    // :17-21. App-layer callers reach this publicly, for example MCPSettingsPage.swift:68 (R-NAME-6).
    public static void Play()
    {
        // A replay from the top, so settling two threads in a row ticks twice. Both calls are optional-
        // chained in Swift (:19-20): when Sound.Value is null (the voice could not be created), both are
        // silent no-ops.
        Voice.Current.Value?.Stop(); // :19
        Voice.Current.Value?.Play(); // :20
    }

    /// <summary>E5 down to C5, the second note right behind the first: a small landing.</summary>
    // :23-27.
    private static readonly IReadOnlyList<ToneSynth.Note> Notes =
    [
        new ToneSynth.Note(659.26, 0, 0.7),
        new ToneSynth.Note(523.25, 0.075, 1),
    ];

    /// <summary>
    /// A woody tone: the fundamental fades fast, a quiet third harmonic gives it a knock at the strike,
    /// and the octave only glints.
    /// </summary>
    // :29-35.
    private static readonly IReadOnlyList<ToneSynth.Partial> Partials =
    [
        new ToneSynth.Partial(1, 1, 0.075),
        new ToneSynth.Partial(3, 0.16, 0.02),
        new ToneSynth.Partial(2, 0.08, 0.03),
    ];
}
