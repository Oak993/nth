using Marrow.App.Port.Web;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.NthUi;
using Marrow.Core.Web;

namespace Marrow.App.Tests;

// PORT-ONLY: web surfaces W10, the Visus and Tabula settings (E:/Droppy/work/design/web-surfaces/DESIGN.md 9; ED-NEW-web-w10-2). Loading never writes; a damaged or old value reads as the designed default; a change writes
// its own key and nothing else; the ranges and the paths are checked on the way in and on the way out of the store.
[Collection("AppSettings")]
[Trait("Decision", "ED-NEW-web-w10-2")]
public sealed class WebW10WebSettingsTests
{
    private static readonly string[] AllKeys =
    [
        WebSettings.Keys.ChatLinks, WebSettings.Keys.SearchEngine, WebSettings.Keys.VisualAnswers, WebSettings.Keys.ArtifactModel, WebSettings.Keys.SearchSuggestions,
        WebSettings.Keys.OpenPreviews, WebSettings.Keys.ReloadOnSave, WebSettings.Keys.LetModelsUseVisus, WebSettings.Keys.AskBeforeNewSite, WebSettings.Keys.AskWhereToSave,
        WebSettings.Keys.LiveAnswers, WebSettings.Keys.InlineHtmlWidgets, WebSettings.Keys.McpApps, WebSettings.Keys.MaxLiveTabs, WebSettings.Keys.DownloadsFolder, WebSettings.Keys.MapTileSource,
    ];

    [Fact]
    public void The_keys_are_new_distinct_and_all_start_with_web()
    {
        Assert.Equal(AllKeys.Length, AllKeys.Distinct(StringComparer.Ordinal).Count());
        Assert.All(AllKeys, key => Assert.StartsWith("web", key, StringComparison.Ordinal));
    }

    [Fact]
    public void The_designed_defaults_are_read_without_writing_anything() => Run(scope =>
    {
        var web = new AppSettings().Web;
        Assert.Equal(ChatLinkTarget.Ask, web.ChatLinks);
        Assert.Equal(SearchEngine.DuckDuckGo, web.SearchEngine);
        Assert.Equal(VisualAnswerLevel.Balanced, web.VisualAnswers);
        Assert.Equal(ArtifactModelChoice.AskEachArtifact, web.ArtifactModel);
        Assert.False(web.SearchSuggestions);
        Assert.True(web.OpenPreviews);
        Assert.True(web.ReloadOnSave);
        Assert.True(web.LetModelsUseVisus);
        Assert.True(web.AskBeforeNewSite);
        Assert.False(web.AskWhereToSave);
        Assert.True(web.LiveAnswers);
        Assert.True(web.InlineHtmlWidgets);
        Assert.True(web.McpApps);
        Assert.Equal(4, web.MaxLiveTabs);
        Assert.Equal("", web.DownloadsFolder);
        Assert.Equal("", web.MapTileSource);
        Assert.DoesNotContain(scope.Defaults.Writes, write => AllKeys.Contains(write.Key));
    });

    [Fact]
    public void The_web_settings_are_one_object_per_app_settings() => Run(_ =>
    {
        var settings = new AppSettings();
        Assert.Same(settings.Web, settings.Web);
    });

    [Fact]
    public void A_damaged_or_foreign_value_reads_as_the_default_and_is_not_rewritten() => Run(scope =>
    {
        scope.Defaults.Inner.Set(WebSettings.Keys.ChatLinks, "Local");
        scope.Defaults.Inner.Set(WebSettings.Keys.SearchEngine, "AltaVista");
        scope.Defaults.Inner.Set(WebSettings.Keys.VisualAnswers, 7L);
        scope.Defaults.Inner.Set(WebSettings.Keys.ArtifactModel, "Always");
        scope.Defaults.Inner.Set(WebSettings.Keys.SearchSuggestions, "yes");
        scope.Defaults.Inner.Set(WebSettings.Keys.OpenPreviews, 2L);
        scope.Defaults.Inner.Set(WebSettings.Keys.MaxLiveTabs, "many");
        scope.Defaults.Inner.Set(WebSettings.Keys.DownloadsFolder, "relative\\folder");
        scope.Defaults.Inner.Set(WebSettings.Keys.MapTileSource, "http://tiles.example.org/{z}/{x}/{y}.png");
        scope.Defaults.Writes.Clear();

        var web = new AppSettings().Web;
        Assert.Equal(ChatLinkTarget.Ask, web.ChatLinks);
        Assert.Equal(SearchEngine.DuckDuckGo, web.SearchEngine);
        Assert.Equal(VisualAnswerLevel.Balanced, web.VisualAnswers);
        Assert.Equal(ArtifactModelChoice.AskEachArtifact, web.ArtifactModel);
        Assert.False(web.SearchSuggestions);
        Assert.True(web.OpenPreviews);
        Assert.Equal(4, web.MaxLiveTabs);
        Assert.Equal("", web.DownloadsFolder);
        Assert.Equal("", web.MapTileSource);
        Assert.Empty(scope.Defaults.Writes);
    });

