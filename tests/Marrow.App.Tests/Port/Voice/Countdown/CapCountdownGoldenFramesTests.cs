using Marrow.App.Port.Voice.Countdown;

namespace Marrow.App.Tests.Port.Voice.Countdown;

// VT-17 review (OD-0106 Countdown C, its amendments and its OKLCH glide): golden frames of the line, computed outside the code under test by an independent implementation of the
// owner's curves (OKLab per Ottosson, CSS ease-in-out cubic-bezier(.42, 0, .58, 1), 0.8 s glides starting at 0:15 and 0:05, a 0.4 s sin-squared brightening of up to 0.05 lightness that
// starts as the colour arrives, an out-of-gamut colour giving up chroma). The values are fixed numbers, so a change of any curve, time, colour or space moves a frame and fails here.
public sealed class CapCountdownGoldenFramesTests
{
    [Theory]
    [InlineData("Dark", 14.6, 0.715405827, 0.612123094, 0.466245283, 0.775000, 0.000000)]
    [InlineData("Dark", 14.4, 0.836190549, 0.647504380, 0.347072310, 0.941877, 0.000000)]
    [InlineData("Dark", 14.1, 0.911135684, 0.690032988, 0.323920230, 1.000000, 0.500000)]
    [InlineData("Dark", 14.0, 0.944040797, 0.721467950, 0.357103444, 1.000000, 1.000000)]
    [InlineData("Dark", 13.9, 0.911135684, 0.690032988, 0.323920230, 1.000000, 0.500000)]
    [InlineData("Dark", 4.6, 0.960578950, 0.482541097, 0.000620956, 1.000000, 0.000000)]
    [InlineData("Dark", 4.4, 0.995981772, 0.308945608, 0.123293438, 1.000000, 0.000000)]
    [InlineData("Dark", 4.1, 1.000000000, 0.330003578, 0.273090069, 1.000000, 0.500000)]
    [InlineData("Dark", 4.0, 1.000000000, 0.406509710, 0.344771832, 1.000000, 1.000000)]
    [InlineData("Dark", 3.9, 1.000000000, 0.330003578, 0.273090069, 1.000000, 0.500000)]
    [InlineData("Light", 14.6, 0.523508983, 0.410217128, 0.285870647, 0.775000, 0.000000)]
    [InlineData("Light", 14.4, 0.666971159, 0.458614960, 0.185763244, 0.941877, 0.000000)]
    [InlineData("Light", 14.1, 0.749524128, 0.504327815, 0.163027678, 1.000000, 0.500000)]
    [InlineData("Light", 14.0, 0.781610656, 0.534387666, 0.200424445, 1.000000, 1.000000)]
    [InlineData("Light", 13.9, 0.749524128, 0.504327815, 0.163027678, 1.000000, 0.500000)]
    [InlineData("Light", 4.6, 0.857104509, 0.397566358, 0.000000000, 1.000000, 0.000000)]
    [InlineData("Light", 4.4, 0.970001644, 0.280671642, 0.114291338, 1.000000, 0.000000)]
    [InlineData("Light", 4.1, 1.000000000, 0.330003578, 0.273090069, 1.000000, 0.500000)]
    [InlineData("Light", 4.0, 1.000000000, 0.406509710, 0.344771832, 1.000000, 1.000000)]
    [InlineData("Light", 3.9, 1.000000000, 0.330003578, 0.273090069, 1.000000, 0.500000)]
    [Trait("Rule", "R-TEST-4")] // Oracle OD-0106
    public void The_line_matches_the_golden_frames_of_the_glides_in_both_themes(string theme, double remaining, double r, double g, double b, double a, double bright)
    {
        var palette = theme == "Dark" ? CountdownPalette.Dark : CountdownPalette.Light;
        var frame = CapCountdown.Frame(TimeSpan.FromSeconds(remaining), false, palette);
        Assert.InRange(Math.Abs(frame.Colour.R - r), 0, 1e-5);
        Assert.InRange(Math.Abs(frame.Colour.G - g), 0, 1e-5);
        Assert.InRange(Math.Abs(frame.Colour.B - b), 0, 1e-5);
        Assert.InRange(Math.Abs(frame.Colour.A - a), 0, 1e-5);
        Assert.InRange(Math.Abs(frame.Brightening - bright), 0, 1e-5);
    }

    [Theory]
    [InlineData(30.0)]
    [InlineData(22.0)]
    [InlineData(15.5)]
    [InlineData(15.0)]
    [Trait("Rule", "R-TEST-4")] // Oracle OD-0106
    public void From_0_30_to_0_15_the_line_is_exactly_the_neutral_label_tone_in_both_themes(double remaining)
    {
        Assert.Equal(CountdownPalette.Dark.Neutral, CapCountdown.Frame(TimeSpan.FromSeconds(remaining), false, CountdownPalette.Dark).Colour);
        Assert.Equal(CountdownPalette.Light.Neutral, CapCountdown.Frame(TimeSpan.FromSeconds(remaining), false, CountdownPalette.Light).Colour);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Oracle OD-0106
    public void The_two_glides_are_0_8_seconds_each_and_the_arrival_is_the_palettes_own_colour()
    {
        Assert.Equal(CountdownPalette.Dark.Warning, CapCountdown.Frame(TimeSpan.FromSeconds(13.5), false, CountdownPalette.Dark).Colour);
        Assert.Equal(CountdownPalette.Dark.Danger, CapCountdown.Frame(TimeSpan.FromSeconds(3.0), false, CountdownPalette.Dark).Colour);
        Assert.Equal(CountdownPalette.Dark.Warning, CapCountdown.Frame(TimeSpan.FromSeconds(5.0001), false, CountdownPalette.Dark).Colour);
    }
}
