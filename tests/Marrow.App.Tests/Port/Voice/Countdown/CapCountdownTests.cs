using Marrow.App.Port.Voice.Countdown;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Port.Voice.Countdown;

// VT-17 (OD-0106 Countdown C and its amendments: colourless from 0:30 to 0:15, a smooth glide to yellow at 0:15 and to red at 0:05 through OKLCH, about 0.8 s each, with a
// soft brightening of about 0.4 s as the colour arrives; OD-0099 Q-V-6 B): the line's length and colour frame by frame at 60 Hz against the curves.
public sealed class CapCountdownTests
{
    private const double Frame60 = 1.0 / 60;
    private static readonly CountdownPalette Dark = CountdownPalette.Dark;

    private static CapCountdownFrame At(double remaining, bool reduce = false, CountdownPalette? palette = null) =>
        CapCountdown.Frame(TimeSpan.FromSeconds(remaining), reduce, palette ?? Dark);

    /// <summary>The 60 Hz frame times (seconds left) from <paramref name="from"/> down to <paramref name="to"/>.</summary>
    private static IEnumerable<double> Frames(double from, double to)
    {
        for (var i = 0; from - i * Frame60 >= to - 1e-9; i++) yield return from - i * Frame60;
    }

    private static void AssertColour(CountdownColour expected, CountdownColour actual, double tolerance = 2e-6)
    {
        Assert.InRange(Math.Abs(expected.R - actual.R), 0, tolerance);
        Assert.InRange(Math.Abs(expected.G - actual.G), 0, tolerance);
        Assert.InRange(Math.Abs(expected.B - actual.B), 0, tolerance);
        Assert.InRange(Math.Abs(expected.A - actual.A), 0, tolerance);
    }

    [Theory]
    [InlineData(30.0, 1.0)]
    [InlineData(22.5, 0.75)]
    [InlineData(15.0, 0.5)]
    [InlineData(7.5, 0.25)]
    [InlineData(3.0, 0.1)]
    [InlineData(0.3, 0.01)]
    [Trait("Req", "Q-V-6 B")]
    [Trait("Oracle", "OD-0106")]
    public void The_line_is_the_time_left_over_30_seconds_of_its_length(double remaining, double fraction)
    {
        var frame = At(remaining);

        Assert.True(frame.Visible);
        Assert.Equal(fraction, frame.Fraction, 9);
    }

