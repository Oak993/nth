using Marrow.App.Port.Skills.Pages;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Services.Port.Skills.Pages;

namespace Marrow.App.Tests.Port.Skills;

[Collection("Headless")]
public sealed class ToolsLibraryBackendTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-326")]
    public void Tools_download_all_lists_only_optional_tools_and_installs_the_original_catalogue_kind() => LibraryPageHarness.Run(headless, LibrarySection.Skills, h =>
    {
        var real = LibraryFixtures.WithPythonNeed(LibraryFixtures.Catalogue());
        var seen = new List<ItemKind>();
        var backend = new FakeLibraryBackend(real, new RecordingFirstInstall(seen));
        var adapted = new ToolsLibraryBackend(backend);
        using var controller = new LibraryController(LibrarySection.Skills, adapted);
        var model = new LibraryModel(controller, tools: true);
        Assert.Equal(new[] { "T1" }, controller.Rows.Select(r => r.Id));
        controller.OpenDownloadAll();
        Assert.Equal("Download all tools?", model.Dialog.Title);
        Assert.Empty(seen);
        h.Wait(controller.Continue());
        Assert.Equal(new[] { ItemKind.Tool }, seen);
    });
}
