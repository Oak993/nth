using Avalonia.Controls;
using Avalonia.Input;
using Marrow.App.Port.Web;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Web;

namespace Marrow.App.Tests;

// PORT-ONLY: web surfaces W10, a link from a chat in the app (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.11; ED-NEW-web-w10-2). The facade a chat's prose, its cards and its live answers call: the choice decides Visus or the
// default browser, the first time it asks (one question at a time, nothing opens until it is answered), Ctrl takes the other browser, a sign-in and another app's address stay with the system, a path
// goes to the local-file opener, and a question that cannot be shown opens nothing and leaves nothing waiting.
[Collection("AppSettings")]
[Trait("Decision", "ED-NEW-web-w10-2")]
public sealed class WebW10ChatLinksTests
{
    private const string Page = "https://example.org/article";

    private static SwiftURL Url(string text) => SwiftURL.Create(text)!;

    private static void Run(Action<AppSettingsScope, WebSettings, ChatLinkSinks> body)
    {
        using var scope = AppSettingsScope.Create();
        using var sinks = new ChatLinkSinks();
        scope.OnMain(() => body(scope, new AppSettings().Web, sinks));
    }

    [Theory]
    [InlineData(ChatLinkTarget.Visus, false, "visus:")]
    [InlineData(ChatLinkTarget.DefaultBrowser, false, "external:")]
    [InlineData(ChatLinkTarget.Visus, true, "external:")]
    [InlineData(ChatLinkTarget.DefaultBrowser, true, "visus:")]
    public void A_web_page_goes_where_the_choice_says_and_Control_takes_the_other_browser(ChatLinkTarget choice, bool control, string expected) => Run((_, web, sinks) =>
    {
        web.ChatLinks = choice;
        Assert.True(ChatLinks.Open(Url(Page), null, control ? KeyModifiers.Control : KeyModifiers.None, settings: web));
        Assert.Equal([expected + Page], sinks.Opened);
        Assert.Empty(sinks.Questions);
    });

    [Fact]
    public void Other_modifiers_are_not_Control() => Run((_, web, sinks) =>
    {
        web.ChatLinks = ChatLinkTarget.Visus;
        ChatLinks.Open(Url(Page), null, KeyModifiers.Shift | KeyModifiers.Alt, settings: web);
        Assert.Equal(["visus:" + Page], sinks.Opened);
    });

    [Fact]
    public void The_first_time_it_asks_over_the_control_the_click_came_from_and_nothing_opens() => Run((_, web, sinks) =>
    {
        var anchor = new Border();
        Assert.True(ChatLinks.Open(Url(Page), null, KeyModifiers.None, anchor, settings: web));
        var question = Assert.Single(sinks.Questions);
        Assert.Same(anchor, question.Anchor);
        Assert.Empty(sinks.Opened);
        Assert.Equal(ChatLinkTarget.Ask, web.ChatLinks);
        Assert.True(question.Choice.Remember);
        Assert.Equal(Url(Page), question.Choice.Url);
    });

    [Fact]
    public void Answering_Visus_with_Remember_opens_it_stores_the_choice_and_the_next_link_does_not_ask() => Run((scope, web, sinks) =>
    {
        ChatLinks.Open(Url(Page), null, KeyModifiers.None, settings: web);
        sinks.Questions.Single().Choice.Choose(visus: true);
        Assert.Equal(["visus:" + Page], sinks.Opened);
        Assert.Equal(ChatLinkTarget.Visus, web.ChatLinks);
        scope.Relaunch();
        Assert.Equal(ChatLinkTarget.Visus, new AppSettings().Web.ChatLinks);

        ChatLinks.Open(Url("https://example.org/second"), null, KeyModifiers.None, settings: web);
        Assert.Single(sinks.Questions);
        Assert.Equal(["visus:" + Page, "visus:https://example.org/second"], sinks.Opened);
    });

