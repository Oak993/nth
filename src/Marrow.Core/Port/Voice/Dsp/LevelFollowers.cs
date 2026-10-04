// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-2): the followers of voice-SPEC V-5.5 (voice-composer.js:718-722).
namespace Marrow.Core.Port.Voice.Dsp;

/// <summary>
/// The smoothing the orb and glow read: per band a follower with attack 0.07 s and release 0.24 s whose values under 1e-4 are set to 0, and
/// the orb's voice value <see cref="Voice"/>, a follower of the smoothed overall level with attack 0.12 s and release 0.45 s. It runs on the
/// UI thread once per frame from the raw values <see cref="LevelAnalyser.Read"/> published.
/// </summary>
public sealed class LevelFollowers
{
    public const double BandAttack = 0.07;
    public const double BandRelease = 0.24;
    public const double VoiceAttack = 0.12;
    public const double VoiceRelease = 0.45;

    /// <summary>A smoothed band value under this is set to 0.</summary>
    public const double Floor = 1e-4;

    /// <summary>The smoothed bands.</summary>
    public LevelBands Bands { get; private set; }

    /// <summary>The orb's voice value <c>v</c>: follows <c>Bands.All</c> as already updated in the same step.</summary>
    public double Voice { get; private set; }

    /// <summary>One frame: <paramref name="dt"/> seconds since the last. A negative or non-finite dt is no time; a non-finite raw value reads as 0.</summary>
    public void Step(LevelBands raw, double dt)
    {
        if (!double.IsFinite(dt) || dt < 0) dt = 0;
        Bands = new LevelBands(Band(Bands.All, raw.All, dt), Band(Bands.Low, raw.Low, dt), Band(Bands.Mid, raw.Mid, dt), Band(Bands.High, raw.High, dt));
        Voice = Follower.Follow(Voice, Bands.All, dt, VoiceAttack, VoiceRelease);
    }

    public void Reset()
    {
        Bands = LevelBands.Zero;
        Voice = 0;
    }

    private static double Band(double current, double target, double dt)
    {
        if (!double.IsFinite(target)) target = 0;
        var next = Follower.Follow(current, target, dt, BandAttack, BandRelease);
        return next < Floor ? 0 : next;
    }
}
