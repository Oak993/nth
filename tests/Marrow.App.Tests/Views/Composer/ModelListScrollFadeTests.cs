using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.UI.Port;
using static Marrow.App.Tests.Views.Composer.EffortSliderModelListTests;

namespace Marrow.App.Tests.Views.Composer;

// OD-0140 (ED-NEW-settingsinapp-1): the owner, 2026-10-11, "slight fade on the lower side too so it seems premium? Just a bit?" and "Slight fade
// below the search models pill too?". The model list's rows fade out softly at its foot while more lie below, and dissolve just under the search
// pill once scrolled (ScrollEdgeFade through PickerScroll: 16 points at the top, 18 at the foot).
[Collection("Headless")]
[Trait("Decision", "OD-0140")]
[Trait("Decision", "ED-NEW-settingsinapp-1")]
public sealed class ModelListScrollFadeTests(HeadlessSession headless)
{
    [Fact]
    public void The_model_list_fades_at_its_foot_while_rows_lie_below_and_just_under_the_search_pill_once_scrolled() => headless.OnUiThread(() =>
    {
        var options = Enumerable.Range(1, 30).Select(i => Option($"m{i}", $"Model {i}")).ToArray();
        using var env = Make(null, model =>
        {
            foreach (var option in options) model.Settings.AddToModelList(Pin(ProviderKind.Codex, option.Id));
        }, (ProviderKind.Codex, options));
        var list = new ModelList(env.Thread, false, _ => { }, () => { });
        var window = env.Show(list);
        env.Step(400); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); // the catalogue arrives and the rows settle
        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single(viewer => viewer.GetVisualDescendants().OfType<ModelListRow>().Any());
        Assert.True(ScrollEdgeFade.GetIsEnabled(scroll));
        Assert.InRange(ScrollEdgeFade.GetTop(scroll), 14, 18);
        Assert.InRange(ScrollEdgeFade.GetBottom(scroll), 14, 20);
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height + 40, $"the list scrolls ({Models(list).Length} rows, extent {scroll.Extent.Height}, viewport {scroll.Viewport.Height})");

        // At rest at its top: the foot fades (rows lie below), the top does not.
        Assert.Equal(1, ScrollEdgeFade.BottomStrength(scroll), 3);
        Assert.Equal(0, ScrollEdgeFade.TopStrength(scroll));
        var mask = Assert.IsType<LinearGradientBrush>(scroll.OpacityMask);
        Assert.Equal(255, mask.GradientStops[0].Color.A); // nothing hidden at the top
        Assert.Equal(0, mask.GradientStops[^1].Color.A); // the last row dissolves at the foot
        Assert.Equal(1 - ScrollEdgeFade.GetBottom(scroll) / scroll.Bounds.Height, mask.GradientStops[3].Offset, 4);

        // Scrolled: the top fades too, from the scroll's own top edge, which lies directly below the search pill.
        scroll.Offset = new Vector(0, 40); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, ScrollEdgeFade.TopStrength(scroll), 3);
        mask = Assert.IsType<LinearGradientBrush>(scroll.OpacityMask);
        Assert.Equal(0, mask.GradientStops[0].Color.A);
        Assert.Equal(0, mask.GradientStops[1].Offset, 6); // no inset: the fade starts at the very top of the list
        Assert.Equal(255, mask.GradientStops[2].Color.A);
        Assert.Equal(ScrollEdgeFade.GetTop(scroll) / scroll.Bounds.Height, mask.GradientStops[2].Offset, 4);
        var field = list.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "Search models");
        var pill = field.GetVisualAncestors().OfType<Border>().First(border => border.BorderThickness.Bottom > 0);
        var pillBottom = pill.TranslatePoint(new Point(0, pill.Bounds.Height), window)!.Value.Y;
        var scrollTop = scroll.TranslatePoint(default, window)!.Value.Y;
        Assert.InRange(scrollTop - pillBottom, 0, 12); // the fade sits right under the pill

        // At the end: nothing below, so the foot is whole again; the top keeps its fade.
        scroll.Offset = new Vector(0, scroll.Extent.Height); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, ScrollEdgeFade.BottomStrength(scroll));
        mask = Assert.IsType<LinearGradientBrush>(scroll.OpacityMask);
        Assert.Equal(255, mask.GradientStops[^1].Color.A);
        Assert.Equal(1, ScrollEdgeFade.TopStrength(scroll), 3);

        // Back at the top: no mask is left where nothing is past an edge... except the foot, which fades again.
        scroll.Offset = default; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, ScrollEdgeFade.TopStrength(scroll));
        Assert.Equal(1, ScrollEdgeFade.BottomStrength(scroll), 3);
    });

    [Fact]
    public void A_list_too_short_to_scroll_has_no_fade_at_all() => headless.OnUiThread(() =>
    {
        var options = new[] { Option("a", "A"), Option("b", "B") };
        using var env = Make(null, model =>
        {
            foreach (var option in options) model.Settings.AddToModelList(Pin(ProviderKind.Codex, option.Id));
        }, (ProviderKind.Codex, options));
        var list = new ModelList(env.Thread, false, _ => { }, () => { });
        var window = env.Show(list);
        env.Step(400); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); // the catalogue arrives and the rows settle
        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single(viewer => viewer.GetVisualDescendants().OfType<ModelListRow>().Any());
        Assert.True(ScrollEdgeFade.GetIsEnabled(scroll));
        Assert.Equal(0, ScrollEdgeFade.TopStrength(scroll));
        Assert.Equal(0, ScrollEdgeFade.BottomStrength(scroll));
        Assert.Null(scroll.OpacityMask);
    });
}
