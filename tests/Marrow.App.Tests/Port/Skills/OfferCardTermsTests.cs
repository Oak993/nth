using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.Detect;
using Marrow.Core.Port.Skills.Terms;
using Marrow.Services.Port.Skills.Terms;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.Skills;

// PORT-ONLY (no Swift twin): SK-14 (ONDEMAND-PLAN.md 5.4: "Items with non-open terms add the terms step inside asked"; DL-15; the locked page's terms box). The offer card shows, for an item that owes terms, the same box the Settings
// detail shows (the row label, the pinned licence's text and the separate "I have read these terms" control), the Download waits for the acceptance, and the acceptance is the step's one record. Scripted source: no network.
[Collection("Headless")]
public sealed class OfferCardTermsTests(HeadlessSession headless)
{
    private const string Hash = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    /// <summary>The embedded catalogue with A2 (Anthropic's, under terms) given the licence file's hash it will be pinned with.</summary>
    private static SkillsCatalogue Catalogue()
    {
        var embedded = SkillsCatalogueLoader.LoadEmbedded();
        return embedded with { Items = embedded.Items.Select(i => i.Id == "A2" ? i with { LicenceFile = "LICENSE.txt", Files = [new CatalogueFile { Path = "LICENSE.txt", Size = 5, Sha256 = Hash }] } : i).ToArray() };
    }

    private static ScriptedOfferSource Owing()
    {
        var source = new ScriptedOfferSource { Card = new OfferCard(["A2"], ["A2"], 8.1) };
        source.Terms["A2"] = new TermsOffer("A2", "Anthropic example skills", SkillsNotices.UnderTerms("Anthropic"), "Proprietary", "https://example.test/licence", Hash, SkillsNotices.ReadControl);
        return source;
    }

    private static TermsBox Box(OfferCardHarness h) => h.Card.GetVisualDescendants().OfType<TermsBox>().Single();

    [Fact]
    [Trait("Decision", "ED-326")]
    public void An_item_that_owes_terms_shows_the_box_with_the_label_the_text_and_the_read_control_and_the_download_waits() => OfferCardHarness.Run(headless, Owing(), h =>
    {
        var box = Box(h);
        Assert.Contains(SkillsNotices.UnderTerms("Anthropic"), box.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        Assert.Equal("Marrow-made licence text of A2", box.LicenceTextShown);
        Assert.True(box.ReadEnabled);
        Assert.False(h.Card.Primary!.IsEffectivelyEnabled);
    }, catalogue: Catalogue());

    [Fact]
    [Trait("Decision", "ED-326")]
    public void The_read_control_stays_off_until_the_text_is_on_screen_and_a_text_that_fails_to_load_says_so() => OfferCardHarness.Run(headless, Owing(), h =>
    {
        h.Source.Licence = _ => Task.FromException<string>(new InvalidOperationException("Licence text seam is not bound."));
        h.Source.Card = new OfferCard(["A2"], ["A2"], 9); // a card that changed: its box is made again
        h.Settle();
        var box = Box(h);
        Assert.False(box.ReadEnabled);
        Assert.Equal("Licence text seam is not bound.", box.ProblemShown);
    }, catalogue: Catalogue());

    [Fact]
    [Trait("Decision", "ED-326")]
    public void Accepting_stores_the_acceptance_through_the_source_and_the_download_comes_on_and_the_box_goes() => OfferCardHarness.Run(headless, Owing(), h =>
    {
        h.Source.OnAcceptTerms = id => h.State = h.State.AcceptTerms(Hash, id, new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero));
        var box = Box(h);
        box.ReadControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        h.Settle();
        Assert.Equal(["A2"], h.Source.Accepted);
        Assert.Empty(h.Card.GetVisualDescendants().OfType<TermsBox>());
        Assert.True(h.Card.Primary!.IsEffectivelyEnabled);
        Assert.Equal(0, h.Source.Accepts); // the download is still the user's yes
    }, catalogue: Catalogue());

    [Fact]
    [Trait("Decision", "ED-326")]
    public void An_acceptance_the_step_refuses_shows_the_reason_and_the_download_stays_off() => OfferCardHarness.Run(headless, Owing(), h =>
    {
        h.Source.OnAcceptTerms = _ => throw new InvalidOperationException("The licence is not pinned yet.");
        Box(h).ReadControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        h.Settle();
        Assert.Equal("The licence is not pinned yet.", Box(h).ProblemShown);
        Assert.False(h.Card.Primary!.IsEffectivelyEnabled);
    }, catalogue: Catalogue());

    [Fact]
    [Trait("Decision", "ED-326")]
    public void An_item_with_open_terms_has_no_box() => OfferCardHarness.Run(headless, OfferCardHarness.Asked(), h =>
    {
        Assert.Empty(h.Card.GetVisualDescendants().OfType<TermsBox>());
    });
}
