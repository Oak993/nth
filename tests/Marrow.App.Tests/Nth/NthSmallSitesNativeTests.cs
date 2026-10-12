using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Sidebar;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth.Beads;
using SkiaSharp;

namespace Marrow.App.Tests.Nth;

// Calculi WU6 (DESIGN.md section 14, WU6; ED-NEW-calculi-wu6-1). The small sites drawn by the real composition path, against the canvas's drawing of the
// same engine sample (NthBeadsPainter is the canvas's drawGlyph and metaball, 1:1, WU3): six sidebar rows and the helper capsule's beads show the sample
// the render thread made, unclipped and with no ink past the envelope; Reduce Motion holds Pedes as three beads with a clear pixel between them at 100%.
// It renders real composition frames, so it is a Native test (the lead runs it in a quiet window; it is written here and not run).
[Collection("Headless")]
public sealed class NthSmallSitesNativeTests(HeadlessSession headless)
{
    private static void Frames(ManualTimeProvider time, int count)
    {
        for (var i = 0; i < count; i++)
        {
            time.Advance(TimeSpan.FromSeconds(1 / 60.0));
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static NthBeadsShot Capture(Avalonia.Controls.Window window)
    {
        window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
        using var bitmap = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
        using var buffer = bitmap.Lock();
        var pixels = new byte[buffer.RowBytes * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, pixels, 0, pixels.Length);
        return new NthBeadsShot(buffer.Size.Width, buffer.Size.Height, pixels);
    }

    // The canvas's drawing of one sample on a transparent envelope-sized bitmap (alpha is the coverage).
    private static SKBitmap Canvas(in NthBeadsSample sample, double size, bool dark)
    {
        var box = NthBeadsPainter.EnvelopeSize(size);
        var bitmap = new SKBitmap((int)Math.Ceiling(box.Width), (int)Math.Ceiling(box.Height), SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        new NthBeadsPainter().Paint(canvas, sample, size, dark);
        return bitmap;
    }

    // Where the envelope's top-left lies in the window: the control's box origin plus the half the box has over the envelope (the host centres it).
    private static Point Origin(NthBeadsControl beads, Avalonia.Controls.Window window)
    {
        var box = NthBeadsPainter.EnvelopeSize(beads.GlyphSize);
        var at = beads.TranslatePoint(default, window)!.Value;
        return new Point(at.X + Math.Max(0, (beads.Bounds.Width - box.Width) / 2), at.Y + Math.Max(0, (beads.Bounds.Height - box.Height) / 2));
    }

    // Every pixel the canvas covers fully is inked on screen, and the screen has no ink where the canvas has none (one pixel of slack for edge rounding).
    private static void Matches(NthBeadsShot shot, Point origin, SKBitmap expected, string because)
    {
        int x0 = (int)Math.Round(origin.X), y0 = (int)Math.Round(origin.Y);
        var backdrop = shot.At(Math.Max(0, x0 - 6), y0);
        bool Inked(int x, int y) { var p = shot.At(x, y); return Math.Abs(p.R - backdrop.R) + Math.Abs(p.G - backdrop.G) + Math.Abs(p.B - backdrop.B) > 36; }
        bool Covered(int x, int y, byte at) => x >= 0 && y >= 0 && x < expected.Width && y < expected.Height && expected.GetPixel(x, y).Alpha > at;
        var strong = 0;
        for (var y = -3; y < expected.Height + 3; y++)
            for (var x = -3; x < expected.Width + 3; x++)
            {
                if (Covered(x, y, 230)) { strong++; Assert.True(Inked(x0 + x, y0 + y), $"{because}: the canvas covers ({x}, {y}) and the frame does not"); }
                var near = false;
                for (var dy = -1; dy <= 1 && !near; dy++) for (var dx = -1; dx <= 1 && !near; dx++) near = Covered(x + dx, y + dy, 8);
                if (!near) Assert.False(Inked(x0 + x, y0 + y), $"{because}: ink at ({x}, {y}) where the canvas has none");
            }
        Assert.True(strong > 0, $"{because}: the canvas drew no bead");
    }

    [Fact]
    [Trait("Decision", "ED-NEW-calcmerge-2")] // ED-NEW-calculi-wu6-1
    public void Native_six_sidebar_rows_and_the_helper_capsule_match_the_canvas_drawing_of_their_samples() => headless.OnUiThread(() =>
    {
        var time = new ManualTimeProvider(); using var clock = SwiftClock.Provider.Override(time);
        var column = new StackPanel { Spacing = 10, Margin = new Thickness(20), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        var slots = Enumerable.Range(0, 6).Select(i => NthWorkingMarks.Working(Guid.Parse($"6f0000{i:00}-1111-4222-8333-444455556666").ToString())).ToList();
        foreach (var slot in slots) column.Children.Add(slot);
        var helper = new NthBeadsSlot(NthBeadsPlace.HelperCapsule, "helper");
        column.Children.Add(helper);
        var window = new Avalonia.Controls.Window { Width = 160, Height = 260, Content = column, Background = Avalonia.Media.Brushes.Black, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark };
        window.Show();
        try
        {
            Frames(time, 120);
            for (var round = 0; round < 12; round++)
            {
                Frames(time, 17 + round); // an odd number of frames each round, so the 30 Hz slots are seen on both parities
                var shot = Capture(window);
                foreach (var slot in slots.Append(helper))
                {
                    var driver = slot.Beads.Painter!.Driver;
                    using var expected = Canvas(driver.Sample, slot.Beads.GlyphSize, dark: true);
                    Matches(shot, Origin(slot.Beads, window), expected, $"{slot.Place} {slot.Key} round {round}");
                }
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-calcmerge-2")] // ED-NEW-calculi-wu6-1
    public void Native_reduce_motion_holds_Pedes_as_three_beads_with_a_clear_pixel_between_them_at_12_DIP() => headless.OnUiThread(() =>
    {
        var time = new ManualTimeProvider(); using var clock = SwiftClock.Provider.Override(time);
        var slot = NthWorkingMarks.Working("calm");
        var host = new Border { Padding = new Thickness(30), Child = slot, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Avalonia.Controls.Window { Width = 100, Height = 100, Content = host, Background = Avalonia.Media.Brushes.Black, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark };
        EnvironmentValues.SetAccessibilityReduceMotion(window, true);
        window.Show();
        try
        {
            Frames(time, 120);
            var shot = Capture(window);
            var origin = Origin(slot.Beads, window);
            var u = NthBeadsPainter.Unit(12);
            var rowY = (int)Math.Round(origin.Y + (NthBeadsGeometry.Foot - NthBeadsGeometry.EnvelopeY0) * u);
            var backdrop = shot.At(5, 5);
            bool Inked(int x) { var p = shot.At(x, rowY); return Math.Abs(p.R - backdrop.R) + Math.Abs(p.G - backdrop.G) + Math.Abs(p.B - backdrop.B) > 60; }
            var runs = 0; var inRun = false;
            for (var x = (int)origin.X - 2; x < (int)(origin.X + NthBeadsPainter.EnvelopeSize(12).Width) + 2; x++)
            {
                var on = Inked(x);
                if (on && !inRun) runs++;
                inRun = on;
            }
            Assert.Equal(3, runs); // three beads along the feet, each separated from the next by at least one clear pixel
        }
        finally { window.Close(); }
    });
}
