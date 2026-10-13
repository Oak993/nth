using Marrow.Core.Web;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: web surfaces W10 (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.11: "Ask the first time (default), Visus, Default browser", "Ctrl+click always
// opens the other one", "Always the system browser: MCP OAuth, nth's own account sign-ins, update and store links, mailto: and other non-web schemes",
// "Local file links keep MarkdownLinkOpener"). The whole routing table as a pure function.
[Trait("Decision", "ED-NEW-web-w10-2")]
public sealed class ChatLinkRulesTests
{
    [Theory]
    [InlineData("https://example.test/a", ChatLinkKind.Web)]
    [InlineData("http://localhost:5173/", ChatLinkKind.Web)]
    [InlineData("HTTPS://EXAMPLE.TEST/A", ChatLinkKind.Web)]
    [InlineData("mailto:fake@example.test", ChatLinkKind.Other)]
    [InlineData("tel:000", ChatLinkKind.Other)]
    [InlineData("slack://channel?team=T1", ChatLinkKind.Other)]
    [InlineData("ms-settings:privacy", ChatLinkKind.Other)]
    [InlineData("file:///C:/fake/readme.md", ChatLinkKind.Local)]
    [InlineData("C:/fake/readme.md", ChatLinkKind.Local)]
    [InlineData("c:\\fake\\readme.md", ChatLinkKind.Local)]
    [InlineData("/fake/readme.md", ChatLinkKind.Local)]
    [InlineData("./notes.md", ChatLinkKind.Local)]
    [InlineData("notes.md", ChatLinkKind.Local)]
    [InlineData("https://", ChatLinkKind.Other)]
    public void A_link_is_a_page_a_local_path_or_another_apps_address(string link, ChatLinkKind expected) =>
        Assert.Equal(expected, ChatLinkRules.KindOf(link));

    [Theory]
    [InlineData("https://example.test/article", ChatLinkTarget.Ask, false, ChatLinkTarget.Ask)]
    [InlineData("https://example.test/article", ChatLinkTarget.Ask, true, ChatLinkTarget.Ask)]
    [InlineData("https://example.test/article", ChatLinkTarget.Visus, false, ChatLinkTarget.Visus)]
    [InlineData("https://example.test/article", ChatLinkTarget.Visus, true, ChatLinkTarget.DefaultBrowser)]
    [InlineData("https://example.test/article", ChatLinkTarget.DefaultBrowser, false, ChatLinkTarget.DefaultBrowser)]
    [InlineData("https://example.test/article", ChatLinkTarget.DefaultBrowser, true, ChatLinkTarget.Visus)]
    [InlineData("https://example.test/article", ChatLinkTarget.Local, false, ChatLinkTarget.Ask)]
    public void A_web_page_follows_the_choice_and_Control_takes_the_other_browser(string link, ChatLinkTarget preference, bool control, ChatLinkTarget expected) =>
        Assert.Equal(expected, ChatLinkRules.Destination(preference, link, control, forcedExternal: false));

    [Theory]
    [InlineData(ChatLinkTarget.Ask)]
    [InlineData(ChatLinkTarget.Visus)]
    [InlineData(ChatLinkTarget.DefaultBrowser)]
    public void Another_apps_address_and_a_local_path_never_follow_the_choice_or_Control(ChatLinkTarget preference)
    {
        foreach (var control in new[] { false, true })
        {
            Assert.Equal(ChatLinkTarget.DefaultBrowser, ChatLinkRules.Destination(preference, "mailto:fake@example.test", control, false));
            Assert.Equal(ChatLinkTarget.DefaultBrowser, ChatLinkRules.Destination(preference, "tel:000", control, false));
            Assert.Equal(ChatLinkTarget.Local, ChatLinkRules.Destination(preference, "file:///C:/fake/readme.md", control, false));
            Assert.Equal(ChatLinkTarget.Local, ChatLinkRules.Destination(preference, "C:/fake/readme.md", control, false));
        }
    }

    [Theory]
    [InlineData(ChatLinkTarget.Ask)]
    [InlineData(ChatLinkTarget.Visus)]
    [InlineData(ChatLinkTarget.DefaultBrowser)]
    public void A_caller_that_knows_the_link_is_a_sign_in_always_gets_the_default_browser(ChatLinkTarget preference)
    {
        foreach (var control in new[] { false, true })
            Assert.Equal(ChatLinkTarget.DefaultBrowser, ChatLinkRules.Destination(preference, "https://example.test/article", control, forcedExternal: true));
    }

    [Theory]
    [InlineData("https://example.test/login")]
    [InlineData("https://example.test/accounts/signin?next=/home")]
    [InlineData("https://example.test/Sign-In")]
    [InlineData("https://example.test/oauth/authorize?foo=1")]
    [InlineData("https://example.test/oauth2/v2/auth")]
    [InlineData("https://example.test/sso/start")]
    [InlineData("https://example.test/anything?client_id=abc&response_type=code")]
    [InlineData("https://example.test/anything?CLIENT_ID=abc&redirect_uri=http%3A%2F%2F127.0.0.1%3A5000")]
    [InlineData("https://example.test/anything?client_id=abc&scope=read")]
    [InlineData("https://accounts.google.com/o/oauth")]
    [InlineData("https://accounts.google.com/")]
    [InlineData("https://login.microsoftonline.com/common/x")]
    [InlineData("https://appleid.apple.com/")]
    public void A_sign_in_address_is_recognised_and_stays_in_the_default_browser_whatever_the_choice(string link)
    {
        Assert.True(ChatLinkRules.IsSignIn(link));
        foreach (var preference in new[] { ChatLinkTarget.Ask, ChatLinkTarget.Visus, ChatLinkTarget.DefaultBrowser })
        foreach (var control in new[] { false, true })
            Assert.Equal(ChatLinkTarget.DefaultBrowser, ChatLinkRules.Destination(preference, link, control, false));
    }

    [Theory]
    [InlineData("https://example.test/guides/login-flow")]
    [InlineData("https://example.test/blog/designing-a-signin-page")]
    [InlineData("https://example.test/docs?client=web")]
    [InlineData("https://example.test/search?q=client_id")]
    [InlineData("https://example.test/?scope=read")]
    [InlineData("https://example.test/oauthor")]
    [InlineData("https://docs.example.test/authentication/overview")]
    [InlineData("mailto:fake@example.test")]
    [InlineData("C:/fake/login/readme.md")]
    public void An_ordinary_page_is_not_a_sign_in(string link) => Assert.False(ChatLinkRules.IsSignIn(link));

    [Fact]
    public void Other_swaps_the_two_browsers_and_leaves_Ask_and_Local_alone()
    {
        Assert.Equal(ChatLinkTarget.DefaultBrowser, ChatLinkRules.Other(ChatLinkTarget.Visus));
        Assert.Equal(ChatLinkTarget.Visus, ChatLinkRules.Other(ChatLinkTarget.DefaultBrowser));
        Assert.Equal(ChatLinkTarget.Ask, ChatLinkRules.Other(ChatLinkTarget.Ask));
        Assert.Equal(ChatLinkTarget.Local, ChatLinkRules.Other(ChatLinkTarget.Local));
    }

    [Theory]
    [InlineData("Visus", ChatLinkTarget.Visus)]
    [InlineData("DefaultBrowser", ChatLinkTarget.DefaultBrowser)]
    [InlineData("Ask", ChatLinkTarget.Ask)]
    [InlineData("Local", ChatLinkTarget.Ask)]
    [InlineData("visus", ChatLinkTarget.Ask)]
    [InlineData("7", ChatLinkTarget.Ask)]
    [InlineData("", ChatLinkTarget.Ask)]
    [InlineData(null, ChatLinkTarget.Ask)]
    public void A_stored_choice_reads_as_one_of_the_three_settings_or_Ask(string? stored, ChatLinkTarget expected) =>
        Assert.Equal(expected, ChatLinkRules.ParseSetting(stored));
}
