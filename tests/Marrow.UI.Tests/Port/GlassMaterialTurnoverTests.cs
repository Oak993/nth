// Source: DroppyCode/UI/Chrome/Chrome.swift (B3 52bc792), :153-184 (the glass follows the appearance). OD-0076 / ED-254 (SPEC 6.5): inside a window
// that turns over, an appearance change eases every colour over 0.32 s, CSS ease, on the frame clock, and nothing changes in the click's own frame.
// ED-320: the glass material took the new appearance (its veil colour, its resolved tint) in the click frame. It now eases on
// AppearanceTurn's curve and clock with the rest.
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Port;

[Collection("Headless")]
public sealed class GlassMaterialTurnoverTests(HeadlessSession headless)
{
    private static void Drain()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static Color Pixel(Window window, int x, int y)
    {
        for (var i = 0; i < 3; i++) Drain();
        using var bitmap = window.CaptureRenderedFrame()!;
        var size = bitmap.PixelSize;
        var bytes = new byte[size.Width * size.Height * 4];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), bytes.Length, size.Width * 4); }
        finally { handle.Free(); }
        var p = (y * size.Width + x) * 4;
        var rgba = bitmap.Format == Avalonia.Platform.PixelFormat.Rgba8888;
        return Color.FromArgb(bytes[p + 3], bytes[p + (rgba ? 0 : 2)], bytes[p + 1], bytes[p + (rgba ? 2 : 0)]);
    }

    // A flat grey window fill (the same in both appearances) under a glass piece that is its parent's first child, with and without a tint, in a window that turns over.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Decision", "ED-254")]
    public void The_glass_eases_to_the_new_appearance_over_the_turnover_and_does_not_change_in_the_click_frame(bool tinted) => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var time = new ManualTimeProvider();
        using var clock = SwiftClock.Provider.Override(time);
        var source = (ManualFrameSource)FrameClock.Source.Current;
        var piece = new Border { Background = Brushes.Transparent, Width = 160, Height = 120, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var effect = GlassEffect.Regular(GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous));
        SwiftGlass.SetEffect(piece, tinted ? effect.Tint(SwiftColor.Hex("FF8800"), 0.5) : effect);
        var window = new Window { Width = 256, Height = 160, RequestedThemeVariant = ThemeVariant.Dark, Content = new Panel { Children = { piece } } };
        window.Background = new SolidColorBrush(Color.FromRgb(128, 128, 128));
        AppearanceTurn.SetTurnsOver(window, true);
        window.Show();
        try
        {
            for (var i = 0; i < 6; i++) { Drain(); source.Advance(TimeSpan.FromSeconds(1.0 / 60)); time.Advance(TimeSpan.FromSeconds(1.0 / 60)); }
            Assert.True(AppearanceTurn.Eases(piece), "the window has drawn a frame, so it turns over");
            var before = Pixel(window, 128, 80);
            window.RequestedThemeVariant = ThemeVariant.Light;
            Drain();
            var click = Pixel(window, 128, 80);
            var samples = new List<Color>();
            for (var k = 1; k <= 24; k++)
            {
                source.Advance(TimeSpan.FromSeconds(1.0 / 60));
                time.Advance(TimeSpan.FromSeconds(1.0 / 60));
                samples.Add(Pixel(window, 128, 80));
            }
            for (var i = 0; i < 40; i++) { source.Advance(TimeSpan.FromSeconds(1.0 / 60)); time.Advance(TimeSpan.FromSeconds(1.0 / 60)); Drain(); }
            var end = Pixel(window, 128, 80);
            Assert.NotEqual(before.R, end.R); // the appearance turned the glass
            Assert.True(Math.Abs(click.R - before.R) <= 1 && Math.Abs(click.G - before.G) <= 1 && Math.Abs(click.B - before.B) <= 1, $"the click frame changed the glass: {before} to {click}");
            // Each frame moves a fraction of the way, never more than the curve's steepest 60 Hz step (about 5 %, allowed 12 %) of the whole change.
            var total = Math.Max(Math.Abs(end.R - before.R), Math.Max(Math.Abs(end.G - before.G), Math.Abs(end.B - before.B)));
            var last = click;
            foreach (var sample in samples)
            {
                var step = Math.Max(Math.Abs(sample.R - last.R), Math.Max(Math.Abs(sample.G - last.G), Math.Abs(sample.B - last.B)));
                Assert.True(step <= 0.12 * total + 1, $"a frame took {step} of {total} at once");
                last = sample;
            }
            // Midway (0.16 s, frame 10 of 24) it is between the two, and the whole change has been made by 0.32 s.
            var mid = samples[9];
            Assert.True(Math.Abs(mid.R - before.R) > 0.2 * total && Math.Abs(mid.R - end.R) > 0.02 * total, $"frame 10 is {mid}, between {before} and {end}");
            Assert.True(Math.Abs(samples[^1].R - end.R) <= 2, $"frame 24 is {samples[^1]}, end {end}");
        }
        finally { window.Close(); }
    });
}
