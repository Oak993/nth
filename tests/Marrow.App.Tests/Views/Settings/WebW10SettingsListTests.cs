using Avalonia.Controls;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// PORT-ONLY: web surfaces W10, the two pages in the Settings sidebar (E:/Droppy/work/design/web-surfaces/DESIGN.md 9; 14 W10 "keep your pages as self-contained section classes with a minimal hook"; ED-NEW-web-w10-4). Visus and Tabula sit directly after Tactus, are
// found by the words of their rows, have symbols the catalogue resolves, and the only hook the Settings window has into them is the page factory line.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w10-4")]
public sealed class WebW10SettingsListTests(HeadlessSession headless)
{
    private static List<SettingsPage> All() => [.. SwiftEnum<SettingsPage>.AllCases];

    [Fact]
    public void Visus_and_Tabula_are_listed_in_that_order_directly_after_Tactus() => headless.OnUiThread(() =>
    {
        var all = All();
        Assert.Equal(all.IndexOf(SettingsPage.PcControl) + 1, all.IndexOf(SettingsPage.Visus));
        Assert.Equal(all.IndexOf(SettingsPage.Visus) + 1, all.IndexOf(SettingsPage.Tabula));
        Assert.Equal(("visus", "Visus"), (SettingsPage.Visus.Id, SettingsPage.Visus.Title));
        Assert.Equal(("tabula", "Tabula"), (SettingsPage.Tabula.Id, SettingsPage.Tabula.Title));
        Assert.True(SettingsPage.Visus.IsReady);
        Assert.True(SettingsPage.Tabula.IsReady);
        Assert.NotNull(SymbolCatalogue.Mapping(SettingsPage.Visus.Symbol));
        Assert.NotNull(SymbolCatalogue.Mapping(SettingsPage.Tabula.Symbol));
        var listed = SettingsSidebar.Pages("").ToList();
        Assert.Equal(listed.IndexOf(SettingsPage.PcControl) + 1, listed.IndexOf(SettingsPage.Visus));
        Assert.Equal(listed.IndexOf(SettingsPage.Visus) + 1, listed.IndexOf(SettingsPage.Tabula));
    });

    [Fact]
    public void Each_page_is_found_by_the_words_of_its_rows_and_by_nothing_unrelated() => headless.OnUiThread(() =>
    {
        foreach (var word in new[] { "visus", "browser", "search engine", "suggestions", "previews", "permissions", "downloads", "cookies", "privacy", "forget", "tabs" })
            Assert.Contains(SettingsPage.Visus, SettingsSidebar.Pages(word));
        foreach (var word in new[] { "tabula", "artifacts", "live answers", "widgets", "html", "mcp apps", "maps", "tiles", "storage" })
            Assert.Contains(SettingsPage.Tabula, SettingsSidebar.Pages(word));
        Assert.DoesNotContain(SettingsPage.Visus, SettingsSidebar.Pages("git"));
        Assert.DoesNotContain(SettingsPage.Visus, SettingsSidebar.Pages("gen"));    // the searches of the older pages keep their answers
        Assert.DoesNotContain(SettingsPage.Visus, SettingsSidebar.Pages("models"));
        Assert.DoesNotContain(SettingsPage.Tabula, SettingsSidebar.Pages("gen"));
        Assert.DoesNotContain(SettingsPage.Tabula, SettingsSidebar.Pages("models"));
        Assert.DoesNotContain(SettingsPage.Tabula, SettingsSidebar.Pages("git"));
        Assert.Equal(new[] { SettingsPage.Visus }, SettingsSidebar.Pages("visus"));
        Assert.Equal(new[] { SettingsPage.Tabula }, SettingsSidebar.Pages("tabula"));
    });

    [Fact]
    public void The_window_builds_each_page_with_one_factory_line_and_neither_has_chrome_controls() => headless.OnUiThread(() =>
    {
        using var defaults = UserDefaults.Standard.Override(new InMemorySettingsStore());
        var visus = SettingsView.PageFor(SettingsPage.Visus, "", "");
        var tabula = SettingsView.PageFor(SettingsPage.Tabula, "", "");
        Assert.IsType<VisusSettingsPage>(visus);
        Assert.IsType<TabulaSettingsPage>(tabula);
        Assert.IsAssignableFrom<Decorator>(visus);
        Assert.False(SettingsView.HasChromeControls(SettingsPage.Visus, visus));
        Assert.False(SettingsView.HasChromeControls(SettingsPage.Tabula, tabula));
        Assert.Empty(SettingsView.ChromeAccessoriesOf(visus));
        Assert.Empty(SettingsView.ChromeAccessoriesOf(tabula));
    });

    [Fact]
    public void The_pages_depend_on_no_window_code_beyond_the_shared_row_parts() => headless.OnUiThread(() =>
    {
        // Self-contained: a page is a Decorator made from parts alone, so the in-window Settings can host it as it is.
        Assert.True(typeof(VisusSettingsPage).IsSealed && typeof(TabulaSettingsPage).IsSealed);
        Assert.NotNull(typeof(VisusSettingsPage).GetConstructor(Type.EmptyTypes));
        Assert.NotNull(typeof(TabulaSettingsPage).GetConstructor(Type.EmptyTypes));
        Assert.NotNull(typeof(VisusSettingsPage).GetConstructor([typeof(VisusSettingsParts)]));
        Assert.NotNull(typeof(TabulaSettingsPage).GetConstructor([typeof(TabulaSettingsParts)]));
    });
}
