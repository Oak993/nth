using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Browser;
using Marrow.Core.Port;
using Marrow.Services.Web;
using Marrow.Testing;
using Marrow.UI.Panes;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Browser;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w8c-1")]
public sealed class VisusFindCanvasTests(HeadlessSession headless)
{
    [Fact]
    public void Canvas_header_buttons_are_twenty_six_DIP_squares() => Mount((bar, _, _) =>
    {
        foreach (var name in new[] { "Previous", "Next", "Close find" })
        {
            var button = Button(bar, name);
            Assert.Equal(26, button.Bounds.Width); Assert.Equal(26, button.Bounds.Height);
            var face = Assert.IsType<Border>(button.Content);
            Assert.Equal(new CornerRadius(8), face.CornerRadius);
            var glyph = Assert.IsType<PaneGlyph>(face.Child);
            Assert.Equal(14, glyph.Bounds.Width); Assert.Equal(14, glyph.Bounds.Height);
        }
    });

    [Fact]
    public void The_leading_search_icon_is_sixteen_DIP_and_paints() => Mount((bar, _, _) =>
    {
        var search = Assert.Single(bar.GetVisualDescendants().OfType<PaneGlyph>(), icon => icon.Kind == PaneGlyphKind.Search);
        Assert.Equal(16, search.Bounds.Width); Assert.Equal(16, search.Bounds.Height);
        using var pixels = Paint(search); Assert.Contains(pixels.Pixels, pixel => pixel.Alpha > 0);
    });

    [Fact]
    public void Find_arrows_use_the_canvas_chevron_with_up_and_down_orientations() => Mount((bar, _, _) =>
    {
        var up = Assert.Single(Button(bar, "Previous").GetVisualDescendants().OfType<PaneGlyph>());
        var down = Assert.Single(Button(bar, "Next").GetVisualDescendants().OfType<PaneGlyph>());
        Assert.Equal(PaneGlyphKind.Chevron, up.Kind); Assert.Equal(PaneGlyphKind.Chevron, down.Kind);
        Assert.Equal(180, Assert.IsType<RotateTransform>(up.RenderTransform).Angle);
        Assert.True(down.RenderTransform is null || down.RenderTransform.Value == Matrix.Identity);
    });

    [Fact]
    public void Disabled_arrows_have_the_canvas_thirty_five_percent_opacity() => Mount((bar, window, find) =>
    {
        var previous = Button(bar, "Previous"); var next = Button(bar, "Next");
        Assert.False(previous.IsEnabled); Assert.False(next.IsEnabled);
        Assert.Equal(.35, previous.Opacity); Assert.Equal(.35, next.Opacity);
        bar.Box.Text = "price";
        // Avalonia12.1.2 TextChanged is posted after rendering, not synchronous with assignment.
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, find.Result.MatchCount);
        Assert.True(previous.IsEnabled); Assert.True(next.IsEnabled);
        Assert.Equal(1, previous.Opacity); Assert.Equal(1, next.Opacity);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Header_button_hover_paints_a_tint_and_eases_back_to_rest(bool reduced) => headless.OnUiThread(() =>
    {
        var time = new ManualTimeProvider(); using var clock = SwiftClock.Provider.Override(time);
        var frames = new ManualFrameSource(); using var source = FrameClock.Source.Override(frames);
        using var platform = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        MountOnUi((bar, window, _) =>
        {
            EnvironmentValues.SetAccessibilityReduceMotion(bar, reduced);
            var close = Button(bar, "Close find");
            var face = Assert.IsType<Border>(close.Content); var glyph = Assert.IsType<PaneGlyph>(face.Child);
            Color Fill() => Assert.IsAssignableFrom<ISolidColorBrush>(face.Background).Color;
            Color Ink() => Assert.IsAssignableFrom<ISolidColorBrush>(glyph.Foreground).Color;
            Assert.Equal(0, Fill().A); Assert.Equal(Color.FromArgb(173, 236, 236, 238), Ink());
            using var off = Paint(close); Assert.Contains(off.Pixels, pixel => pixel.Alpha > 0);
            window.MouseMove(close.TranslatePoint(new Point(close.Bounds.Width / 2, close.Bounds.Height / 2), window)!.Value);
            Dispatcher.UIThread.RunJobs();
            using (var start = Paint(close)) { if (!reduced) Assert.Equal(off.Bytes, start.Bytes); else Assert.False(off.Bytes.SequenceEqual(start.Bytes)); }
            time.Advance(TimeSpan.FromMilliseconds(75)); frames.Advance(TimeSpan.FromMilliseconds(75));
            // Independent CSS ease (.25,.1,.25,1) at t=.5 is .8024033876. Avalonia stores the .10
            // endpoint as 8-bit alpha26 first: round(26*p)=21 (floating CSS alpha rounds to20).
            // This exact port quantization check allows no additional channel or timing tolerance.
            Assert.Equal(Color.FromArgb(reduced ? (byte)26 : (byte)21, 255, 255, 255), Fill());
            Assert.Equal(Color.FromArgb(reduced ? (byte)255 : (byte)239, 236, 236, 238), Ink());
            time.Advance(TimeSpan.FromMilliseconds(75)); frames.Advance(TimeSpan.FromMilliseconds(75));
            Assert.Equal(Color.FromArgb(26, 255, 255, 255), Fill()); Assert.Equal(Color.FromArgb(255, 236, 236, 238), Ink());
            using var on = Paint(close); Assert.False(off.Bytes.SequenceEqual(on.Bytes), "The canvas header button must show its hover tint.");
            window.MouseMove(new Point(0, 0)); Dispatcher.UIThread.RunJobs();
            if (reduced) { using var instant = Paint(close); Assert.Equal(off.Bytes, instant.Bytes); }
            time.Advance(TimeSpan.FromMilliseconds(150)); frames.Advance(TimeSpan.FromMilliseconds(150));
            using var restored = Paint(close); Assert.Equal(off.Bytes, restored.Bytes);
        });
    });

    private void Mount(Action<VisusFindBar, Avalonia.Controls.Window, VisusFindController> check) => headless.OnUiThread(() => MountOnUi(check));
    private static void MountOnUi(Action<VisusFindBar, Avalonia.Controls.Window, VisusFindController> check)
    {
        using var visus = new VisusRig(); var tab = visus.Page("https://example.test/");
        VisusRig.Live(tab).FindHandler = (_, _, _) => Task.FromResult(new WebFindResult(3, 0));
        using var find = new VisusFindController(visus.Session); Assert.True(find.Open());
        var bar = new VisusFindBar(find, find.Close);
        var window = new Avalonia.Controls.Window { Width = 330, Height = 38, Content = bar, RequestedThemeVariant = ThemeVariant.Dark }; window.Show();
        try { window.UpdateLayout(); check(bar, window, find); }
        finally { window.Close(); }
    }
    private static Button Button(VisusFindBar bar, string name) => Assert.Single(bar.GetVisualDescendants().OfType<Button>(), button => AutomationProperties.GetName(button) == name);
    private static SKBitmap Paint(Control control)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(control.Bounds.Width), (int)Math.Ceiling(control.Bounds.Height))); bitmap.Render(control);
        using var stream = new MemoryStream(); bitmap.Save(stream, new PngBitmapEncoderOptions()); stream.Position = 0; return SKBitmap.Decode(stream);
    }
}
