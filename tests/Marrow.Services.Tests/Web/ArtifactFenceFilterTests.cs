using Marrow.Core.Models;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces W12, Tabula (DESIGN.md 6.2 fallback). The fenced fallback between a session and its chat: the
// chat sees a chip where an nth-artifact block is, the block is saved when it closes (a new artifact, or a new version of
// the id it names), and every other event passes untouched. No model provider is called; the answers are written here.
[Trait("Decision", "ED-NEW-web-w12-2")]
public sealed class ArtifactFenceFilterTests
{
    private static readonly Guid Chat = Guid.Parse("0b7f3c9a-41d2-4e8b-a6c5-2f1e0d9c8b02");
    private static readonly Guid TurnId = Guid.Parse("0b7f3c9a-41d2-4e8b-a6c5-2f1e0d9c8b03");

    private const string Answer =
        "Here is the calculator:\n"
        + "```nth-artifact id=pricing kind=html title=\"Pricing calculator\"\n"
        + "<h3>Orbit pricing</h3>\n"
        + "```\n"
        + "Ask for changes any time.";

    private static ArtifactFenceFilter Filter(ArtifactHostScope scope) =>
        new(Chat, scope.Host, () => (new ArtifactAuthor("claude", "sonnet", TurnId), "Make a pricing calculator"));

    private static (string Shown, List<ProviderEvent> Events) Stream(ArtifactFenceFilter filter, string id, string text, int piece)
    {
        var events = new List<ProviderEvent>();
        for (var at = 0; at < text.Length; at += piece)
            events.AddRange(filter.Filter(new ProviderEvent.MessageDelta(id, text[at..Math.Min(text.Length, at + piece)])));
        var shown = string.Concat(events.OfType<ProviderEvent.MessageDelta>().Select(d => d.Text));
        events.AddRange(filter.Filter(new ProviderEvent.MessageCompleted(id, text)));
        return (shown, events);
    }

    [Fact]
    public void Streamed_deltas_show_a_chip_in_place_of_the_block_and_save_it_when_it_closes()
    {
        using var scope = new ArtifactHostScope();
        const string expected = "Here is the calculator:\n\n[Pricing calculator · HTML app](nth-artifact://pricing)\n\nAsk for changes any time.";
        foreach (var piece in new[] { 1, 7, Answer.Length })
        {
            scope.Store.Clear(Chat);
            var (shown, events) = Stream(Filter(scope), "m1", Answer, piece);
            Assert.Equal(expected, shown);
            Assert.Equal(expected, Assert.IsType<ProviderEvent.MessageCompleted>(events[^1]).Text);
            Assert.DoesNotContain(events, e => e is ProviderEvent.Notice);
            Assert.DoesNotContain(events, e => e is ProviderEvent.MessageDelta { Text.Length: 0 });
            var saved = scope.Store.Read(Chat, "pricing");
            Assert.Equal(("Pricing calculator", ArtifactKind.Html, 1, "<h3>Orbit pricing</h3>\n"), (saved.Artifact.Title, saved.Artifact.Kind, saved.Version.Number, saved.Text));
            Assert.Equal((new ArtifactAuthor("claude", "sonnet", TurnId), "Make a pricing calculator"), (saved.Version.Author, saved.Version.Prompt));
        }
    }

    [Fact]
    public void A_message_that_arrives_whole_or_differs_from_its_deltas_is_parsed_whole_and_each_block_is_saved_once()
    {
        using var scope = new ArtifactHostScope();
        var filter = Filter(scope);
        var whole = filter.Filter(new ProviderEvent.MessageCompleted("m1", Answer));
        Assert.StartsWith("Here is the calculator:\n\n[Pricing calculator", Assert.IsType<ProviderEvent.MessageCompleted>(Assert.Single(whole)).Text, StringComparison.Ordinal);
        Assert.Equal(1, scope.Store.Read(Chat, "pricing").Version.Number);

        // Deltas carry the block; the final text adds a line. The block is not saved a second time.
        foreach (var delta in new[] { Answer[..90], Answer[90..] })
            filter.Filter(new ProviderEvent.MessageDelta("m2", delta));
        Assert.Equal(2, scope.Store.Read(Chat, "pricing").Version.Number);
        var final = filter.Filter(new ProviderEvent.MessageCompleted("m2", Answer + "\nThanks."));
        Assert.EndsWith("any time.\nThanks.", Assert.IsType<ProviderEvent.MessageCompleted>(Assert.Single(final)).Text, StringComparison.Ordinal);
        Assert.Equal(2, scope.Store.Read(Chat, "pricing").Artifact.Latest.Number);
    }