    [Fact]
    public void Answering_with_Remember_cleared_opens_this_link_and_asks_again_next_time() => Run((scope, web, sinks) =>
    {
        ChatLinks.Open(Url(Page), null, KeyModifiers.None, settings: web);
        var choice = sinks.Questions.Single().Choice;
        choice.Remember = false;
        choice.Choose(visus: false);
        Assert.Equal(["external:" + Page], sinks.Opened);
        Assert.Equal(ChatLinkTarget.Ask, web.ChatLinks);
        Assert.DoesNotContain(scope.Defaults.Writes, write => write.Key == WebSettings.Keys.ChatLinks);

        ChatLinks.Open(Url("https://example.org/second"), null, KeyModifiers.None, settings: web);
        Assert.Equal(2, sinks.Questions.Count);
    });

    [Fact]
    public void Control_on_the_first_click_sends_this_link_to_the_other_browser_and_remembers_the_answer_given() => Run((_, web, sinks) =>
    {
        ChatLinks.Open(Url(Page), null, KeyModifiers.Control, settings: web);
        sinks.Questions.Single().Choice.Choose(visus: true);
        Assert.Equal(["external:" + Page], sinks.Opened);
        Assert.Equal(ChatLinkTarget.Visus, web.ChatLinks);
    });

    [Fact]
    public void Cancelling_opens_nothing_stores_nothing_and_the_next_click_asks_again() => Run((scope, web, sinks) =>
    {
        ChatLinks.Open(Url(Page), null, KeyModifiers.None, settings: web);
        sinks.Questions.Single().Choice.Cancel();
        Assert.Empty(sinks.Opened);
        Assert.Equal(ChatLinkTarget.Ask, web.ChatLinks);
        Assert.DoesNotContain(scope.Defaults.Writes, write => write.Key == WebSettings.Keys.ChatLinks);
        ChatLinks.Open(Url(Page), null, KeyModifiers.None, settings: web);
        Assert.Equal(2, sinks.Questions.Count);
    });

    [Fact]
    public void A_second_click_while_the_question_is_open_asks_nothing_new_and_the_answer_opens_the_first_link() => Run((_, web, sinks) =>
    {
        ChatLinks.Open(Url(Page), null, KeyModifiers.None, settings: web);
        ChatLinks.Open(Url("https://example.org/second"), null, KeyModifiers.None, settings: web);
        ChatLinks.Open(Url("https://example.org/third"), null, KeyModifiers.Control, settings: web);
        var question = Assert.Single(sinks.Questions);
        Assert.Empty(sinks.Opened);
        question.Choice.Choose(visus: true);
        question.Choice.Choose(visus: false); // a button pressed twice changes nothing
        question.Choice.Cancel();
        Assert.Equal(["visus:" + Page], sinks.Opened);
        Assert.Equal(ChatLinkTarget.Visus, web.ChatLinks);
    });

    [Fact]
    public void A_question_that_cannot_be_shown_is_cancelled_and_nothing_is_left_waiting() => Run((_, web, sinks) =>
    {
        var lines = new List<string>();
        using var sink = Logger.Sink.Override((message, _) => lines.Add(message));
        var asked = 0;
        ChatLinks.ShowChoice = (_, _) =>
        {
            asked++;
            throw new InvalidOperationException("no window to show it in https://example.org/secret-path");
        };
        ChatLinks.Open(Url("https://example.org/secret-path"), null, KeyModifiers.None, settings: web);
        Assert.Equal(1, asked);
        Assert.Empty(sinks.Opened);
        var line = Assert.Single(lines);
        Assert.Equal("web: the link question could not be shown (InvalidOperationException)", line);
        Assert.DoesNotContain("secret-path", line, StringComparison.Ordinal);
        ChatLinks.Open(Url(Page), null, KeyModifiers.None, settings: web);
        Assert.Equal(2, asked); // not stuck behind the failed one
    });

    [Theory]
    [InlineData("https://accounts.example.org/login?next=/home")]
    [InlineData("https://example.org/oauth/authorize?client_id=abc&response_type=code")]
    [InlineData("https://accounts.google.com/o/oauth2/v2/auth")]
    public void A_sign_in_stays_in_the_default_browser_whatever_the_choice_and_the_click(string signIn) => Run((_, web, sinks) =>
    {
        foreach (var choice in new[] { ChatLinkTarget.Ask, ChatLinkTarget.Visus, ChatLinkTarget.DefaultBrowser })
        foreach (var control in new[] { KeyModifiers.None, KeyModifiers.Control })
        {
            web.ChatLinks = choice;
            ChatLinks.Open(Url(signIn), null, control, settings: web);
        }
        Assert.Equal(6, sinks.Opened.Count);
        Assert.All(sinks.Opened, line => Assert.Equal("external:" + signIn, line));
        Assert.Empty(sinks.Questions);
    });

