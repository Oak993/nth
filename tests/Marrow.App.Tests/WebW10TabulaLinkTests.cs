using Marrow.App.Port.Web;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Artifacts;
using Marrow.Core.Models;
using Marrow.Core.Web;
using Marrow.Testing;

namespace Marrow.App.Tests;

// PORT-ONLY: web surfaces W10 landing check (ED-NEW-web-w10-9; closes the open question 4 of the W10 report). A link an artifact asks to open, once the
// reader agrees on its card (W26 F3), follows "Open links from chats in" like a link in prose: TabulaContext.OpenLink has the same default as the live-answer row.
// A sign-in address still stays with the system whatever the choice, and the first-time question is asked once for it.
[Collection("AppSettings")]
[Trait("Decision", "ED-NEW-web-w10-9")]
public sealed class WebW10TabulaLinkTests
{
    private const string Page = "https://example.org/artifact-link";

    private static TabulaContext ContextOf(string root)
    {
        var thread = Guid.NewGuid();
        var store = new ArtifactStore(() => Path.Combine(root, "threads"), () => DateTimeOffset.UnixEpoch);
        return new TabulaContext { Thread = thread, Store = store, State = TabulaState.Load(thread, null) };
    }

    private static void Run(Action<WebSettings, ChatLinkSinks, TabulaContext> body)
    {
        using var scope = AppSettingsScope.Create();
        using var sinks = new ChatLinkSinks();
        var root = Path.Combine(TestDataDirectory.Read()!, "WebW10Tabula", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        scope.OnMain(() =>
        {
            var web = new AppSettings().Web;
            using var attach = WebSettingsHost.Attach(web);
            body(web, sinks, ContextOf(root));
        });
    }

    [Theory]
    [InlineData(ChatLinkTarget.Visus, "visus:")]
    [InlineData(ChatLinkTarget.DefaultBrowser, "external:")]
    public void An_artifacts_link_goes_where_the_choice_says(ChatLinkTarget choice, string expected) => Run((web, sinks, context) =>
    {
        web.ChatLinks = choice;
        context.OpenLink(Page);
        Assert.Equal([expected + Page], sinks.Opened);
        Assert.Empty(sinks.Questions);
    });

    [Fact]
    public void The_first_time_it_asks_and_opens_nothing_until_the_reader_answers() => Run((web, sinks, context) =>
    {
        context.OpenLink(Page);
        var question = Assert.Single(sinks.Questions);
        Assert.Empty(sinks.Opened);
        question.Choice.Choose(visus: true);
        Assert.Equal(["visus:" + Page], sinks.Opened);
        Assert.Equal(ChatLinkTarget.Visus, web.ChatLinks);
    });

    [Fact]
    public void A_sign_in_address_in_an_artifact_stays_in_the_default_browser_and_never_asks() => Run((web, sinks, context) =>
    {
        const string signIn = "https://accounts.example.org/login?next=/home";
        foreach (var choice in new[] { ChatLinkTarget.Ask, ChatLinkTarget.Visus, ChatLinkTarget.DefaultBrowser })
        {
            web.ChatLinks = choice;
            context.OpenLink(signIn);
        }
        Assert.Equal(["external:" + signIn, "external:" + signIn, "external:" + signIn], sinks.Opened);
        Assert.Empty(sinks.Questions);
    });
}
