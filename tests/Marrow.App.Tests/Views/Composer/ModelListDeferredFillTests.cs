using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Xunit;
using static Marrow.App.Tests.Views.Composer.EffortSliderModelListTests;

namespace Marrow.App.Tests.Views.Composer;

// ED-NEW-stutterfix-2: a long model list opens with its first screens of rows and adds the rest a slice per frame, its scroll
// extent exact from the first frame; a search, a key press or a scroll into the rows still to come realises them first.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-stutterfix-2")]
public sealed class ModelListDeferredFillTests(HeadlessSession headless)
{
    // No chosen model list: every ready provider's models (ModelCatalog.Entries, PR-COMPOSER-140), 486 Codex ones among them.
    private static EffortListEnv Long() => Make(() => Thread(ProviderKind.Codex, "gpt-0"), null,
        (ProviderKind.Codex, Enumerable.Range(0, 486).Select(i => Option("gpt-" + i, "Model " + i)).ToArray()));

    private static ScrollViewer Scroll(Control list) => Assert.Single(list.GetVisualDescendants().OfType<ScrollViewer>());

    private sealed record Shape(IReadOnlyList<ModelCatalog.Entry> Entries, double Height)
    {
        public int Count => Entries.Count;
        public double Pitch => Height + 1; // rows 1 apart
        public double Extent => Count * Pitch - 1;
        public IEnumerable<string> Names(int start, int count) => Entries.Skip(start).Take(count).Select(e => e.Option.ShortName);
    }

    private static Shape ShapeOf(EffortListEnv env, Control list)
    {
        var entries = ModelCatalog.Entries(env.Model, env.Thread);
        Assert.True(entries.Count >= 486, $"{entries.Count} entries");
        return new Shape(entries, Models(list)[0].Bounds.Height);
    }

    [Fact]
    public void A_long_list_opens_with_its_first_screens_and_fills_in_over_frames_at_the_same_extent() => headless.OnUiThread(() =>
    {
        using var env = Long();
        var list = new ModelList(env.Thread, false, _ => { }, () => { });
        var window = env.Show(list);
        window.UpdateLayout();
        var shape = ShapeOf(env, list);
        var first = Models(list);
        Assert.InRange(first.Length, (int)Math.Ceiling(440 / shape.Pitch), shape.Count - 1);
        Assert.Equal(shape.Names(0, first.Length), first.Select(Name));
        Assert.Equal(shape.Extent, Scroll(list).Extent.Height, 0.5);
        for (var frame = 0; frame < 600 && Models(list).Length < shape.Count; frame++)
        {
            env.Step(16); window.UpdateLayout();
            Assert.Equal(shape.Extent, Scroll(list).Extent.Height, 0.5);
        }
        Assert.Equal(shape.Names(0, shape.Count), Models(list).Select(Name));
        Assert.All(Models(list), row => Assert.Equal(shape.Height, row.Bounds.Height, 0.5));
        Assert.Equal(shape.Count, Assert.IsType<StackPanel>(Scroll(list).Content).Children.Count); // the spacer is gone
        Assert.Equal(shape.Extent, Scroll(list).Extent.Height, 0.5);
    });

    [Fact]
    public void A_search_realises_every_row_before_it_filters() => headless.OnUiThread(() =>
    {
        using var env = Long();
        var list = new ModelList(env.Thread, false, _ => { }, () => { });
        var window = env.Show(list);
        var shape = ShapeOf(env, list);
        Assert.True(Models(list).Length < shape.Count);
        var search = Assert.Single(list.GetVisualDescendants().OfType<TextBox>());
        search.Text = "model 485"; Dispatcher.UIThread.RunJobs(); env.Step(500); window.UpdateLayout();
        Assert.Equal(shape.Count, Models(list).Length);
        Assert.Equal(["Model 485"], Models(list).Where(r => r.IsVisible).Select(Name));
        search.Text = ""; Dispatcher.UIThread.RunJobs(); env.Step(500); window.UpdateLayout();
        Assert.Equal(shape.Count, Models(list).Count(r => r.IsVisible));
        Assert.Equal(shape.Extent, Scroll(list).Extent.Height, 0.5);
    });

    [Fact]
    public void Keyboard_navigation_reaches_and_scrolls_to_rows_not_yet_realised() => headless.OnUiThread(() =>
    {
        using var env = Long();
        string? chosen = null;
        var list = new ModelList(env.Thread, false, entry => chosen = entry.Id, () => { });
        var window = env.Show(list);
        var shape = ShapeOf(env, list);
        Assert.True(Models(list).Length < 60);
        var search = Assert.Single(list.GetVisualDescendants().OfType<TextBox>());
        for (var i = 0; i < 60; i++) search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down });
        env.Step(500); window.UpdateLayout();
        var scroll = Scroll(list);
        // the 61st row (index 60) is brought to the bottom edge: its top at 60 rows down, its bottom at the viewport's foot
        Assert.Equal(60 * shape.Pitch + shape.Height - scroll.Viewport.Height, scroll.Offset.Y, 0.5);
        search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Assert.Equal(shape.Entries[60].Id, chosen);
    });

    [Fact]
    public void Scrolling_past_the_realised_rows_realises_the_rows_it_reaches() => headless.OnUiThread(() =>
    {
        using var env = Long();
        var list = new ModelList(env.Thread, false, _ => { }, () => { });
        var window = env.Show(list);
        var shape = ShapeOf(env, list);
        var scroll = Scroll(list);
        scroll.Offset = new Vector(0, 5000); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); env.Step(16); window.UpdateLayout();
        Assert.Equal(5000, scroll.Offset.Y, 0.5);
        Assert.True(Models(list).Length * shape.Pitch >= 5000 + scroll.Viewport.Height, $"{Models(list).Length} rows realised under a viewport at 5000");
        var shown = Models(list).Where(r => r.TranslatePoint(default, (Visual)scroll.Content!)!.Value.Y is var y && y + shape.Height > 5000 && y < 5000 + scroll.Viewport.Height).Select(Name).ToArray();
        Assert.True(shown.Length >= (int)(scroll.Viewport.Height / shape.Pitch), $"{shown.Length} rows in the viewport");
        Assert.Equal(shape.Names((int)(5000 / shape.Pitch), shown.Length), shown);
        Assert.Equal(shape.Extent, scroll.Extent.Height, 0.5);
    });

    [Fact]
    public void A_list_of_a_few_screens_is_built_whole_as_before() => headless.OnUiThread(() =>
    {
        using var env = Make(() => Thread(ProviderKind.Codex, "gpt-0"), model =>
        {
            for (var i = 0; i < 15; i++) model.Settings.AddToModelList(Pin(ProviderKind.Codex, "gpt-" + i));
        }, (ProviderKind.Codex, Enumerable.Range(0, 15).Select(i => Option("gpt-" + i, "Model " + i)).ToArray()));
        var list = new ModelList(env.Thread, false, _ => { }, () => { });
        env.Show(list);
        Assert.Equal(15, Models(list).Length);
        Assert.Equal(15, Assert.IsType<StackPanel>(Scroll(list).Content).Children.Count);
    });
}
