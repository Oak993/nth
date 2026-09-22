using System.Globalization;
using Marrow.Core.Port;

namespace Marrow.Core.Tests.Port;

// R-NUM rules (IMPLEMENTING.md 3.11). Source: draft A (proto-A SwiftNumberTests), draft B (the "F" agreement
// measurement), draft C (the exact Fixed).
public sealed class SwiftNumberTests
{
    [Theory]
    [Trait("Rule", "R-NUM-2")]
    [InlineData(1.0, "1.0")]
    [InlineData(0.1, "0.1")]
    [InlineData(100.0, "100.0")]
    [InlineData(0.0001, "0.0001")]
    [InlineData(0.00001, "1e-05")]
    [InlineData(2.5e-7, "2.5e-07")]
    [InlineData(1e15, "1000000000000000.0")]
    [InlineData(1.5e300, "1.5e+300")]
    [InlineData(5e-324, "5e-324")]
    [InlineData(-0.0, "-0.0")]
    [InlineData(-123.456, "-123.456")]
    [InlineData(double.PositiveInfinity, "inf")]
    [InlineData(double.NegativeInfinity, "-inf")]
    [InlineData(double.NaN, "nan")]
    public void Description_matches_Swift_away_from_the_upper_threshold(double value, string expected) =>
        Assert.Equal(expected, SwiftNumber.Description(value));

    [Theory]
    [Trait("Rule", "R-NUM-2")]
    [Trait("Oracle", "O-03")]
    [InlineData(9007199254740992.0, "9007199254740992.0")]
    [InlineData(1e16, "10000000000000000.0")]
    [InlineData(18014398509481984.0, "18014398509481984.0")]
    [InlineData(18014398509481988.0, "1.8014398509481988e+16")]
    [InlineData(1e17, "1e+17")]
    public void Description_switches_to_exponent_form_above_two_to_the_54(double value, string expected) =>
        Assert.Equal(expected, SwiftNumber.Description(value));

    [Fact]
    [Trait("Rule", "R-NUM-2")]
    public void Dotnet_default_double_text_is_not_Swift_text()
    {
        Assert.Equal("1E-05", 0.00001.ToString(CultureInfo.InvariantCulture));
        Assert.Equal("1", 1.0.ToString(CultureInfo.InvariantCulture));
        Assert.Equal("1,5", 1.5.ToString(CultureInfo.GetCultureInfo("de-DE"))); // interpolation uses the culture
    }

    [Theory]
    [Trait("Rule", "R-CODE-5")]
    [InlineData(1.0, "1")]
    [InlineData(-0.0, "-0")]
    [InlineData(0.5, "0.5")]
    [InlineData(1e-5, "1e-05")]
    public void A_Double_in_JSON_drops_a_trailing_point_zero(double value, string expected) =>
        Assert.Equal(expected, SwiftNumber.JsonText(value));

    [Fact]
    [Trait("Rule", "R-CODE-5")]
    public void A_non_finite_Double_is_an_EncodingError() => Assert.Throws<EncodingError>(() => SwiftNumber.JsonText(double.NaN));

    [Fact]
    [Trait("Rule", "R-NUM-3")]
    public void Rounded_is_half_away_from_zero()
    {
        Assert.Equal(3.0, SwiftNumber.Rounded(2.5));
        Assert.Equal(-3.0, SwiftNumber.Rounded(-2.5));
        Assert.Equal(2.0, Math.Round(2.5)); // the pitfall: Math.Round is half to even
        Assert.Equal(1.0, SwiftNumber.Rounded(0.5));
    }

    [Theory]
    [Trait("Rule", "R-NUM-2")]
    [InlineData(0.25, 1, "0.2")]
    [InlineData(0.35, 1, "0.3")]
    [InlineData(0.75, 1, "0.8")]
    [InlineData(1.25, 1, "1.2")]
    [InlineData(0.05, 1, "0.1")]
    [InlineData(-0.25, 1, "-0.2")]
    [InlineData(2.5, 0, "2")]
    [InlineData(1234.5678, 2, "1234.57")]
    public void Fixed_is_printf_on_the_exact_binary_value(double value, int digits, string expected) =>
        Assert.Equal(expected, SwiftNumber.Fixed(value, digits));

