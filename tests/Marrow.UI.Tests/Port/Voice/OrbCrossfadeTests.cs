using Marrow.Core.Port.Voice;
using Marrow.UI.Port.Voice.Orb;

namespace Marrow.UI.Tests.Port.Voice;

// VT-8, V-T53 (V-11.5): "both orbs stay ready; a change crossfades the two views (follow tau 0.12 s, snapped within 0.004)".
public sealed class OrbCrossfadeTests
{
    private static void Run(OrbCrossfade fade, VoiceOrbKind to, double seconds, double step = 1.0 / 240, bool reduce = false)
    {
        for (var i = 0; i < (int)Math.Round(seconds / step); i++) fade.Step(to, step, reduce);
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T53")]
    public void The_constants_are_V_11_5s()
    {
        Assert.Equal(0.12, OrbCrossfade.Tau);
        Assert.Equal(0.004, OrbCrossfade.Snap);
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T53")]
    public void A_new_crossfade_shows_only_the_chosen_orb()
    {
        var fade = new OrbCrossfade(VoiceOrbKind.Membrane);
        Assert.Equal(1, fade.Mix(VoiceOrbKind.Membrane));
        Assert.Equal(0, fade.Mix(VoiceOrbKind.Siri));
        Assert.True(fade.Visible(VoiceOrbKind.Membrane));
        Assert.False(fade.Visible(VoiceOrbKind.Siri));
        Assert.True(fade.Settled);
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T53")]
    public void After_one_tau_the_new_orb_has_come_in_by_one_minus_one_over_e()
    {
        var fade = new OrbCrossfade(VoiceOrbKind.Siri);
        Assert.True(fade.Step(VoiceOrbKind.Membrane, 0, false));   // the change is reported once
        Run(fade, VoiceOrbKind.Membrane, 0.12, 0.004);
        var expected = 1 - Math.Exp(-1);
        Assert.Equal(expected, fade.Mix(VoiceOrbKind.Membrane), 9);
        Assert.Equal(1 - expected, fade.Mix(VoiceOrbKind.Siri), 9);
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T53")]
    public void The_two_views_are_complementary_all_the_way_so_the_picture_never_dims()
    {
        var fade = new OrbCrossfade(VoiceOrbKind.Siri);
        fade.Step(VoiceOrbKind.Membrane, 0, false);
        for (var i = 0; i < 100; i++)
        {
            fade.Step(VoiceOrbKind.Membrane, 1.0 / 60, false);
            if (!fade.Settled) Assert.Equal(1.0, fade.Mix(VoiceOrbKind.Siri) + fade.Mix(VoiceOrbKind.Membrane), 9);
        }
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T53")]
    public void A_mix_within_0_004_of_its_target_lands_on_it_and_the_old_view_stops_being_drawn()
    {
        var fade = new OrbCrossfade(VoiceOrbKind.Siri);
        fade.Step(VoiceOrbKind.Membrane, 0, false);
        // the old orb's mix is exp(-t / 0.12): 0.004 at 0.12 * ln 250 = 0.6626 s
        Run(fade, VoiceOrbKind.Membrane, 0.65);
        Assert.InRange(fade.Mix(VoiceOrbKind.Siri), 0.004, 0.0046);
        Assert.True(fade.Visible(VoiceOrbKind.Siri));
        Assert.False(fade.Settled);
        Run(fade, VoiceOrbKind.Membrane, 0.03);
        Assert.Equal(0, fade.Mix(VoiceOrbKind.Siri));
        Assert.Equal(1, fade.Mix(VoiceOrbKind.Membrane));
        Assert.False(fade.Visible(VoiceOrbKind.Siri));
        Assert.True(fade.Settled);
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T53")]
    public void The_follow_does_not_depend_on_the_frame_rate()
    {
        var slow = new OrbCrossfade(VoiceOrbKind.Siri);
        var fast = new OrbCrossfade(VoiceOrbKind.Siri);
        slow.Step(VoiceOrbKind.Membrane, 0, false);
        fast.Step(VoiceOrbKind.Membrane, 0, false);
        Run(slow, VoiceOrbKind.Membrane, 0.3, 1.0 / 30);
        Run(fast, VoiceOrbKind.Membrane, 0.3, 1.0 / 480);
        Assert.Equal(fast.Mix(VoiceOrbKind.Membrane), slow.Mix(VoiceOrbKind.Membrane), 9);
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T53")]
    public void Reduce_motion_lands_the_swap_at_once()
    {
        var fade = new OrbCrossfade(VoiceOrbKind.Siri);
        Assert.True(fade.Step(VoiceOrbKind.Membrane, 0.016, reduceMotion: true));
        Assert.Equal(1, fade.Mix(VoiceOrbKind.Membrane));
        Assert.Equal(0, fade.Mix(VoiceOrbKind.Siri));
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T53")]
    public void A_change_back_mid_fade_turns_around_from_where_the_mixes_are()
    {
        var fade = new OrbCrossfade(VoiceOrbKind.Siri);
        fade.Step(VoiceOrbKind.Membrane, 0, false);
        Run(fade, VoiceOrbKind.Membrane, 0.12);
        var siri = fade.Mix(VoiceOrbKind.Siri);
        Assert.True(fade.Step(VoiceOrbKind.Siri, 0, false));
        Assert.Equal(siri, fade.Mix(VoiceOrbKind.Siri), 12);
        Run(fade, VoiceOrbKind.Siri, 0.12);
        Assert.True(fade.Mix(VoiceOrbKind.Siri) > siri);
    }
}
