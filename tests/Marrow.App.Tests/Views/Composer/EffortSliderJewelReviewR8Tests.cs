using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Testing;

namespace Marrow.App.Tests.Views.Composer;

// REVIEW (adversarial, unit jewel-knob, review round 8 on 20f64c18b): each test states what the unit's tests must do and
// failed on the head reviewed; each passes from the round 8 answer, its body unchanged, kept under a plain name. The last
// test is the guard on the records OD-0090 amended: since the blitz-effortslider2 unit it lists only UI-COMPOSER-035.
[Collection("Headless")]
public sealed class EffortSliderJewelReviewR8Tests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-252")] // ED-252: under reduce motion the knob reads the strike's still moment, as the tube draws it; its signal is scaled by the fast effect's opacity, so turning fast mode on lights it only as far as the bolt has faded in
    public void Under_reduce_motion_the_knob_lights_fully_once_the_bolt_has_faded_in() => headless.OnUiThread(() =>
    {
        // the knob's loop under reduce motion runs "only while something moves (the springs, a fade, the light letting go
        // after fast mode)" (EffortSlider.OnFrame): in fast mode !IsFast && lit is false, and the fast effect's own 0.3 s
        // opacity fade is not one of the things that keep it running. Turned on, the knob reads the bolt at the opacity of
        // the first frame after the turn and then idles, so it stays lit at that fraction while the bolt goes on to 1.
        using var fresh = JewelFixtures.Rig(index: 1, fast: true, reduceMotion: true);
        for (var i = 0; i < 30; i++) fresh.Frame();
        using var rig = JewelFixtures.Rig(index: 1, reduceMotion: true);
        for (var i = 0; i < 4; i++) rig.Frame();
        rig.Slider.UpdateLook(true, "Title", null);
        var effect = rig.Slider.GetVisualDescendants().OfType<TrackEffect>().Single(e => TrackEffect.IsFast(e.Look));
        rig.Frame(); // the next refresh: the bolt only just fading in
        var first = rig.Slider.Signal.RawCharge;
        for (var i = 0; i < 60; i++) rig.Frame(); // a second: the 0.3 s fade long over
        Assert.Equal(1, effect.Opacity);
        Assert.Equal(1, fresh.Slider.Signal.RawCharge); // the still moment, read by a knob that started in fast mode
        Assert.True(rig.Slider.Signal.RawCharge == 1 && rig.Slider.Signal.Charge == fresh.Slider.Signal.Charge,
            $"a second after fast mode came on the bolt is fully in, but the knob reads charge {rig.Slider.Signal.RawCharge} (shaped {rig.Slider.Signal.Charge}, flare {rig.Slider.Signal.Flare}; the first frame read {first}); a knob that started in fast mode reads {fresh.Slider.Signal.RawCharge} (shaped {fresh.Slider.Signal.Charge}, flare {fresh.Slider.Signal.Flare})");
    });

    [Fact]
    [Trait("Rule", "R-TEST-1")] // a test that carries a Req trait proves every clause of that record
    public void No_test_credits_UI_COMPOSER_035_whose_page_layers_over_the_orb_have_no_pixel_comparison()
    {
        // OD-0090 replaced the effort slider's knob (EffortSlider.swift:777-792 the plain Liquid Glass lens and its glow disc,
        // :802-818 the drag's springs and its pointer rounding, :713 thumbSize 42, :620-624 and :820 the 42 point heights).
        // Its records were amended for it with a deviation naming OD-0090 (PR-COMPOSER-117, -123, -125, -126, -131 and
        // UI-COMPOSER-036), and each of those is now proven clause by clause by tests that carry its Req trait (the geometry,
        // the springs against the locked page's own frames, the haptic, the accessibility element, the shadows from the page's
        // numbers, the single-level note, the derived colours).
        // UI-COMPOSER-035 stays guarded. Its colours clause states the page's layers drawn over the orb: "a refractive glass
        // shell of its own with the tube refracted at its rim, a fine red and blue fringe and a soft inner shade just inside
        // its hairline (jewel-knob.html:113-115, :128-130)" (JewelOrb.cs: the page pass, .thru.r and .kw::after). The orb
        // itself is compared with the page's own pixels (EffortSliderJewelOrbTests.The_orb_draws_the_locked_orbs_pixels:
        // Chrome's WebGL2, no page uniforms), and the page pass is only checked for being drawn (EffortSliderJewelCpuTests,
        // EffortSliderJewelFramesTests): no test compares the refracted ring, the fringe or the inner shade with a page capture,
        // so the whole record is not proven. Closing it needs a capture of the page's knob element (orb, ring and layers over a
        // tube) from the locked page and a pixel comparison with the port's page pass.
        string[] superseded = ["UI-COMPOSER-035"];
        var credited = Traceability.TestMethods(typeof(EffortSliderJewelReviewR8Tests).Assembly)
            .SelectMany(m => Traceability.Traits(m).Where(t => t.Name == "Req" && superseded.Contains(t.Value))
                .Select(t => $"{m.DeclaringType!.Name}.{m.Name} ({t.Value})"))
            .ToList();
        Assert.True(credited.Count == 0, "credited although the page layers over the orb are not compared with the page: " + string.Join(", ", credited));
    }
}
