using Marrow.App.Port.Skills.Pages;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Skills.Pages;

namespace Marrow.App.Tests.Port.Skills;

[Collection("Headless")]
public sealed class CustomAddSheetReviewTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-326")]
    public void Changing_source_discards_the_previous_check_and_a_second_check_can_show_its_own_results() => LibraryPageHarness.Run(headless, LibrarySection.Skills, h =>
    {
        var backend = new CustomAddFake();
        var sheet = new CustomAddSheet(CustomAddKind.Skills, backend, source => Task.FromResult<string?>(source == CustomAddSource.Zip ? "C:\\two.zip" : "C:\\one"), () => { });
        h.Root.Children.Add(sheet);
        h.Wait(sheet.SelectSource(CustomAddSource.Folder));
        h.Wait(sheet.Check());
        Assert.Equal("list", sheet.Phase);
        h.Wait(sheet.SelectSource(CustomAddSource.Zip));
        Assert.Equal("open", sheet.Phase);
        Assert.Empty(sheet.Candidates);
        backend.Found = [new("second", "second", "Another fixture", null)];
        h.Wait(sheet.Check());
        Assert.Equal("list", sheet.Phase);
        Assert.Equal(new[] { "second" }, sheet.Candidates.Keys);
        Assert.Empty(backend.Adds);
    });
}
