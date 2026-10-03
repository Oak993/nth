using Marrow.Core.Models.ComposerLights;

namespace Marrow.Core.Tests.Models.ComposerLights;

// DEVIATION OD-0089 (owner-ruled addition; ED-251): the playground's resolved props and its frame clock. Expected values are
// the playground's own logic.js beamEff() and advance() under node (2026-10-03): Component built with dark true unless said,
// advance('u', AURA_DEFAULTS, BEAM_DEFAULTS, dt, e = dt, now, null) called frame by frame (tick() keeps k at 1).
public sealed class ComposerLightClockTests
{
    [Theory]
    [Trait("Decision", "ED-251")] // logic.js beamEff(b, st) for 820 x 52 (CW, CH, CR 22)
    [InlineData("default", 0.26, 0.42, 0.24, 1.3, 1.2, 30, 3.692552685682387)]
    [InlineData("defaultLight", 0.12, 0.26, 0.34, 1.3, 1.5, 30, 3.692552685682387)]
    [InlineData("mono", 0.13, 0.21, 0.12, 1.3, 1.2, 30, 3.692552685682387)]
    [InlineData("respondRest", 0.11440000000000002, 0.18480000000000002, 0.10560000000000001, 1.3, 1.2, 30, 3.692552685682387)]
    [InlineData("respondTyping", 0.16640000000000005, 0.26880000000000004, 0.15360000000000001, 1.3, 1.2, 30, 3.692552685682387)]
    [InlineData("line", 1.14, 0.7, 0.8, 1.3, 1.2, 13, 5.609709962168979)]
    [InlineData("po", 1.96, 1.04, 0.42, 1.7, 0.6, 30, 2.3)]
    [InlineData("pi", 1.54, 0.44, 0.66, 0.75, 1.2, 30, 2.3)]
    [InlineData("pkg", 0.26, 0.42, 0.24, 1.3, 1.2, 30, 1.96)]
    public void Resolved_props_are_beamEff(string name, double so, double io, double bo, double br, double sat, double hr, double dur)
    {
        var d = BorderBeamOptions.Default;
        var (b, phase, dark) = name switch
        {
            "defaultLight" => (d, ComposerLightPhase.Rest, false),
            "mono" => (d with { Palette = BeamPalette.Mono }, ComposerLightPhase.Rest, true),
            "respondRest" => (d with { Respond = true, Strength = 0.8 }, ComposerLightPhase.Rest, true),
            "respondTyping" => (d with { Respond = true, Strength = 0.8 }, ComposerLightPhase.Typing, true),
            "line" => (d.WithType(BeamType.Line) with { HueRange = 40 }, ComposerLightPhase.Running, true),
            "po" => (d.WithType(BeamType.PulseOutside) with { GlowSize = 1.3 }, ComposerLightPhase.Rest, false),
            "pi" => (d.WithType(BeamType.PulseInner) with { Theme = BeamTheme.Dark }, ComposerLightPhase.Rest, false),
            "pkg" => (d with { Fitted = false }, ComposerLightPhase.Rest, true),
            _ => (d, ComposerLightPhase.Rest, true),
        };
        var e = BorderBeamResolved.For(b, phase, dark, 820, 52, 22);
        Assert.Equal(so, e.StrokeOpacity, 12);
        Assert.Equal(io, e.InnerOpacity, 12);
        Assert.Equal(bo, e.BloomOpacity, 12);
        Assert.Equal(br, e.Brightness, 12);
        Assert.Equal(sat, e.Saturation, 12);
        Assert.Equal(hr, e.HueRange, 12);
        Assert.Equal(dur, e.Duration, 12);
        Assert.Equal(22, e.Radius);
        Assert.Equal(2.343, e.GlowX, 12);
        Assert.Equal(0.371, e.GlowY, 12);
        if (name == "po") { Assert.Equal(10.4, e.Blur, 12); Assert.Equal(7.8, e.CoreBlur, 12); Assert.Equal(19.5, e.BloomBlur, 12); }
        if (name == "defaultLight") { Assert.Equal(6, e.CoreBlur); Assert.Equal(15, e.BloomBlur); }
        Assert.Equal(name == "mono", e.Static);
    }

