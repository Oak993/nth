using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// REVIEW (adversarial review of unit blitz-aboutupdateviews, 2026-10-04). OD-0076 (porting/owner_decisions.md:712-719): "nothing on
// screen changes by a cut ... content that swaps crossfades ... Every transition runs on the frame clock (ED-237) with no
// single-frame jump, and the system's reduce-motion setting stills them." The unit's own ED-NEW-aboutupdateviews lists these
// swaps as OD-0076 transition debt T1-T3 (cuts). For the version pill's face swap the Swift itself animates the change:
// :671 `.animation(reduceMotion ? nil : .easeInOut(duration: 0.18), value: isReady)` wraps the `if isReady {...} else {...}`
// branches (:643-660), whose removal and insertion take SwiftUI's default opacity transition under that animation.
// Each test looks one frame (16 ms) after the state change, with motion not reduced: a crossfade still shows the old content
// (or the new content is not yet fully opaque); a cut shows only the new content at full opacity.
[Collection("Headless")]
public sealed class AboutUpdateViewsReviewTests(HeadlessSession headless)
{
    private static void Set(object target, string property, object? value) =>
        target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public)!.SetValue(target, value);

    private static double EffectiveOpacity(Visual visual)
    {
        var opacity = 1.0;
        for (Visual? v = visual; v is not null; v = v.GetVisualParent()) opacity *= v.Opacity;
        return opacity;
    }

    private static bool Visible(Control root, Func<Visual, bool> match) =>
        root.GetVisualDescendants().Where(match).Any(v => EffectiveOpacity(v) > 0.01 && v.IsVisible);

    private static double Opacity(Control root, Func<Visual, bool> match) =>
        root.GetVisualDescendants().Where(match).Select(EffectiveOpacity).DefaultIfEmpty(0).Max();

    private static Avalonia.Controls.Window Show(Control view)
    {
        var window = new Avalonia.Controls.Window { Content = view, Width = 600, Height = 200, UseLayoutRounding = false };
        window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
        return window;
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-071")] // REVIEW: OD-0076 - "Check now" to "Checking…" swaps by a cut (ED-NEW-aboutupdateviews T1)
    [Trait("Rule", "R-TEST-4")] // partial: only the label crossfade clause added by OD-0076
    public void REVIEW_Check_now_label_crossfades_into_Checking_rather_than_cutting() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var frames = new ManualFrameSource(); using var frameScope = FrameClock.Source.Override(frames);
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
        var checker = runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], "")); using var shared = UpdateChecker.OverrideShared(checker);
        var row = new AboutUpdateCheckRow(); var window = Show(row);
        Assert.True(Visible(row, v => v is TextBlock { Text: "Check now" }));
        Set(checker, nameof(UpdateChecker.IsChecking), true); Dispatcher.UIThread.RunJobs();
        frames.Advance(TimeSpan.FromMilliseconds(16)); Dispatcher.UIThread.RunJobs();
        var oldStillShowing = Visible(row, v => v is TextBlock { Text: "Check now" });
        var newOpacity = Opacity(row, v => v is TextBlock { Text: "Checking…" });
        Assert.True(oldStillShowing || newOpacity < 0.99,
            $"one frame after the check started, \"Check now\" is gone and \"Checking…\" is at opacity {newOpacity}: a cut, not a crossfade (OD-0076)");
        window.Close();
    });

    [Fact]
    [Trait("Req", "UI-UPDATE-005")] // REVIEW: :671 + OD-0076 - the standing/ready face swap crossfades its symbol and title (ED-NEW-aboutupdateviews T2)
    [Trait("Rule", "R-TEST-4")] // partial: only the content crossfade of the face change
    public void REVIEW_The_ready_face_crossfades_in_over_the_standing_face() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource(); using var frameScope = FrameClock.Source.Override(frames);
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
        var checker = runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], "")); using var shared = UpdateChecker.OverrideShared(checker);
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        AboutVersionPill pill = null!; Avalonia.Controls.Window window = null!;
        runtime.OnMain(() =>
        {
            pill = new AboutVersionPill(); window = Show(pill);
            Assert.True(Visible(pill, v => v is TextBlock { Text: "Not checked yet" }));
            Set(checker, nameof(UpdateChecker.Update), AboutUpdateViewsProofLifecycleTests.Release());
        });
        runtime.OnMain(() =>
        {
            Dispatcher.UIThread.RunJobs();
            runtime.Time.Advance(TimeSpan.FromMilliseconds(16)); frames.Advance(TimeSpan.FromMilliseconds(16)); Dispatcher.UIThread.RunJobs();
            var oldStillShowing = Visible(pill, v => v is TextBlock { Text: "Not checked yet" } || v is SwiftImage { SymbolName: "clock" });
            var newOpacity = Opacity(pill, v => v is TextBlock { Text: "Update & restart" });
            Assert.True(oldStillShowing || newOpacity < 0.99,
                $"16 ms into the 0.18 s ease the standing's title and clock are gone and \"Update & restart\" is at opacity {newOpacity}: the branches swap by a cut");
            window.Close();
        });
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-056")] // REVIEW: OD-0076 - the installing slider and the standing face swap by a cut (ED-NEW-aboutupdateviews T3)
    [Trait("Rule", "R-TEST-4")] // partial: only the slider-to-face crossfade added by OD-0076
    public void REVIEW_The_slider_crossfades_into_the_standing_face_when_the_celebration_completes() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var frames = new ManualFrameSource(); using var frameScope = FrameClock.Source.Override(frames);
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
        var checker = runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], "")); using var shared = UpdateChecker.OverrideShared(checker);
        var progress = UpdateInstallProgressFixtures.NewProgress(); using var running = UpdateInstallProgress.Running.Override(progress);
        progress.ArmCelebration(); Assert.True(progress.ConsumePendingCelebration());
        var pill = new AboutVersionPill(); var window = Show(pill);
        Assert.Single(pill.GetVisualDescendants().OfType<UpdateProgressSlider>());
        progress.CompleteCelebration(); Dispatcher.UIThread.RunJobs();
        frames.Advance(TimeSpan.FromMilliseconds(16)); Dispatcher.UIThread.RunJobs();
        var sliderStillShowing = Visible(pill, v => v is UpdateProgressSlider);
        var faceOpacity = Opacity(pill, v => v is TextBlock { Text: "Not checked yet" });
        Assert.True(sliderStillShowing || faceOpacity < 0.99,
            $"one frame after the celebration completed the slider is gone and the standing is at opacity {faceOpacity}: a cut (OD-0076)");
        window.Close();
    });
}
