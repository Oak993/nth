using Marrow.Core.Port.Voice.Dsp;

namespace Marrow.Core.Tests.Port.Voice;

// VT-2 (voice-SPEC V-5.5, V-T20): the followers. follow(x, target, dt, up, down) = x + (target - x)(1 - exp(-dt / tau)), tau = up when rising and
// down when falling. Bands: attack 0.07 s, release 0.24 s, values under 1e-4 set to 0. The orb's voice value: 0.12 s / 0.45 s.
// The expected numbers are written out from the formula, not read from the code.
public sealed class LevelFollowersTests
{
    private static double Rise(double dt, double tau) => 1 - Math.Exp(-dt / tau);

    [Fact]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T20")]
    [Trait("Decision", "ED-267")] // V-5.5: follow uses the attack when the target is above, the release when below
    public void Follow_rises_with_the_attack_and_falls_with_the_release()
    {
        Assert.Equal(1 - Math.Exp(-1), Follower.Follow(0, 1, 0.07, 0.07, 0.24), 12);
        Assert.Equal(Math.Exp(-1), Follower.Follow(1, 0, 0.24, 0.07, 0.24), 12);
        Assert.Equal(0.5 + (1 - 0.5) * Rise(0.1, 0.12), Follower.Follow(0.5, 1, 0.1, 0.12, 0.45), 12);
        Assert.Equal(0.5 + (0 - 0.5) * Rise(0.1, 0.45), Follower.Follow(0.5, 0, 0.1, 0.12, 0.45), 12);
    }

    [Fact]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T20")]
    [Trait("Decision", "ED-267")] // V-5.5: no time passed means no change; a target equal to the value stays
    public void Follow_holds_for_no_time_and_for_an_equal_target()
    {
        Assert.Equal(0.3, Follower.Follow(0.3, 1, 0, 0.07, 0.24));
        Assert.Equal(0.3, Follower.Follow(0.3, 0.3, 0.5, 0.07, 0.24));
    }

    [Fact]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T20")]
    [Trait("Decision", "ED-267")] // V-5.5: tau is at least 1 ms (the design's follow: max(1e-3, tau))
    public void Follow_never_divides_by_a_time_constant_under_a_millisecond()
    {
        Assert.Equal(1 - Math.Exp(-1), Follower.Follow(0, 1, 0.001, 0, 0.24), 12);
        Assert.Equal(1.0, Follower.Follow(0, 1, 10, 0, 0.24), 12);
    }

    [Fact]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T20")]
    [Trait("Decision", "ED-267")] // V-5.5: bands attack 0.07, release 0.24; voice 0.12 / 0.45 following the smoothed all
    public void One_step_up_moves_each_band_by_the_attack_and_the_voice_by_its_own()
    {
        var f = new LevelFollowers();
        f.Step(new LevelBands(1.0, 0.5, 0.25, 0.8), 0.07);

        var rise = Rise(0.07, 0.07);
        Assert.Equal(1.0 * rise, f.Bands.All, 12);
        Assert.Equal(0.5 * rise, f.Bands.Low, 12);
        Assert.Equal(0.25 * rise, f.Bands.Mid, 12);
        Assert.Equal(0.8 * rise, f.Bands.High, 12);
        // the voice follows the band value just updated (voice-composer.js:721 reads S.bands.all after the loop)
        Assert.Equal(f.Bands.All * Rise(0.07, 0.12), f.Voice, 12);
    }

    [Fact]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T20")]
    [Trait("Decision", "ED-267")] // V-5.5: release 0.24 for the bands and 0.45 for the voice
    public void One_step_down_moves_each_band_by_the_release_and_the_voice_by_its_own()
    {
        var f = new LevelFollowers();
        f.Step(new LevelBands(1, 1, 1, 1), 100);          // far past the attack: bands at 1, voice at 1 - exp(-100/0.12) = 1
        Assert.Equal(1.0, f.Bands.All, 12);
        Assert.Equal(1.0, f.Voice, 12);

        f.Step(LevelBands.Zero, 0.24);
        Assert.Equal(Math.Exp(-1), f.Bands.All, 12);
        Assert.Equal(Math.Exp(-1), f.Bands.High, 12);
        Assert.Equal(1.0 + (Math.Exp(-1) - 1.0) * Rise(0.24, 0.45), f.Voice, 12);
    }

