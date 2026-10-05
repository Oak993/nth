// PORT-ONLY: first-release feature unit SK-14 ("Download it yourself", the locked skills-downloads design: step 1 the file and its source, step 2 the folder, step 3 Check again). A fake backend and a scripted install:
// no file, no network, no browser.
using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Pages;

[Trait("Feature", "SK-14")]
[Trait("Spec", "DL-17")]
[Trait("Rule", "R-TEST-4")]
public sealed class LibraryControllerManualTests
{
    [Fact]
    public async Task Check_again_keeps_the_files_the_folder_holds_for_the_item_and_then_downloads_it_the_normal_way()
    {
        using var rig = new LibraryRig();
        rig.Backend.State = AddonsState.Restore([new ItemRecord("S1", ItemState.Failed, null, LibraryFixtures.Pin, "Couldn’t reach example.test.")], [], [], []);
        rig.Backend.ManualMatches = 1;
        await LibraryRig.Within(rig.Page.CheckAgain("S1"));
        Assert.Equal(["place S1"], rig.Backend.Calls.Where(c => c.StartsWith("place", StringComparison.Ordinal)));
        Assert.Equal(["S1"], rig.Install.Calls);
    }

    [Fact]
    public async Task Check_again_on_an_item_that_is_not_failed_does_nothing()
    {
        using var rig = new LibraryRig();
        await LibraryRig.Within(rig.Page.CheckAgain("S1"));
        Assert.Empty(rig.Backend.Calls);
        Assert.Empty(rig.Install.Calls);
    }

    [Fact]
    public void The_fallback_is_the_designs_text_for_the_item_and_the_folder_is_the_designs_folder()
    {
        using var rig = new LibraryRig();
        var item = rig.Backend.Catalogue.Items.First(i => i.Id == "S1");
        Assert.Equal(ManualFallbacks.For(item), rig.Page.ManualFallbackOf("S1"));
        Assert.Null(rig.Page.ManualFallbackOf("no-such-item"));
    }

    [Fact]
    public void The_page_and_the_folder_open_through_the_backend()
    {
        using var rig = new LibraryRig();
        var fallback = rig.Page.ManualFallbackOf("S1");
        Assert.NotNull(fallback);
        rig.Page.OpenManualPage("S1");
        rig.Page.OpenManualFolder();
        Assert.Equal(["page " + fallback.Page.AbsoluteUri, "folder"], rig.Backend.Calls);
    }
}
