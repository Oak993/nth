// PORT-ONLY: first-release feature unit SK-14 (DL-18, skills-ONDEMAND-PLAN.md 5.4 and 5.6; OD-0099 Offer A, words S1). What the in-chat offer card shows in each state, as a value: no Avalonia, no network.
// The words are the ones the locked skills-downloads design draws for the same states (LibraryWords); the asked sentence, the why-needed line, the decline label and the done, continuing and declined
// captions are not in the repository's design sources, so no view carries one (ED-384).
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.Detect;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Offers;

namespace Marrow.Services.Tests.Port.Skills.Offers;

[Trait("Feature", "SK-14")]
[Trait("Spec", "DL-18")]
[Trait("Rule", "R-TEST-4")]
public sealed class OfferCardProjectorTests
{
    private static readonly SkillsCatalogue Catalogue = SkillsCatalogueLoader.LoadEmbedded();

    private static OfferCard Card(params string[] items) => new(items.TakeLast(1).ToArray(), items, items.Select(id => Catalogue.Items.Single(i => i.Id == id).ApproxSizeMb).Sum());

    private static OfferCardView Show(OfferStage stage, OfferProgress? progress = null, string? failure = null, AddonsState? state = null, params string[] items) =>
        OfferCardProjector.Project(Catalogue, state ?? AddonsState.Empty, Card(items.Length == 0 ? ["T3"] : items), stage, progress, failure)!;

    [Fact]
    public void No_card_shows_nothing() => Assert.Null(OfferCardProjector.Project(Catalogue, AddonsState.Empty, null, OfferStage.Asked, null, null));

    [Fact]
    public void Asked_names_the_items_and_their_source_and_offers_the_download_with_its_size_and_a_decline_but_no_cancel()
    {
        var view = Show(OfferStage.Asked);
        var item = Catalogue.Items.Single(i => i.Id == "T3");
        Assert.Equal([item.Title], view.Titles);
        Assert.Equal(item.ApproxSizeMb, view.SizeMb);
        Assert.Equal([new Uri(item.SourceUrl!).Host], view.Hosts);
        Assert.Equal(LibraryWords.Download(item.ApproxSizeMb), view.PrimaryLabel);
        Assert.True(view.PrimaryEnabled);
        Assert.True(view.CanDecline);
        Assert.False(view.CanCancel);
        Assert.Null(view.Percent);
        Assert.Null(view.Status);
    }

    [Fact]
    public void A_card_of_several_items_lists_them_dependencies_first_with_the_combined_size_and_each_host_once()
    {
        var view = Show(OfferStage.Asked, null, null, null, "T1", "T3");
        Assert.Equal(new[] { "T1", "T3" }.Select(id => Catalogue.Items.Single(i => i.Id == id).Title), view.Titles);
        Assert.Equal(new[] { "T1", "T3" }.Sum(id => Catalogue.Items.Single(i => i.Id == id).ApproxSizeMb), view.SizeMb);
        Assert.Equal(view.Hosts.Distinct(StringComparer.Ordinal).ToArray(), view.Hosts);
    }

    [Fact]
    public void Downloading_shows_the_bytes_and_the_percent_and_a_cancel_and_no_download_button()
    {
        var view = Show(OfferStage.Downloading, new OfferProgress(OfferStage.Downloading, 333, 1000));
        Assert.Equal((33, 333L, 1000L), (view.Percent, view.BytesDone, view.BytesTotal));
        Assert.True(view.CanCancel);
        Assert.Null(view.PrimaryLabel);
        Assert.False(view.CanDecline);
    }

    [Fact]
    public void A_total_of_zero_has_no_percent_and_the_percent_never_passes_a_hundred()
    {
        Assert.Null(Show(OfferStage.Downloading, new OfferProgress(OfferStage.Downloading, 0, null)).Percent);
        Assert.Equal(100, Show(OfferStage.Downloading, new OfferProgress(OfferStage.Downloading, 5, 5)).Percent);
    }

    [Fact]
    public void Checking_says_checking_with_no_cancel_and_no_buttons()
    {
        var view = Show(OfferStage.Checking, new OfferProgress(OfferStage.Checking, 1000, 1000));
        Assert.Equal(LibraryWords.Checking, view.Status);
        Assert.False(view.CanCancel);
        Assert.Null(view.PrimaryLabel);
        Assert.False(view.CanDecline);
    }

    [Fact]
    public void Failed_keeps_the_reason_as_given_and_offers_retry_and_a_decline()
    {
        var view = Show(OfferStage.Failed, failure: "Couldn't reach libreoffice.org.");
        Assert.Equal((LibraryWords.DidNotDownload, "Couldn't reach libreoffice.org.", LibraryWords.Retry), (view.Status, view.Failure, view.PrimaryLabel));
        Assert.True(view.PrimaryEnabled);
        Assert.True(view.CanDecline);
    }

