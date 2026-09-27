using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Tour;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Chrome = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Tests.Views.Tour;

[Collection("Headless")]
public sealed class TourViewReviewTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-UPDATE-100")] // App/Views/Tour/TourView.swift:185-188,248-252
    [Trait("Req", "UI-UPDATE-040")] // App/Views/Tour/TourView.swift:185-188
    public void Visible_back_button_really_returns_to_the_previous_page_then_fades_on_page_one() => headless.OnUiThread(() =>
    {
        var time=new ManualTimeProvider();using var date=SwiftClock.Provider.Override(time);
        var frames=new ManualFrameSource();using var source=FrameClock.Source.Override(frames);
        var view=new TourView(Pages());var w=Show(view);
        try
        {
            var back=view.GetVisualDescendants().OfType<Button>().Single(b=>b.GetVisualDescendants().OfType<SwiftImage>().Any(i=>i.SymbolName=="chevron.left"));
            var host=Assert.IsAssignableFrom<Control>(back.GetVisualParent());
            view.Advance();Settle(time,frames);Assert.Equal(1,view.CurrentIndex);Assert.Equal(1,host.Opacity);
            back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Settle(time,frames);
            Assert.Equal(0,view.CurrentIndex);Assert.Equal(0,host.Opacity);Assert.True(back.IsEnabled);Assert.True(back.IsHitTestVisible);
            view.Advance();view.Advance();Settle(time,frames);Assert.Equal(2,view.CurrentIndex);
            view.GoBack();Settle(time,frames);Assert.Equal(1,view.CurrentIndex);Assert.Equal(1,host.Opacity);
        }
        finally{w.Close();}
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "UI-UPDATE-036")] // App/Views/Tour/TourView.swift:79-95
    public void Glass_tint_scrim_and_hairline_match_both_appearances(bool dark) => headless.OnUiThread(() =>
    {
        var view=new TourView([]);var w=Show(view);w.RequestedThemeVariant=dark?ThemeVariant.Dark:ThemeVariant.Light;Dispatcher.UIThread.RunJobs();
        try
        {
            var variant=dark?ThemeVariant.Dark:ThemeVariant.Light;
            var scrim=Assert.IsType<Border>(view.Children[0]);
            Assert.Equal(dark?Color.FromArgb(77,0,0,0):Color.FromArgb(87,255,255,255),Assert.IsAssignableFrom<ISolidColorBrush>(scrim.Background).Color);
            var glass=view.GetVisualDescendants().OfType<Border>().Single(b=>SwiftGlass.GetEffect(b)is not null);
            Assert.Equal(Chrome.GlassTint.Opacity(dark?.22:.16).Resolve(variant),Assert.IsAssignableFrom<ISolidColorBrush>(glass.Background).Color);
            var stroke=Assert.IsType<Border>(view.Children[2]);
            Assert.Equal(Chrome.Overlay(.14).Resolve(variant),Assert.IsAssignableFrom<ISolidColorBrush>(stroke.BorderBrush).Color);
        }
        finally{w.Close();}
    });

    [Fact]
    [Trait("Req", "UI-UPDATE-044")] // App/Views/Tour/TourView.swift:204-212
    public void Primary_action_label_is_title3_15_point_semibold() => headless.OnUiThread(() =>
    {
        var view=new TourView(Pages());var w=Show(view);
        try
        {
            var text=view.GetVisualDescendants().OfType<TextBlock>().Single(t=>t.Text=="Continue");
            Assert.Equal(15,TextElement.GetFontSize(text));Assert.Equal(FontWeight.SemiBold,TextElement.GetFontWeight(text));
        }
        finally{w.Close();}
    });

    [Fact]
    [Trait("Req", "UI-UPDATE-038")] // App/Views/Tour/TourView.swift:124-137
    public void Gradient_seat_contains_the_two_source_gray_and_alpha_values() => headless.OnUiThread(() =>
    {
        var view=new TourView(Pages());var w=Show(view);
        try
        {
            var seat=view.GetVisualDescendants().OfType<Border>().Single(b=>b.Height==96);
            var gradient=Assert.IsType<LinearGradientBrush>(seat.Background);
            Assert.Equal(Color.FromArgb(71,20,20,20),gradient.GradientStops[1].Color);
            Assert.Equal(Color.FromArgb(140,20,20,20),gradient.GradientStops[2].Color);
        }
        finally{w.Close();}
    });

    private static TourPage[] Pages()=>[new("missing","One","First"),new("missing","Two","Second"),new("missing","Three","Third")];
    private static Avalonia.Controls.Window Show(Control c){var w=new Avalonia.Controls.Window{Content=c,SizeToContent=SizeToContent.WidthAndHeight,RequestedThemeVariant=ThemeVariant.Light};w.Show();Dispatcher.UIThread.RunJobs();return w;}
    private static void Settle(ManualTimeProvider time,ManualFrameSource frames){time.Advance(TimeSpan.FromMilliseconds(250));frames.Advance(TimeSpan.FromMilliseconds(250));Dispatcher.UIThread.RunJobs();}
}
