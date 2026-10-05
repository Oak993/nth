using Avalonia.Controls;
using Marrow.App.Port.Settings;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Pages;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.Skills;

// Inspect real controls before measuring them; Windows font availability cannot mask admission checks.
[Collection("Headless")]
[Trait("Feature", "SK-16")]
[Trait("Spec", "DL-19")]
[Trait("Review", "SK-16")]
[Trait("Decision", "ED-326")]
public sealed class REVIEW_Sk16UnboundControlsTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(LibrarySection.Skills)]
    [InlineData(LibrarySection.Plugins)]
    public void REVIEW_Download_stays_disabled_when_the_first_install_seam_is_unbound(LibrarySection section) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var backend = new FakeLibraryBackend(LibraryFixtures.Catalogue(), new FailClosedFirstInstall());
        using var controller = new LibraryController(section, backend);
        var row = controller.Rows.First();
        var action = new ActionArea(new LibraryModel(controller), row.Id);
        action.Apply(row, removeAsked: false);
        Assert.False(controller.FetchingAvailable);
        Assert.False(Assert.Single(((StackPanel)action.Live!).Children.OfType<Button>()).IsEnabled);
    });

    [Theory]
    [InlineData(LibrarySection.Skills)]
    [InlineData(LibrarySection.Plugins)]
    public void REVIEW_Download_all_stays_disabled_when_the_first_install_seam_is_unbound(LibrarySection section) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var backend = new FakeLibraryBackend(LibraryFixtures.Catalogue(), new FailClosedFirstInstall());
        using var controller = new LibraryController(section, backend);
        using var seam = SkillsPageSeams.Controllers.Override(new LibraryControllers(_ => controller));
        LibraryPage page = section == LibrarySection.Skills ? new SkillsSettingsPage() : new PluginsSettingsPage();
        Assert.False(controller.FetchingAvailable);
        Assert.False(page.DownloadAllButton.IsEnabled);
    });

    [Fact]
    public void REVIEW_Retry_stays_disabled_when_the_first_install_seam_is_unbound() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var backend = new FakeLibraryBackend(LibraryFixtures.Catalogue(), new FailClosedFirstInstall())
        {
            State = AddonsState.Restore([new ItemRecord("S1", ItemState.Failed, null, "fixture", "fixture failure")], [], [], []),
        };
        using var controller = new LibraryController(LibrarySection.Skills, backend);
        var row = controller.Rows.Single(r => r.Id == "S1");
        var action = new ActionArea(new LibraryModel(controller), row.Id);
        action.Apply(row, removeAsked: false);
        Assert.False(Assert.Single(((StackPanel)action.Live!).Children.OfType<Button>()).IsEnabled);
    });

    [Fact]
    public void REVIEW_An_installed_skill_set_says_Ready_in_new_chats() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var backend = new FakeLibraryBackend(LibraryFixtures.Catalogue(), new FailClosedFirstInstall());
        backend.Install("S1", LibraryFixtures.Pin);
        using var controller = new LibraryController(LibrarySection.Skills, backend);
        var row = controller.Rows.Single(r => r.Id == "S1");
        var action = new ActionArea(new LibraryModel(controller), row.Id);
        action.Apply(row, removeAsked: false);
        Assert.Contains(((StackPanel)action.Live!).Children.OfType<TextBlock>(), t => t.Text == "Ready in new chats");
    });
}
