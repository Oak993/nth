using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

[Collection("Headless")]
public sealed class DesignGapsSaturationTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(.6, 175, 22, 22)]
    [InlineData(.8, 215, 11, 11)]
    [InlineData(1, 255, 0, 0)]
    public void The_live_subtree_draws_the_CSS_saturate_matrix(double saturation, int red, int green, int blue) => headless.OnUiThread(() =>
    {
        var ink = new Border { Background = Brushes.Red };
        var layer = new SaturationLayer(ink) { Amount = saturation };
        var window = new Window { Width = 80, Height = 60, Background = Brushes.Black, Content = layer };
        window.Show();
        try { Expect(Pixels(window).At(40, 30), red, green, blue); }
        finally { window.Close(); }
    });

    [Fact]
    public void A_child_only_redraw_is_filtered_and_the_sibling_and_background_are_untouched() => headless.OnUiThread(() =>
    {
        var ink = new Border { Background = Brushes.Red };
        var layer = new SaturationLayer(ink) { Amount = .6, Width = 40, Height = 60 };
        var sibling = new Border { Background = Brushes.Blue, Width = 40, Height = 60 };
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { layer, sibling } };
        var window = new Window { Width = 80, Height = 60, Background = Brushes.Green, Content = row };
        window.Show();
        try
        {
            var before = Pixels(window);
            Expect(before.At(20, 30), 175, 22, 22);
            Expect(before.At(60, 30), 0, 0, 255);
            ink.Background = Brushes.Blue; // only the child is dirty: no snapshot, no whole-window invalidation
            var after = Pixels(window);
            Expect(after.At(20, 30), 7, 7, 160);
            Expect(after.At(60, 30), 0, 0, 255);
            layer.Amount = 1;
            Expect(Pixels(window).At(20, 30), 0, 0, 255);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Overlapping_children_are_composited_before_one_group_opacity() => headless.OnUiThread(() =>
    {
        var children = new Panel { Children = { new Border { Background = Brushes.Red }, new Border { Background = Brushes.Blue } } };
        var layer = new SaturationLayer(children) { Amount = .6, Opacity = .5 };
        var window = new Window { Width = 80, Height = 60, Background = Brushes.Black, Content = layer };
        window.Show();
        try { Expect(Pixels(window).At(40, 30), 4, 4, 80); }
        finally { window.Close(); }
    });

    private static RenderedPixels Pixels(Window window)
    {
        for (var i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
        return RenderedPixels.Capture(window);
    }
    private static void Expect((int Red, int Green, int Blue) value, int r, int g, int b)
    {
        Assert.InRange(value.Red, r - 1, r + 1);
        Assert.InRange(value.Green, g - 1, g + 1);
        Assert.InRange(value.Blue, b - 1, b + 1);
    }
}