    [Fact]
    public void A_caller_that_knows_a_link_is_a_sign_in_can_force_the_default_browser() => Run((_, web, sinks) =>
    {
        web.ChatLinks = ChatLinkTarget.Visus;
        ChatLinks.Open(Url(Page), null, KeyModifiers.Control, forceExternal: true, settings: web);
        Assert.Equal(["external:" + Page], sinks.Opened);
    });

    [Theory]
    [InlineData("mailto:fake@example.org")]
    [InlineData("tel:000")]
    [InlineData("slack://channel?team=T1")]
    public void Another_apps_address_goes_to_the_system_and_never_asks(string link) => Run((_, web, sinks) =>
    {
        web.ChatLinks = ChatLinkTarget.Visus;
        ChatLinks.Open(Url(link), null, KeyModifiers.None, settings: web);
        web.ChatLinks = ChatLinkTarget.Ask;
        ChatLinks.Open(Url(link), null, KeyModifiers.Control, settings: web);
        Assert.Equal(["external:" + link, "external:" + link], sinks.Opened);
        Assert.Empty(sinks.Questions);
    });

    [Theory]
    [InlineData("file:///C:/fake/readme.md")]
    [InlineData("C:/fake/readme.md")]
    [InlineData("/fake/readme.md")]
    public void A_path_goes_to_the_local_file_opener_whatever_the_choice(string link) => Run((_, web, sinks) =>
    {
        foreach (var choice in new[] { ChatLinkTarget.Ask, ChatLinkTarget.Visus, ChatLinkTarget.DefaultBrowser })
        {
            web.ChatLinks = choice;
            ChatLinks.Open(Url(link), null, KeyModifiers.Control, settings: web);
        }
        Assert.Equal(3, sinks.Opened.Count);
        Assert.All(sinks.Opened, line => Assert.StartsWith("local:", line, StringComparison.Ordinal));
        Assert.Empty(sinks.Questions);
    });

    [Fact]
    public void A_link_with_no_chat_of_its_own_opens_through_the_attached_settings_and_nested_scopes_keep_the_outer_control() => Run((_, web, sinks) =>
    {
        web.ChatLinks = ChatLinkTarget.Visus;
        using var attach = WebSettingsHost.Attach(web);
        var anchor = new Border();
        var inner = ChatLinks.With(null, anchor, () => ChatLinks.OpenFromAnywhere(Url(Page)));
        Assert.True(inner);
        Assert.Equal(["visus:" + Page], sinks.Opened);
        var nested = ChatLinks.With(null, anchor, () => ChatLinks.With(null, null, () => ChatLinks.OpenFromAnywhere("https://example.org/b")));
        Assert.True(nested);
        Assert.Equal(["visus:" + Page, "visus:https://example.org/b"], sinks.Opened);
    });

    [Fact]
    public void A_question_asked_with_no_chat_still_names_the_control_it_was_scoped_to() => Run((_, web, sinks) =>
    {
        using var attach = WebSettingsHost.Attach(web);
        var anchor = new Border();
        ChatLinks.With(null, anchor, () => ChatLinks.OpenFromAnywhere(Url(Page)));
        Assert.Same(anchor, Assert.Single(sinks.Questions).Anchor);
    });

    [Fact]
    public void A_different_settings_object_gets_its_own_router_so_a_stale_question_never_blocks_it() => Run((scope, first, sinks) =>
    {
        ChatLinks.Open(Url(Page), null, KeyModifiers.None, settings: first); // leaves a question open on the first settings
        Assert.Single(sinks.Questions);
        var second = new AppSettings().Web;
        second.ChatLinks = ChatLinkTarget.Visus;
        ChatLinks.Open(Url(Page), null, KeyModifiers.None, settings: second);
        Assert.Equal(["visus:" + Page], sinks.Opened);
        Assert.Single(sinks.Questions);
        _ = scope;
    });

    [Fact]
    public void The_sheet_question_uses_the_designed_words() => Assert.Equal("Open links in Visus or your default browser?", ChatLinkChoice.Title);
}
