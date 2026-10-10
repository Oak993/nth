using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Xunit;
using static Marrow.App.Tests.Views.Composer.EffortSliderModelListTests;

namespace Marrow.App.Tests.Views.Composer;

// ED-NEW-stutterfix-8: a search folds the rows the viewport can show while the fold runs; rows it can never show take their end state when
// the fold ends, a spacer standing for their height at each frame. Two lists side by side, one folding every row as before: at every frame the
// viewport shows the same rows at the same places, with the same extent and the same pixels, through a narrowing search, a search typed while a
// fold runs, keys pressed while it runs, and a cleared search; and the held list lays out far fewer rows per frame.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-stutterfix-8")]
public sealed class ModelListSearchFoldTests(HeadlessSession headless)
{
    private static EffortListEnv Long() => Make(() => Thread(ProviderKind.Codex, "gpt-0"), null,
        (ProviderKind.Codex, Enumerable.Range(0, 486).Select(i => Option("gpt-" + i, "Model " + i)).ToArray()));

    // Every row, seen or not, ends where the plain fold leaves it.
    private static void Same(ModelListFoldPair pair)
    {
        var plain = Models(pair.Plain); var held = Models(pair.Held);
        Assert.Equal(plain.Length, held.Length);
        for (var i = 0; i < plain.Length; i++)
        {
            Assert.Equal(Name(plain[i]), Name(held[i]));
            Assert.Equal((plain[i].Height, plain[i].Opacity, plain[i].IsVisible, plain[i].IsHitTestVisible), (held[i].Height, held[i].Opacity, held[i].IsVisible, held[i].IsHitTestVisible));
        }
    }

    private static void Run(Action<ModelListFoldPair> body)
    {
        using var env = Long();
        var plain = new ModelList(env.Thread, false, _ => { }, () => { }) { Width = 330 }; // the panel's width (EffortSliderPanel)
        var held = new ModelList(env.Thread, false, _ => { }, () => { }) { Width = 330 };
        var window = env.Show(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { plain, held } }, 680);
        for (int i = 0, still = 0, last = -1; i < 600 && still < 20; i++)
        {
            env.Step(16); window.UpdateLayout();
            var now = Models(plain).Length + Models(held).Length; still = now == last ? still + 1 : 0; last = now;
        }
        Assert.Equal(Models(plain).Length, Models(held).Length);
        Assert.Equal(ModelListFoldPair.Seen(plain), ModelListFoldPair.Seen(held));
        body(new ModelListFoldPair(env, window, plain, held));
    }

    [Fact]
    public void A_narrowing_search_shows_the_same_frames_and_lays_out_only_the_rows_it_can_show() => headless.OnUiThread(() => Run(pair =>
    {
        pair.Search("model 4");
        var plain = 0; var held = 0;
        for (var frame = 0; frame < 30; frame++) { var (p, h) = pair.Frame(); plain += p; held += h; }
        Assert.True(held * 4 < plain, $"rows resized per fold: {held} held, {plain} plain");
        Same(pair);
    }));

    [Fact]
    public void A_search_typed_while_a_fold_runs_goes_on_from_the_values_reached() => headless.OnUiThread(() => Run(pair =>
    {
        pair.Search("model 4");
        for (var frame = 0; frame < 6; frame++) pair.Frame();
        pair.Search("model 48");
        for (var frame = 0; frame < 4; frame++) pair.Frame();
        pair.Search("model");
        for (var frame = 0; frame < 30; frame++) pair.Frame();
        Same(pair);
    }));

    [Fact]
    public void Keys_pressed_while_a_fold_runs_move_through_the_same_rows() => headless.OnUiThread(() => Run(pair =>
    {
        pair.Search("model 1");
        for (var frame = 0; frame < 4; frame++) pair.Frame();
        for (var i = 0; i < 30; i++) pair.Key(Key.Down); // past the first screen
        for (var frame = 0; frame < 40; frame++) pair.Frame();
        Assert.Equal(ModelListFoldPair.Scroll(pair.Plain).Offset, ModelListFoldPair.Scroll(pair.Held).Offset);
    }));

    [Fact]
    public void A_cleared_search_unfolds_the_same_frames() => headless.OnUiThread(() => Run(pair =>
    {
        pair.Search("model 9");
        for (var frame = 0; frame < 30; frame++) pair.Frame();
        pair.Search("");
        for (var frame = 0; frame < 30; frame++) pair.Frame();
        Same(pair);
    }));
}
