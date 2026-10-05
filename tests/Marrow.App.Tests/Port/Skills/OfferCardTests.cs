using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Skills.Detect;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Offers;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.Skills;

// PORT-ONLY (no Swift twin): SK-14 (DL-18, ONDEMAND-PLAN.md 5.4 to 5.6, OD-0099 Offer A / words S1, OD-0076). The in-chat offer card in a headless window over a scripted offer: asked, downloading, checking, done and continuing,
// declined, failed and Retry; every change transitioned on the frame clock and stilled by Reduce Motion; the card in the layout flow above the composer, never over its text; Return acts only with the card focused.
// The words are the locked design's (LibraryWords); the asked sentence, the why-needed line, the decline control and the done, continuing and declined captions are not in the repository's design sources, so the card
// has none (ED-NEW-cloud-sk-14). No download, no network, no provider run.
[Collection("Headless")]
public sealed class OfferCardTests(HeadlessSession headless)
{
    /// <summary>A click reaches nothing on a disabled button, as in the Settings pages' own harness.</summary>
    private static void Press(Button button)
    {
        if (button.IsEffectivelyEnabled) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static IEnumerable<string> Words(Control root) => root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)).Select(t => t.Text!);

    [Fact]
    [Trait("Decision", "ED-326")]
    public void Asked_names_the_item_and_its_source_and_offers_the_download_with_its_size_and_nothing_else() => OfferCardHarness.Run(headless, OfferCardHarness.Asked(), h =>
    {
        var item = h.Catalogue.Items.Single(i => i.Id == "T3");
        Assert.Equal("asked", h.Card.Kind);
        Assert.Equal(item.Title, h.Card.TitleText.Text);
        Assert.Equal(new Uri(item.SourceUrl!).Host, h.Card.HostText.Text);
        Assert.Equal(LibraryWords.Download(item.ApproxSizeMb), Marrow.App.Port.Skills.Pages.LibraryButtons.Words(h.Card.Primary!));
        Assert.True(h.Card.Primary!.IsEffectivelyEnabled);
        Assert.Null(h.Card.CancelButton);
        Assert.Single(h.Card.GetVisualDescendants().OfType<Button>()); // the decline control's words are not in the design sources: none is drawn
    });

    [Fact]
    [Trait("Decision", "ED-326")]
    public void Pressing_the_download_asks_the_offer_once() => OfferCardHarness.Run(headless, OfferCardHarness.Asked(), h =>
    {
        Press(h.Card.Primary!);
        Assert.Equal(1, h.Source.Accepts);
    });

    [Fact]
    [Trait("Decision", "ED-326")]
    public void Downloading_draws_the_bar_150_wide_as_the_design_pages_offer_state_rule_does() => OfferCardHarness.Run(headless, OfferCardHarness.Asked(), h =>
    {
        // design-src/skills-downloads/rounds/skills-downloads-v3-KBarrEq1.html:342 `.mw .ofs .bar{width:150px}` (the only offer-card geometry the pages still carry; also in v4 and the final page's CSS ancestors)
        h.Source.Stage = OfferStage.Downloading;
        h.Source.Progress = new OfferProgress(OfferStage.Downloading, 250, 1000);
        h.Settle();
        Assert.Equal(150, h.Card.Bar!.Width);
    });

    [Fact]
    [Trait("Decision", "ED-326")]
    public void Downloading_shows_the_bar_and_the_percent_that_only_move_forward_and_a_cancel() => OfferCardHarness.Run(headless, OfferCardHarness.Asked(), h =>
    {
        h.Source.Stage = OfferStage.Downloading;
        h.Source.Progress = new OfferProgress(OfferStage.Downloading, 250, 1000);
        h.Settle();
        Assert.Equal("downloading", h.Card.Kind);
        Assert.Equal("25%", h.Card.PercentText!.Text);
        Assert.NotNull(h.Card.Bar);
        Assert.Null(h.Card.Primary);
        h.Source.Progress = new OfferProgress(OfferStage.Downloading, 700, 1000);
        h.Settle();
        Assert.Equal("70%", h.Card.PercentText!.Text);
        Press(h.Card.CancelButton!);
        Assert.Equal(1, h.Source.Cancels);
    });

    [Fact]
    [Trait("Decision", "ED-326")]
    public void Checking_says_checking_with_no_button() => OfferCardHarness.Run(headless, OfferCardHarness.Asked(), h =>
    {
        h.Source.Stage = OfferStage.Downloading; h.Source.Progress = new OfferProgress(OfferStage.Downloading, 5, 10); h.Settle();
        h.Source.Stage = OfferStage.Checking; h.Source.Progress = new OfferProgress(OfferStage.Checking, 10, 10); h.Settle();
        Assert.Equal("checking", h.Card.Kind);
        Assert.Contains(LibraryWords.Checking, Words(h.Card));
        Assert.Empty(h.Card.GetVisualDescendants().OfType<Button>());
    });

    [Fact]
    [Trait("Decision", "ED-326")]
    public void A_failure_shows_its_reason_as_given_and_Retry_asks_the_offer_again() => OfferCardHarness.Run(headless, OfferCardHarness.Asked(), h =>
    {
        h.Source.Stage = OfferStage.Failed; h.Source.Failure = "Couldn't reach libreoffice.org."; h.Settle();
        Assert.Equal("failed", h.Card.Kind);
        Assert.Contains(LibraryWords.DidNotDownload, Words(h.Card));
        Assert.Equal("Couldn't reach libreoffice.org.", h.Card.FailureText!.Text);
        Assert.Equal(LibraryWords.Retry, Marrow.App.Port.Skills.Pages.LibraryButtons.Words(h.Card.Primary!));
        Press(h.Card.Primary!);
        Assert.Equal(1, h.Source.Accepts);
        h.Source.Stage = OfferStage.Downloading; h.Source.Failure = null; h.Source.Progress = new OfferProgress(OfferStage.Downloading, 0, 10); h.Settle();
        Assert.Equal("downloading", h.Card.Kind);
        Assert.Null(h.Card.FailureText);
    });

    [Theory]
    [Trait("Decision", "ED-326")]
    [InlineData(OfferStage.Done)]
    [InlineData(OfferStage.Continuing)]
    [InlineData(OfferStage.Declined)]
    public void Done_continuing_and_declined_leave_no_card_on_screen_because_the_design_draws_no_words_for_them(OfferStage stage) => OfferCardHarness.Run(headless, OfferCardHarness.Asked(), h =>
    {
        h.Source.Stage = stage; h.Settle();
        Assert.Equal("none", h.Card.Kind);
        Assert.False(h.Card.IsVisible);
        Assert.Empty(Words(h.Card));
    });

    [Fact]
    [Trait("Decision", "ED-326")]
    public void With_no_card_there_is_nothing_on_screen() => OfferCardHarness.Run(headless, new ScriptedOfferSource(), h =>
    {
        Assert.Equal("none", h.Card.Kind);
        Assert.False(h.Card.IsVisible);
    });

    [Fact]
    [Trait("Decision", "ED-326")]
    public void An_item_whose_terms_are_owed_keeps_the_download_off() => OfferCardHarness.Run(headless, new ScriptedOfferSource(), h =>
    {
        var owed = h.Catalogue.Items.First(i => ItemTerms.Required(i, AddonsState.Empty));
        h.Source.Card = new OfferCard([owed.Id], [owed.Id], owed.ApproxSizeMb);
        h.Settle();
        Assert.Equal("asked", h.Card.Kind);
        Assert.False(h.Card.Primary!.IsEffectivelyEnabled);
        Press(h.Card.Primary!);
        Assert.Equal(0, h.Source.Accepts);
    });

    [Fact]
    [Trait("Decision", "ED-326")]
    public void Return_acts_only_when_the_card_has_the_focus() => OfferCardHarness.Run(headless, OfferCardHarness.Asked(), h =>
    {
        h.Composer.Focus();
        h.Settle();
        h.Composer.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = h.Composer });
        Assert.Equal(0, h.Source.Accepts); // Return in the composer is the composer's
        h.Card.Focus();
        h.Settle();
        h.Card.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = h.Card });
        Assert.Equal(1, h.Source.Accepts);
    });

    [Fact]
    [Trait("Decision", "ED-326")]
    public void Return_does_nothing_while_the_primary_action_is_off_or_there_is_none() => OfferCardHarness.Run(headless, new ScriptedOfferSource(), h =>
    {
        var owed = h.Catalogue.Items.First(i => ItemTerms.Required(i, AddonsState.Empty));
        h.Source.Card = new OfferCard([owed.Id], [owed.Id], owed.ApproxSizeMb);
        h.Settle();
        h.Card.Focus();
        h.Card.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = h.Card });
        h.Source.Stage = OfferStage.Downloading; h.Source.Progress = new OfferProgress(OfferStage.Downloading, 1, 10); h.Settle();
        h.Card.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = h.Card });
        Assert.Equal(0, h.Source.Accepts);
    });

    [Fact]
    [Trait("Decision", "ED-326")]
    public void The_card_sits_in_the_layout_above_the_composer_and_never_covers_its_text() => OfferCardHarness.Run(headless, OfferCardHarness.Asked("T1", "T3"), h =>
    {
        Assert.Same(h.Column, h.Card.GetVisualParent());
        var card = h.Card.Bounds;
        var composer = h.Composer.Bounds;
        Assert.True(card.Height > 0);
        Assert.True(card.Bottom <= composer.Top + 0.001, "the card ends at " + card.Bottom + " and the composer starts at " + composer.Top);
        Assert.True(card.Right <= h.Column.Bounds.Width + 0.001);
        foreach (var stage in new[] { OfferStage.Downloading, OfferStage.Checking, OfferStage.Failed })
        {
            h.Source.Stage = stage; h.Source.Failure = "Couldn't reach libreoffice.org."; h.Source.Progress = new OfferProgress(stage, 3, 10);
            h.Settle();
            Assert.True(h.Card.Bounds.Bottom <= h.Composer.Bounds.Top + 0.001, stage + ": the card overlaps the composer");
            Assert.True(h.Card.Bounds.Right <= h.Column.Bounds.Width + 0.001, stage + ": the card is wider than its column");
        }
    });

    [Fact]
    [Trait("Decision", "ED-261")] // OD-0076: a change of state is a crossfade on the frame clock, never a cut
    public void A_change_of_state_crossfades_over_several_frames() => OfferCardHarness.Run(headless, OfferCardHarness.Asked(), h =>
    {
        var before = h.Card.Primary!.GetVisualAncestors().OfType<Control>().First(c => c.Parent is not null && c.Opacity <= 1);
        h.Source.Stage = OfferStage.Downloading; h.Source.Progress = new OfferProgress(OfferStage.Downloading, 1, 10);
        h.Settle();
        var incoming = h.Card.Bar!;
        var seen = new List<double>();
        for (var i = 0; i < 20; i++) { seen.Add(FadeOf(incoming)); h.Step(16); }
        Assert.True(seen.Distinct().Count() >= 5, "the change was a cut: " + string.Join(", ", seen));
        Assert.Equal(seen.OrderBy(v => v).ToList(), seen);
        Assert.Equal(1, FadeOf(incoming), 6);
        _ = before;
    }, motion: true);

    [Fact]
    [Trait("Decision", "ED-261")] // OD-0076 with Reduce Motion: movement is stilled to a short fade (ONDEMAND 5.6; the locked page's reduced-motion rule is .01 s), so the change lands within a frame or two
    public void With_Reduce_Motion_a_change_of_state_lands_within_a_short_fade() => OfferCardHarness.Run(headless, OfferCardHarness.Asked(), h =>
    {
        h.Source.Stage = OfferStage.Downloading; h.Source.Progress = new OfferProgress(OfferStage.Downloading, 1, 10);
        h.Window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("downloading", h.Card.Kind);
        h.Step(20);
        Assert.Equal(1, FadeOf(h.Card.Bar!), 6);
        Assert.Null(h.Card.Primary);
    }, motion: true, reduceMotion: true);

    private static double FadeOf(Control control) => control.GetVisualAncestors().Prepend(control).Select(c => c.Opacity).Aggregate(1.0, (a, b) => a * b);

    [Fact]
    [Trait("Decision", "ED-326")]
    public void When_the_offer_cannot_fetch_the_download_and_retry_are_off_and_the_unavailable_message_shows() => OfferCardHarness.Run(headless, OfferCardHarness.Asked(), h =>
    {
        h.Source.CanFetch = false;
        h.Settle();
        Assert.False(h.Card.Primary!.IsEffectivelyEnabled);
        Assert.Contains(LibraryWords.DownloadsUnavailable, Words(h.Card));
        h.Source.Stage = OfferStage.Failed; h.Source.Failure = "x"; h.Settle();
        Assert.False(h.Card.Primary!.IsEffectivelyEnabled);
        Press(h.Card.Primary!);
        Assert.Equal(0, h.Source.Accepts);
    });
}
