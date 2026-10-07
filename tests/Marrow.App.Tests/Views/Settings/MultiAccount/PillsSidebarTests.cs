using Marrow.App.Port.Settings;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Views.Settings.MultiAccount;

// MA-8 (T11 step 2, OD-0117): Pills is a category of its own directly below Models, listed for everyone (it is disabled, not hidden, below two connected accounts).
public sealed class PillsSidebarTests
{
    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void Pills_is_listed_directly_below_Models_whatever_is_connected()
    {
        var pages = SettingsSidebar.Pages("").ToList();
        Assert.Equal(pages.IndexOf(SettingsPage.Models) + 1, pages.IndexOf(SettingsPage.Pills));
        Assert.True(SettingsPage.Pills.IsReady);
        Assert.Equal(SwiftEnum<SettingsPage>.AllCases.Where(p => p.IsReady).Select(p => p.Id), pages.Select(p => p.Id));
    }

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void The_search_finds_Pills_by_its_name_and_its_words()
    {
        Assert.Contains(SettingsPage.Pills, SettingsSidebar.Pages("pill"));
        Assert.Contains(SettingsPage.Pills, SettingsSidebar.Pages("handshake"));
        Assert.Equal("pill.fill", SettingsPage.Pills.Symbol);
        Assert.Equal("pills", SettingsPage.Pills.Id);
    }

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void Until_the_app_binds_the_pages_there_is_no_context_so_other_pages_never_build_the_accounts()
    {
        Assert.Null(Marrow.App.Port.MultiAccount.Settings.MultiAccountSettingsSeams.For(new Avalonia.Controls.Border()));
    }
}