    private static readonly double[] F60Pi = [1.0438482553663146, 1.0970002857019643, -10.416570542444887, 3.292480620029533, 1.073061962334668, 1.1084490822848427, 11.182137446941175, -18.229721639678196, 1.0684682760235789, 1.0815164385865876, 2.6486489825824684, -9.810472701651744, 1.0203495786286068, 0.8727415355847539, 0.5261930034635858, 0.7818189357462175, 0.8592163172778214];
    private static readonly double[] F120Po = [0.9521685430864754, 1.1453679772812013, 6.32170836550992, -5.669544977309156, 1.1357426344073343, 1.0192356396674116, -3.5217077986998166, 0.28871639285570616, 0.9992747402303758, 1.122310984154428, 10.646535047842713, 3.575309726372206, 1.158853687611455, 0.9921763763760227, 0.6964637438136672, 0.540450539126554, 0.67536776291483];

    [Fact]
    [Trait("Decision", "ED-251")] // logic.js advance(): 240 frames of 1/60 s; at frame 121 the aura's speed goes 12 -> 4 s and breathing off
    public void The_clock_integrates_every_phase_as_the_playground()
    {
        var clock = new ComposerLightClock(breathing: true, lightTheme: false);
        var dur = BorderBeamMath.Duration(BorderBeamOptions.Default, 820, 52, 22);
        double speed = 12; var breathing = true;
        for (var i = 1; i <= 240; i++)
        {
            if (i == 121) { speed = 4; breathing = false; }
            clock.Advance(1 / 60.0, false, speed, breathing, dur, false, false, 22);
            switch (i)
            {
                case 1:
                    Assert.Equal(10.135091095689507, clock.AuraAngle, 9); Assert.Equal(0.9999571656219447, clock.AuraScale, 12);
                    Assert.Equal(1.624892184548802, clock.BeamAngle, 9); Assert.Equal(-0.9999708042257582, clock.HueSwing, 12);
                    Assert.Equal(0.375, clock.PulseHueInner, 12); Assert.Equal(0.42857142857142855, clock.PulseHueOutside, 12);
                    Assert.Equal(0.06406223046137201, clock.LineX, 12); Assert.Equal(0.8001650618321672, clock.LineH, 12);
                    break;
                case 60:
                    Assert.Equal(23.495415406345586, clock.AuraAngle, 9); Assert.Equal(0.8749999999999998, clock.AuraScale, 12);
                    Assert.Equal(97.49353107292802, clock.BeamAngle, 9); Assert.Equal(-0.8877383092049993, clock.HueSwing, 12);
                    Assert.Equal(-0.7416764884086344, clock.HueSwing8, 12); Assert.Equal(0.7290768204573336, clock.LineEdge, 12);
                    Assert.Equal(1.265729988292747, clock.LineSpike1, 12); Assert.Equal(0.8388703963292123, clock.LineSpike2, 12);
                    for (var j = 0; j < 17; j++) Assert.Equal(F60Pi[j], clock.PulseInner[j], 9);
                    break;
                case 120:
                    Assert.Equal(58.07550684712711, clock.AuraAngle, 9); Assert.Equal(0.75, clock.AuraScale, 12);
                    Assert.Equal(194.98706214585644, clock.BeamAngle, 9); Assert.Equal(1.4791846359085328, clock.LineW, 12);
                    for (var j = 0; j < 17; j++) Assert.Equal(F120Po[j], clock.PulseOutside[j], 9);
                    break;
                case 180:
                    Assert.Equal(157.83176367529302, clock.AuraAngle, 9); Assert.Equal(0.9897393751720126, clock.AuraScale, 12);
                    Assert.Equal(292.48059321878486, clock.BeamAngle, 9); Assert.Equal(0.7624460922744024, clock.LineX, 12);
                    break;
                case 240:
                    Assert.Equal(194.06814518761547, clock.AuraAngle, 9); Assert.Equal(1, clock.AuraScale, 9);
                    Assert.Equal(29.974124291713284, clock.BeamAngle, 9); Assert.Equal(0.5364505641957726, clock.HueSwing, 12);
                    Assert.Equal(90, clock.PulseHueInner, 9); Assert.Equal(102.85714285714315, clock.PulseHueOutside, 9);
                    break;
            }
        }
    }

