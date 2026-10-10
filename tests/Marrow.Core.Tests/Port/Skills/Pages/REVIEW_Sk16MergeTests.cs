using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;

namespace Marrow.Core.Tests.Port.Skills.Pages;

[Trait("Feature", "SK-16")]
[Trait("Spec", "DL-19")]
[Trait("Review", "SK-16")]
[Trait("Rule", "R-TEST-4")]
public sealed class REVIEW_Sk16MergeTests
{
    [Fact]
    public void REVIEW_Required_space_never_advertises_less_than_the_admission_check_requires()
    {
        var catalogue = PageFixtures.Catalogue();
        catalogue = catalogue with { Items = catalogue.Items.Select(i => i.Id == "SA" ? i with { ApproxSizeMb = 5663.636363 } : i).ToList() };
        var rows = LibraryRows.Build(LibrarySection.Skills, catalogue, AddonsState.Empty, PageFixtures.Records(), _ => null, new Switches());
        var plan = DownloadAllPlan.Build(LibrarySection.Skills, rows, catalogue, new DriveSpace("C:", 12_200_000_000));
        Assert.False(plan.CanContinue);
        Assert.True(plan.NeedBytes > 12_200_000_000);
        Assert.Equal("Needs 13 GB free on C:.", plan.RoomNote);
    }

    [Theory]
    [InlineData("running", "One at a time. Hide it and keep using nth.")]
    [InlineData("partial", "Retry tries the failed ones again. Or download them yourself.")]
    public void REVIEW_Reviewed_SK15_dialog_words_are_preserved(string phase, string expected)
    {
        // c7526e100's source-backed LibraryReviewTests and download-all assertions pin these exact words.
        Assert.Equal(expected, phase == "running" ? LibraryWords.RunningSub : LibraryWords.PartialFoot);
    }
}
