using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/AboutUpdateViews.swift (B3 52bc792), :241-251 (popover presentation), :638-682
// (the pill's ready face), :795-818 (the celebration sweep) and :821-845 (the Software update row); App/UpdateChecker.swift:156-187. OD-0012 I1 for the empty feed. The checks here run against a
// loopback socket that answers only when polled (LoopbackConnectionCounter), so a check stays in flight until the test
// lets it end; no request leaves the machine.
[Collection("Headless")]
public sealed class AboutUpdateViewsRecordTests(HeadlessSession headless)
{
    private static Avalonia.Controls.Window Show(Control view)
    {
        var window = new Avalonia.Controls.Window { Content = view, Width = 500, Height = 400, UseLayoutRounding = false };
        window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void Set(object target, string property, object? value) =>
        target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public)!.SetValue(target, value);

    private static void Tap(Control control, Avalonia.Controls.Window window)
    {
        var at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
        window.MouseMove(at, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
        window.MouseDown(at, MouseButton.Left, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
        window.MouseUp(at, MouseButton.Left, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
    }

    // The row rebuilds its button whenever the checker's state changes, so it is looked up again each time.
    private static Button CheckButton(AboutUpdateCheckRow row) => Assert.Single(row.GetVisualDescendants().OfType<Button>());

    private static string Label(AboutUpdateCheckRow row) =>
        Assert.Single(CheckButton(row).GetVisualDescendants().OfType<TextBlock>()).Text!;

    private static string Detail(AboutUpdateCheckRow row)
    {
        var chrome = Assert.IsType<ChromeRow>(row.Child);
        // The detail is the row's detail text outside the button (Chrome.SettingsRowDetailSize, ED-NEW-textsize-2; the button's own label is 11 pt, the small control size).
        return Assert.Single(chrome.GetVisualDescendants().OfType<TextBlock>(), t => t.FontSize == Chrome.SettingsRowDetailSize && t.FindAncestorOfType<Button>() is null).Text!;
    }

    // Avalonia raises Popup.Opened and Popup.Closed from the open popup itself; the headless platform cannot open one
    // (HeadlessFixtures.RecordingPresenter), so the event the platform would raise is raised here on its own handlers (ED-119).
    private static void Raise(Popup popup, string name)
    {
        var field = typeof(Popup).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
        var handler = (Delegate)field.GetValue(popup)!;
        var argument = handler.Method.GetParameters()[1].ParameterType; // EventArgs for Opened, the popup's own close args for Closed
        handler.DynamicInvoke(popup, Activator.CreateInstance(argument, nonPublic: true));
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-071")] // App/Views/Settings/AboutUpdateViews.swift:826-833: Checking... and disabled while it runs, Check now again after
    [Trait("Req", "UI-UPDATE-022")] // App/Views/Settings/AboutUpdateViews.swift:826-833: label and disabled state follow the same isChecking flag
    [Trait("Req", "PR-SETTINGS-240")] // App/Views/Settings/AboutUpdateViews.swift:836-844: the detail while asking, then the error ahead of the checked phrase
    [Trait("Req", "PR-UPDATE-076")] // App/Views/Settings/AboutUpdateViews.swift:822-845: the running check cannot be cancelled, the button is disabled instead
    public void A_pressed_Check_now_reads_Checking_and_is_disabled_while_the_check_runs_then_returns_with_the_error_in_front() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); var source = new ManualFrameSource(); using var frames = FrameClock.Source.Override(source);
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0")); using var locale = SwiftFormatters.CurrentLocale.Override("en_US");
        // OD-0076: the button and the sentence swap over 0.18 s; each state is read once that swap has finished (the leaving button is gone).
        void Settle() { runtime.Time.Advance(TimeSpan.FromMilliseconds(300)); source.Advance(TimeSpan.FromMilliseconds(300)); Dispatcher.UIThread.RunJobs(); }
        using var feed = new LoopbackConnectionCounter(completeWithInvalidJson: true);
        var checker = runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], feed.Url("/releases"))); using var shared = UpdateChecker.OverrideShared(checker);
        var row = new AboutUpdateCheckRow(); var window = Show(row);
        Assert.Equal("Check now", Label(row)); Assert.True(CheckButton(row).IsEnabled);
        Assert.Equal("nth checks GitHub for new versions a few times a day. Not checked yet.", Detail(row));

        Tap(CheckButton(row), window); // :827-829 Task { await checker.check() }
        Assert.True(checker.IsChecking); Settle();
        Assert.Equal("Checking…", Label(row)); // :827 one U+2026
        Assert.False(CheckButton(row).IsEnabled); // :832
        Assert.Equal("Asking GitHub for the latest release…", Detail(row)); // :837
        Tap(CheckButton(row), window); // a press on the disabled button does nothing
        Assert.Equal("Checking…", Label(row)); Assert.False(CheckButton(row).IsEnabled);

