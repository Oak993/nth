using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Tour;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Tour;

[Collection("Headless")]
public sealed class TourWindowControllerReviewTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-UPDATE-095")] // App/Views/Tour/TourWindowController.swift:37-46,117-137,152-186
    public void Height_measures_real_wrapped_glyphs_and_takes_the_tallest_page() => headless.OnUiThread(() =>
    {
        var pages = new[] {new TourPage("missing", new string('W', 90), "A description with real wrapping."), new TourPage("missing", new string('i',90), "Short.")};
        var controller = TourWindowController.Shared;
        controller.Close();
        var expected = Math.Max(Measure(pages[0], "Continue", 660), Measure(pages[1], "Done", 660));
        Assert.NotEqual(Measure(pages[0], "Continue", 660), Measure(pages[1], "Done", 660));
        var w = controller.Present(pages);
        try { Assert.Equal(Math.Round(660 / TourView.ImageAspectRatio, MidpointRounding.AwayFromZero) + expected, w.Height); }
        finally { controller.Close(); }
    });

    private static double Measure(TourPage page, string label, double width)
    {
        var title = new TextBlock {Text=page.Title, TextWrapping=TextWrapping.Wrap};
        SwiftFont.System(28, SwiftFont.Weight.Bold).Apply(title);
        var description = new TextBlock {Text=page.Description, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,6,0,0)};
        SwiftFont.Body.Apply(description);
        var button = new TextBlock {Text=label};
        SwiftFont.Title3.FontWeight(SwiftFont.Weight.Semibold).Apply(button);
        var stack = new StackPanel {Spacing=0, Children={new Border{Height=12},title,description,new Border{Height=12},new Border{Width=220,Height=42,Child=button}}};
        var panel = new Border {Padding=new Thickness(32,12,32,24), Child=stack};
        panel.Measure(new Size(width,double.PositiveInfinity));
        return panel.DesiredSize.Height;
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-097")] // App/Views/Tour/TourWindowController.swift:92-101
    public void Centers_over_the_main_windows_screen_position() => headless.OnUiThread(() =>
    {
        var main = new Avalonia.Controls.Window {Width=900, Height=700, Position=new PixelPoint(420,250)};
        main.Show(); Dispatcher.UIThread.RunJobs();
        var controller = TourWindowController.Shared;
        controller.Close();
        try
        {
            var seam=typeof(TourWindowController).GetProperty("MainWindowForTesting",BindingFlags.NonPublic|BindingFlags.Instance);
            Assert.NotNull(seam);
            seam.SetValue(controller,main);
            var w = controller.Present([]);
            var scale = main.RenderScaling;
            var frame = main.FrameSize ?? main.Bounds.Size;
            Assert.Equal(new PixelPoint((int)Math.Round(main.Position.X+(frame.Width-w.Width)*scale/2),
                (int)Math.Round(main.Position.Y+(frame.Height-w.Height)*scale/2)), w.Position);
        }
        finally
        {
            typeof(TourWindowController).GetProperty("MainWindowForTesting",BindingFlags.NonPublic|BindingFlags.Instance)?.SetValue(controller,null);
            controller.Close(); main.Close();
        }
    });

    [Fact]
    [Trait("Req", "PR-UPDATE-096")] // App/Views/Tour/TourWindowController.swift:76-90
    public void Transparent_floating_card_has_a_rounded_shadow_and_draggable_background() => headless.OnUiThread(() =>
    {
        var controller=TourWindowController.Shared;
        controller.Close();
        var w=controller.Present([]);
        try
        {
            Assert.Equal(WindowDecorations.None,w.WindowDecorations);
            Assert.True(w.Topmost); Assert.False(w.ShowInTaskbar);
            Assert.Null(w.Background);
            var view=Assert.IsType<TourView>(w.Content);
            var scrim=Assert.IsType<Border>(view.Children[0]);
            Assert.True(scrim.BoxShadow.Count>0);
            Assert.True(scrim.BoxShadow[0].Blur>0 && scrim.BoxShadow[0].Color.A>0);
            Assert.Equal(new CornerRadius(20),scrim.CornerRadius); // TourView.swift:63
            var calls=0;
            var seam=typeof(TourWindowController).GetProperty("MoveDragForTesting",BindingFlags.NonPublic|BindingFlags.Instance);
            Assert.NotNull(seam);
            seam.SetValue(controller,(Action<PointerPressedEventArgs>)(_=>calls++));
            w.MouseDown(new Point(10,10),MouseButton.Left);
            w.MouseUp(new Point(10,10),MouseButton.Left);
            Assert.Equal(1,calls);
            w.Activate(); Assert.True(w.IsActive);
        }
        finally
        {
            typeof(TourWindowController).GetProperty("MoveDragForTesting",BindingFlags.NonPublic|BindingFlags.Instance)?.SetValue(controller,null);
            controller.Close();
        }
    });
}
