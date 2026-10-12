using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Orb;
using Marrow.UI.Port.Voice.Orb;

namespace Marrow.UI.Tests.Port.Voice;

// VT-8, V-T53 (V-11.5, V-11.4): the scene hands the new orb the old one's phase, draws both while they cross, and steps only what is drawn; the ball follows the slot's spring.
public sealed class OrbSceneTests
{
    private static OrbSceneInput Input(VoiceOrbKind kind, OrbMotionMode mode = OrbMotionMode.Rest, double slot = 32, bool dark = true, bool reduce = false,
        OrbBands? bands = null, bool hovered = false) =>
        new(kind, mode, hovered, false, bands ?? default, slot, dark, reduce);

    private static void Run(OrbScene scene, ref double now, OrbSceneInput input, double seconds, double dpr = 1)
    {
        for (var end = now + seconds; now < end - 1e-9;)
        {
            now += 1.0 / 60;
            scene.Advance(now, input, dpr);
        }
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T53")]
    public void A_new_scene_draws_the_chosen_orb_alone()
    {
        var scene = new OrbScene(VoiceOrbKind.Membrane, 32, true);
        scene.Advance(0, Input(VoiceOrbKind.Membrane), 1);
        Assert.Equal(1, scene.Opacity(VoiceOrbKind.Membrane));
        Assert.Equal(0, scene.Opacity(VoiceOrbKind.Siri));
        Assert.NotNull(scene.Runtime(VoiceOrbKind.Membrane));
        Assert.Null(scene.Runtime(VoiceOrbKind.Siri));
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T53")]
    public void The_new_orb_takes_the_old_ones_phase_and_goes_on_from_it()
    {
        var scene = new OrbScene(VoiceOrbKind.Siri, 32, true);
        var now = 0.0;
        Run(scene, ref now, Input(VoiceOrbKind.Siri), 2.0);
        var phase = scene.Runtime(VoiceOrbKind.Siri)!.Phase;
        Assert.True(phase > 0.4, "Siri Wave rests at 0.246: two seconds is about 0.49");
        now += 1.0 / 60;
        scene.Advance(now, Input(VoiceOrbKind.Membrane), 1);
        var oldPhase = scene.Runtime(VoiceOrbKind.Siri)!.Phase;
        var handed = scene.Runtime(VoiceOrbKind.Membrane)!.Phase;
        // the new orb is made with the old one's phase as it stood when the choice changed, and its first frame has no delta (it adds nothing)
        Assert.Equal(phase, handed);
        Assert.Equal(phase + (1.0 / 60) * OrbSeeds.IdleSpeed(VoiceOrbKind.Siri), oldPhase, 4);
        Run(scene, ref now, Input(VoiceOrbKind.Membrane), 1.0);
        Assert.Equal(handed + 1.0 * OrbSeeds.IdleSpeed(VoiceOrbKind.Membrane), scene.Runtime(VoiceOrbKind.Membrane)!.Phase, 1);
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T53")]
    public void Both_orbs_are_drawn_while_they_cross_with_opacities_that_add_to_one()
    {
        var scene = new OrbScene(VoiceOrbKind.Siri, 32, true);
        var now = 0.0;
        Run(scene, ref now, Input(VoiceOrbKind.Siri), 0.5);
        Run(scene, ref now, Input(VoiceOrbKind.Membrane), 0.2);
        Assert.InRange(scene.Opacity(VoiceOrbKind.Siri), 0.05, 0.95);
        Assert.InRange(scene.Opacity(VoiceOrbKind.Membrane), 0.05, 0.95);
        Assert.Equal(1.0, scene.Opacity(VoiceOrbKind.Siri) + scene.Opacity(VoiceOrbKind.Membrane), 6);
        Run(scene, ref now, Input(VoiceOrbKind.Membrane), 1.0);
        Assert.Equal(0, scene.Opacity(VoiceOrbKind.Siri));
        Assert.Equal(1, scene.Opacity(VoiceOrbKind.Membrane));
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T53")]
    public void An_orb_that_is_not_drawn_is_not_stepped()
    {
        var scene = new OrbScene(VoiceOrbKind.Siri, 32, true);
        var now = 0.0;
        Run(scene, ref now, Input(VoiceOrbKind.Siri), 0.3);
        Run(scene, ref now, Input(VoiceOrbKind.Membrane), 1.0);   // the swap is over: siri has no mix left
        var frozen = scene.Runtime(VoiceOrbKind.Siri)!.Phase;
        Run(scene, ref now, Input(VoiceOrbKind.Membrane), 1.0);
        Assert.Equal(frozen, scene.Runtime(VoiceOrbKind.Siri)!.Phase);
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T53")]
    public void Reduce_motion_swaps_at_once_and_stops_the_phase()
    {
        var scene = new OrbScene(VoiceOrbKind.Siri, 32, true);
        var now = 0.0;
        Run(scene, ref now, Input(VoiceOrbKind.Siri, reduce: true), 0.2);
        var phase = scene.Runtime(VoiceOrbKind.Siri)!.Phase;
        now += 1.0 / 60;
        scene.Advance(now, Input(VoiceOrbKind.Membrane, reduce: true), 1);
        Assert.Equal(0, scene.Opacity(VoiceOrbKind.Siri));
        Assert.Equal(1, scene.Opacity(VoiceOrbKind.Membrane));
        Assert.Equal(phase, scene.Runtime(VoiceOrbKind.Membrane)!.Phase);
        Run(scene, ref now, Input(VoiceOrbKind.Membrane, reduce: true), 1.0);
        Assert.Equal(phase, scene.Runtime(VoiceOrbKind.Membrane)!.Phase);
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T51")]
    public void The_ball_follows_the_slot_spring_inside_the_72_px_view()
    {
        var scene = new OrbScene(VoiceOrbKind.Siri, 32, true);
        var now = 0.0;
        scene.Advance(now, Input(VoiceOrbKind.Siri, slot: 32), 1);
        Assert.Equal(32.0 / 72, scene.Runtime(VoiceOrbKind.Siri)!.Values[4], 5);
        Run(scene, ref now, Input(VoiceOrbKind.Siri, slot: 26), 2.0);
        Assert.Equal(26, scene.Motion.Diameter, 2);
        Assert.Equal(26.0 / 72, scene.Runtime(VoiceOrbKind.Siri)!.Values[4], 3);
        Assert.Equal(72, OrbScene.ViewPx);
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T51")]
    [Trait("Decision", "ED-NEW-voicefix-ui-7")]
    public void A_new_device_ratio_lays_the_orb_out_again_at_2x()
    {
        var scene = new OrbScene(VoiceOrbKind.Siri, 32, true);
        var now = 0.0;
        scene.Advance(now, Input(VoiceOrbKind.Siri), 1);
        Assert.Equal((72, 2), (scene.Runtime(VoiceOrbKind.Siri)!.DevicePx, scene.Runtime(VoiceOrbKind.Siri)!.RenderScale));
        now += 1.0 / 60;
        scene.Advance(now, Input(VoiceOrbKind.Siri), 2);
        Assert.Equal((144, 2), (scene.Runtime(VoiceOrbKind.Siri)!.DevicePx, scene.Runtime(VoiceOrbKind.Siri)!.RenderScale)); // every scale (ED-NEW-voicefix-ui-7)
        Assert.Equal(288, scene.Runtime(VoiceOrbKind.Siri)!.Values[0]);
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T51")]
    public void The_theme_reaches_the_lift_term_only()
    {
        var scene = new OrbScene(VoiceOrbKind.Siri, 32, true);
        var now = 0.0;
        Run(scene, ref now, Input(VoiceOrbKind.Siri, OrbMotionMode.Listening, bands: new OrbBands(0.5, 0.5, 0.5, 0.5)), 1.0);
        Assert.Equal(1, scene.Runtime(VoiceOrbKind.Siri)!.Mrw.Dark);
        var values = scene.Runtime(VoiceOrbKind.Siri)!.Values.ToArray();
        now += 1.0 / 60;
        scene.Advance(now, Input(VoiceOrbKind.Siri, OrbMotionMode.Listening, dark: false, bands: new OrbBands(0.5, 0.5, 0.5, 0.5)), 1);
        Assert.Equal(0, scene.Runtime(VoiceOrbKind.Siri)!.Mrw.Dark);
        Assert.True(scene.Runtime(VoiceOrbKind.Siri)!.Mrw.Lift > 0.5);
        // the colours (index 40 and up) are the seed mix's, theme-free
        Assert.Equal(values[40..], scene.Runtime(VoiceOrbKind.Siri)!.Values[40..].ToArray());
    }
}
