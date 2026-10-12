using System.Globalization;
using Marrow.Core.NthUi;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.NthUi;

// DESIGN.md 13.1 NthUiTests, fmt (web surfaces W15; ED-NEW-web-w15-1, traceability alias ED-NEW-webnthui-1): the 7.2
// `fmt(value, pattern)` patterns as the reference documents them, the approved canvas's "$0" reading "$1,007" (its usd()
// groups thousands), and the same output whatever culture the machine runs in unless the view is given one.
[Trait("Decision", "ED-NEW-webnthui-1")]
public sealed class NthUiFormatTests
{
    [Theory]
    [InlineData(100.74, "$0", "$101")]
    [InlineData(1006.6, "$0", "$1,007")]
    [InlineData(-1006.6, "$0", "-$1,007")]
    [InlineData(-0.4, "$0", "$0")]
    [InlineData(1234.5, "$0.00", "$1,234.50")]
    [InlineData(2026, "0", "2026")]
    [InlineData(1234567, "#,##0", "1,234,567")]
    [InlineData(1234567.891, "#,##0.##", "1,234,567.89")]
    [InlineData(3.5, "0.##", "3.5")]
    [InlineData(3, "0.00", "3.00")]
    [InlineData(7, "00", "07")]
    [InlineData(0.256, "0%", "26%")]
    [InlineData(0.256, "0.0%", "25.6%")]
    [InlineData(5, "0\\%", "5%")]
    [InlineData(40, "0 GB/day", "40 GB/day")]
    [InlineData(12.5, "€0.00", "€12.50")]
    [InlineData(1500, "0.0a", "1.5k")]
    [InlineData(2_400_000, "$0.0a", "$2.4M")]
    [InlineData(999_999, "0a", "1M")]
    [InlineData(3_200_000_000, "0.#a", "3.2B")]
    [InlineData(950, "0a", "950")]
    [InlineData(0.5, "0.0", "0.5")]
    [InlineData(1e20, "0", "100000000000000000000")]
    [InlineData(42, "about 0 apples", "about 42 apples")]
    [InlineData(42, "no digits", "42")]
    public void Number_patterns_read_like_a_spreadsheet(double value, string pattern, string expected)
    {
        using var culture = CultureScope.Of("fr-FR");
        Assert.Equal(expected, NthUiFormat.Format(NthUiValue.Of(value), pattern));
    }

    [Theory]
    [InlineData("2026-11-02", "dddd, MMMM d", "Monday, November 2")]
    [InlineData("2026-11-16", "MMM d, yyyy", "Nov 16, 2026")]
    [InlineData("2026-11-16", "d/M/yy", "16/11/26")]
    [InlineData("2026-11-16T14:30", "HH:mm", "14:30")]
    [InlineData("2026-11-16", "yyyy-MM-dd' week'", "2026-11-16 week")]
    [InlineData("not a date", "MMM d", "not a date")]
    public void Iso_dates_take_date_patterns(string value, string pattern, string expected)
    {
        using var culture = CultureScope.Of("ja-JP");
        Assert.Equal(expected, NthUiFormat.Format(NthUiValue.Of(value), pattern));
    }

    [Fact]
    public void Other_values_and_bad_patterns_fall_back_to_their_display()
    {
        Assert.Equal("1,500", NthUiFormat.Format(NthUiValue.Of("1500"), "#,##0"));
        Assert.Equal("true", NthUiFormat.Format(NthUiValue.True, "0"));
        Assert.Equal("1, 2", NthUiFormat.Format(NthUiValue.List([NthUiValue.Of(1), NthUiValue.Of(2)]), "0.00"));
        Assert.Equal("—", NthUiFormat.Format(NthUiValue.Error("x"), "$0"));
        Assert.Equal("3.14159", NthUiFormat.Format(NthUiValue.Of(3.14159), null));
        Assert.Equal("3", NthUiFormat.Format(NthUiValue.Of(3), new string('0', NthUiFormat.MaxPatternLength + 1)));
        Assert.Equal("0.3", NthUiValue.Of(0.1 + 0.2).Display());
        Assert.Equal("1.5E+20", NthUiValue.Of(1.5e20).Display());
        Assert.Equal("-0.000025", NthUiValue.Of(-0.000025).Display());
    }

    [Fact]
    public void A_view_given_a_culture_uses_its_separators()
    {
        var german = CultureInfo.GetCultureInfo("de-DE");
        Assert.Equal("$1.234,50", NthUiFormat.Format(NthUiValue.Of(1234.5), "$#,##0.00", german));
        Assert.Equal("2,5", NthUiValue.Of(2.5).Display(german));
        Assert.Equal("$1,234.50", NthUiFormat.Format(NthUiValue.Of(1234.5), "$#,##0.00"));
    }
}