    [Theory]
    [InlineData(OfferStage.Done)]
    [InlineData(OfferStage.Continuing)]
    [InlineData(OfferStage.Declined)]
    public void Done_continuing_and_declined_carry_no_words_no_buttons_and_no_cancel(OfferStage stage)
    {
        var view = Show(stage);
        Assert.Equal((null, null, false, false, false), (view.Status, view.PrimaryLabel, view.PrimaryEnabled, view.CanCancel, view.CanDecline));
    }

    [Fact]
    public void Asked_and_failed_carry_the_asked_sentence_the_why_line_and_the_decline_label_and_nothing_else_does()
    {
        var item = Catalogue.Items.Single(i => i.Id == "T3");
        OfferCardView With(OfferStage stage) => OfferCardProjector.Project(Catalogue, AddonsState.Empty, Card("T3"), stage, stage is OfferStage.Downloading or OfferStage.Checking ? new OfferProgress(stage, 1, 2) : null, "x", why: "`soffice` can't run without it.")!;
        foreach (var stage in new[] { OfferStage.Asked, OfferStage.Failed })
        {
            var view = With(stage);
            Assert.Equal((OfferWords.Asked([item.Title]), "`soffice` can't run without it.", OfferWords.DeclineLabel), (view.Sentence, view.Why, view.DeclineLabel));
        }
        foreach (var stage in new[] { OfferStage.Downloading, OfferStage.Checking })
            Assert.Equal((null, null, null), (With(stage).Sentence, With(stage).Why, With(stage).DeclineLabel));
    }

    [Fact]
    public void Done_continuing_and_declined_carry_their_caption_and_the_other_states_none()
    {
        var item = Catalogue.Items.Single(i => i.Id == "T3");
        Assert.Equal(OfferWords.Done([item.Title]), Show(OfferStage.Done).Caption);
        Assert.Equal(OfferWords.Continuing([item.Title]), Show(OfferStage.Continuing).Caption);
        Assert.Equal(OfferWords.Declined([item.Title], [item.Kind]), Show(OfferStage.Declined).Caption);
        foreach (var stage in new[] { OfferStage.Asked, OfferStage.Downloading, OfferStage.Checking, OfferStage.Failed })
            Assert.Null(Show(stage, stage is OfferStage.Downloading or OfferStage.Checking ? new OfferProgress(stage, 1, 2) : null, "x").Caption);
    }

    [Fact]
    public void An_item_whose_terms_are_owed_keeps_the_card_asked_with_the_download_off()
    {
        var owed = Catalogue.Items.First(i => i.TermsClass != TermsClass.Open && ItemTerms.Required(i, AddonsState.Empty));
        var view = OfferCardProjector.Project(Catalogue, AddonsState.Empty, new OfferCard([owed.Id], [owed.Id], owed.ApproxSizeMb), OfferStage.Asked, null, null)!;
        Assert.True(view.TermsOwed);
        Assert.False(view.PrimaryEnabled);
        Assert.Equal(LibraryWords.Download(owed.ApproxSizeMb), view.PrimaryLabel);
    }

    [Fact]
    public void An_unknown_size_leaves_the_download_label_without_one()
    {
        var view = OfferCardProjector.Project(Catalogue, AddonsState.Empty, new OfferCard(["T3"], ["T3"], null), OfferStage.Asked, null, null)!;
        Assert.Null(view.SizeMb);
        Assert.Equal(LibraryWords.Download(null), view.PrimaryLabel);
    }

    [Theory]
    [InlineData(OfferStage.Asked)]
    [InlineData(OfferStage.Failed)]
    public void When_nothing_can_be_fetched_the_primary_action_is_off_and_the_one_unavailable_message_says_why(OfferStage stage)
    {
        var view = OfferCardProjector.Project(Catalogue, AddonsState.Empty, Card("T3"), stage, null, "x", canFetch: false)!;
        Assert.False(view.PrimaryEnabled);
        Assert.NotNull(view.PrimaryLabel);
        Assert.Equal(LibraryWords.DownloadsUnavailable, view.Note);
    }

    [Fact]
    public void While_it_downloads_the_availability_adds_no_note()
    {
        var view = OfferCardProjector.Project(Catalogue, AddonsState.Empty, Card("T3"), OfferStage.Downloading, new OfferProgress(OfferStage.Downloading, 1, 2), null, canFetch: false)!;
        Assert.Null(view.Note);
    }
}
