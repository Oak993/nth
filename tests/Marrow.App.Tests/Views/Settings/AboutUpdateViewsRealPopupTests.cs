using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/AboutUpdateViews.swift:241-251 (.popover(isPresented:, arrowEdge: .top) on each release-section card) and :255-301
// (the popover). The cards, their real Popup on Avalonia's overlay host and real pointer input (SettingsRestPopupPresenter): the
// open above the card with its content already there on the first open, a wheel over a long list, light dismissal by a press outside, and a
// press on the open popover's own card. The recording presenter of the other About tests cannot show any of this.
[Collection("Headless")]
public sealed class AboutUpdateViewsRealPopupTests(HeadlessSession headless) : SettingsRestFixture(headless)
{
    private readonly IPopoverPresenter production = Presentation.Presenter.Current; // the port's own presenter, before the fixture swaps it
    private static UpdateReleaseSectionCards Cards(Control root) => Nodes<UpdateReleaseSectionCards>(root).Single();

    private static ReleaseNoteCategory? Presented(UpdateReleaseSectionCards cards)
    {
        var state = typeof(UpdateReleaseSectionCards).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cards)!;
        return (ReleaseNoteCategory?)state.GetType().GetProperty("Presented")!.GetValue(state);
    }

    private static Control Make()
    {
        var sections = ReleaseNoteCategoryExtensions.AllCases.Select((category, index) => new UpdateReleaseNotesDigest.Section
        {
            Category = category,
            Bullets = new ValueList<string>(Enumerable.Range(1, index == 0 ? 40 : 2).Select(number => $"Change number {number}, long enough to need more than one line in a popover that is 320 points wide.")),
            RollupCount = 0,
        }).ToArray();
        var cards = new UpdateReleaseSectionCards(new UpdateReleaseNotesDigest { Sections = new ValueList<UpdateReleaseNotesDigest.Section>(sections) }, "9.0.0");
        return new Border { Margin = new Thickness(0, 420, 0, 0), VerticalAlignment = VerticalAlignment.Top, Child = cards }; // low in the window: room above the cards
    }

    [Fact]
    [Trait("Req", "UI-UPDATE-015")] // AboutUpdateViews.swift:241-251, :255-301 on a real popup and real input
    public void A_release_card_opens_its_popover_above_itself_with_its_content_scrolls_it_and_a_press_outside_or_on_the_card_closes_it() => With(Make, (view, _, h, window) =>
    {
        var recorder = new SettingsRestPopupRecorder(production);
        using var presentation = Presentation.Presenter.Override(recorder);
        var cards = Cards(view);
        var card = cards.GetVisualDescendants().OfType<Button>().First();
        Assert.Null(Presented(cards));

        // Open: above the card, its content there on the first open.
        Click(window, card);
        Tick(h, 1000); // the popover grows in
        Drain(window);
        var popup = Assert.Single(recorder.Popups);
        Assert.True(popup.IsOpen);
        Assert.Equal(ReleaseNoteCategory.NewFeatures, Presented(cards));
        var content = Assert.IsAssignableFrom<Control>(popup.Child);
        Assert.Equal(PlacementMode.Top, popup.Placement); // :244 arrowEdge: .top
        var texts = Nodes<TextBlock>(content).Select(text => text.Text).ToArray();
        Assert.Contains("New features", texts);
        Assert.Contains("9.0.0", texts);
        Assert.Contains(texts, text => text is not null && text.StartsWith("Change number 1,", StringComparison.Ordinal));
        var top = content.TranslatePoint(new Point(0, 0), window)!.Value;
        var cardTop = card.TranslatePoint(new Point(0, 0), window)!.Value;
        Assert.True(top.Y + content.Bounds.Height <= cardTop.Y + 1, $"the popover ({top.Y} + {content.Bounds.Height}) lies above the card (top {cardTop.Y})");

        // Scroll: the 40 bullets are taller than the 320 point cap, and a wheel over them moves them.
        var scroll = Assert.Single(Nodes<ScrollViewer>(content));
        Assert.Equal(320, scroll.Viewport.Height);
        Assert.True(scroll.Extent.Height > 320, $"extent {scroll.Extent}");
        var inside = scroll.TranslatePoint(new Point(scroll.Bounds.Width / 2, scroll.Bounds.Height / 2), window)!.Value;
        window.MouseMove(inside);
        window.MouseWheel(inside, new Vector(0, -3));
        Drain(window);
        Assert.True(scroll.Offset.Y > 0, $"the wheel scrolled the list (offset {scroll.Offset})");

        // A press outside closes it and clears the presented card.
        var outside = new Point(4, 4);
        Assert.IsType<Border>(window.Content).Background = Avalonia.Media.Brushes.Transparent;
        Drain(window); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Assert.NotNull(window.InputHitTest(outside));
        Assert.False(new Rect(top, content.Bounds.Size).Contains(outside));
        window.MouseDown(outside, Avalonia.Input.MouseButton.Left);
        window.MouseUp(outside, Avalonia.Input.MouseButton.Left);
        Drain(window);
        Assert.True(popup.IsOpen); // T45: preserve the closing frames rather than cutting the panel away
        Tick(h, 200);
        Drain(window);
        Assert.False(popup.IsOpen);
        Assert.Null(Presented(cards));

        // The next tap on the card opens it again.
        Click(window, card);
        Tick(h, 1000);
        Drain(window);
        Assert.True(popup.IsOpen);
        Assert.Equal(ReleaseNoteCategory.NewFeatures, Presented(cards));

        // A press on the open popover's own card closes it, and the card's own tap does not open it again.
        Click(window, card);
        Tick(h, 1000);
        Drain(window);
        Assert.False(popup.IsOpen);
        Assert.Null(Presented(cards));

        // and the tap after that opens it.
        Click(window, card);
        Tick(h, 1000);
        Drain(window);
        Assert.True(popup.IsOpen);
        Assert.Equal(ReleaseNoteCategory.NewFeatures, Presented(cards));
    }, nativePopup: true);
}