    [Fact]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T20")]
    [Trait("Decision", "ED-267")] // V-5.5: a band value under 1e-4 is set to 0, on the way down and on the way up
    public void A_band_under_1e_minus_4_is_set_to_zero()
    {
        var f = new LevelFollowers();
        f.Step(new LevelBands(2e-4, 2e-4, 2e-4, 2e-4), 1e6);     // reaches its target, above the threshold: kept
        Assert.Equal(2e-4, f.Bands.Mid, 12);

        f.Step(LevelBands.Zero, 0.1);                              // 2e-4 * exp(-0.1 / 0.24) = 1.32e-4: kept
        Assert.Equal(2e-4 * Math.Exp(-0.1 / 0.24), f.Bands.Mid, 12);
        f.Step(LevelBands.Zero, 0.1);                              // 8.7e-5: under 1e-4, so exactly 0
        Assert.Equal(0.0, f.Bands.All);
        Assert.Equal(0.0, f.Bands.Low);
        Assert.Equal(0.0, f.Bands.Mid);
        Assert.Equal(0.0, f.Bands.High);

        var g = new LevelFollowers();
        g.Step(new LevelBands(5e-5, 5e-5, 5e-5, 5e-5), 1e6);       // a rise to under the threshold never shows
        Assert.Equal(LevelBands.Zero, g.Bands);
    }

    [Fact]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T20")]
    [Trait("Decision", "ED-267")] // ED-301 (6): only the band values are zeroed; the voice value is the plain follow (voice-composer.js:722)
    public void The_voice_value_is_the_plain_follow_of_the_smoothed_all()
    {
        var f = new LevelFollowers();
        f.Step(new LevelBands(1, 0, 0, 0), 100);
        var v = f.Voice;
        f.Step(LevelBands.Zero, 0.24 * 50);                         // all decays to exactly 0 (under 1e-4)
        Assert.Equal(0.0, f.Bands.All);
        Assert.Equal(v + (0.0 - v) * Rise(0.24 * 50, 0.45), f.Voice, 15);
    }

    [Fact]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T20")]
    [Trait("Decision", "ED-267")] // V-5.5: per frame, so many small steps follow the same curve as the formula applied step by step
    public void Sixty_frames_a_second_follow_the_formula_step_by_step()
    {
        var f = new LevelFollowers();
        double all = 0, voice = 0;
        for (var i = 0; i < 90; i++)
        {
            var raw = i < 45 ? 0.8 : 0.0;
            const double dt = 1.0 / 60;
            all = all + (raw - all) * (1 - Math.Exp(-dt / (raw > all ? 0.07 : 0.24)));
            if (all < 1e-4) all = 0;
            voice = voice + (all - voice) * (1 - Math.Exp(-dt / (all > voice ? 0.12 : 0.45)));
            f.Step(new LevelBands(raw, 0, 0, 0), dt);
            Assert.Equal(all, f.Bands.All, 12);
            Assert.Equal(voice, f.Voice, 12);
        }
    }

    [Fact]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T20")]
    [Trait("Decision", "ED-267")] // ED-301 (7): a bad frame time or reading cannot poison the state
    public void A_negative_or_non_finite_dt_or_reading_changes_nothing_bad()
    {
        var f = new LevelFollowers();
        f.Step(new LevelBands(0.5, 0.5, 0.5, 0.5), 0.5);
        var before = (f.Bands, f.Voice);

        f.Step(new LevelBands(1, 1, 1, 1), -1);
        f.Step(new LevelBands(1, 1, 1, 1), double.NaN);
        Assert.Equal(before, (f.Bands, f.Voice));

        f.Step(new LevelBands(double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.NaN), 0.1);
        Assert.True(double.IsFinite(f.Bands.All) && double.IsFinite(f.Bands.Low) && double.IsFinite(f.Bands.Mid) && double.IsFinite(f.Bands.High));
        Assert.True(double.IsFinite(f.Voice));
    }

    [Fact]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T20")]
    [Trait("Decision", "ED-267")] // a new session starts at rest
    public void Reset_returns_everything_to_rest()
    {
        var f = new LevelFollowers();
        f.Step(new LevelBands(1, 1, 1, 1), 1);
        f.Reset();
        Assert.Equal(LevelBands.Zero, f.Bands);
        Assert.Equal(0.0, f.Voice);
    }
}