    [Fact]
    [Trait("Rule", "R-NUM-2")]
    public void Fixed_agrees_with_dotnet_F_for_finite_values_and_spells_non_finite_as_printf()
    {
        var random = new Random(20260922);
        for (var i = 0; i < 5_000; i++)
        {
            var value = (random.NextDouble() - 0.5) * Math.Pow(10, random.Next(-6, 9));
            var digits = random.Next(0, 7);
            Assert.Equal(value.ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture), SwiftNumber.Fixed(value, digits));
        }
        Assert.Equal("nan", SwiftNumber.Fixed(double.NaN, 1));
        Assert.Equal("-inf", SwiftNumber.Fixed(double.NegativeInfinity, 1));
    }

    [Fact]
    [Trait("Rule", "R-FMT-1")]
    public void NumberFormatter_decimal_rounds_half_even_with_the_locale_grouping()
    {
        var english = CultureInfo.GetCultureInfo("en-US");
        Assert.Equal("2", SwiftFormat.DecimalNoFraction(2.5, english));
        Assert.Equal("4", SwiftFormat.DecimalNoFraction(3.5, english));
        Assert.Equal("1,234,568", SwiftFormat.DecimalNoFraction(1234567.5, english));
    }

    [Fact]
    [Trait("Rule", "R-NUM-1")]
    public void ParseInt_matches_Swift_Int_init()
    {
        Assert.Equal(5L, SwiftNumber.ParseInt("+5"));
        Assert.Equal(-5L, SwiftNumber.ParseInt("-5"));
        Assert.Null(SwiftNumber.ParseInt(" 5"));
        Assert.Null(SwiftNumber.ParseInt("5 "));
        Assert.Null(SwiftNumber.ParseInt("+"));
        Assert.Null(SwiftNumber.ParseInt("1_000"));
        Assert.Null(SwiftNumber.ParseInt("\U00000663"));
        Assert.Null(SwiftNumber.ParseInt("9223372036854775808"));
        Assert.True(int.TryParse(" 5", CultureInfo.InvariantCulture, out _)); // the pitfall: default TryParse trims
    }

    [Fact]
    [Trait("Rule", "R-NUM-1")]
    public void ParseDouble_matches_Swift_Double_init()
    {
        Assert.Equal(3.8, SwiftNumber.ParseDouble("3.8"));
        Assert.Equal(0.5, SwiftNumber.ParseDouble(".5"));
        Assert.Equal(5.0, SwiftNumber.ParseDouble("5."));
        Assert.Equal(1e5, SwiftNumber.ParseDouble("1e5"));
        Assert.Equal(double.PositiveInfinity, SwiftNumber.ParseDouble("inf"));
        Assert.Equal(double.NegativeInfinity, SwiftNumber.ParseDouble("-Infinity"));
        Assert.True(double.IsNaN(SwiftNumber.ParseDouble("nan")!.Value));
        Assert.Equal(8.0, SwiftNumber.ParseDouble("0x1p3"));
        Assert.Equal(0.75, SwiftNumber.ParseDouble("0x.c"));
        Assert.Null(SwiftNumber.ParseDouble(" 1"));
        Assert.Null(SwiftNumber.ParseDouble("1,5"));
        Assert.Null(SwiftNumber.ParseDouble("1e"));
        Assert.Null(SwiftNumber.ParseDouble(""));
    }

    [Fact]
    [Trait("Rule", "R-NUM-1")]
    public void ScanHexInt64_reads_a_prefix_and_leaves_zero_on_failure()
    {
        Assert.True(SwiftScanner.ScanHexInt64(" 0xFFz", out var value));
        Assert.Equal(255UL, value);
        Assert.False(SwiftScanner.ScanHexInt64("zz", out value));
        Assert.Equal(0UL, value); // Color(hex: "zz") is black
    }

    [Fact]
    [Trait("Rule", "R-NUM-4")]
    [Trait("Decision", "ED-074")]
    public void Int_conversion_and_arithmetic_trap_like_Swift()
    {
        Assert.Equal(1L, SwiftNumber.TruncatingToInt(1.9));
        Assert.Equal(-1L, SwiftNumber.TruncatingToInt(-1.9));
        Assert.Throws<SwiftTrapException>(() => SwiftNumber.TruncatingToInt(double.NaN));
        Assert.Throws<SwiftTrapException>(() => SwiftNumber.TruncatingToInt(1e20));
        var big = long.MaxValue;
        Assert.Throws<OverflowException>(() => big + 1); // CheckForOverflowUnderflow: Swift's + traps
        Assert.Equal(long.MinValue, unchecked(big + 1)); // Swift's &+
    }

    [Fact]
    [Trait("Rule", "R-VAL-4")]
    public void Record_equality_treats_NaN_as_equal_unlike_Swift()
    {
        var nan = double.NaN;
        var other = double.NaN;
        Assert.False(nan == other); // Swift and C# operators agree
        Assert.True(nan.Equals(other)); // what a record's generated equality uses: differs from Swift
        Assert.True(0.0.Equals(-0.0));
        Assert.Equal(0.0.GetHashCode(), (-0.0).GetHashCode());
    }
}
