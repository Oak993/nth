using Avalonia.Media;
using Avalonia.Styling;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Port;

// SwiftUI's Color.mix(with:by:in:) takes a colour space and its default is .perceptual (Oklab); EffortSlider.swift:841 and :964
// name none. ENGINEERING DECISION (ED-NEW-effortslider2, OD-0098: the framework's documented behaviour where the code relies on
// it): those two sites mix in Oklab. The expected values below are not read from the port: they come from an independent
// Oklab implementation (Ottosson's published matrices, E:/Droppy/work/scratch/effortslider2/oklab.py).
public sealed class SwiftColorPerceptualMixTests
{
    private static Color Drawn(SwiftColor colour) => colour.Resolve(ThemeVariant.Light);

    [Theory]
    [Trait("Rule", "R-TEST-4")] // expected values from an independent Oklab implementation: Color.mix(with:by:) defaults to the perceptual space
    [InlineData(0.24, 0.36, 0.98, 0.18, 90, 126, 254)]    // EffortSlider.swift:911 DeepSeek's fill lightened by :964
    [InlineData(0.49, 0.58, 1.0, 0.18, 147, 168, 255)]    // :877 DeepSeek's title colour lightened by :964
    [InlineData(246 / 255.0, 32 / 255.0, 102 / 255.0, 0.22, 255, 103, 134)] // :841 a pink accent
    [InlineData(0.80, 0.42, 0.28, 0.18, 216, 134, 105)]   // :895 Claude's fill
    [InlineData(0, 0, 0, 0.5, 99, 99, 99)]                // half way from black to white is not the sRGB middle grey 128
    public void Mixing_toward_white_interpolates_in_oklab(double r, double g, double b, double by, int er, int eg, int eb)
    {
        var mixed = Drawn(new SwiftColor(r, g, b).MixPerceptual(SwiftColor.White, by));
        Assert.True(Math.Abs(mixed.R - er) <= 1 && Math.Abs(mixed.G - eg) <= 1 && Math.Abs(mixed.B - eb) <= 1, $"({mixed.R}, {mixed.G}, {mixed.B}) is not ({er}, {eg}, {eb})");
        Assert.Equal(255, mixed.A);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // the negative control: the component-wise Mix the port's other sites use differs from the perceptual one, here by the whole grey
    public void The_component_wise_mix_is_a_different_colour()
    {
        var perceptual = Drawn(SwiftColor.Black.MixPerceptual(SwiftColor.White, 0.5));
        var componentWise = Drawn(SwiftColor.Black.Mix(SwiftColor.White, 0.5));
        Assert.InRange(componentWise.R, 127, 129);
        Assert.True(componentWise.R - perceptual.R >= 25, $"{componentWise.R} against {perceptual.R}");
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // the ends are the colours themselves, the opacity is interpolated linearly, and an appearance pair mixes each member
    public void The_ends_are_exact_the_opacity_is_linear_and_a_pair_mixes_both_members()
    {
        var blue = new SwiftColor(0.2, 0.4, 0.9, 0.5);
        static void Near(Color expected, Color actual) => Assert.True(Math.Abs(expected.R - actual.R) <= 1 && Math.Abs(expected.G - actual.G) <= 1 && Math.Abs(expected.B - actual.B) <= 1 && expected.A == actual.A, $"{actual} is not {expected}");
        Near(Drawn(blue), Drawn(blue.MixPerceptual(SwiftColor.White, 0))); // the round trip through Oklab is exact to one level
        Near(Color.FromArgb(255, 255, 255, 255), Drawn(blue.MixPerceptual(SwiftColor.White, 1)));
        Assert.InRange(Drawn(blue.MixPerceptual(SwiftColor.White, 0.5)).A, 190, 192); // 0.5 -> 1 half way: 0.75
        var pair = SwiftColor.Dynamic(light: new SwiftColor(0.24, 0.36, 0.98), dark: new SwiftColor(0.49, 0.58, 1.0));
        var mixed = pair.MixPerceptual(SwiftColor.White, 0.18);
        Near(Drawn(new SwiftColor(0.24, 0.36, 0.98).MixPerceptual(SwiftColor.White, 0.18)), mixed.Resolve(ThemeVariant.Light));
        Near(new SwiftColor(0.49, 0.58, 1.0).MixPerceptual(SwiftColor.White, 0.18).Resolve(ThemeVariant.Dark), mixed.Resolve(ThemeVariant.Dark));
    }
}
