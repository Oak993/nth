using Avalonia.Input;
using Marrow.App.Tests.Fixtures;
using Xunit;

namespace Marrow.App.Tests.Views.Composer;

// ED-NEW-stutterfix-8: an OpenRouter picker's search folds the groups and rows its viewport can show while the fold runs; the others fold on
// hidden, a spacer standing for their height at each frame. Two pickers side by side, one folding every item as before: at every frame the
// viewport shows the same items at the same places, opacity and scale, with the same extent and the same pixels, through a narrowing search,
// a search changed while a fold runs, keys pressed and a scroll while it runs, and a cleared search; and the held picker lays out far fewer
// items per frame.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-stutterfix-8")]
public sealed class OpenRouterPickerFoldTests(HeadlessSession headless)
{
    private static void Run(Action<OpenRouterFoldPair> body)
    {
        using var pair = new OpenRouterFoldPair();
        Assert.Equal(OpenRouterFoldPair.Seen(pair.Plain), OpenRouterFoldPair.Seen(pair.Held));
        body(pair);
    }

    [Fact]
    public void A_narrowing_search_shows_the_same_frames_and_lays_out_only_the_items_it_can_show() => headless.OnUiThread(() => Run(pair =>
    {
        pair.Search("acme");
        var plain = 0; var held = 0;
        for (var frame = 0; frame < 40; frame++) { var (p, h) = pair.Frame(); plain += p; held += h; }
        Assert.True(held * 4 < plain, $"items resized per fold: {held} held, {plain} plain");
        pair.Same();
    }));

    [Fact]
    public void A_search_changed_while_a_fold_runs_goes_on_from_the_values_reached() => headless.OnUiThread(() => Run(pair =>
    {
        pair.Search("acme");
        for (var frame = 0; frame < 6; frame++) pair.Frame();
        pair.Search("bolt");
        for (var frame = 0; frame < 5; frame++) pair.Frame();
        pair.Search("m4");
        for (var frame = 0; frame < 40; frame++) pair.Frame();
        pair.Same();
    }));

    [Fact]
    public void Keys_pressed_while_a_fold_runs_move_through_the_same_rows() => headless.OnUiThread(() => Run(pair =>
    {
        pair.Search("acme");
        for (var frame = 0; frame < 4; frame++) pair.Frame();
        for (var i = 0; i < 30; i++) pair.Key(Key.Down); // past the first screen
        for (var frame = 0; frame < 40; frame++) pair.Frame();
        Assert.Equal(OpenRouterFoldPair.Scroll(pair.Plain).Offset, OpenRouterFoldPair.Scroll(pair.Held).Offset);
        pair.Same();
    }));

    [Fact]
    public void A_scroll_while_a_fold_runs_shows_the_same_frames() => headless.OnUiThread(() => Run(pair =>
    {
        pair.Search("acme");
        for (var frame = 0; frame < 3; frame++) pair.Frame();
        pair.ScrollTo(900);
        for (var frame = 0; frame < 40; frame++) pair.Frame();
        pair.Same();
    }));

    [Fact]
    public void A_cleared_search_unfolds_the_same_frames() => headless.OnUiThread(() => Run(pair =>
    {
        pair.Search("acme");
        for (var frame = 0; frame < 40; frame++) pair.Frame();
        pair.Search("");
        var plain = 0; var held = 0;
        for (var frame = 0; frame < 40; frame++) { var (p, h) = pair.Frame(); plain += p; held += h; }
        Assert.True(held * 2 < plain, $"items resized per unfold: {held} held, {plain} plain");
        pair.Same();
    }));
}