    [Theory]
    [InlineData(0L, 2)]
    [InlineData(1L, 2)]
    [InlineData(2L, 2)]
    [InlineData(6L, 6)]
    [InlineData(8L, 8)]
    [InlineData(9L, 8)]
    [InlineData(1000L, 8)]
    [InlineData(-3L, 2)]
    public void A_stored_live_tab_count_is_brought_into_range_without_a_write(long stored, int expected) => Run(scope =>
    {
        scope.Defaults.Inner.Set(WebSettings.Keys.MaxLiveTabs, stored);
        scope.Defaults.Writes.Clear();
        Assert.Equal(expected, new AppSettings().Web.MaxLiveTabs);
        Assert.Empty(scope.Defaults.Writes);
    });

    [Fact]
    public void A_change_writes_its_own_key_once_and_raises_Changed_once() => Run(scope =>
    {
        var web = new AppSettings().Web;
        var raised = 0;
        web.Changed += () => raised++;
        web.OpenPreviews = false;
        Assert.Equal([("set", WebSettings.Keys.OpenPreviews)], scope.Defaults.Writes);
        Assert.Equal(1, raised);
        scope.Defaults.Writes.Clear();
        web.SearchEngine = SearchEngine.Brave;
        Assert.Equal([("set", WebSettings.Keys.SearchEngine)], scope.Defaults.Writes);
        Assert.Equal(2, raised);
        scope.Defaults.Writes.Clear();
        web.MaxLiveTabs = 6;
        Assert.Equal([("set", WebSettings.Keys.MaxLiveTabs)], scope.Defaults.Writes);
        Assert.Equal(3, raised);
    });

    [Fact]
    public void Local_is_not_a_choice_for_the_link_setting() => Run(scope =>
    {
        var web = new AppSettings().Web;
        web.ChatLinks = ChatLinkTarget.Visus;
        scope.Defaults.Writes.Clear();
        web.ChatLinks = ChatLinkTarget.Local;
        web.ChatLinks = (ChatLinkTarget)77;
        Assert.Equal(ChatLinkTarget.Visus, web.ChatLinks);
        Assert.Empty(scope.Defaults.Writes);
    });

    [Fact]
    public void A_value_outside_an_enum_is_ignored_and_nothing_is_written() => Run(scope =>
    {
        var web = new AppSettings().Web;
        web.SearchEngine = (SearchEngine)99;
        web.VisualAnswers = (VisualAnswerLevel)99;
        web.ArtifactModel = (ArtifactModelChoice)99;
        Assert.Equal(SearchEngine.DuckDuckGo, web.SearchEngine);
        Assert.Equal(VisualAnswerLevel.Balanced, web.VisualAnswers);
        Assert.Equal(ArtifactModelChoice.AskEachArtifact, web.ArtifactModel);
        Assert.DoesNotContain(scope.Defaults.Writes, write => AllKeys.Contains(write.Key));
    });

