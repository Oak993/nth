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
    [Trait("Req", "PR-UPDATE-095")] // App/Views/Tour/TourWindowController.swift:117-137,152-179
    public void Offscreen_sizing_view_uses_real_fonts_spacer_floors_padding_and_custom_button_text() => headless.OnUiThread(() =>
    {
        var type = typeof(TourWindowController).Assembly.GetType("Marrow.App.Views.Tour.TourBottomPanelSizingView");
        Assert.NotNull(type);
        var page = new TourPage("missing", "A wide title WWWW", "A description that really wraps across several lines at a narrow window width.");
        var sizing = Assert.IsAssignableFrom<Border>(Activator.CreateInstance(type, page, "Finish the introduction"));
        Assert.Equal(new Thickness(32,12,32,24), sizing.Padding);
        var stack = Assert.IsType<StackPanel>(sizing.Child);
        Assert.Equal(0, stack.Spacing);
        Assert.Equal(12, Assert.IsType<Border>(stack.Children[0]).Height);
        Assert.Equal(12, Assert.IsType<Border>(stack.Children[3]).Height);
        var title = Assert.IsType<TextBlock>(stack.Children[1]);
        Assert.Equal(page.Title, title.Text); Assert.Equal(28, title.FontSize); Assert.Equal(FontWeight.Bold, title.FontWeight);
        Assert.Contains("Inter", title.FontFamily.Name, StringComparison.Ordinal);
        Assert.Equal(TextWrapping.Wrap, title.TextWrapping); Assert.Equal(TextAlignment.Center, title.TextAlignment);
        var description = Assert.IsType<TextBlock>(stack.Children[2]);
        Assert.Equal(page.Description, description.Text); Assert.Equal(13, description.FontSize);
        Assert.Equal(TextWrapping.Wrap, description.TextWrapping); Assert.Equal(new Thickness(0,6,0,0), description.Margin);
        var frame = Assert.IsType<Border>(stack.Children[4]);
        Assert.Equal(220,frame.Width); Assert.Equal(42,frame.Height);
        var button = Assert.IsType<TextBlock>(frame.Child);
        Assert.Equal("Finish the introduction",button.Text); Assert.Equal(15,button.FontSize); Assert.Equal(FontWeight.SemiBold,button.FontWeight);
        sizing.Measure(new Size(300,double.PositiveInfinity)); var narrow = sizing.DesiredSize.Height;
        sizing.Measure(new Size(660,double.PositiveInfinity));
        Assert.True(narrow > sizing.DesiredSize.Height);
    });

    [Theory]
    [InlineData(360)]
    [InlineData(660)]
    [InlineData(820)]
    [Trait("Req", "PR-UPDATE-095")] // App/Views/Tour/TourWindowController.swift:117-137,152-179
    public void Window_measures_all_pages_at_its_actual_width_with_custom_action_titles(double width) => headless.OnUiThread(() =>
    {
        var pages = new[] { new TourPage("missing","Short","Brief."), new TourPage("missing","A title with much wider glyphs WWWWWWWWWWWWWWW",string.Join(" ",Enumerable.Repeat("A long wrapping description",20))) };
        var expected = Math.Max(Measure(pages[0],"Go onward",width),Measure(pages[1],"Finish this tour",width));
        var controller = TourWindowController.Shared; controller.Close();
        var window = controller.Present(pages,width,"Go onward","Finish this tour");
        try { Assert.Equal(Math.Round(width/TourView.ImageAspectRatio,MidpointRounding.AwayFromZero)+expected,window.Height); }
        finally { controller.Close(); }
    });

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
