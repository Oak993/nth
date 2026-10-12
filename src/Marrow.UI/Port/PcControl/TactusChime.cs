// PORT-ONLY: Tactus, the soft sound of a hands-off request (ED-NEW-pcctl-38; DESIGN.md 4.5 "An optional soft sound plays
// when a request appears (Settings, off by default)", 4.7 "Sound when Tactus needs a moment"; canvas Settings row "A soft
// tone when the pill asks you to keep your hands off"). The design gives no tone, so this one is the port's: two quiet
// round notes stepping up (C5 then E5), an ask rather than an alert, on the app's own synth and output (ToneSynth,
// SwiftSound), as quiet as the settle chime. Built once, on the first request.
using Marrow.UI.Sound;

namespace Marrow.UI.Port.PcControl;

public static class TactusChime
{
    /// <summary>Quiet, as the settle chime (0.18): a cue under the pill's motion, not a notification.</summary>
    public const float Volume = 0.18f;

    /// <summary>C5 then E5 a little after: a small rising ask.</summary>
    public static readonly IReadOnlyList<ToneSynth.Note> Notes =
    [
        new ToneSynth.Note(523.25, 0, 0.8),
        new ToneSynth.Note(659.26, 0.09, 1),
    ];

    /// <summary>A round tone: the fundamental rings a little longer, the octave only glints.</summary>
    public static readonly IReadOnlyList<ToneSynth.Partial> Partials =
    [
        new ToneSynth.Partial(1, 1, 0.12),
        new ToneSynth.Partial(2, 0.1, 0.03),
    ];

    public const double Seconds = 0.45;

    private static readonly Lazy<SwiftSound?> Sound = new(Create, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Plays the tone from its start (a silent no-op when no output could be made).</summary>
    public static void Play()
    {
        Sound.Value?.Stop();
        Sound.Value?.Play();
    }

    private static SwiftSound? Create()
    {
        var voice = SwiftSound.Create(ToneSynth.Wave(Notes, Partials, Seconds));
        if (voice is not null) voice.Volume = Volume;
        return voice;
    }
}
