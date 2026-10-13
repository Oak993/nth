using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Artifacts;
using Marrow.Core.Models;
using Marrow.Services.Web;

namespace Marrow.App.Tests.Views.Artifacts;

// PORT-ONLY: final security gate N4 (ED-NEW-secfinal-40; DESIGN.md web surfaces 6.5 "nth.send(text) puts text in the main composer as a
// draft. The user sends."). The page chooses when nth.send runs and what it says, so each send meets a card of nth's own over the pane
// ("This artifact wants to add to your message") with how the text starts; only the card's button adds it. The card is a Tabula card,
// so it takes no focus and ignores clicks for 400 ms (ED-NEW-web-sec-artifacts-3, pinned by TabulaConsentShieldTests).
[Collection("Headless")]
[Trait("Decision", "ED-NEW-secfinal-40")]
public sealed class TabulaDraftConsentTests(HeadlessSession headless)
{
    private const char Rlo = (char)0x202E; // right-to-left override: the card writes it out instead of obeying it

    private const string Page = "<!doctype html><html><body><h3>Orbit</h3></body></html>";

    private static List<string> Words(TabulaRig rig) =>
        ((Control)rig.View.Cards.Popup!.Child!).GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();

    [Fact]
    public void The_draft_card_names_the_artifact_shows_how_the_text_starts_and_only_its_button_answers_it() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig();
        var first = rig.Show(ArtifactKind.Html, Page);
        var address = new ArtifactAddress(TabulaRig.Chat, first.Artifact.Id);
        var text = "Plan: Team, 12 seats" + Rlo + " and a hidden end" + new string('x', 400);

        var asked = TabulaBridgeHost.Instance.ConfirmDraftAsync(address, text, CancellationToken.None);
        rig.Layout();
        Assert.Equal(TabulaBridgeHost.DraftTitle, rig.View.Cards.Showing);
        Assert.False(asked.IsCompleted, "the card was answered without a click");
        Assert.Empty(rig.Drafts);
        var words = Words(rig);
        Assert.Contains(TabulaBridgeHost.DraftDetail(text.Length), words);
        Assert.Contains(words, w => w.StartsWith("Plan: Team, 12 seats\\u202E and a hidden end", StringComparison.Ordinal) && w.EndsWith('…'));
        Assert.DoesNotContain(words, w => w.Contains(Rlo, StringComparison.Ordinal));
        Assert.Equal([TabulaBridgeHost.DraftAllow, TabulaBridgeHost.NotNow], rig.View.Cards.Buttons.Select(b => AutomationProperties.GetName(b)).ToList());

        rig.View.Cards.Answer(1);
        rig.Layout();
        Assert.True(asked.IsCompletedSuccessfully && asked.Result);

        var refused = TabulaBridgeHost.Instance.ConfirmDraftAsync(address, "Plan: Team", CancellationToken.None);
        rig.Layout();
        rig.View.Cards.Answer(0);
        rig.Layout();
        Assert.True(refused.IsCompletedSuccessfully);
        Assert.False(refused.Result);
        Assert.Equal(TabulaBridgeHost.Declined, rig.View.StatusText);
        Assert.Empty(rig.Drafts);
    });

    [Fact]
    public void No_pane_showing_the_artifact_means_no_and_no_card() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig();
        rig.Show(ArtifactKind.Html, Page);
        var nobody = TabulaBridgeHost.Instance.ConfirmDraftAsync(new ArtifactAddress(TabulaRig.Neighbour, "other"), "Plan", CancellationToken.None);
        rig.Layout();
        Assert.True(nobody.IsCompletedSuccessfully);
        Assert.False(nobody.Result);
        Assert.Null(rig.View.Cards.Showing);
    });
}