    [Fact]
    public void The_downloads_folder_is_kept_only_as_a_full_path() => Run(scope =>
    {
        var web = new AppSettings().Web;
        web.DownloadsFolder = "relative\\folder";
        Assert.Equal("", web.DownloadsFolder);
        web.DownloadsFolder = "C:\\fake\\Downloads";
        Assert.Equal("C:\\fake\\Downloads", web.DownloadsFolder);
        web.DownloadsFolder = "C:\\fake\\bad\0name";
        Assert.Equal("", web.DownloadsFolder);
        web.DownloadsFolder = new string('a', 2000);
        Assert.Equal("", web.DownloadsFolder);
        scope.Relaunch();
        Assert.Equal("", new AppSettings().Web.DownloadsFolder);
        var again = new AppSettings().Web;
        again.DownloadsFolder = "D:\\files";
        scope.Relaunch();
        Assert.Equal("D:\\files", new AppSettings().Web.DownloadsFolder);
    });

    [Fact]
    public void The_tile_source_is_kept_only_as_a_checked_address_and_an_unsafe_one_changes_nothing() => Run(scope =>
    {
        var web = new AppSettings().Web;
        web.MapTileSource = "  https://tiles.example.org/{z}/{x}/{y}.png  ";
        Assert.Equal("https://tiles.example.org/{z}/{x}/{y}.png", web.MapTileSource);
        scope.Defaults.Writes.Clear();
        web.MapTileSource = "http://tiles.example.org/{z}/{x}/{y}.png";
        web.MapTileSource = "https://localhost/{z}/{x}/{y}.png";
        web.MapTileSource = "not an address";
        Assert.Equal("https://tiles.example.org/{z}/{x}/{y}.png", web.MapTileSource);
        Assert.Empty(scope.Defaults.Writes);
        scope.Relaunch();
        Assert.Equal("https://tiles.example.org/{z}/{x}/{y}.png", new AppSettings().Web.MapTileSource);
        var cleared = new AppSettings().Web;
        cleared.MapTileSource = "";
        scope.Relaunch();
        Assert.Equal("", new AppSettings().Web.MapTileSource);
    });

    [Fact]
    public void Every_switch_and_choice_survives_a_relaunch() => Run(scope =>
    {
        var web = new AppSettings().Web;
        web.ChatLinks = ChatLinkTarget.DefaultBrowser;
        web.SearchEngine = SearchEngine.Startpage;
        web.VisualAnswers = VisualAnswerLevel.Fewer;
        web.ArtifactModel = ArtifactModelChoice.Never;
        web.SearchSuggestions = true;
        web.OpenPreviews = false;
        web.ReloadOnSave = false;
        web.LetModelsUseVisus = false;
        web.AskBeforeNewSite = false;
        web.AskWhereToSave = true;
        web.LiveAnswers = false;
        web.InlineHtmlWidgets = false;
        web.McpApps = false;
        web.MaxLiveTabs = 7;
        scope.Relaunch();
        var back = new AppSettings().Web;
        Assert.Equal(ChatLinkTarget.DefaultBrowser, back.ChatLinks);
        Assert.Equal(SearchEngine.Startpage, back.SearchEngine);
        Assert.Equal(VisualAnswerLevel.Fewer, back.VisualAnswers);
        Assert.Equal(ArtifactModelChoice.Never, back.ArtifactModel);
        Assert.True(back.SearchSuggestions);
        Assert.False(back.OpenPreviews);
        Assert.False(back.ReloadOnSave);
        Assert.False(back.LetModelsUseVisus);
        Assert.False(back.AskBeforeNewSite);
        Assert.True(back.AskWhereToSave);
        Assert.False(back.LiveAnswers);
        Assert.False(back.InlineHtmlWidgets);
        Assert.False(back.McpApps);
        Assert.Equal(7, back.MaxLiveTabs);
    });

    [Fact]
    public void The_settings_host_reads_the_attached_settings_and_else_settings_over_the_current_store() => Run(scope =>
    {
        Assert.Null(WebSettingsHost.Active);
        var fallback = WebSettingsHost.Current;
        Assert.Same(fallback, WebSettingsHost.Current); // one per store
        var attached = new AppSettings().Web;
        using (WebSettingsHost.Attach(attached))
        {
            Assert.Same(attached, WebSettingsHost.Active);
            Assert.Same(attached, WebSettingsHost.Current);
        }
        Assert.Null(WebSettingsHost.Active);
        Assert.Same(fallback, WebSettingsHost.Current);
        _ = scope;
    });

    private static void Run(Action<AppSettingsScope> body)
    {
        using var scope = AppSettingsScope.Create();
        scope.OnMain(() => body(scope));
    }
}
