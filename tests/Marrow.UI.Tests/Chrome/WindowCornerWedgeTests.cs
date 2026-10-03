using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.UI.Tests.Chrome;

// Source: DroppyCode/UI/Chrome/Chrome.swift:8 (windowCornerRadius 26), :211-224 (the scrim and the solid window), :249-259 (glass under the
// scrim); OD-0075 (owner, 2026-10-02: "the wedge is filled with the window's scrim"; settles OD-0072); ED-236 UNRESOLVED (a): the system's
// window is a rectangle, the shape the content draws is 26 pt, and the blur showed unscrimmed between them (luma 126 beside 93).
//
// The layer is proved headless on pixels: the window background is a known grey, so the colour of a corner is the scrim over that grey and
// the colour of everything else is the grey itself. What the real window's Acrylic shows through it is a native check (see the unit report).
[Collection("Headless")]
public sealed class WindowCornerWedgeTests(HeadlessSession headless)
{
    private static readonly Color Grey = Color.FromRgb(128, 128, 128);

    private static (byte Red, byte Green, byte Blue)[,] Render(WindowCornerWedge wedge, ThemeVariant theme, int width = 300, int height = 200, Action<Window>? then = null)
    {
        var window = new Window { Width = width, Height = height, Background = new SolidColorBrush(Grey), RequestedThemeVariant = theme, Content = wedge };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        try
        {
            then?.Invoke(window);
            return Pixels(window);
        }
        finally { window.Close(); }
    }

    private static (byte Red, byte Green, byte Blue)[,] Pixels(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("no rendered frame");
        var width = frame.PixelSize.Width;
        var height = frame.PixelSize.Height;
        var bytes = new byte[width * height * 4];
        var pin = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
        try { frame.CopyPixels(new PixelRect(frame.PixelSize), pin.AddrOfPinnedObject(), bytes.Length, width * 4); }
        finally { pin.Free(); }
        var rgb = frame.Format == Avalonia.Platform.PixelFormat.Bgra8888 ? (2, 1, 0) : (0, 1, 2);
        var pixels = new (byte, byte, byte)[width, height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var at = (y * width + x) * 4;
                pixels[x, y] = (bytes[at + rgb.Item1], bytes[at + rgb.Item2], bytes[at + rgb.Item3]);
            }
        return pixels;
    }

    // The scrim over the grey: black in the dark theme, white in the light one (Chrome.swift:256-257), at the given alpha.
    private static int Over(double alpha, bool dark) => (int)Math.Round(Grey.R * (1 - alpha) + (dark ? 0 : 255) * alpha);

