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

    // REVIEW (second round, 2026-10-04). OD-0076: "a card ... that changes size eases to the new size ... with no single-frame jump". At the settings
    // window's default size (SettingsView.cs:170 Width = 780, :122; the 212 pt sidebar, :178; the page stack's 24 pt each side, :249) the card is
    // 520 pt wide and the resting sentence "Marrow checks GitLab for new versions a few times a day. Not checked yet." takes more lines than
    // "Asking GitLab for the latest release…". The row's sentence swap replaces the words at the middle of the fade, so the card's height must
    // ease, not step by a whole line in one frame (the unit's ED-NEW-aboutupdateviews T1 leaves this "NOT eased (UNVERIFIED, rare)").
    [Fact]
    [Trait("Req", "PR-SETTINGS-240")] // REVIEW: the detail swap while a check starts; OD-0076 card size change
    [Trait("Rule", "R-TEST-4")] // partial: only that the card's height does not jump when the sentence rewraps
    public void REVIEW_The_card_does_not_jump_a_line_when_the_check_sentence_rewraps_at_the_default_settings_width() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var frames = new ManualFrameSource(); using var frameScope = FrameClock.Source.Override(frames);
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0")); using var locale = SwiftFormatters.CurrentLocale.Override("en_US");
        var checker = runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], "")); using var shared = UpdateChecker.OverrideShared(checker);
        var row = new AboutUpdateCheckRow();
        var card = new Marrow.UI.Chrome.ChromeCard(() => new StackPanel { Children = { row } });
        var window = new Avalonia.Controls.Window
        {
            Width = 568, Height = 300, UseLayoutRounding = false,
            Content = new Border { Margin = new Thickness(24, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Child = card },
        };
        window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        var heights = new List<double> { card.Bounds.Height };
        Set(checker, nameof(UpdateChecker.IsChecking), true); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        for (var i = 0; i < 20; i++)
        {
            runtime.Time.Advance(TimeSpan.FromMilliseconds(16)); frames.Advance(TimeSpan.FromMilliseconds(16)); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            heights.Add(card.Bounds.Height);
        }
        var travel = Math.Abs(heights[^1] - heights[0]);
        var largest = heights.Zip(heights.Skip(1), (a, b) => Math.Abs(b - a)).Max();
        Assert.True(travel < 0.5 || largest < travel * 0.5,
            $"the card goes from {heights[0]} to {heights[^1]} pt with a single-frame step of {largest} pt (frames: {string.Join(", ", heights)}): a cut (OD-0076)");
        window.Close();
    });
}