    [Fact]
    [Trait("Decision", "ED-251")] // logic.js advance() for a light-theme Line beam (dark false): 150 frames of 20 ms, the aura at speed 4 without breathing
    public void A_light_line_clock_mixes_the_light_oscillators()
    {
        var b = BorderBeamOptions.Default.WithType(BeamType.Line);
        var clock = new ComposerLightClock(breathing: false, lightTheme: true);
        var dur = BorderBeamMath.Duration(b, 820, 52, 22);
        for (var i = 1; i <= 150; i++) clock.Advance(0.02, false, 4, false, dur, false, true, 22);
        Assert.Equal(304.1405630135041, clock.AuraAngle, 9);
        Assert.Equal(192.52332246824756, clock.BeamAngle, 9);
        Assert.Equal(0.520872204113746, clock.LineX, 12);
        Assert.Equal(1.4826064965718784, clock.LineW, 12);
        Assert.Equal(1.024892475535473, clock.LineH, 12);
        Assert.Equal(1, clock.LineEdge, 12);
        Assert.Equal(1.03275345554502, clock.LineSpike1, 12);
        Assert.Equal(0.8004298259945023, clock.LineSpike2, 12);
        Assert.Equal(1.2590417878411175, clock.PulseOutside[12], 9);
        Assert.Equal(1, clock.ThemeMix, 12);
    }

    [Fact]
    [Trait("Decision", "ED-251")] // logic.js tick(): reduce motion holds k at 0, so no phase moves; a frame's step is capped at 50 ms
    public void Reduce_motion_stills_every_phase()
    {
        var clock = new ComposerLightClock(true, false);
        for (var i = 0; i < 30; i++) clock.Advance(1 / 60.0, true, 12, true, 3.69, false, false, 22);
        Assert.Equal(0, clock.K);
        Assert.Equal(10, clock.AuraAngle, 4);            // 10 + 180 bez(0): the reference's speen at rest
        Assert.Equal(1, clock.AuraScale, 12);
        Assert.Equal(0, clock.BeamAngle, 12);
        Assert.Equal(-1, clock.HueSwing, 9);
        var capped = new ComposerLightClock(true, false);
        capped.Advance(5, false, 12, true, 3.69, false, false, 22);
        Assert.Equal(0.05 / 3.69 * 360, capped.BeamAngle, 9);
    }

    [Fact]
    [Trait("Decision", "ED-251")] // logic.js beamRows(): the Auto values the sliders show (E.autoDur, E.autoBr, E.autoSat, E.autoCb, E.autoBb, the glow scale) whatever is set
    public void Auto_values_ignore_what_is_set()
    {
        var b = BorderBeamOptions.Default.WithType(BeamType.PulseOutside) with { Duration = 9, Brightness = 2, Saturation = 0.3, CoreBlur = 12, BloomBlur = 40, GlowX = 3, GlowY = 3, GlowSize = 1.3 };
        var auto = BorderBeamResolved.AutoValues(b, false, 820, 52, 22);
        Assert.Equal((2.3, 1.7, 0.6, 7.8, 19.5, 2.343, 0.371), (auto.Duration, auto.Brightness, auto.Saturation, auto.CoreBlur, auto.BloomBlur, auto.GlowX, auto.GlowY));
        Assert.Equal(3.692552685682387, BorderBeamResolved.AutoValues(BorderBeamOptions.Default with { Duration = 5 }, true, 820, 52, 22).Duration, 12);
    }
}
