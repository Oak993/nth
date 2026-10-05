using Marrow.Core.Port.Skills.Pages;

namespace Marrow.Core.Tests.Port.Skills.Pages;

// Unit SK-15 (DL-17): the sizes in the words of the final skills-downloads design page. The owner's rule of 2026-10-04 (review of version 3): "32 MB is not a big download, anything
// above 100 MB, you can say large download for that message. For anything 100MB or under, use different wording."
public sealed class SizeTextTests
{
    [Theory]
    [InlineData(0.24, "0.24 MB")]
    [InlineData(0.28, "0.28 MB")]
    [InlineData(3.5, "3.5 MB")]
    [InlineData(8.1, "8.1 MB")]
    [InlineData(9.96, "10 MB")]
    [InlineData(32.2, "32 MB")]
    [InlineData(356, "356 MB")]
    [InlineData(1040.2, "1.04 GB")]
    [InlineData(1077.1, "1.08 GB")]
    [Trait("Feature", "SK-15")]
    [Trait("Rule", "R-TEST-4")]
    public void A_size_is_printed_in_the_design_s_units_and_digits(double mb, string expected) => Assert.Equal(expected, SizeText.Short(mb));

    [Fact]
    [Trait("Feature", "SK-15")]
    [Trait("Rule", "R-TEST-4")]
    public void The_exact_figure_gives_megabytes_and_for_a_gigabyte_both()
    {
        Assert.Equal("32.2 MB", SizeText.Exact(32.2));
        Assert.Equal("0.24 MB", SizeText.Exact(0.24));
        Assert.Equal("1.04 GB (1,040.2 MB)", SizeText.Exact(1040.2));
    }

    [Fact]
    [Trait("Feature", "SK-15")]
    [Trait("Rule", "R-TEST-4")]
    public void The_disk_figure_says_about_and_free_space_is_in_whole_units()
    {
        Assert.Equal("about 32 MB", SizeText.Disk(32.2));
        Assert.Equal("about 2.8 GB", SizeText.Disk(2800));
        Assert.Equal("212 GB", SizeText.Free(212_000_000_000));
        Assert.Equal("4.5 GB", SizeText.Free(4_500_000_000));
        Assert.Equal("640 MB", SizeText.Free(640_000_000));
    }

    [Fact]
    [Trait("Feature", "SK-15")]
    [Trait("Rule", "R-TEST-4")]
    public void The_large_download_warning_starts_strictly_above_one_hundred_megabytes()
    {
        Assert.False(SizeText.IsLarge(32.2));
        Assert.False(SizeText.IsLarge(100));
        Assert.True(SizeText.IsLarge(100.1));
        Assert.True(SizeText.IsLarge(1040.2));
    }

    [Fact]
    [Trait("Feature", "SK-15")]
    [Trait("Rule", "R-TEST-4")]
    public void Figures_do_not_follow_the_machine_s_culture()
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal("1.04 GB (1,040.2 MB)", SizeText.Exact(1040.2));
            Assert.Equal("3.5 MB", SizeText.Short(3.5));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = before; }
    }
}
