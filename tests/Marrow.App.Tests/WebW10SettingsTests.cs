using System.Reflection;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;

namespace Marrow.App.Tests;

// PORT-ONLY: CU-W10; approved DESIGN 5.11 and 9. Reflection lets acceptance tests
// execute against the pre-implementation build, so missing APIs fail as tests.
[Collection("AppSettings")]
[Trait("Decision", "ED-NEW-web-w10-1")]
public sealed class WebW10SettingsTests
{
    private static object Web(AppSettings settings)
    {
        var property = typeof(AppSettings).GetProperty("Web");
        Assert.NotNull(property);
        return property.GetValue(settings)!;
    }

    private static object Get(object web, string name) => web.GetType().GetProperty(name)!.GetValue(web)!;
    private static void Set(object web, string name, object value)
    {
        var property = web.GetType().GetProperty(name)!;
        property.SetValue(web, property.PropertyType.IsEnum ? Enum.Parse(property.PropertyType, (string)value) : value);
    }

    private static object Router(object web, List<string> opened)
    {
        var type = typeof(AppSettings).Assembly.GetType("Marrow.App.Port.Web.ChatLinkRouter");
        Assert.NotNull(type);
        return Activator.CreateInstance(type, web,
            (Action<SwiftURL>)(u => opened.Add("visus:" + u.AbsoluteString)),
            (Action<SwiftURL>)(u => opened.Add("external:" + u.AbsoluteString)),
            (Action<SwiftURL>)(u => opened.Add("local:" + u.AbsoluteString)))!;
    }

    [Fact]
    public void Ask_opens_nothing_until_a_choice_and_cancel_keeps_the_default()
    {
        using var scope = AppSettingsScope.Create();
        scope.OnMain(() =>
        {
            var web = Web(new AppSettings()); var opened = new List<string>(); var router = Router(web, opened);
            router.GetType().GetMethod("Open")!.Invoke(router, [SwiftURL.Create("https://example.test/first")!, false, false]);
            Assert.Empty(opened);
            var pending = Get(router, "Pending");
            Assert.Equal(true, Get(pending, "Remember"));
            pending.GetType().GetMethod("Cancel")!.Invoke(pending, null);
            Assert.Empty(opened); Assert.Equal("Ask", Get(web, "ChatLinks").ToString());
            Assert.Null(router.GetType().GetProperty("Pending")!.GetValue(router));
        });
    }

    [Fact]
    public void Choice_captures_the_first_URL_and_does_not_create_overlapping_prompts()
    {
        using var scope = AppSettingsScope.Create();
        scope.OnMain(() =>
        {
            var web = Web(new AppSettings()); var opened = new List<string>(); var router = Router(web, opened);
            var open = router.GetType().GetMethod("Open")!;
            open.Invoke(router, [SwiftURL.Create("https://example.test/first")!, true, false]);
            var pending = Get(router, "Pending");
            open.Invoke(router, [SwiftURL.Create("https://example.test/second")!, false, false]);
            Assert.Same(pending, Get(router, "Pending"));
            pending.GetType().GetMethod("Choose")!.Invoke(pending, [true]);
            Assert.Equal("external:https://example.test/first", Assert.Single(opened));
            Assert.Equal("Visus", Get(web, "ChatLinks").ToString());
            pending.GetType().GetMethod("Choose")!.Invoke(pending, [false]);
            Assert.Single(opened);
        });
    }

    [Fact]
    public void Approved_defaults_are_loaded_without_writing_any_setting()
    {
        using var scope = AppSettingsScope.Create();
        scope.OnMain(() =>
        {
            var web = Web(new AppSettings());
            Assert.Equal("Ask", Get(web, "ChatLinks").ToString());
            Assert.Equal("DuckDuckGo", Get(web, "SearchEngine").ToString());
            Assert.Equal("Balanced", Get(web, "VisualAnswers").ToString());
            Assert.Equal("AskEachArtifact", Get(web, "ArtifactModel").ToString());
            foreach (var name in new[] { "OpenPreviews", "ReloadOnSave", "LetModelsUseVisus", "AskBeforeNewSite", "LiveAnswers", "InlineHtmlWidgets", "McpApps" })
                Assert.Equal(true, Get(web, name));
            Assert.Equal(false, Get(web, "SearchSuggestions"));
            Assert.Equal(false, Get(web, "AskWhereToSave"));
            Assert.Equal(4, Get(web, "MaxLiveTabs"));
            Assert.Empty(scope.Defaults.Writes);
        });
    }

