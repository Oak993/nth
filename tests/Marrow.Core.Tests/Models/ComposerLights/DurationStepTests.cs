using Marrow.Core.Models.ComposerLights;

namespace Marrow.Core.Tests.Models.ComposerLights;

// customization-SPEC CZ-13.2, CZ-T11: the Customization page's Speed slider steps 0.05 (cz.js:530) while the port's stored slider stepped 0.02; 0.05 is not a multiple of 0.02, so Clamped() moved
// 0.55 to 0.56. The stored step is 0.01 (every 0.02 and every 0.05 value stays put). The Corner slider runs 0..26 on the page but stores up to 32, and a stored 30 is not rewritten.
public sealed class DurationStepTests
{
    [Theory]
    [InlineData(0.5)]
    [InlineData(0.55)]
    [InlineData(0.6)]
    [InlineData(1.01)]
    [InlineData(3.69)]
    [InlineData(7.25)]
    [InlineData(13.95)]
    [InlineData(14)]
    [Trait("Feature", "CZ-4")]
    [Trait("Spec", "CZ-T11")]
    [Trait("Decision", "ED-251")]
    public void A_page_speed_survives_clamping_and_a_json_round_trip(double seconds)
    {
        var settings = ComposerLightSettings.Default with { Beam = BorderBeamOptions.Default with { Duration = seconds } };
        Assert.Equal(seconds, settings.Beam.Clamped().Duration);
        Assert.Equal(seconds, ComposerLightSettings.FromJson(settings.ToJson()).Beam.Duration);
        Assert.Equal(seconds, ComposerLightSettings.FromJson(settings.Clamped().ToJson()).Beam.Duration);
    }

    [Fact]
    [Trait("Feature", "CZ-4")]
    [Trait("Spec", "CZ-T11")]
    [Trait("Decision", "ED-251")]
    public void Every_step_of_the_speed_slider_is_a_fixed_point_of_the_clamp()
    {
        Assert.Equal(0.01, BorderBeamOptions.DurationSlider.Step);
        for (var i = 50; i <= 1400; i++)
        {
            var value = Math.Round(i * 0.01, 2);
            Assert.Equal(value, BorderBeamOptions.DurationSlider.Snap(value));
        }
    }

    [Fact]
    [Trait("Feature", "CZ-4")]
    [Trait("Spec", "CZ-T11")]
    [Trait("Decision", "ED-251")]
    public void A_stored_corner_above_the_pages_end_stays_as_it_is_stored()
    {
        var settings = ComposerLightSettings.Default with { Beam = BorderBeamOptions.Default with { Radius = 30 } };
        Assert.Equal(30, settings.Beam.Clamped().Radius);
        Assert.Equal(30, ComposerLightSettings.FromJson(settings.ToJson()).Beam.Radius);
        Assert.Equal(32, BorderBeamOptions.RadiusSlider.Max);
    }
}