    [Theory]
    [InlineData(30.0001)]
    [InlineData(45.0)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [Trait("Req", "Q-V-6 B")]
    [Trait("Oracle", "OD-0106")]
    public void Outside_the_last_30_seconds_there_is_no_line(double remaining)
    {
        var frame = At(remaining);

        Assert.False(frame.Visible);
        Assert.InRange(frame.Fraction, 0, 1);
    }

    [Fact]
    [Trait("Req", "Q-V-6 B")]
    [Trait("Oracle", "OD-0106")]
    public void The_line_only_ever_shortens_over_the_run_at_60_Hz()
    {
        var last = 2.0;
        foreach (var r in Frames(30, 0.01))
        {
            var f = At(r).Fraction;
            Assert.True(f <= last + 1e-12, $"at {r:F3} s the line grew");
            last = f;
        }
        Assert.True(last < 0.001);
    }

    [Fact]
    [Trait("Oracle", "OD-0106")]
    public void Under_Reduce_Motion_the_line_steps_once_a_second()
    {
        var seen = new SortedSet<double>();
        foreach (var r in Frames(30, 0.01)) seen.Add(At(r, reduce: true).Fraction);

        Assert.All(seen, f => Assert.Equal(Math.Round(f * 30), f * 30, 9)); // a whole number of 30ths
        Assert.Equal(30, seen.Count); // 30/30 down to 1/30: one step a second
        Assert.Equal(1.0, At(29.5, reduce: true).Fraction, 9); // holds until the second is up
        Assert.Equal(29.0 / 30, At(29.0, reduce: true).Fraction, 9);
        Assert.Equal(1.0 / 30, At(0.2, reduce: true).Fraction, 9);
    }

    [Fact]
    [Trait("Oracle", "OD-0106")]
    public void From_0_30_to_0_15_the_line_is_the_neutral_label_tone_with_no_brightening()
    {
        foreach (var r in Frames(30, 15))
        {
            var f = At(r);
            if (r > 15 + 1e-6) Assert.Equal(Dark.Neutral, f.Colour); // the palette's own colour, exactly
            else AssertColour(Dark.Neutral, f.Colour, 1e-6); // the frame at 0:15 itself (float noise either side of it)
            Assert.Equal(0.0, f.Brightening);
        }
    }

    [Fact]
    [Trait("Oracle", "OD-0106")]
    public void The_glide_to_yellow_starts_at_0_15_and_follows_the_curve_at_every_60_Hz_frame()
    {
        foreach (var r in Frames(15.5, 13.5))
        {
            var expected = CountdownReference.Expected(r, reduce: false, Dark);
            var f = At(r);
            AssertColour(expected.Colour, f.Colour);
            Assert.InRange(Math.Abs(expected.Bright - f.Brightening), 0, 1e-5); // TimeSpan ticks are 100 ns: the brightening's slope turns that into 1e-7
        }
        Assert.Equal(Dark.Neutral, At(15.0).Colour); // not a frame before 0:15
        Assert.NotEqual(Dark.Neutral, At(14.9).Colour);
    }

    [Fact]
    [Trait("Oracle", "OD-0106")]
    public void The_glide_to_red_starts_at_0_05_and_follows_the_curve_at_every_60_Hz_frame()
    {
        foreach (var r in Frames(5.5, 3.5))
        {
            var expected = CountdownReference.Expected(r, reduce: false, Dark);
            var f = At(r);
            AssertColour(expected.Colour, f.Colour);
            Assert.InRange(Math.Abs(expected.Bright - f.Brightening), 0, 1e-5); // TimeSpan ticks are 100 ns: the brightening's slope turns that into 1e-7
        }
        AssertColour(Dark.Warning, At(5.0).Colour); // yellow holds until 0:05, red is not at 0:10
        AssertColour(Dark.Warning, At(10.0).Colour);
    }

    [Fact]
    [Trait("Oracle", "OD-0106")]
    public void The_whole_run_matches_the_curves_in_both_themes_and_under_Reduce_Motion()
    {
        foreach (var palette in new[] { CountdownPalette.Dark, CountdownPalette.Light })
            foreach (var reduce in new[] { false, true })
                foreach (var r in Frames(30, 0.01))
                {
                    var expected = CountdownReference.Expected(r, reduce, palette);
                    var f = At(r, reduce, palette);
                    AssertColour(expected.Colour, f.Colour);
                    Assert.InRange(Math.Abs(expected.Bright - f.Brightening), 0, 1e-5); // TimeSpan ticks are 100 ns: the brightening's slope turns that into 1e-7
                }
    }

    [Fact]
    [Trait("Oracle", "OD-0106")]
    public void Each_colour_arrives_exactly_at_the_end_of_its_glide_and_holds()
    {
        // 0.8 s after 0:15 it is yellow (the brightening starts there), and the brightening is over 0.4 s later
        AssertColour(Dark.Warning, At(15 - 0.8).Colour);
        AssertColour(Dark.Warning, At(15 - 0.8 - 0.4).Colour);
        AssertColour(Dark.Warning, At(8).Colour);
        AssertColour(Dark.Danger, At(5 - 0.8).Colour);
        AssertColour(Dark.Danger, At(5 - 0.8 - 0.4).Colour);
        AssertColour(Dark.Danger, At(0.05).Colour);
    }

    [Fact]
    [Trait("Oracle", "OD-0106")]
    public void The_arrival_brightening_is_soft_0_4_seconds_and_peaks_in_the_middle()
    {
        var peak = At(15 - 0.8 - 0.2);
        Assert.Equal(1.0, peak.Brightening, 9);
        Assert.Equal(0.0, At(15 - 0.8).Brightening, 9);
        Assert.Equal(0.0, At(15 - 0.8 - 0.4).Brightening, 9);
        Assert.Equal(0.0, At(15 - 0.8 - 0.41).Brightening);
        Assert.Equal(0.0, At(15 - 0.79).Brightening); // none while the colour is still gliding
        foreach (var r in Frames(15 - 0.8, 15 - 0.8 - 0.4))
        {
            var b = At(r).Brightening;
            Assert.InRange(b, 0, 1);
        }
        // symmetric around the middle: the same amount an equal time before and after the peak
        Assert.Equal(At(15 - 0.8 - 0.1).Brightening, At(15 - 0.8 - 0.3).Brightening, 9);
        // the brightening lifts the colour above yellow at the peak and the same again for red
        Assert.True(Lightness(peak.Colour) > Lightness(Dark.Warning) + 0.04);
        Assert.True(Lightness(At(5 - 0.8 - 0.2).Colour) > Lightness(Dark.Danger) + 0.04);
    }

    private static double Lightness(CountdownColour c) => OklchReference.FromSrgb(c.R, c.G, c.B).L;

    [Fact]
    [Trait("Oracle", "OD-0106")]
    public void Under_Reduce_Motion_the_glide_stays_at_0_2_seconds_with_no_brightening()
    {
        AssertColour(Dark.Warning, At(15 - 0.2, reduce: true).Colour);
        Assert.NotEqual(Dark.Warning, At(15 - 0.19, reduce: true).Colour);
        AssertColour(Dark.Danger, At(5 - 0.2, reduce: true).Colour);
        foreach (var r in Frames(30, 0.01)) Assert.Equal(0.0, At(r, reduce: true).Brightening);
        // while the same moment in normal motion is still on its way
        Assert.NotEqual(Dark.Warning, At(15 - 0.2).Colour);
    }

    [Fact]
    [Trait("Oracle", "OD-0106")]
    public void The_colour_is_never_cut_no_frame_steps_more_than_6_percent_of_a_channel()
    {
        foreach (var palette in new[] { CountdownPalette.Dark, CountdownPalette.Light })
            foreach (var reduce in new[] { false, true })
            {
                var previous = At(30, reduce, palette).Colour;
                var worst = 0.0;
                foreach (var r in Frames(30 - Frame60, 0.01))
                {
                    var c = At(r, reduce, palette).Colour;
                    worst = Math.Max(worst, Math.Max(Math.Abs(c.R - previous.R), Math.Max(Math.Abs(c.G - previous.G), Math.Max(Math.Abs(c.B - previous.B), Math.Abs(c.A - previous.A)))));
                    previous = c;
                }
                // normal motion: the glide spans 0.8 s (48 frames); under Reduce Motion it spans 0.2 s (12 frames) and is allowed a proportionally bigger step
                Assert.True(worst < (reduce ? 0.2 : 0.06), $"{(reduce ? "reduced" : "normal")}: the largest frame step was {worst:F4}");
            }
    }

    [Fact]
    [Trait("Oracle", "OD-0106")]
    public void The_glide_to_yellow_goes_straight_to_yellows_hue_with_chroma_and_lightness_never_dipping_below_the_ends()
    {
        var neutral = OklchReference.FromSrgb(Dark.Neutral.R, Dark.Neutral.G, Dark.Neutral.B);
        var warning = OklchReference.FromSrgb(Dark.Warning.R, Dark.Warning.G, Dark.Warning.B);
        var lastChroma = 0.0;
        foreach (var r in Frames(15 - Frame60, 15 - 0.8))
        {
            var c = At(r).Colour;
            var lch = OklchReference.FromSrgb(c.R, c.G, c.B);
            if (lch.C > 0.02) Assert.InRange(Math.Abs(lch.H - warning.H), 0, 8); // no swing through another hue
            // the neutral's faint blue tint (chroma 0.009) passes through the colourless axis on its way to yellow: below one just-noticeable step, never a muddy midpoint
            Assert.True(lch.C >= neutral.C - 0.01, "no grey, muddy midpoint");
            if (lastChroma > 0.02) Assert.True(lch.C >= lastChroma - 1e-9, "once it has colour, chroma only grows towards yellow");
            Assert.True(lch.L >= Math.Min(neutral.L, warning.L) - 0.005);
            lastChroma = lch.C;
        }
    }

    [Fact]
    [Trait("Oracle", "OD-0106")]
    public void The_glide_to_red_turns_the_hue_one_way_and_keeps_its_chroma_with_no_brown_dip()
    {
        var warning = OklchReference.FromSrgb(Dark.Warning.R, Dark.Warning.G, Dark.Warning.B);
        var danger = OklchReference.FromSrgb(Dark.Danger.R, Dark.Danger.G, Dark.Danger.B);
        var lastHue = warning.H + 1e-6;
        foreach (var r in Frames(5 - Frame60, 5 - 0.8))
        {
            var c = At(r).Colour;
            var lch = OklchReference.FromSrgb(c.R, c.G, c.B);
            Assert.True(lch.H <= lastHue + 0.01, "the hue only turns towards red");
            Assert.True(lch.C >= Math.Min(warning.C, danger.C) - 0.01, "no grey, muddy midpoint");
            lastHue = lch.H;
        }
        Assert.InRange(danger.H, 0, warning.H); // red's hue is below yellow's: the turn is the short way, through orange
    }

    [Fact]
    [Trait("Oracle", "OD-0106")]
    [Trait("Decision", "ED-NEW-cloud-vt-17")]
    public void The_theme_palettes_are_the_pills_label_tone_the_voice_designs_warning_and_the_apps_error_red()
    {
        static CountdownColour Hex(int rgb, double a = 1) => new(((rgb >> 16) & 0xFF) / 255.0, ((rgb >> 8) & 0xFF) / 255.0, (rgb & 0xFF) / 255.0, a);

        Assert.Equal(new CountdownPalette(Hex(0x8D8E94, 0.55), Hex(0xE0A84A), Hex(0xFF3B30)), CountdownPalette.Dark); // --pgLab at 55%, --vwarn (dark), systemRed
        Assert.Equal(new CountdownPalette(Hex(0x55565B, 0.55), Hex(0xB7791F), Hex(0xFF3B30)), CountdownPalette.Light); // --pgLab at 55%, --vwarn (light), systemRed
    }
}