    [Fact]
    public void A_block_naming_a_known_id_updates_it_and_an_id_of_another_kind_becomes_a_new_artifact()
    {
        using var scope = new ArtifactHostScope();
        var filter = Filter(scope);
        Stream(filter, "m1", Answer, 5);
        var (shown, _) = Stream(filter, "m2", "Better:\n```nth-artifact id=pricing\n<h3>Orbit pricing, yearly</h3>\n```\n", 4);
        Assert.Contains("[Pricing calculator · HTML app](nth-artifact://pricing)", shown, StringComparison.Ordinal); // an update names only the id
        var updated = scope.Store.Read(Chat, "pricing");
        Assert.Equal((2, "Pricing calculator", "<h3>Orbit pricing, yearly</h3>\n"), (updated.Version.Number, updated.Artifact.Title, updated.Text));

        var (logo, _) = Stream(filter, "m3", "```nth-artifact id=pricing kind=svg title=Badge\n<svg/>\n```", 3);
        Assert.Equal("[Badge · SVG image](nth-artifact://badge)\n", logo);
        Assert.Equal(ArtifactKind.Svg, scope.Store.Read(Chat, "badge").Artifact.Kind);
        Assert.Equal(2, scope.Store.Read(Chat, "pricing").Artifact.Latest.Number);

        var (untitled, _) = Stream(filter, "m4", "```nth-artifact\n<!doctype html><p>Hi</p>\n```\n```nth-artifact lang=python\nprint(1)\n```", 6);
        Assert.Equal("[artifact](nth-artifact://artifact)\n\n[artifact-2](nth-artifact://artifact-2)\n", untitled); // no title: the chip names the id
        Assert.Equal((ArtifactKind.Html, ArtifactKind.Code), (scope.Store.Read(Chat, "artifact").Artifact.Kind, scope.Store.Read(Chat, "artifact-2").Artifact.Kind));
        var code = scope.Store.Read(Chat, "artifact-2");
        Assert.Equal(("python", ".py", "print(1)\n"), (code.Artifact.Language, Path.GetExtension(code.Version.File), code.Text));
    }

    [Fact]
    public void A_project_block_carries_the_whole_file_set_so_files_it_no_longer_has_are_removed()
    {
        using var scope = new ArtifactHostScope();
        var filter = Filter(scope);
        const string first =
            "````nth-artifact id=site kind=project title=\"Orbit site\"\n"
            + "```html path=index.html\n<link href=\"style.css\">\n```\n"
            + "```css path=style.css\nbody{}\n```\n"
            + "````\n";
        Stream(filter, "m1", first, 9);
        var v1 = scope.Store.Read(Chat, "site");
        Assert.Equal((ArtifactKind.Project, "index.html"), (v1.Artifact.Kind, v1.Version.Entry));
        Assert.Equal(new[] { "index.html", "style.css" }, v1.Version.Files.Order(StringComparer.Ordinal));
        const string second =
            "````nth-artifact id=site\n"
            + "```html path=index.html\n<script src=\"app.js\"></script>\n```\n"
            + "```js path=app.js\nconsole.log(1);\n```\n"
            + "````\n";
        Stream(filter, "m2", second, 11);
        var v2 = scope.Store.Read(Chat, "site");
        Assert.Equal(2, v2.Version.Number);
        Assert.Equal(new[] { "app.js", "index.html" }, v2.Version.Files.Order(StringComparer.Ordinal));
        Assert.Equal("console.log(1);\n", v2.Files!["app.js"]);
    }

    [Fact]
    public void A_block_the_store_refuses_leaves_a_warning_beside_the_chip_and_an_unfinished_block_is_saved_at_turn_end()
    {
        using var scope = new ArtifactHostScope();
        var filter = Filter(scope);
        var (shown, events) = Stream(filter, "m1", "```nth-artifact kind=project title=\"Empty site\"\nno files here\n```\n", 8);
        Assert.Equal("[Empty site · Project](nth-artifact://empty-site)\n\n", shown);
        var notice = Assert.IsType<ProviderEvent.Notice>(Assert.Single(events, e => e is ProviderEvent.Notice)).Value;
        Assert.Equal(Notice.Level.Warning, notice.NoticeLevel);
        Assert.StartsWith("Tabula could not save “Empty site”: the project block has no files", notice.Message, StringComparison.Ordinal);
        Assert.Empty(scope.Store.List(Chat));

        var held = filter.Filter(new ProviderEvent.MessageDelta("m2", "Draft:\n```nth-artifact kind=markdown title=Plan\n# Plan\nStep one"));
        Assert.Equal("Draft:\n\n[Plan · Document](nth-artifact://plan)\n", string.Concat(held.OfType<ProviderEvent.MessageDelta>().Select(d => d.Text)));
        Assert.Null(scope.Store.Find(Chat, "plan"));
        var end = filter.Filter(new ProviderEvent.TurnCompleted(TurnStatus.Interrupted, null));
        Assert.IsType<ProviderEvent.TurnCompleted>(end[^1]);
        Assert.Equal("# Plan\nStep one", scope.Store.Read(Chat, "plan").Text);
        Assert.Equal(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null), Assert.Single(filter.Filter(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null))));
    }

    [Fact]
    public void With_artifacts_off_every_event_passes_untouched()
    {
        var filter = new ArtifactFenceFilter(Chat, new ArtifactHost(null, null), () => (ArtifactAuthor.You, null));
        ProviderEvent[] events =
        [
            new ProviderEvent.MessageDelta("m1", Answer[..60]),
            new ProviderEvent.MessageCompleted("m1", Answer),
            new ProviderEvent.TurnCompleted(TurnStatus.Completed, null),
        ];
        foreach (var @event in events)
            Assert.Same(@event, Assert.Single(filter.Filter(@event)));
        using var scope = new ArtifactHostScope();
        var on = Filter(scope);
        var other = new ProviderEvent.Exited("gone");
        Assert.Same(other, Assert.Single(on.Filter(other)));
        Assert.Equal(ArtifactKind.Svg, ArtifactFenceFilter.Guess("  <?xml version=\"1.0\"?><svg/>", null));
        Assert.Equal(ArtifactKind.Markdown, ArtifactFenceFilter.Guess("plain words", null));
    }
}
