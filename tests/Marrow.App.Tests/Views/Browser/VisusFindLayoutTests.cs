using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Media;
using SkiaSharp;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Browser;
using Marrow.Services.Web;

namespace Marrow.App.Tests.Views.Browser;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w8c-5")]
public sealed class VisusFindLayoutTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Find_query_uses_visible_theme_ink(bool dark) => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig(); visus.Page("https://example.test/");
        using var find = new VisusFindController(visus.Session);
        var bar = new VisusFindBar(find, find.Close);
        var window = new Avalonia.Controls.Window { Width = 330, Height = 38, Content = bar,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            window.UpdateLayout();
            var ink = Assert.IsAssignableFrom<ISolidColorBrush>(bar.Box.Foreground).Color;
            Assert.InRange(ink.A, (byte)219, byte.MaxValue);
            if (dark) { Assert.InRange(ink.R, (byte)220, byte.MaxValue); Assert.InRange(ink.G, (byte)220, byte.MaxValue); Assert.InRange(ink.B, (byte)220, byte.MaxValue); }
            else { Assert.InRange(ink.R, (byte)0, (byte)35); Assert.InRange(ink.G, (byte)0, (byte)35); Assert.InRange(ink.B, (byte)0, (byte)35); }
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Find_popup_positions_the_visible_card_ten_below_and_twenty_four_inside_the_toolbar_right_edge() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        visus.Page("https://example.test/");
        var body = new VisusPaneBody(visus.Session);
        var popup = Assert.Single(body.Children.OfType<Popup>(), p => p.Child is PopoverCard card && ReferenceEquals(card.Child, body.FindBar));
        var card = Assert.IsType<PopoverCard>(popup.Child);
        Assert.Same(body.Toolbar, popup.PlacementTarget);
        Assert.Equal(PlacementMode.BottomEdgeAlignedRight, popup.Placement);
        Assert.Equal(10, popup.VerticalOffset + card.Margin.Top);
        Assert.Equal(24, card.Margin.Right - popup.HorizontalOffset);
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Match_case_paints_distinct_off_and_on_faces_and_restores_off(bool dark, bool reduced) => headless.OnUiThread(() =>
    {
        var time = new ManualTimeProvider(); using var clock = SwiftClock.Provider.Override(time);
        var frames = new ManualFrameSource(); using var frameScope = FrameClock.Source.Override(frames);
        using var motion = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = false });
        using var visus = new VisusRig();
        var tab = visus.Page("https://example.test/");
        VisusRig.Live(tab).FindHandler = (_, _, _) => Task.FromResult(new WebFindResult(3, 0));
        using var find = new VisusFindController(visus.Session);
        Assert.True(find.Open());
        var bar = new VisusFindBar(find, find.Close);
        EnvironmentValues.SetAccessibilityReduceMotion(bar, reduced);
        var window = new Avalonia.Controls.Window { Width = 330, Height = 38, Content = bar,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            window.UpdateLayout();
            var toggle = Assert.Single(bar.GetVisualDescendants().OfType<CheckBox>());
            byte[] Paint()
            {
                window.UpdateLayout();
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(toggle.Bounds.Width), (int)Math.Ceiling(toggle.Bounds.Height)));
                bitmap.Render(toggle);
                using var stream = new MemoryStream(); bitmap.Save(stream, new PngBitmapEncoderOptions()); stream.Position = 0;
                using var pixels = SKBitmap.Decode(stream);
                Assert.Contains(pixels.Pixels, pixel => pixel.Alpha > 0);
                return pixels.Bytes;
            }
            toggle.IsChecked = false; var off = Paint();
            toggle.IsChecked = true;
            if (!reduced) Assert.Equal(off, Paint()); // starts from the current face, without a snap
            else Assert.False(off.SequenceEqual(Paint()), "Reduce Motion must show the new state immediately.");
            time.Advance(TimeSpan.FromSeconds(.5)); frames.Advance(TimeSpan.FromSeconds(.5));
            var on = Paint();
            Assert.False(off.SequenceEqual(on), "Match case must visibly distinguish its enabled state in each theme.");
            toggle.IsChecked = false;
            if (!reduced) Assert.Equal(on, Paint());
            else Assert.Equal(off, Paint());
            time.Advance(TimeSpan.FromSeconds(.5)); frames.Advance(TimeSpan.FromSeconds(.5));
            Assert.Equal(off, Paint());
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Match_case_is_a_visible_compact_toggle_inside_the_find_bar() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var tab = visus.Page("https://example.test/");
        VisusRig.Live(tab).FindHandler = (_, _, _) => Task.FromResult(new WebFindResult(3, 0));
        using var find = new VisusFindController(visus.Session);
        Assert.True(find.Open());
        var bar = new VisusFindBar(find, find.Close);
        bar.Box.Text = "price";
        var window = new Avalonia.Controls.Window { Width = 330, Height = 38, Content = bar }; window.Show();
        try
        {
            window.UpdateLayout();
            var toggle = Assert.Single(bar.GetVisualDescendants().OfType<CheckBox>());
            Assert.NotNull(toggle.Template);
            var face = Assert.Single(toggle.GetVisualDescendants().OfType<TextBlock>());
            Assert.Equal("Aa", face.Text);
            Assert.True(face.Bounds.Width > 0 && face.Bounds.Height > 0);
            Assert.Equal("Match case", AutomationProperties.GetName(toggle));
            Assert.InRange(toggle.Bounds.Width, 16, 22);
            var at = toggle.TranslatePoint(default, bar)!.Value;
            Assert.True(new Rect(bar.Bounds.Size).Contains(new Rect(at, toggle.Bounds.Size)));
            var next = bar.GetVisualDescendants().OfType<Button>().First(button => AutomationProperties.GetName(button) == "Previous");
            Assert.True(at.X + toggle.Bounds.Width <= next.TranslatePoint(default, bar)!.Value.X);
            var forward = bar.GetVisualDescendants().OfType<Button>().First(button => AutomationProperties.GetName(button) == "Next");
            var close = bar.GetVisualDescendants().OfType<Button>().First(button => AutomationProperties.GetName(button) == "Close find");
            Assert.Equal(6, forward.TranslatePoint(default, bar)!.Value.X - next.TranslatePoint(default, bar)!.Value.X - next.Bounds.Width, 3);
            Assert.Equal(6, close.TranslatePoint(default, bar)!.Value.X - forward.TranslatePoint(default, bar)!.Value.X - forward.Bounds.Width, 3);
        }
        finally { window.Close(); }
    });
}
