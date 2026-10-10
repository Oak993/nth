using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/AboutUpdateViews.swift (B3 52bc792), :305-524.
[Collection("Headless")]
public sealed class AboutUpdateViewsProofSliderTests(HeadlessSession headless)
{
    private static Avalonia.Controls.Window Show(Control slider)
    {
        var window = new Avalonia.Controls.Window { Width = 160, Height = 80, Content = slider, UseLayoutRounding = false };
        window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        return window;
    }

    [Theory]
    [Trait("Req", "UI-SETTINGS-058")] // App/Views/Settings/AboutUpdateViews.swift:382-491 (with sparkle and chase tests)
    [Trait("Req", "UI-UPDATE-007")] // App/Views/Settings/AboutUpdateViews.swift:382-491
    [Trait("Req", "UI-UPDATE-008")] // App/Views/Settings/AboutUpdateViews.swift:412-428 (with chase test)
    [InlineData(-1, 7, 0)] [InlineData(0.012, 8.08, 0)] [InlineData(0.031, 9.79, 0.5)]
    [InlineData(0.05, 11.5, 1)] [InlineData(0.5, 52, 1)] [InlineData(1.5, 97, 1)]
    public void Actual_slider_has_capsule_track_full_width_gradient_smoothstep_reveal_and_fractional_white_head(double fraction, double headX, double opacity) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var slider = new UpdateProgressSlider(fraction) { Width = 104 }; var window = Show(slider);
        var track = Assert.IsType<ProgressTrack>(slider.Child); Assert.Equal(20, slider.Bounds.Height);
        var empty = Assert.IsType<Border>(track.Children[0]); var fill = Assert.IsType<FillCanvas>(track.Children[1]); var head = Assert.IsType<Border>(track.Children[2]);
        Assert.Equal(new Rect(0, 3, 104, 14), empty.Bounds); Assert.Equal(new CornerRadius(7), empty.CornerRadius);
        Assert.Equal(RoundedCornerStyle.Circular, SwiftShapes.GetCornerStyle(empty));
        Assert.Equal(Color.FromArgb(18, 0, 0, 0), Assert.IsAssignableFrom<ISolidColorBrush>(empty.Background).Color); // :405 black .07
        Assert.False(fill.IsHitTestVisible); Assert.Equal(opacity, fill.Opacity, 12);
        // Inspect the real clip's authored double rectangle, rather than Skia's float path bounds.
        // :458-462 still requires the exact reveal width; neither values nor tolerances are relaxed.
        var content = Assert.IsType<Grid>(fill.Child); var clip = Assert.IsType<RectangleGeometry>(content.Clip);
        Assert.Equal(new Rect(0, 0, headX, 14), clip.Rect);
        Assert.Equal(Math.Min(headX, 14) / 2, clip.RadiusX); Assert.Equal(clip.RadiusX, clip.RadiusY);
        Assert.False(content.Clip.FillContains(new Point(0, 0))); Assert.True(content.Clip.FillContains(new Point(7, 7))); // radius = 14/2, :458-462
        var gradient = Assert.IsType<Border>(content.Children[0]); Assert.Equal(104, gradient.Bounds.Width);
        var brush = Assert.IsType<LinearGradientBrush>(gradient.Background);
        Assert.Equal(new RelativePoint(0, 0.5, RelativeUnit.Relative), brush.StartPoint); Assert.Equal(new RelativePoint(1, 0.5, RelativeUnit.Relative), brush.EndPoint);
        Assert.Equal(new[] { 0.0, 0.5, 1.0 }, brush.GradientStops.Select(s => s.Offset));
        Assert.Equal(new[] { Color.FromRgb(0x67, 0xB9, 0xEE), Color.FromRgb(0x1A, 0x77, 0xB4), Color.FromRgb(0x12, 0x6C, 0xAF) }, brush.GradientStops.Select(s => s.Color));
        Assert.Equal(new Rect(headX - 10, 0, 20, 20), head.Bounds); // :416 y=10, :428 end-cap centres
        var disc = Assert.IsType<Ellipse>(head.Child); Assert.Equal(12.8, disc.Width); Assert.Equal(12.8, disc.Height);
        Assert.Equal(Color.FromArgb(245, 255, 255, 255), Assert.IsAssignableFrom<ISolidColorBrush>(disc.Fill).Color); // :413 white .96
        window.RequestedThemeVariant = ThemeVariant.Dark; Dispatcher.UIThread.RunJobs();
        Assert.Equal(Color.FromArgb(26, 255, 255, 255), Assert.IsAssignableFrom<ISolidColorBrush>(empty.Background).Color); // white .10
        Assert.Equal(Color.FromArgb(245, 255, 255, 255), Assert.IsAssignableFrom<ISolidColorBrush>(disc.Fill).Color);
        Assert.Equal("Updating Nth", AutomationProperties.GetName(slider));
        var peer = ControlAutomationPeer.CreatePeerForElement(slider)!; Assert.Equal(AutomationControlType.ProgressBar, peer.GetAutomationControlType());
        Assert.True(Assert.IsAssignableFrom<IValueProvider>(peer).IsReadOnly); Assert.IsNotType<Button>(slider);
        Assert.All(track.GetVisualDescendants().OfType<Control>(), c => Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(c)));
        window.Close();
    });

    [Fact]
    [Trait("Req", "UI-UPDATE-009")] // App/Views/Settings/AboutUpdateViews.swift:395,450-487,494-524 (with existing seeded-field tests)
    [Trait("Req", "UI-UPDATE-007")] // App/Views/Settings/AboutUpdateViews.swift:450-491
    [Trait("Req", "UI-SETTINGS-058")] // App/Views/Settings/AboutUpdateViews.swift:450-491
    public void Real_sparkles_use_source_insets_diameter_drift_twinkle_clip_thirty_hertz_and_pause() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch); using var time = SwiftClock.Provider.Override(clock);
        var frames = new ManualFrameSource(); using var frameScope = FrameClock.Source.Override(frames);
        using var scrolling = ScrollActivity.Shared.Override(new ScrollActivity());
        var slider = new UpdateProgressSlider(0.5) { Width = 104 }; var window = Show(slider);
        var sparkles = slider.GetVisualDescendants().OfType<Ellipse>().Where(e => e.Classes.Contains("update-sparkle")).ToArray(); Assert.Equal(14, sparkles.Length);
        var first = sparkles[0];
        // Hand-evaluated first SplitMix64 particle (see SecondTests) substituted into :472-487 at t=0:
        // x=5 + .14227714622904464*(104-10) + sin(3.962854788881442*2.1)*1.2641436300426923 - 1.415433605072985/2;
        // y=3 + .57059065349669102*(14-6) - size/2; alpha=.08+.72*(.5+.5*sin(phase))^2.
        Assert.Equal(1.415433605072985, first.Width, 10); Assert.Equal(first.Width, first.Height);
        Assert.Equal(18.794540432495445, Canvas.GetLeft(first), 9); Assert.Equal(6.857008425437035, Canvas.GetTop(first), 9);
        Assert.Equal(0.0929277119314875, first.Opacity, 10); Assert.Equal(Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(first.Fill).Color);
        Assert.NotNull(Assert.IsType<Grid>(Assert.Single(slider.GetVisualDescendants().OfType<FillCanvas>()).Child).Clip);
        clock.Advance(TimeSpan.FromMilliseconds(20)); frames.Advance(TimeSpan.FromMilliseconds(20)); Dispatcher.UIThread.RunJobs();
        Assert.Equal(0.0929277119314875, first.Opacity, 10); // 20ms < 1/30s: no draw
        clock.Advance(TimeSpan.FromMilliseconds(14)); frames.Advance(TimeSpan.FromMilliseconds(14)); Dispatcher.UIThread.RunJobs();
        Assert.Equal(18.783640949596222, Canvas.GetLeft(first), 9); Assert.Equal(0.08936680811000929, first.Opacity, 10); // same equations at t=.034
        var id = Guid.NewGuid(); ScrollActivity.Shared.Current.SetScrolling(true, id); Dispatcher.UIThread.RunJobs();
        var frozen = first.Opacity; clock.Advance(TimeSpan.FromSeconds(1)); frames.Advance(TimeSpan.FromSeconds(1)); Dispatcher.UIThread.RunJobs(); Assert.Equal(frozen, first.Opacity);
        ScrollActivity.Shared.Current.SetScrolling(false, id); Dispatcher.UIThread.RunJobs();
        EnvironmentValues.SetAccessibilityReduceMotion(slider, true);
        // The actual production reduction entry is currently mount-time, like Swift's environment evaluation.
        window.Content = null; window.Content = slider; Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(slider.GetVisualDescendants().OfType<Ellipse>(), e => e.Classes.Contains("update-sparkle"));
        Assert.Equal(0, frames.Pending); window.Close();
    });

    [Fact]
    [Trait("Req", "UI-UPDATE-008")] // App/Views/Settings/AboutUpdateViews.swift:305-379,412-428
    [Trait("Req", "UI-SETTINGS-058")] // App/Views/Settings/AboutUpdateViews.swift:366-375
    public void Production_smoothed_head_glides_monotonically_at_sixty_hertz_and_stops_when_settled_or_scrolling() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch); using var time = SwiftClock.Provider.Override(clock);
        var frames = new ManualFrameSource(); using var frameScope = FrameClock.Source.Override(frames); using var scrolling = ScrollActivity.Shared.Override(new ScrollActivity());
        var progress = UpdateInstallProgressFixtures.NewProgress(); progress.Begin(); progress.NoteDownloadProgress(0.2); // .05+.50*.2=.15, AppUpdater.swift:109-113
        var smoothed = new SmoothedUpdateProgressSlider(progress.UpdateInstallProgressFractionTarget) { Width = 104 };
        EnvironmentValues.SetAccessibilityReduceMotion(smoothed, true); // only remove the separate 30Hz sparkle clock
        var window = Show(smoothed); var slider = Assert.IsType<UpdateProgressSlider>(smoothed.Child);
        Assert.Equal(0.15, slider.Fraction, 12); Assert.Equal(0, frames.Pending);
        progress.NoteDownloadProgress(1); Dispatcher.UIThread.RunJobs(); Assert.Equal(1, frames.Pending);
        clock.Advance(TimeSpan.FromMilliseconds(10)); frames.Advance(TimeSpan.FromMilliseconds(10)); Dispatcher.UIThread.RunJobs(); Assert.Equal(0.15, slider.Fraction, 12);
        clock.Advance(TimeSpan.FromMilliseconds(10)); frames.Advance(TimeSpan.FromMilliseconds(10)); Dispatcher.UIThread.RunJobs();
        // :331 dt=.020, response=.3: .15 + (.55-.15)*(1-exp(-.020/.3))
        Assert.Equal(0.1757972059873529, slider.Fraction, 12);
        var before = slider.Fraction; progress.NoteDownloadProgress(0.1); Dispatcher.UIThread.RunJobs(); Assert.Equal(0.55, progress.Fraction, 12); // raw target remains monotonic
        var id = Guid.NewGuid(); ScrollActivity.Shared.Current.SetScrolling(true, id); Dispatcher.UIThread.RunJobs();
        clock.Advance(TimeSpan.FromSeconds(1)); frames.Advance(TimeSpan.FromSeconds(1)); Dispatcher.UIThread.RunJobs(); Assert.Equal(before, slider.Fraction); Assert.Equal(0, frames.Pending);
        ScrollActivity.Shared.Current.SetScrolling(false, id); Dispatcher.UIThread.RunJobs();
        for (var i = 0; i < 30; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(250)); frames.Advance(TimeSpan.FromMilliseconds(250)); Dispatcher.UIThread.RunJobs();
            Assert.InRange(slider.Fraction, before, 0.55); before = slider.Fraction;
        }
        Assert.Equal(0.55, slider.Fraction, 12); Assert.Equal(0, frames.Pending); window.Close();
    });
}