        // End the in-flight check with one loopback response containing invalid JSON (no HTTP transport retry).
        var finished = new TaskCompletionSource<bool>();
        using var observe = new Tracked<bool>(() => checker.LastCheckedAt is not null && !checker.IsChecking, done => { if (done) finished.TrySetResult(true); });
        var connections = 0;
        for (var wait = 0; wait < 500 && connections == 0; wait++) { connections = feed.Connections(); if (connections == 0) Thread.Sleep(20); }
        Assert.Equal(1, connections); // the one press made one request
        HeadlessSession.RunDispatcherUntil(finished.Task, TimeSpan.FromSeconds(30));
        Dispatcher.UIThread.RunJobs();
        var error = Assert.IsType<string>(checker.LastError); Settle();
        Assert.Equal("Check now", Label(row)); Assert.True(CheckButton(row).IsEnabled);
        Assert.Equal(error + " Checked " + SwiftFormatters.RelativeNamed(checker.LastCheckedAt!.Value) + ".", Detail(row)); // :839
        window.Close();
    });

    [Fact]
    [Trait("Req", "UI-UPDATE-015")] // App/Views/Settings/AboutUpdateViews.swift:241-251: the popover shows the section with the release's version, wearing presentedChrome
    public void A_card_popover_opens_with_that_section_and_the_release_version_in_presented_chrome() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var recorder = new RecordingPresenter(); using var presentation = Presentation.Presenter.Override(recorder);
        runtime.OnMain(() =>
        {
            var sections = new[]
            {
                new UpdateReleaseNotesDigest.Section { Category = ReleaseNoteCategory.NewFeatures, Bullets = new ValueList<string>(["Feature one"]), RollupCount = 0 },
                new UpdateReleaseNotesDigest.Section { Category = ReleaseNoteCategory.BugFixes, Bullets = new ValueList<string>(["Fix one", "Fix two"]), RollupCount = 0 },
            };
            var cards = new UpdateReleaseSectionCards(new UpdateReleaseNotesDigest { Sections = new ValueList<UpdateReleaseNotesDigest.Section>(sections) }, "9.0.0");
            var window = Show(cards); var buttons = cards.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.False(buttons[2].IsEnabled); // Refinements has no section: nothing to present
            Tap(buttons[1], window);
            var (popup, presented) = recorder.Calls.Last(); Assert.True(presented);
            Raise(popup, "Opened"); // :245-250 the popover content is built when it opens
            var content = Assert.IsType<UpdateReleaseSectionPopover>(popup.Child);
            Assert.True(Popovers.GetIsCapsuleButtonBorderShape(content)); // :250 .presentedChrome() capsule button shape
            Assert.Equal(ThemeManager.Spec.Accent, Popovers.GetTint(content)); // :250 tinted with the theme accent
            var texts = LogicalQuery.SelfAndDescendants<TextBlock>(content).Select(t => t.Text).ToArray(); // the popup is not in a visual tree headless
            Assert.Equal(new[] { "Bug fixes", "9.0.0", "Fix one", "Fix two" }, texts); // :247 section and the release's version
            window.Close();
        });
    });

    [Fact]
    [Trait("Req", "UI-UPDATE-015")] // App/Views/Settings/AboutUpdateViews.swift:242-243: an outside click (the popup closing itself) clears the presented card
    public void Dismissing_the_popover_from_outside_clears_the_presented_card_so_the_next_tap_opens_it_again() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var recorder = new RecordingPresenter(); using var presentation = Presentation.Presenter.Override(recorder);
        runtime.OnMain(() =>
        {
            var section = new UpdateReleaseNotesDigest.Section { Category = ReleaseNoteCategory.NewFeatures, Bullets = new ValueList<string>(["Feature one"]), RollupCount = 0 };
            var cards = new UpdateReleaseSectionCards(new UpdateReleaseNotesDigest { Sections = new ValueList<UpdateReleaseNotesDigest.Section>([section]) }, "9.0.0");
            var window = Show(cards); var button = cards.GetVisualDescendants().OfType<Button>().First();
            Tap(button, window);
            var (popup, presented) = recorder.Calls.Last(); Assert.True(presented);
            Raise(popup, "Closed"); // the platform closes a light-dismissed popup: :243 set(false) clears presented
            Dispatcher.UIThread.RunJobs();
            Assert.False(recorder.Calls.Last().Presented);
            Tap(button, window); // presented is nil again, so this tap opens it (a stale value would close it)
            Assert.True(recorder.Calls.Last().Presented); Assert.Same(popup, recorder.Calls.Last().Popup);
            window.Close();
        });
    });

    [Fact]
    [Trait("Req", "UI-UPDATE-005")] // App/Views/Settings/AboutUpdateViews.swift:671: .animation(reduceMotion ? nil : .easeInOut(duration: 0.18), value: isReady)
    public void Under_Reduce_Motion_the_ready_face_and_the_standing_face_swap_at_once_without_the_ease() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
        var checker = runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], "")); using var shared = UpdateChecker.OverrideShared(checker);
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        var blue = new SwiftColor(0.040, 0.478, 1.000); // Chrome.swift:86
        AboutVersionPill pill = null!; Avalonia.Controls.Window window = null!;
        (Border Face, SwiftImage Icon, TextBlock Title) Parts()
        {
            Dispatcher.UIThread.RunJobs();
            var face = Assert.Single(pill.GetVisualDescendants().OfType<Border>(), b => b.Child is StackPanel); var row = Assert.IsType<StackPanel>(face.Child);
            return (face, Assert.IsType<SwiftImage>(row.Children[0]), Assert.IsType<TextBlock>(row.Children[1]));
        }
        runtime.OnMain(() =>
        {
            pill = new AboutVersionPill(); EnvironmentValues.SetAccessibilityReduceMotion(pill, true); window = Show(pill);
            Set(checker, nameof(UpdateChecker.Update), AboutUpdateViewsProofLifecycleTests.Release());
        });
        runtime.OnMain(() =>
        {
            var (face, icon, title) = Parts(); var theme = face.ActualThemeVariant;
            Assert.Equal("Update & restart", title.Text);
            // No time has passed: with the ease suppressed the ready colours and the wash are already complete.
            Assert.Equal(blue.Opacity(0.2).Resolve(theme), Assert.IsAssignableFrom<ISolidColorBrush>(face.Background).Color);
            Assert.Equal(blue.Resolve(theme), Assert.IsAssignableFrom<ISolidColorBrush>(icon.Foreground).Color);
            Assert.Equal(blue.Resolve(theme), Assert.IsAssignableFrom<ISolidColorBrush>(title.Foreground).Color);
            Set(checker, nameof(UpdateChecker.Update), null);
        });
        runtime.OnMain(() =>
        {
            var (face, icon, title) = Parts(); var theme = face.ActualThemeVariant;
            Assert.Equal("Not checked yet", title.Text);
            Assert.Equal(blue.Opacity(0).Resolve(theme), Assert.IsAssignableFrom<ISolidColorBrush>(face.Background).Color);
            Assert.Equal(Chrome.PrimaryText.Resolve(theme), Assert.IsAssignableFrom<ISolidColorBrush>(title.Foreground).Color);
            Assert.Equal(Chrome.PrimaryText.Opacity(0.7).Resolve(theme), Assert.IsAssignableFrom<ISolidColorBrush>(icon.Foreground).Color);
            window.Close();
        });
    });

    [Theory]
    [Trait("Req", "PR-UPDATE-061")] // App/Views/Settings/AboutUpdateViews.swift:801,808,815: a cancelled sweep goes no further, in whichever sleep it was cancelled
    [InlineData(100)] [InlineData(2000)]
    public void A_sweep_cancelled_in_a_sleep_leaves_a_celebration_that_starts_afterwards_alone(int cancelAfterMilliseconds) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
        var progress = UpdateInstallProgressFixtures.NewProgress(); using var running = UpdateInstallProgress.Running.Override(progress);
        using var shared = UpdateChecker.OverrideShared(runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], "")));
        var page = new AboutSoftwareUpdateSection(); var host = new StackPanel { Children = { page } }; Avalonia.Controls.Window window = null!;
        runtime.OnMain(() => { progress.ArmCelebration(); window = Show(host); });
        runtime.AdvanceMilliseconds(cancelAfterMilliseconds); // 100: in the 550 ms sleep; 2000: in the last, 700 ms sleep
        runtime.OnMain(() =>
        {
            Assert.Equal(UpdateInstallProgress.Phase.Celebrating, progress.UpdateInstallProgressPhase);
            host.Children.Remove(page); // :737-743 cancels the sweep and completes the celebration at once
            Assert.Equal(UpdateInstallProgress.Phase.Idle, progress.UpdateInstallProgressPhase);
            progress.ArmCelebration(); Assert.True(progress.ConsumePendingCelebration()); // a new celebration, not driven by the old sweep
            Assert.Equal(UpdateInstallProgress.Phase.Celebrating, progress.UpdateInstallProgressPhase);
        });
        runtime.AdvanceMilliseconds(1); // the cancelled sweep wakes and reaches :815
        runtime.OnMain(() =>
        {
            Assert.Equal(UpdateInstallProgress.Phase.Celebrating, progress.UpdateInstallProgressPhase);
            Assert.Equal(0.86, progress.Fraction); // the old sweep did not push the new celebration to 1 (:802)
            window.Close();
        });
        runtime.OnMain(() => progress.CompleteCelebration());
    });
}
