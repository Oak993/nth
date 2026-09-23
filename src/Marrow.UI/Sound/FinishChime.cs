using Marrow.UI.Port;

namespace Marrow.UI.Sound;

// Source: DroppyCode/UI/Sound/FinishChime.swift (B2 1fbf81e), :1-37. Record PR-THEME-081.
//
// :6-7 a caseless `@MainActor enum` -> a static class (R-FILE-1/R-NAME-1). MainActor isolation is
// assumed, not re-checked per call (R-ISO-1): upstream reaches FinishChime.play() only on the main
// actor, so the lazily-built static state below needs no lock.

/// <summary>
/// The soft two-note chime that marks a turn finishing: a rising fifth with a celesta-like tone,
/// synthesized on first use so no sound file ships with the app and the tone is tunable right here.
/// </summary>
public static class FinishChime
{
    /// <summary>
    /// How loud the chime plays relative to the system volume. Well under a stock alert: it registers
    /// without cutting into whatever else is playing.
    /// </summary>
    // :9-10 `private static let volume: Float = 0.35`.
    private static readonly float Volume = 0.35f;

    /// <summary>D5 up to A5, the second note a beat later and a touch stronger, the way "done" lands.</summary>
    // :25-28
    private static readonly IReadOnlyList<ToneSynth.Note> Notes =
    [
        new ToneSynth.Note(587.33, 0, 0.8),
        new ToneSynth.Note(880, 0.14, 1),
    ];

    /// <summary>
    /// The fundamental carries the tone; the octave and double octave add a brief glint at the strike
    /// and are gone within a tenth of a second, which keeps it soft rather than pingy.
    /// </summary>
    // :32-36
    private static readonly IReadOnlyList<ToneSynth.Partial> Partials =
    [
        new ToneSynth.Partial(1, 1, 0.14),
        new ToneSynth.Partial(2, 0.22, 0.06),
        new ToneSynth.Partial(4, 0.08, 0.04),
    ];

    // :12-16 `static let sound: NSSound? = { ... }()`: built lazily, exactly once, on first access,
    // and never rebuilt even when Create failed (Swift caches the nil too). SoundBuilt is port-only:
    // it distinguishes "not built yet" from "built and nil", which a plain null check on Sound cannot.
    private static SwiftSound? Sound;
    private static bool SoundBuilt;

    /// <summary>
    /// NSSound declines a play while one is in flight, so several threads finishing in the same moment
    /// make one chime, never a pile-up. When the sound could not be created, play is a silent no-op.
    /// </summary>
    // :18-22 `static func play() { sound?.play() }`.
    public static void Play()
    {
        if (!SoundBuilt)
        {
            SoundBuilt = true;
            var sound = SwiftSound.Create(ToneSynth.Wave(Notes, Partials, 0.7));
            if (sound is not null) sound.Volume = Volume;
            Sound = sound;
        }
        Sound?.Play();
    }
}
