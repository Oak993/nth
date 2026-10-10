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
using Marrow.UI.Port.Nth;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/AboutUpdateViews.swift (B3 52bc792), :305-524.
[Collection("Headless")]
public sealed class AboutUpdateViewsProofSliderTests(HeadlessSession headless)
{
    private static Avalonia.Controls.Window Show(Control slider)
    {
        var window = new Avalonia.Controls.Window { Width = 220, Height = 80, Content = slider, UseLayoutRounding = false };
        window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        return window;
    }

    [Theory]
    [Trait("Req", "UI-SETTINGS-058")] // the install face's control (the Mac's slider is replaced by the nth capsule, motion pack step 10)
    [Trait("Req", "UI-UPDATE-007")]
    [InlineData(-1, 0)] [InlineData(0, 0)] [InlineData(0.012, 0.012)] [InlineData(0.5, 0.5)] [InlineData(1.5, 1)]
    public void The_install_control_is_a_168_by_26_capsule_with_the_fraction_read_back_and_a_read_only_progress_peer(double fraction, double shown) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var slider = new UpdateProgressSlider(fraction) { Width = 168 }; var window = Show(slider);
        Assert.Equal(26, slider.Bounds.Height);
        var capsule = Assert.IsType<Marrow.App.Port.Updates.UpdateInstallCapsule>(slider.Child);
        Assert.Equal((168.0, 26.0), (capsule.Bounds.Width, capsule.Bounds.Height)); // step 10: "168 x 26, r 13"
        Assert.Equal(shown, capsule.Value, 12); // shown part way, it is there at once (no spring from nothing)
        Assert.Equal(shown, capsule.Shown, 12);
        Assert.False(capsule.IsHitTestVisible); // not a button: the install runs by itself
        Assert.Equal(fraction >= 1, capsule.IsDone);
        Assert.Equal("Updating Marrow", AutomationProperties.GetName(slider));
        var peer = ControlAutomationPeer.CreatePeerForElement(slider)!; Assert.Equal(AutomationControlType.ProgressBar, peer.GetAutomationControlType());
        Assert.True(Assert.IsAssignableFrom<IValueProvider>(peer).IsReadOnly); Assert.IsNotType<Button>(slider);
        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(capsule)); // the control's own name and value say it
        window.Close();
    });

    [Fact]
    [Trait("Req", "UI-UPDATE-008")] // the install's fraction reaches the capsule at once, and the capsule springs to it on the render thread: the UI thread runs no frame loop
    public void The_smoothed_control_hands_each_new_fraction_to_the_capsule_and_asks_for_no_frames() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch); using var time = SwiftClock.Provider.Override(clock);
        var frames = new ManualFrameSource(); using var frameScope = FrameClock.Source.Override(frames);
        var progress = UpdateInstallProgressFixtures.NewProgress(); progress.Begin(); progress.NoteDownloadProgress(0.2); // .05+.50*.2=.15, AppUpdater.swift:109-113
        var smoothed = new SmoothedUpdateProgressSlider(progress.UpdateInstallProgressFractionTarget) { Width = 168 };
        var window = Show(smoothed); var slider = Assert.IsType<UpdateProgressSlider>(smoothed.Child);
        var capsule = Assert.IsType<Marrow.App.Port.Updates.UpdateInstallCapsule>(slider.Child);
        Assert.Equal(0.15, slider.Fraction, 12); Assert.Equal(0.15, capsule.Value, 12); Assert.Equal(0, frames.Pending);
        progress.NoteDownloadProgress(1); Dispatcher.UIThread.RunJobs();
        Assert.Equal(0.55, slider.Fraction, 12); Assert.Equal(0.55, capsule.Value, 12); // the raw target, not a chase towards it
        Assert.Equal(0, frames.Pending); // no UI-thread frame loop (the pack animates on the render thread)
        // step 3 spring from the old value at the moment of the change: critically damped, stiffness 110
        clock.Advance(TimeSpan.FromMilliseconds(100));
        var w = NthSpring.Omega; var expected = 0.55 + (0.15 - 0.55) * (1 + w * 0.1) * Math.Exp(-w * 0.1);
        Assert.Equal(expected, capsule.Shown, 6);
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(0.55, capsule.Shown, 6);
        progress.NoteDownloadProgress(0.1); Dispatcher.UIThread.RunJobs(); Assert.Equal(0.55, progress.Fraction, 12); // raw target remains monotonic
        window.Close();
    });

    [Fact]
    [Trait("Req", "UI-UPDATE-008")]
    public void At_100_percent_the_capsule_is_done_and_before_it_is_not() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var slider = new UpdateProgressSlider(0.4) { Width = 168 }; var window = Show(slider);
        var capsule = Assert.IsType<Marrow.App.Port.Updates.UpdateInstallCapsule>(slider.Child);
        Assert.False(capsule.IsDone);
        slider.SetFraction(0.999); Assert.False(capsule.IsDone);
        slider.SetFraction(1); Assert.True(capsule.IsDone); Assert.Equal(1, capsule.Value);
        window.Close();
    });
}
