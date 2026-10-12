using System.Globalization;
using Marrow.UI.NthUi.Charts;

namespace Marrow.UI.Tests.NthUi;

// DESIGN.md 7.2 chart components and 7.5 tabular numerals (web surfaces W16b; ED-NEW-web-w16b-1): the numbers an axis is made
// of are round (1, 2 and 5 times a power of ten), a column of labels shows the same decimals, no tick carries floating point
// dust, and a pie's whole percentages add up to 100.
public sealed class NthUiChartMathTests
{
    [Theory]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    [InlineData(0.17, 0.2)]
    [InlineData(1, 1)]
    [InlineData(1.01, 2)]
    [InlineData(2.1, 2.5)]
    [InlineData(21, 25)]
    [InlineData(2.6, 5)]
    [InlineData(4.2, 5)]
    [InlineData(5.1, 10)]
    [InlineData(8, 10)]
    [InlineData(120, 200)]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(double.NaN, 1)]
    [InlineData(double.PositiveInfinity, 1)]
    public void The_step_is_the_smallest_of_1_2_2_5_5_and_10_times_a_power_of_ten_that_is_big_enough(double raw, double expected) =>
        Assert.Equal(expected, NthUiChartMath.NiceStep(raw), 9);

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void Ticks_are_round_values_inside_the_range_with_no_floating_point_dust()
    {
        Assert.Equal([0.0, 20, 40, 60], NthUiChartMath.Ticks(0, 60));
        Assert.Equal([0.0, 20, 40, 60, 80, 100], NthUiChartMath.Ticks(0, 100));
        Assert.Equal([3.8, 4, 4.2, 4.4, 4.6, 4.8], NthUiChartMath.Ticks(3.8, 4.8));
        Assert.Equal([0.0, 0.1, 0.2, 0.3, 0.4, 0.5], NthUiChartMath.Ticks(0, 0.5));
        Assert.Equal([-20.0, 0, 20, 40], NthUiChartMath.Ticks(-20, 40));
        Assert.Empty(NthUiChartMath.Ticks(5, 5));
        Assert.Empty(NthUiChartMath.Ticks(double.NaN, 5));
        Assert.True(NthUiChartMath.Ticks(0, 1e9).Length <= 8);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void A_domain_widens_to_whole_steps_except_for_an_edge_the_author_fixed()
    {
        Assert.Equal((10.0, 35.0, 5.0), NthUiChartMath.Domain(12, 35));
        Assert.Equal((0.0, 40.0, 10.0), NthUiChartMath.Domain(0, 35));
        Assert.Equal((0.0, 60.0, 20.0), NthUiChartMath.Domain(0, 60, true, true));
        Assert.Equal((0.0, 40.0, 10.0), NthUiChartMath.Domain(0, 37, true, false));
        Assert.Equal((100.0, 105.0, 1.0), NthUiChartMath.Domain(100, 105));
        Assert.Equal((3.8, 4.8, 0.2), NthUiChartMath.Domain(3.9, 4.7));
        Assert.Equal((0.0, 1000.0, 200.0), NthUiChartMath.Domain(199, 999));
        Assert.Equal((4.0, 6.0, 0.5), NthUiChartMath.Domain(5, 5));
        var flat = NthUiChartMath.Domain(0, 0);
        Assert.True(flat.Lo < 0 && flat.Hi > 0);
        Assert.Equal((0.0, 50.0, 10.0), NthUiChartMath.Domain(10, 40, breathe: true));
        Assert.Equal((0.0, 1000.0, 200.0), NthUiChartMath.Domain(199, 999, breathe: true));
        Assert.Equal((0.5, 3.5, 0.5), NthUiChartMath.Domain(1, 3, breathe: true));
        Assert.Equal((10.0, 40.0, 10.0), NthUiChartMath.Domain(10, 40, true, true, breathe: true));
        var swapped = NthUiChartMath.Domain(10, 0);
        Assert.Equal((0.0, 10.0), (swapped.Lo, swapped.Hi));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void Tick_labels_of_one_axis_all_show_the_same_decimals_in_the_reader_s_culture()
    {
        var en = CultureInfo.GetCultureInfo("en-US");
        Assert.Equal(["3.8", "4.0", "4.2"], new[] { 3.8, 4, 4.2 }.Select(v => NthUiChartMath.TickLabel(v, 0.2, en)));
        Assert.Equal(["0", "20", "40"], new[] { 0.0, 20, 40 }.Select(v => NthUiChartMath.TickLabel(v, 20, en)));
        Assert.Equal("0.15", NthUiChartMath.TickLabel(0.15, 0.05, en));
        Assert.Equal("0", NthUiChartMath.TickLabel(-0.0, 1, en));
        Assert.Equal("4,0", NthUiChartMath.TickLabel(4, 0.2, CultureInfo.GetCultureInfo("de-DE")));
        Assert.Equal("0.75", NthUiChartMath.TickLabel(0.75, 0.25, en));
        Assert.Equal("7.5", NthUiChartMath.TickLabel(7.5, 2.5, en));
        Assert.Equal([0, 0, 1, 1, 2, 3, 2, 1], new[] { 1.0, 20, 0.5, 0.2, 0.05, 0.001, 0.25, 2.5 }.Select(NthUiChartMath.Decimals));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void Shares_ignore_what_is_not_a_positive_number_and_percentages_add_up_to_one_hundred()
    {
        Assert.Equal([0.5, 0.3, 0.2], NthUiChartMath.Shares([2100, 1260, 840]).Select(v => Math.Round(v, 9)));
        Assert.Equal([0.0, 1, 0], NthUiChartMath.Shares([null, 5, -3]));
        Assert.Equal([0.0, 0], NthUiChartMath.Shares([0, 0]));
        Assert.Equal([50, 30, 20], NthUiChartMath.Percentages([0.5, 0.3, 0.2]));
        Assert.Equal([34, 33, 33], NthUiChartMath.Percentages([1 / 3.0, 1 / 3.0, 1 / 3.0]));
        Assert.Equal([13, 87], NthUiChartMath.Percentages([0.126, 0.874]));
        Assert.Equal([0, 0], NthUiChartMath.Percentages([0.0, 0.0]));
        foreach (var split in new[] { new[] { .07, .13, .8 }, new[] { .333, .333, .334 }, new[] { .01, .99 }, new[] { .45, .45, .1 } })
            Assert.Equal(100, NthUiChartMath.Percentages(split).Sum());
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void A_value_falls_between_zero_and_one_inside_the_domain()
    {
        Assert.Equal(0.25, NthUiChartMath.Fraction(20, 10, 50), 9);
        Assert.Equal(0, NthUiChartMath.Fraction(7, 3, 3));
    }
}
