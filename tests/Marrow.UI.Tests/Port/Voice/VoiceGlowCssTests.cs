using Marrow.UI.Port.Voice.Glow;

namespace Marrow.UI.Tests.Port.Voice;

// VT-9 (V-13.1): the small helpers of the stylesheet recipe behave as the JavaScript they carry: Math.round (halves up), toFixed, lt(), S(), wa()/qa().
public sealed class VoiceGlowCssTests
{
    [Theory]
    [InlineData(0.5, 1)]
    [InlineData(1.5, 2)]
    [InlineData(2.5, 3)]
    [InlineData(-0.5, 0)]
    [InlineData(-1.5, -1)]
    [InlineData(2.4999, 2)]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T60")]
    public void Round_is_Math_round_with_halves_up(double value, double expected) => Assert.Equal(expected, VoiceGlowCss.Round(value));

    [Theory]
    [InlineData(7.04, 7.0)]
    [InlineData(7.05, 7.1)]
    [InlineData(-0.04, 0.0)]
    [InlineData(21.96, 22.0)]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T60")]
    public void Tenth_is_the_designs_S(double value, double expected) => Assert.Equal(expected, VoiceGlowCss.Tenth(value), 12);

    [Theory]
    [InlineData(10, 0.7, 7)]
    [InlineData(10, 0.05, 0.5)]    // max(0.5, ...)
    [InlineData(8, 0.7, 5.6)]
    [InlineData(10, 1, 10)]
    [InlineData(10, 2.8, 28)]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T60")]
    public void Lt_is_max_half_a_pixel_and_the_value_to_a_hundredth(double n, double e, double expected) => Assert.Equal(expected, VoiceGlowCss.Lt(n, e), 12);

    [Theory]
    [InlineData(0.46, 2, 0.46)]
    [InlineData(1.16, 2, 1.16)]
    [InlineData(1.2349, 2, 1.23)]
    [InlineData(0.0674, 3, 0.067)]
    [InlineData(0.0625, 3, 0.063)]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T60")]
    public void Fixed_is_toFixed_read_back(double value, int digits, double expected) => Assert.Equal(expected, VoiceGlowCss.Fixed(value, digits), 12);

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T60")]
    public void Parse_reads_hex_and_rgb_texts_as_the_package_does()
    {
        Assert.Equal(new GlowRgb(255, 0, 0), VoiceGlowCss.Parse("#ff0000"));
        Assert.Equal(new GlowRgb(255, 0, 0), VoiceGlowCss.Parse("#F00"));
        Assert.Equal(new GlowRgb(0, 128, 255), VoiceGlowCss.Parse(" rgb(0, 128, 255) "));
        Assert.Equal(new GlowRgb(10, 20, 31), VoiceGlowCss.Parse("rgba(10, 20.4, 30.5, 0.2)"));   // channels rounded half up, the alpha ignored
        Assert.Null(VoiceGlowCss.Parse("nope"));
        Assert.Null(VoiceGlowCss.Parse("#ff00"));
        Assert.Null(VoiceGlowCss.Parse(null));
        Assert.Null(VoiceGlowCss.Parse("hsl(0, 100%, 50%)"));
    }
}
