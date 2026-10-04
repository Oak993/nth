using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/EffortSlider.swift (B3 52bc792), :1039-1072 TrackEffect's TimelineView
// (PR-COMPOSER-133: 30 or 60 frames a second, paused by reduced motion or a window that is not in front, no hit testing, hidden
// from accessibility; :1053 nothing is drawn by a fill 8 points wide or less). DEVIATION OD-0051: the solo supercharged track is the
// liquid tube and runs at 60 like every other. The real display's count over ten seconds is EffortSliderNativeFrameTests'.
[Collection("Headless")]
public sealed class EffortSliderCadenceTests(HeadlessSession headless)
{
    private static readonly System.Reflection.FieldInfo TimeField = typeof(TrackEffect).GetField("time", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

    private static Avalonia.Controls.Window Show(Control content)
    {
        var window = new Avalonia.Controls.Window { Width = 330, Height = 220, Content = content };
        window.Show(); window.Activate(); Dispatcher.UIThread.RunJobs();
        return window;
    }

    [Theory]
    [Trait("Req", "PR-COMPOSER-133")] // App/Views/Composer/EffortSlider.swift:1049-1071; DEVIATION OD-0051 (60, not 30, for the solo supercharged track)
    [InlineData(TrackLook.Kind.Supercharged, false)]
    [InlineData(TrackLook.Kind.Supercharged, true)]
    [InlineData(TrackLook.Kind.Fast, false)]
    [InlineData(TrackLook.Kind.Fast, true)]
    [InlineData(TrackLook.Kind.Fusion, false)]
    [InlineData(TrackLook.Kind.Fusion, true)]
    public void Ten_seconds_draw_600_frames_and_reduced_motion_or_a_minimised_window_pauses_them(TrackLook.Kind kind, bool paired) => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource(); using var frameBinding = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider(); using var clockBinding = SwiftClock.Provider.Override(clock);
        var effect = new TrackEffect(new TrackLook(kind, EffortBrand.Purple, paired ? new EffortPairBrand(new HydraPair(ProviderKind.Codex)) : null));
        var window = Show(effect);
        try
        {
            // a 120 Hz display: the track's own schedule, not the display's, sets the rate
            var step = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 120);
            int Drawn(double seconds)
            {
                var count = 0;
                for (var i = 0; i < (int)Math.Round(seconds * 120); i++)
                {
                    var before = (double)TimeField.GetValue(effect)!;
                    clock.Advance(step); frames.Advance(step); Dispatcher.UIThread.RunJobs();
                    if ((double)TimeField.GetValue(effect)! != before) count++;
                }
                return count;
            }
            Assert.InRange(Drawn(10), 599, 601); // :1049 one frame in sixty a second, for every look, the solo supercharged track included (OD-0051)

            // reduced motion: the timeline is paused (:1049 paused: reduceMotion || ...); no frame is drawn
            EnvironmentValues.SetAccessibilityReduceMotion(window, true); Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, Drawn(1));
            EnvironmentValues.SetAccessibilityReduceMotion(window, false); Dispatcher.UIThread.RunJobs();
            Assert.InRange(Drawn(1), 59, 61);

            // a window that is not in front: paused as well
            window.WindowState = WindowState.Minimized; Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, Drawn(1));
            window.WindowState = WindowState.Normal; window.Activate(); Dispatcher.UIThread.RunJobs();
            Assert.InRange(Drawn(1), 59, 61);
            // never a hit target and never an accessibility element (:1070-1071)
            Assert.False(effect.IsHitTestVisible);
            Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(effect));
        }
        finally { window.Close(); }
    });

    private static SKBitmap Render(TrackEffect effect)
    {
        TimeField.SetValue(effect, 1.0);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(effect.Bounds.Width), (int)effect.Bounds.Height));
        using (var context = bitmap.CreateDrawingContext()) effect.Child!.Render(context);
        using var stream = new MemoryStream(); bitmap.Save(stream, new PngBitmapEncoderOptions()); stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    [Theory]
    [Trait("Req", "PR-COMPOSER-133")] // App/Views/Composer/EffortSlider.swift:1053 `guard size.width > 8`: a fill 8 points wide or less draws nothing
    [InlineData(8, false)]
    [InlineData(4, false)]
    [InlineData(9, true)]
    [InlineData(40, true)]
    public void A_fill_of_8_points_or_less_draws_nothing(int width, bool draws) => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var effect = new TrackEffect(new TrackLook(TrackLook.Kind.Supercharged, EffortBrand.Claude)) { TrackWidth = 240 };
        effect.Measure(new Size(width, 34)); effect.Arrange(new Rect(0, 0, width, 34));
        using var bitmap = Render(effect);
        var visible = false;
        for (var y = 0; y < bitmap.Height && !visible; y++)
            for (var x = 0; x < bitmap.Width && !visible; x++) visible = bitmap.GetPixel(x, y).Alpha > 0;
        Assert.Equal(draws, visible);
    });
}
