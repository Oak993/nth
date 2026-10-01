using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// PortControlThemes (PORT-ONLY): a ScrollViewer built without a template still scrolls, as SwiftUI's ScrollView does.
[Collection("Headless")]
public sealed class PortControlThemesTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-107")] // (5) the one shared builder registration point, where PortControlThemes registers
    public void A_scroll_view_built_without_a_template_measures_its_content_and_scrolls_with_the_wheel_over_its_whole_frame() => headless.OnUiThread(() =>
    {
        var tall = new Border { Height = 2000, Width = 100 };
        var scroll = new ScrollViewer { Content = tall };
        var window = new Window { Content = scroll, Width = 300, Height = 300 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal(2000, scroll.Extent.Height, 0);
            Assert.Equal(300, scroll.Viewport.Height, 0);
            Assert.Equal(0, tall.TranslatePoint(default, window)!.Value.Y, 0); // the top of the content is at the top
            // Over empty space beside the 100-point-wide content: the scroll view itself takes the wheel.
            window.MouseWheel(new Point(250, 150), new Vector(0, -1));
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(scroll.Offset.Y > 0, "the wheel did not scroll: " + scroll.Offset);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-107")] // (5) the one shared builder registration point, where PortControlThemes registers
    public void An_explicit_template_still_wins_over_the_default_theme() => headless.OnUiThread(() =>
    {
        var own = new Avalonia.Controls.Templates.FuncControlTemplate<ScrollViewer>((_, _) => new Border());
        var scroll = new ScrollViewer { Template = own };
        Assert.Same(own, scroll.Template);
    });
}