    private static void AssertGrey(byte red, byte green, byte blue, int expected, string where)
    {
        Assert.True(Math.Abs(red - expected) <= 2 && Math.Abs(green - expected) <= 2 && Math.Abs(blue - expected) <= 2,
            $"{where}: expected {expected}, drawn ({red}, {green}, {blue})");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Decision", "ED-236")] // OD-0075; Chrome.swift:8 windowCornerRadius 26, :211-215 the scrim
    public void The_four_corners_outside_the_26_point_curve_wear_the_scrim_and_the_rest_of_the_window_is_untouched(bool dark) => headless.OnUiThread(() =>
    {
        const int width = 300, height = 200;
        var scrim = WindowBackdrop.Scrim(0.5, dark); // 0.26 dark, 0.18 light
        var px = Render(new WindowCornerWedge(0.5), dark ? ThemeVariant.Dark : ThemeVariant.Light, width, height);
        var filled = Over(scrim, dark);
        // The corner pixels themselves, and a pixel on the diagonal just outside the curve (the arc meets the diagonal 7.6 px in).
        foreach (var (x, y) in new[] { (0, 0), (1, 1), (5, 5), (width - 1, 0), (width - 2, 1), (width - 6, 5), (0, height - 1), (1, height - 2), (5, height - 6), (width - 1, height - 1), (width - 2, height - 2), (width - 6, height - 6) })
            AssertGrey(px[x, y].Red, px[x, y].Green, px[x, y].Blue, filled, $"corner ({x}, {y})");
        // Inside the shape and along its straight edges nothing is drawn: the grey of the window itself.
        foreach (var (x, y) in new[] { (10, 10), (13, 13), (26, 26), (width / 2, height / 2), (width / 2, 0), (width / 2, height - 1), (0, height / 2), (width - 1, height / 2), (30, 0), (0, 30), (width - 31, height - 1), (width - 1, height - 31) })
            AssertGrey(px[x, y].Red, px[x, y].Green, px[x, y].Blue, Grey.R, $"inside ({x}, {y})");
    });

    [Fact]
    [Trait("Decision", "ED-236")] // OD-0075; the same shape the window's content is clipped to (RootView.swift:66-67)
    public void The_wedge_is_the_exact_complement_of_the_shape_the_content_is_clipped_to() => headless.OnUiThread(() =>
    {
        var size = new Size(300, 200);
        var wedge = WindowCornerWedge.WedgeGeometry(size);
        var shape = SwiftShapes.RoundedRectangleGeometry(new Rect(size), ChromeTokens.WindowCornerRadius, RoundedCornerStyle.Continuous);
        var inShape = 0;
        var inWedge = 0;
        for (var y = 0.25; y < size.Height; y += 0.5)
            for (var x = 0.25; x < size.Width; x += 0.5)
            {
                var point = new Point(x, y);
                var shaped = shape.FillContains(point);
                var wedged = wedge.FillContains(point);
                // Every point of the window is in the shape (the content) or in the wedge, and never in both: no gap the blur shows through
                // and no scrim over the glass.
                Assert.True(shaped != wedged, $"({x}, {y}): shape {shaped}, wedge {wedged}");
                if (shaped) inShape++; else inWedge++;
            }
        Assert.True(inWedge > 0 && inShape > 0);
        // Four corners of the 26 pt circular arc: r^2 (1 - pi/4) each; the sample is every quarter point-squared.
        var area = inWedge * 0.25;
        Assert.Equal(4 * 26 * 26 * (1 - Math.PI / 4), area, 1.5);
    });

    [Theory]
    [InlineData(true, 0.0, 0.0)]
    [InlineData(true, 0.25, 0.13)]
    [InlineData(true, 0.75, 0.63)]
    [InlineData(false, 0.25, 0.09)]
    [InlineData(false, 0.75, 0.59)]
    [InlineData(true, 0.97, 0.9556)]
    [InlineData(true, 0.98, 1.0)]
    [InlineData(false, 1.0, 1.0)]
    [Trait("Decision", "ED-236")] // OD-0075; Chrome.swift:211-215 the scrim, :220-224 the solid window
    public void The_wedge_follows_the_backdrop_opacity_and_goes_solid_with_the_window(bool dark, double setting, double alpha) => headless.OnUiThread(() =>
    {
        var wedge = new WindowCornerWedge(0.5); // built at one opacity, then told the setting as the window tells it
        wedge.Update(setting);
        Assert.Equal(alpha, wedge.Alpha(dark), 3);
        var px = Render(wedge, dark ? ThemeVariant.Dark : ThemeVariant.Light);
        AssertGrey(px[1, 1].Red, px[1, 1].Green, px[1, 1].Blue, Over(alpha, dark), $"corner at {setting}");
        AssertGrey(px[150, 100].Red, px[150, 100].Green, px[150, 100].Blue, Grey.R, "centre");
    });

    [Fact]
    [Trait("Decision", "ED-236")] // OD-0075
    public void The_wedge_redraws_when_the_theme_or_the_setting_changes_and_takes_no_pointer() => headless.OnUiThread(() =>
    {
        var wedge = new WindowCornerWedge(0.5);
        Assert.False(wedge.IsHitTestVisible);
        var window = new Window { Width = 300, Height = 200, Background = new SolidColorBrush(Grey), RequestedThemeVariant = ThemeVariant.Dark, Content = wedge };
        window.Show();
        try
        {
            var dark = Pixels(window);
            AssertGrey(dark[1, 1].Red, dark[1, 1].Green, dark[1, 1].Blue, Over(0.26, dark: true), "dark");
            window.RequestedThemeVariant = ThemeVariant.Light;
            var light = Pixels(window);
            AssertGrey(light[1, 1].Red, light[1, 1].Green, light[1, 1].Blue, Over(0.18, dark: false), "light");
            wedge.Update(0.25);
            var quarter = Pixels(window);
            AssertGrey(quarter[1, 1].Red, quarter[1, 1].Green, quarter[1, 1].Blue, Over(0.09, dark: false), "after the setting changed");
        }
        finally { window.Close(); }
    });
}
