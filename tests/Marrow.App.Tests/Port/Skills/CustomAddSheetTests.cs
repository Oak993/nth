using Avalonia.Controls;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Skills.Pages;

namespace Marrow.App.Tests.Port.Skills;

[Collection("Headless")]
public sealed class CustomAddSheetTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-326")] // OD-0099 Custom C and Browse amendment; locked sd-page.js:280-303,446-460
    public void Folder_and_zip_are_browsed_readonly_and_GitHub_is_the_only_typed_source() => LibraryPageHarness.Run(headless, LibrarySection.Skills, h =>
    {
        var backend = new CustomAddFake();
        var picked = new List<CustomAddSource>();
        Task<string?> Pick(CustomAddSource source) { picked.Add(source); return Task.FromResult<string?>(source == CustomAddSource.Zip ? "C:\\fixture.zip" : "C:\\fixture"); }
        var sheet = new CustomAddSheet(CustomAddKind.Skills, backend, Pick, () => { });
        h.Root.Children.Add(sheet);
        h.Wait(sheet.SelectSource(CustomAddSource.Folder));
        Assert.True(sheet.Location.IsReadOnly);
        Assert.Equal("C:\\fixture", sheet.Location.Text);
        Assert.Equal("Add skills from a folder", sheet.TitleText);
        h.Wait(sheet.Check());
        Assert.Equal("C:\\fixture", backend.Checks.Single().Location);
        h.Wait(sheet.SelectSource(CustomAddSource.Zip));
        Assert.Equal("C:\\fixture.zip", sheet.Location.Text);
        Assert.True(sheet.Location.IsReadOnly);
        h.Wait(sheet.SelectSource(CustomAddSource.GitHub));
        Assert.False(sheet.Location.IsReadOnly);
        Assert.False(sheet.Browse.IsVisible);
        sheet.Location.Text = "example/fixture/skills";
        h.Wait(sheet.Check());
        Assert.Equal("example/fixture/skills", backend.Checks.Last().Location);
        Assert.Equal(new[] { CustomAddSource.Folder, CustomAddSource.Zip }, picked);
        Assert.Equal(500, sheet.Width);
    });

    [Fact]
    [Trait("Decision", "ED-326")]
    public void Invalid_candidates_cannot_be_selected_and_valid_items_add_in_parallel_only_after_Add() => LibraryPageHarness.Run(headless, LibrarySection.Skills, h =>
    {
        var backend = new CustomAddFake { Found = [new("one", "one", "One", null), new("two", "two", "Two", null), new("bad", "bad", "", "No SKILL.md")], Release = new() };
        var sheet = new CustomAddSheet(CustomAddKind.Skills, backend, _ => Task.FromResult<string?>("C:\\fixture"), () => { });
        h.Root.Children.Add(sheet);
        h.Wait(sheet.SelectSource(CustomAddSource.Folder));
        h.Wait(sheet.Check());
        Assert.Empty(backend.Adds);
        Assert.Equal("Found 2 skills and 1 that can\u2019t be added.", sheet.FoundText);
        Assert.False(sheet.Candidates["bad"].Tick.IsEnabled);
        Assert.Equal("Add 2 skills", sheet.PrimaryText);
        var adding = sheet.AddSelected();
        Assert.Equal(new[] { "one", "two" }, backend.Adds);
        backend.Release.SetResult();
        h.Wait(adding);
        Assert.Equal("done", sheet.Phase);
        Assert.Equal("2 of 2 added. The added ones are under Your own.", sheet.FootText);
    });

    [Fact]
    [Trait("Decision", "ED-326")]
    public void An_unbound_backend_and_a_cancelled_picker_cannot_add_or_check() => LibraryPageHarness.Run(headless, LibrarySection.Skills, h =>
    {
        var backend = new CustomAddFake();
        var sheet = new CustomAddSheet(CustomAddKind.Plugins, backend, _ => Task.FromResult<string?>(null), () => { });
        h.Root.Children.Add(sheet);
        h.Wait(sheet.SelectSource(CustomAddSource.Zip));
        Assert.Equal("No .zip chosen", sheet.Location.Text);
        Assert.False(sheet.Primary.IsEnabled);
        h.Wait(sheet.Check());
        Assert.Empty(backend.Checks);
        var unavailable = new CustomAddSheet(CustomAddKind.Tools, null, _ => Task.FromResult<string?>("C:\\fixture"), () => { });
        h.Root.Children.Add(unavailable);
        h.Wait(unavailable.SelectSource(CustomAddSource.Folder));
        Assert.False(unavailable.Primary.IsEnabled);
    });
}