    [Fact]
    public void Choices_switches_and_live_tab_limit_survive_a_relaunch()
    {
        using var scope = AppSettingsScope.Create();
        scope.OnMain(() =>
        {
            var web = Web(new AppSettings());
            Set(web, "ChatLinks", "Visus"); Set(web, "VisualAnswers", "More");
            Set(web, "SearchEngine", "Bing"); Set(web, "ArtifactModel", "Never");
            Set(web, "LiveAnswers", false); Set(web, "InlineHtmlWidgets", false);
            Set(web, "McpApps", false); Set(web, "SearchSuggestions", true);
            Set(web, "MaxLiveTabs", 99);
            Assert.Equal(8, Get(web, "MaxLiveTabs"));
            scope.Relaunch();
            var restored = Web(new AppSettings());
            Assert.Equal("Visus", Get(restored, "ChatLinks").ToString());
            Assert.Equal("More", Get(restored, "VisualAnswers").ToString());
            Assert.Equal("Bing", Get(restored, "SearchEngine").ToString());
            Assert.Equal("Never", Get(restored, "ArtifactModel").ToString());
            Assert.Equal(false, Get(restored, "LiveAnswers"));
            Assert.Equal(false, Get(restored, "InlineHtmlWidgets"));
            Assert.Equal(false, Get(restored, "McpApps"));
            Assert.Equal(true, Get(restored, "SearchSuggestions"));
            Assert.Equal(8, Get(restored, "MaxLiveTabs"));
            Set(restored, "MaxLiveTabs", -5); Assert.Equal(2, Get(restored, "MaxLiveTabs"));
        });
    }

    [Theory]
    [InlineData(true, true, false, "Visus", "Visus")]
    [InlineData(false, true, false, "DefaultBrowser", "DefaultBrowser")]
    [InlineData(true, false, false, "Visus", "Ask")]
    [InlineData(false, false, false, "DefaultBrowser", "Ask")]
    [InlineData(true, true, true, "DefaultBrowser", "Visus")]
    public void First_choice_remembers_only_when_checked_and_Control_inverts_this_open(bool visus, bool remember, bool control, string target, string saved)
    {
        using var scope = AppSettingsScope.Create();
        scope.OnMain(() =>
        {
            var web = Web(new AppSettings());
            Assert.Equal(target, web.GetType().GetMethod("CompleteChoice")!.Invoke(web, [visus, remember, control])!.ToString());
            Assert.Equal(saved, Get(web, "ChatLinks").ToString());
            scope.Relaunch();
            Assert.Equal(saved, Get(Web(new AppSettings()), "ChatLinks").ToString());
        });
    }

    [Theory]
    [InlineData("Ask", false, "Ask")]
    [InlineData("Ask", true, "Ask")]
    [InlineData("Visus", false, "Visus")]
    [InlineData("Visus", true, "DefaultBrowser")]
    [InlineData("DefaultBrowser", false, "DefaultBrowser")]
    [InlineData("DefaultBrowser", true, "Visus")]
    public void Chat_web_links_follow_the_choice_and_Control_opens_the_other_browser(string preference, bool control, string expected)
    {
        using var scope = AppSettingsScope.Create();
        scope.OnMain(() =>
        {
            var web = Web(new AppSettings()); Set(web, "ChatLinks", preference);
            Assert.Equal(expected, web.GetType().GetMethod("Destination")!.Invoke(web, ["https://example.test/article", control, false])!.ToString());
        });
    }

    [Theory]
    [InlineData("https://example.test/signin", true, "DefaultBrowser")]
    [InlineData("mailto:fake@example.test", false, "DefaultBrowser")]
    [InlineData("tel:000", false, "DefaultBrowser")]
    [InlineData("file:///C:/fake/readme.md", false, "Local")]
    [InlineData("C:/fake/readme.md", false, "Local")]
    public void Sensitive_and_non_web_links_keep_their_external_or_local_route(string url, bool forcedExternal, string expected)
    {
        using var scope = AppSettingsScope.Create();
        scope.OnMain(() =>
        {
            var web = Web(new AppSettings()); Set(web, "ChatLinks", "Visus");
            Assert.Equal(expected, web.GetType().GetMethod("Destination")!.Invoke(web, [url, true, forcedExternal])!.ToString());
        });
    }
}
