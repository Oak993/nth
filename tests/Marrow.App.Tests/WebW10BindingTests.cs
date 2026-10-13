using Marrow.App.Tests.Fixtures;
using Marrow.App.Port.Web;
using Marrow.App.Port.Web.McpApps;
using Marrow.App.Views.Browser;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.NthUi;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.Services.MCP;
using Marrow.Services.Web;
using Marrow.Testing;

namespace Marrow.App.Tests;

// PORT-ONLY: web surfaces W10, the settings meet the units that read them (E:/Droppy/work/design/web-surfaces/DESIGN.md 9; ED-NEW-web-w10-3). Every seam the web units left for the settings reads the attached settings on each call, so a change takes
// effect at once; with nothing attached each seam keeps the default it was made with; and disposing the binding puts every seam back.
[Collection("AppSettings")]
[Trait("Decision", "ED-NEW-web-w10-3")]
public sealed class WebW10BindingTests
{
    private static WebSettings Fresh() => new(new InMemorySettingsStore());

    private static void OnMain(Action body)
    {
        using var scope = AppSettingsScope.Create();
        scope.OnMain(body);
    }

    [Fact]
    public void With_nothing_attached_every_seam_keeps_the_default_it_was_made_with() => OnMain(() =>
    {
        var engine = VisusHost.SearchEngine();
        var folder = VisusHost.DownloadsFolder();
        using var binding = WebSettingsBinding.Bind();
        Assert.Null(WebSettingsHost.Active);
        Assert.Equal(engine, VisusHost.SearchEngine());
        Assert.Equal(SearchEngine.DuckDuckGo, VisusHost.SearchEngine());
        Assert.False(VisusHost.SearchSuggestions());
        Assert.False(VisusHost.PageWindowsInDefaultBrowser());
        Assert.False(VisusHost.AskWhereToSave());
        Assert.Equal(folder, VisusHost.DownloadsFolder());
        Assert.True(ProjectPreviewHost.OpenAutomatically());
        Assert.True(ProjectPreviewHost.ReloadOnSave());
        Assert.Equal(4, new WebSurfaceOptions().MaxLiveViews);
        Assert.True(McpAppCatalog.Enabled());
        Assert.True(MCPWire.AdvertiseApps());
        Assert.Equal(VisualAnswerLevel.Balanced, UiTeaching.Visual.Current());
        Assert.True(UiTeaching.LiveAnswers.Current());
        Assert.Equal(WebAgentSettings.Default.Enabled, VisusAgentStartup.Settings().Enabled);
        Assert.True(VisusAgentStartup.Settings().AskNewSites);
    });

    [Fact]
    public void Each_visus_seam_reads_the_attached_settings_on_every_call() => OnMain(() =>
    {
        var web = Fresh();
        using var binding = WebSettingsBinding.Bind();
        using var attach = WebSettingsHost.Attach(web);

        web.SearchEngine = SearchEngine.Brave;
        Assert.Equal(SearchEngine.Brave, VisusHost.SearchEngine());
        web.SearchEngine = SearchEngine.Bing;
        Assert.Equal(SearchEngine.Bing, VisusHost.SearchEngine());

        Assert.False(VisusHost.SearchSuggestions());
        web.SearchSuggestions = true;
        Assert.True(VisusHost.SearchSuggestions());

        Assert.False(VisusHost.PageWindowsInDefaultBrowser());
        web.ChatLinks = ChatLinkTarget.DefaultBrowser;
        Assert.True(VisusHost.PageWindowsInDefaultBrowser());
        web.ChatLinks = ChatLinkTarget.Visus;
        Assert.False(VisusHost.PageWindowsInDefaultBrowser());

        web.AskWhereToSave = true;
        Assert.True(VisusHost.AskWhereToSave());

        web.OpenPreviews = false;
        Assert.False(ProjectPreviewHost.OpenAutomatically());
        web.ReloadOnSave = false;
        Assert.False(ProjectPreviewHost.ReloadOnSave());

        web.MaxLiveTabs = 6;
        Assert.Equal(6, new WebSurfaceOptions().MaxLiveViews);
        web.MaxLiveTabs = 2;
        Assert.Equal(2, new WebSurfaceOptions().MaxLiveViews);
    });

