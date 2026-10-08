using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Port.ModelsPalette;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Xunit;
using static Marrow.App.Tests.Views.Composer.EffortSliderModelListTests;

namespace Marrow.App.Tests.Views.Composer;

// Owner T46: search by model or maker on the actual non-router list, preserving the history guard.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-pickerpill-1")]
public sealed class ModelPickerSearchTests(HeadlessSession headless)
{
    [Fact]
    public void Empty_search_enter_keeps_the_checked_model_until_navigation_starts() => headless.OnUiThread(() =>
    {
        using var env = Make(() => Thread(ProviderKind.Codex, "gpt-b"), model =>
        {
            model.Settings.AddToModelList(Pin(ProviderKind.Codex, "gpt-a"));
            model.Settings.AddToModelList(Pin(ProviderKind.Codex, "gpt-b"));
        }, (ProviderKind.Codex, [Option("gpt-a", "Alpha"), Option("gpt-b", "Beta")]));
        string? chosen = null;
        var list = new ModelList(env.Thread, false, entry => chosen = entry.Id, () => { });
        var window = env.Show(list);
        try
        {
            var search = Assert.Single(list.GetVisualDescendants().OfType<TextBox>());
            search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Null(chosen);
            search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Up });
            env.Step(500); window.UpdateLayout();
            search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Equal("codex/gpt-a", chosen);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Arrow_navigation_paints_the_active_row_before_a_query_is_entered() => headless.OnUiThread(() =>
    {
        using var env = Make(() => Thread(ProviderKind.Codex, "gpt-a"), model =>
        {
            model.Settings.AddToModelList(Pin(ProviderKind.Codex, "gpt-a"));
            model.Settings.AddToModelList(Pin(ProviderKind.Codex, "gpt-b"));
        }, (ProviderKind.Codex, [Option("gpt-a", "Alpha"), Option("gpt-b", "Beta")]));
        var list = new ModelList(env.Thread, false, _ => { }, () => { });
        var window = env.Show(list);
        try
        {
            var search = Assert.Single(list.GetVisualDescendants().OfType<TextBox>());
            search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down });
            env.Step(500); window.UpdateLayout();
            var plate = Assert.IsType<Border>(RowButton(ModelRow(list, "Beta")).Content);
            Assert.True(Assert.IsAssignableFrom<ISolidColorBrush>(plate.Background).Color.A > 0);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Codex_list_searches_maker_and_model_and_keys_choose_only_an_enabled_result() => headless.OnUiThread(() =>
    {
        using var env = Make(() => Thread(ProviderKind.Codex, "gpt-a"), model =>
        {
            model.Settings.AddToModelList(Pin(ProviderKind.Claude, "claude-opus"));
            model.Settings.AddToModelList(Pin(ProviderKind.Codex, "gpt-a"));
            model.Settings.AddToModelList(Pin(ProviderKind.Codex, "gpt-b"));
        }, (ProviderKind.Codex, [Option("gpt-a", "Alpha"), Option("gpt-b", "Beta")]),
            (ProviderKind.Claude, [Option("claude-opus", "Opus")]));
        string? chosen = null;
        var list = new ModelList(env.Thread, true, entry => chosen = entry.Id, () => { });
        var window = env.Show(list);
        try
        {
            var search = Assert.Single(list.GetVisualDescendants().OfType<TextBox>());
            search.Text = "ANTHROPIC"; Dispatcher.UIThread.RunJobs(); env.Step(500); window.UpdateLayout();
            Assert.Equal(["Opus"], Models(list).Where(r => r.IsVisible).Select(Name));
            search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Null(chosen);
            search.Text = "openai"; Dispatcher.UIThread.RunJobs(); env.Step(500); window.UpdateLayout();
            Assert.Equal(["Alpha", "Beta"], Models(list).Where(r => r.IsVisible).Select(Name));
            var alpha = ModelRow(list, "Alpha");
            var alphaPlate = Assert.IsType<Border>(RowButton(alpha).Content);
            Assert.True(Assert.IsAssignableFrom<ISolidColorBrush>(alphaPlate.Background).Color.A > 0, "the keyed highlight is painted on the row plate");
            Assert.All(Models(list), r => Assert.True(r.ClipToBounds));
            search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down });
            search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Equal("codex/gpt-b", chosen);
            search.Text = "alpha"; Dispatcher.UIThread.RunJobs(); env.Step(500); window.UpdateLayout();
            Assert.Equal(["Alpha"], Models(list).Where(r => r.IsVisible).Select(Name));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Search_survives_catalogue_refresh_and_key_navigation_scrolls_smoothly() => headless.OnUiThread(() =>
    {
        using var env = Make(() => Thread(ProviderKind.Codex, "gpt-0"), model =>
        {
            for (var i = 0; i < 30; i++) model.Settings.AddToModelList(Pin(ProviderKind.Codex, "gpt-" + i));
        }, (ProviderKind.Codex, Enumerable.Range(0, 30).Select(i => Option("gpt-" + i, "Model " + i)).ToArray()));
        var list = new ModelList(env.Thread, false, _ => { }, () => { });
        var window = env.Show(list);
        try
        {
            var search = Assert.Single(list.GetVisualDescendants().OfType<TextBox>());
            search.Text = "openai"; Dispatcher.UIThread.RunJobs(); env.Step(500); window.UpdateLayout();
            var scroll = Assert.Single(list.GetVisualDescendants().OfType<ScrollViewer>());
            for (var i = 0; i < 18; i++) search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down });
            Assert.Equal(0, scroll.Offset.Y);
            env.Step(170); window.UpdateLayout(); var halfway = scroll.Offset.Y;
            Assert.True(halfway > 0);
            env.Step(200); window.UpdateLayout(); Assert.True(scroll.Offset.Y > halfway);
            Marrow.App.Tests.App.Fixtures.HydraCookbookResolveFixture.SetModels(env.Model.Providers, ProviderKind.Codex,
                Enumerable.Range(0, 30).Select(i => Option("gpt-" + i, "Renamed model " + i)).ToArray());
            Dispatcher.UIThread.RunJobs(); env.Step(500); window.UpdateLayout();
            Assert.Equal("openai", Assert.Single(list.GetVisualDescendants().OfType<TextBox>()).Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Long_router_and_codex_lists_expose_a_persistent_draggable_scrollbar() => headless.OnUiThread(() =>
    {
        using var env = Make(() => Thread(ProviderKind.Codex, "gpt-0"), model =>
        {
            for (var i = 0; i < 486; i++) model.Settings.AddToModelList(Pin(ProviderKind.Codex, "gpt-" + i));
        }, (ProviderKind.Codex, Enumerable.Range(0, 486).Select(i => Option("gpt-" + i, "Model " + i)).ToArray()));
        var list = new ModelList(env.Thread, false, _ => { }, () => { });
        var router = new OpenRouterPicker(Enumerable.Range(0, 486).Select(i => new PickerModel("acme/" + i, "Model " + i, "Acme", "#10AA60")).ToArray(), "", _ => { }, () => { });
        foreach (var control in new Control[] { list, router })
        {
            var window = env.Show(control);
            try
            {
                var scroll = Assert.Single(control.GetVisualDescendants().OfType<ScrollViewer>());
                Assert.Equal(ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
                Assert.False(scroll.AllowAutoHide);
                var bar = Assert.Single(scroll.GetVisualDescendants().OfType<ScrollBar>(), b => b.Orientation == Avalonia.Layout.Orientation.Vertical);
                Assert.True(bar.IsVisible);
                var thumb = Assert.Single(bar.GetVisualDescendants().OfType<Thumb>());
                Assert.True(thumb.Bounds.Height > 0);
                thumb.RaiseEvent(new VectorEventArgs { RoutedEvent = Thumb.DragDeltaEvent, Vector = new Avalonia.Vector(0, 100) });
                Assert.True(scroll.Offset.Y > 0, "dragging the scrollbar changes the actual viewport");
            }
            finally { window.Close(); }
        }
    });
}
