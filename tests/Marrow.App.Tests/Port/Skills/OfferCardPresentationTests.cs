using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.Services.Port.Skills.Offers;

namespace Marrow.App.Tests.Port.Skills;

[Collection("Headless")]
public sealed class OfferCardPresentationTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-326")] // ONDEMAND 5.4 requires bytes and percent, independently of catalogue estimates.
    public void Downloading_shows_transferred_bytes_and_only_shows_percent_when_the_total_is_known() => OfferCardHarness.Run(headless, OfferCardHarness.Asked(), h =>
    {
        h.Source.Stage = OfferStage.Downloading;
        h.Source.Progress = new OfferProgress(OfferStage.Downloading, 1_000_000, 4_000_000);
        h.Settle();
        Assert.Contains("1 MB of 4 MB", h.Card.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        Assert.Equal("25%", h.Card.PercentText!.Text);
        h.Source.Progress = new OfferProgress(OfferStage.Downloading, 500_000, null);
        h.Settle();
        Assert.Contains("500 kB", h.Card.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        Assert.DoesNotContain("0%", h.Card.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text));
        Assert.Single(h.Card.GetVisualDescendants().OfType<Marrow.UI.Port.Nth.NthSpinner>(), s => s.IsEffectivelyVisible);
    });

    [Theory]
    [Trait("Decision", "ED-261")] // OD-0076: all content including the header must leave the layout through the fade/glide.
    [InlineData(OfferStage.Done)]
    [InlineData(OfferStage.Continuing)]
    [InlineData(OfferStage.Declined)]
    public void The_last_visible_frame_has_already_collapsed_the_whole_header_and_padding(OfferStage terminal) => OfferCardHarness.Run(headless, OfferCardHarness.Asked(), h =>
    {
        var original = h.Card.Bounds.Height;
        Assert.True(original > 20);
        h.Source.Stage = terminal;
        h.Settle();
        var last = original;
        var heights = new List<double>();
        for (var frame = 0; frame < 60 && h.Card.IsVisible; frame++)
        {
            last = h.Card.Bounds.Height;
            heights.Add(last);
            h.Step(10);
        }
        Assert.False(h.Card.IsVisible);
        Assert.True(last < 2, "The final hide cut " + last + " points of card/header height.");
        Assert.True(heights.Distinct().Count() > 5);
        Assert.Equal(heights.OrderByDescending(x => x), heights);
    }, motion: true);
}