    [Fact]
    public void A_chosen_downloads_folder_is_used_while_it_exists_and_never_replaced_by_another_when_it_is_gone() => OnMain(() =>
    {
        var web = Fresh();
        var chosen = Path.Combine(Path.GetTempPath(), "nth-w10-downloads-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chosen);
        try
        {
            using var binding = WebSettingsBinding.Bind();
            using var attach = WebSettingsHost.Attach(web);
            var standard = VisusHost.DownloadsFolder();
            web.DownloadsFolder = chosen;
            Assert.Equal(chosen, VisusHost.DownloadsFolder());
            Directory.Delete(chosen);
            Assert.Null(VisusHost.DownloadsFolder()); // the download says it has no folder; it does not drop the file somewhere else
            web.DownloadsFolder = "";
            Assert.Equal(standard, VisusHost.DownloadsFolder());
        }
        finally
        {
            if (Directory.Exists(chosen)) Directory.Delete(chosen);
        }
    });

    [Fact]
    public void The_model_settings_reach_the_web_tools_and_the_defaults_come_back_when_nothing_is_attached() => OnMain(() =>
    {
        var web = Fresh();
        using var binding = WebSettingsBinding.Bind();
        using (WebSettingsHost.Attach(web))
        {
            web.LetModelsUseVisus = false;
            web.AskBeforeNewSite = false;
            var settings = VisusAgentStartup.Settings();
            Assert.False(settings.Enabled);
            Assert.False(settings.AskNewSites);
            web.LetModelsUseVisus = true;
            Assert.True(VisusAgentStartup.Settings().Enabled);
        }
        Assert.True(VisusAgentStartup.Settings().Enabled);
        Assert.True(VisusAgentStartup.Settings().AskNewSites);
    });

    [Fact]
    public void MCP_Apps_off_draws_a_tool_call_as_a_plain_one_and_stops_saying_nth_hosts_apps() => OnMain(() =>
    {
        var web = Fresh();
        using var binding = WebSettingsBinding.Bind();
        using var attach = WebSettingsHost.Attach(web);
        Assert.True(McpAppCatalog.Enabled());
        Assert.NotNull(MCPWire.ClientCapabilities()["extensions"]);
        web.McpApps = false;
        Assert.False(McpAppCatalog.Enabled());
        Assert.Null(MCPWire.ClientCapabilities()["extensions"]);
        Assert.NotNull(MCPWire.ClientCapabilities()["resources"]);
    });

    [Fact]
    public void The_teaching_follows_visual_answers_and_live_answers() => OnMain(() =>
    {
        var web = Fresh();
        using var binding = WebSettingsBinding.Bind();
        using var attach = WebSettingsHost.Attach(web);
        web.VisualAnswers = VisualAnswerLevel.More;
        Assert.Equal(VisualAnswerLevel.More, UiTeaching.Visual.Current());
        Assert.Equal(VisualAnswerLevel.More, UiTeaching.Options(["ui_show"]).Visual);
        web.LiveAnswers = false;
        Assert.False(UiTeaching.LiveAnswers.Current());
        Assert.False(UiTeaching.Options(["ui_show"]).LiveAnswers);
    });

    [Fact]
    public void The_live_answer_host_follows_both_switches_and_is_put_back_when_the_binding_ends() => OnMain(() =>
    {
        var web = Fresh();
        var host = new ArtifactHost(null, null);
        using var scope = ArtifactHost.Shared.Override(host);
        var binding = WebSettingsBinding.Bind();
        using var attach = WebSettingsHost.Attach(web);
        Assert.True(host.Live.Enabled);
        Assert.True(host.Live.HtmlEnabled);
        web.LiveAnswers = false;
        Assert.False(host.Live.Enabled);
        web.InlineHtmlWidgets = false;
        Assert.False(host.Live.HtmlEnabled);
        web.LiveAnswers = true;
        Assert.True(host.Live.Enabled);
        binding.Dispose();
        web.LiveAnswers = false;
        web.InlineHtmlWidgets = false;
        Assert.True(host.Live.Enabled);
        Assert.True(host.Live.HtmlEnabled);
    });

    [Fact]
    public void An_artifacts_call_to_the_model_follows_Artifacts_may_use_your_model() => OnMain(() =>
    {
        var web = Fresh();
        var folder = Path.Combine(Path.GetTempPath(), "nth-w10-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var before = WebSettingsBinding.Runtime;
        try
        {
            var runtime = new ArtifactRuntime(new ArtifactStore(() => folder), new ArtifactLibraryFiles(folder));
            Assert.Equal(ArtifactModelPolicy.Ask, runtime.ModelPolicy());
            WebSettingsBinding.Runtime = () => runtime;
            var binding = WebSettingsBinding.Bind();
            using var attach = WebSettingsHost.Attach(web);
            Assert.Equal(ArtifactModelPolicy.Ask, runtime.ModelPolicy());
            web.ArtifactModel = ArtifactModelChoice.Never;
            Assert.Equal(ArtifactModelPolicy.Never, runtime.ModelPolicy());
            web.ArtifactModel = ArtifactModelChoice.AskEachArtifact;
            Assert.Equal(ArtifactModelPolicy.Ask, runtime.ModelPolicy());
            web.ArtifactModel = ArtifactModelChoice.Never;
            binding.Dispose();
            Assert.Equal(ArtifactModelPolicy.Ask, runtime.ModelPolicy());
        }
        finally
        {
            WebSettingsBinding.Runtime = before;
            Directory.Delete(folder, recursive: true);
        }
    });

    [Fact]
    public void A_widget_card_is_the_inline_widget_while_inline_widgets_are_on_and_the_plain_card_when_they_are_off() => OnMain(() =>
    {
        var web = Fresh();
        var drawn = new List<string>();
        using var hostScope = LiveAnswerHtml.Host.Override((view, _) =>
        {
            drawn.Add(view.Id);
            return new Avalonia.Controls.Border();
        });
        using var binding = WebSettingsBinding.Bind();
        using var attach = WebSettingsHost.Attach(web);
        var view = new ToolLiveView { Id = "toy", Kind = LiveViewKind.Html, Text = "<p>hi</p>", Complete = true };
        Assert.NotNull(LiveAnswerHtml.Host.Current(view, null!));
        Assert.Equal(["toy"], drawn);
        web.InlineHtmlWidgets = false;
        Assert.Null(LiveAnswerHtml.Host.Current(view, null!));
        Assert.Equal(["toy"], drawn);
        web.InlineHtmlWidgets = true;
        Assert.NotNull(LiveAnswerHtml.Host.Current(view, null!));
    });

    [Fact]
    public void The_two_pages_get_their_parts_from_the_binding_and_lose_them_with_it() => OnMain(() =>
    {
        Assert.Null(VisusSettingsPage.Bound());
        Assert.Null(TabulaSettingsPage.Bound());
        using (WebSettingsBinding.Bind())
        {
            var visus = Assert.IsType<VisusSettingsParts>(VisusSettingsPage.Bound());
            Assert.NotNull(visus.RemovePermission);
            Assert.NotNull(visus.ObservePermissions);
            Assert.NotNull(visus.PickFolder);
            Assert.NotNull(visus.ClearBrowsingData);
            Assert.NotNull(visus.ForgetAgentData);
            var tabula = Assert.IsType<TabulaSettingsParts>(TabulaSettingsPage.Bound());
            Assert.NotNull(tabula.Storage);
            Assert.NotNull(tabula.ClearArtifacts);
        }
        Assert.Null(VisusSettingsPage.Bound());
        Assert.Null(TabulaSettingsPage.Bound());
    });

    [Fact]
    public void Disposing_the_binding_puts_every_seam_back() => OnMain(() =>
    {
        var web = Fresh();
        web.SearchEngine = SearchEngine.Google;
        web.SearchSuggestions = true;
        web.OpenPreviews = false;
        web.McpApps = false;
        web.MaxLiveTabs = 8;
        var binding = WebSettingsBinding.Bind();
        using (WebSettingsHost.Attach(web))
        {
            Assert.Equal(SearchEngine.Google, VisusHost.SearchEngine());
            Assert.False(McpAppCatalog.Enabled());
        }
        binding.Dispose();
        using var attach = WebSettingsHost.Attach(web);
        Assert.Equal(SearchEngine.DuckDuckGo, VisusHost.SearchEngine());
        Assert.False(VisusHost.SearchSuggestions());
        Assert.True(ProjectPreviewHost.OpenAutomatically());
        Assert.True(McpAppCatalog.Enabled());
        Assert.Equal(4, new WebSurfaceOptions().MaxLiveViews);
    });
}
