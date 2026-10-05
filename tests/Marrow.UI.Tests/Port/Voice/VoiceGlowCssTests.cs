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

    // the values the recipe and the band round: opacities, band alphas (d x a x w), the lengths, exact binary fractions (the ties) and a few ulps either side of every half
    private static IEnumerable<(double Value, int Digits)> Sweep()
    {
        var random = new Random(9);
        foreach (var digits in new[] { 2, 3, 4 })
        {
            var power = Math.Pow(10, digits);
            for (var n = 0; n <= 3000; n++)
            {
                var half = (n + 0.5) / power;
                yield return (half, digits);
                yield return (Math.BitDecrement(half), digits);
                yield return (Math.BitIncrement(half), digits);
                yield return (Math.BitDecrement(Math.BitDecrement(half)), digits);
                yield return (n / power, digits);
            }
            for (var k = 0; k < 4000; k++) yield return (k / 1024.0 / 4, digits);                       // exact binary fractions: ties for 2, 3 and 4 digits
            for (var k = 0; k < 20000; k++) yield return (random.NextDouble() * 3, digits);
        }
        // the band's own alphas: (0.42 or 0.4) x h x (1, 0.55, 0.9, 0.3) x (0.16, 0.2, 0.26, 0.34) over the strengths the driver reaches
        foreach (var dark in new[] { 0.42, 0.4 })
            for (var h = 0.005; h <= 1; h += 0.0007)
                foreach (var colour in new[] { 1, 0.55, 0.9, 0.3 })
                    foreach (var width in new[] { 0.16, 0.2, 0.26, 0.34 })
                        yield return (dark * h * colour * width, 3);
        yield return (1.005, 2);
        yield return (0.0625, 3);
        yield return (2.675, 2);
        yield return (-0.0625, 3);
        yield return (-1.125, 2);
        yield return (0, 3);
        yield return (1e-9, 3);
        yield return (123456.789, 2);
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-NEW-f-vt-9")]
    [Trait("Spec", "V-T60")]
    public void Fixed_equals_the_exact_toFixed_on_a_sweep_of_every_kind_of_value()
    {
        // Fixed is the fused-multiply-add form; FixedByText is the exact decimal expansion it replaced (the oracle)
        var count = 0;
        foreach (var (value, digits) in Sweep())
        {
            var expected = VoiceGlowCss.FixedByText(value, digits);
            var actual = VoiceGlowCss.Fixed(value, digits);
            if (expected != actual) Assert.Fail($"Fixed({value:R}, {digits}) = {actual:R}, the exact rounding is {expected:R}");
            count++;
        }
        Assert.True(count > 100000, count + " values swept");
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-NEW-f-vt-9")]
    [Trait("Spec", "V-T62")]
    public void Fixed_allocates_nothing()
    {
        var sink = VoiceGlowCss.Fixed(0.1234, 3);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) sink += VoiceGlowCss.Fixed(i / 7919.0, 3);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(sink > 0);
    }
}
