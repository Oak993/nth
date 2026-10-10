using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/AboutUpdateViews.swift (B3 52bc792), :581-583,638-845;
// App/AppUpdater.swift:89-225,261-283. The actual page/pill/checker/updater run against real manual clocks.
[Collection("Headless")]
public sealed class AboutUpdateViewsProofLifecycleTests(HeadlessSession headless)
{
    internal static AvailableUpdate Release(string notes = "", string? url = null) => new()
    {
        Version = "9.0.0", Tag = "v9.0.0", Notes = notes,
        DownloadURL = SwiftURL.Create(url ?? "http://127.0.0.1:1/Marrow.exe")!,
        PageURL = SwiftURL.Create("https://gitlab.com/droppyformac1/droppy-code/-/releases/v9.0.0")!,
    };

    private static void Set(object target, string property, object? value) =>
        target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public)!.SetValue(target, value);

    private static object? Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

    private static Avalonia.Controls.Window Show(Control view)
    {
        var window = new Avalonia.Controls.Window { Content = view, Width = 600, Height = 500 };
        window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); return window;
    }

    private static void Click(Button button, Avalonia.Controls.Window window)
    {
        var at = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
        window.MouseMove(at, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
        window.MouseDown(at, MouseButton.Left, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
        Assert.True(button.IsPressed); // Prove real input reached the production button, before checking its action.
        window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-242")] // App/Views/Settings/AboutUpdateViews.swift:617-621,693-703
    [Trait("Decision", "ED-071")] // App/Views/Settings/AboutUpdateViews.swift:821-845; OD-0012 I1; partial until small glass style/capture is proven.
    [Trait("Req", "PR-SETTINGS-240")] // App/Views/Settings/AboutUpdateViews.swift:826-844; OD-0012 I1: empty feed, no request, "No update feed is set. " + checked suffix
    [Trait("Req", "PR-UPDATE-076")] // App/Views/Settings/AboutUpdateViews.swift:839; OD-0012 I1 error in front of the checked phrase
    [Trait("Req", "UI-SETTINGS-071")] // App/Views/Settings/AboutUpdateViews.swift:826-829; OD-0012 I1 click asks the checker, empty feed sends no request
    [Trait("Req", "UI-SETTINGS-056")] // App/Views/Settings/AboutUpdateViews.swift:617-621,693-703 (with geometry/modifier tests)
    [Trait("Req", "UI-UPDATE-003")] // App/Views/Settings/AboutUpdateViews.swift:617-621,693-703
    [Trait("Req", "PR-UPDATE-075")] // App/Views/Settings/AboutUpdateViews.swift:678-703
    public void Empty_feed_click_is_observed_to_send_zero_requests_and_updates_the_real_pill_and_persisted_row() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); var source = new ManualFrameSource(); using var frames = FrameClock.Source.Override(source);
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0")); using var probe = new AboutUpdateHttpProbe();
        var checker = runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], "")); using var shared = UpdateChecker.OverrideShared(checker);
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        var row = new AboutUpdateCheckRow(); var pill = new AboutVersionPill(); Avalonia.Controls.Window window = null!;
        runtime.OnMain(() => { window = Show(new StackPanel { Children = { pill, row } }); Click(Assert.Single(row.GetVisualDescendants().OfType<Button>()), window); });
        runtime.RunUntilIdle();
        runtime.OnMain(() =>
        {
            Dispatcher.UIThread.RunJobs(); AboutSwap.Settle(runtime, source); Assert.Equal(0, probe.Requests);
            Assert.Equal("No update feed is set.", checker.LastError); Assert.NotNull(checker.LastCheckedAt);
            Assert.Equal(checker.LastCheckedAt, runtime.Defaults.Object("updateChecker_lastChecked"));
            var stamp = "Checked " + SwiftFormatters.RelativeNamed(checker.LastCheckedAt!.Value) + "."; // :837; ED-096 named relative formatter
            Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "No update feed is set. " + stamp);
            Assert.Contains(pill.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Check failed");
            var button = Assert.Single(pill.GetVisualDescendants().OfType<Button>());
            Assert.Equal("No update feed is set.", ToolTip.GetTip(Assert.Single(pill.GetVisualDescendants().OfType<Control>(), c => ToolTip.GetTip(c) is not null))); // :674-680
            Assert.Equal("Nth 1.7.0, check failed", AutomationProperties.GetName(button)); // :702
            window.Close();
        });
        // Positive control: same production checker does reach the HTTP observer for a real loopback request.
        using var server = System.Threading.Tasks.Task.Run(() => new LoopbackFeedServer(_ => CannedResponse.Json("[]"))).GetAwaiter().GetResult();
        var positive = runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], server.Url("/releases")));
        var request = positive.Check(); HeadlessSession.RunDispatcherUntil(request, TimeSpan.FromSeconds(20)); request.GetAwaiter().GetResult(); Assert.Single(server.Requests); Assert.Equal(1, probe.Requests);
    });

    [Fact]
    [Trait("Decision", "ED-071")] // App/Views/Settings/AboutUpdateViews.swift:826-829; UpdateChecker.swift:178-180; partial row proof.
    [Trait("Req", "PR-UPDATE-076")] // App/Views/Settings/AboutUpdateViews.swift:826-829: the button runs a foreground check, never a background one (no toast)
    [Trait("Req", "UI-UPDATE-022")] // App/Views/Settings/AboutUpdateViews.swift:826-829; UpdateChecker.swift:178-180: foreground, cannot raise a notification
    public void Successful_pointer_check_is_foreground_and_never_toasts_a_newer_release() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); var source = new ManualFrameSource(); using var frames = FrameClock.Source.Override(source);
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
        var toasts = new RecordingToastCenter(); using var toastScope = UpdateToastCenter.Ambient.Override(toasts);
        using var server = System.Threading.Tasks.Task.Run(() => new LoopbackFeedServer(_ => CannedResponse.Json(ReleaseFeed.Feed(
            ReleaseFeed.Release("v9.0.0", [ReleaseFeed.Link("Marrow.exe", "http://127.0.0.1:1/artifact.exe")]))))).GetAwaiter().GetResult();
        var checker = runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], server.Url("/releases"))); using var shared = UpdateChecker.OverrideShared(checker);
        var row = new AboutUpdateCheckRow(); Avalonia.Controls.Window window = null!;
        var finished = new TaskCompletionSource<bool>();
        using var observe = new Tracked<bool>(() => checker.LastCheckedAt is not null && !checker.IsChecking, done => { if (done) finished.TrySetResult(true); });
        runtime.OnMain(() => { window = Show(row); Click(Assert.Single(row.GetVisualDescendants().OfType<Button>()), window); });
        HeadlessSession.RunDispatcherUntil(finished.Task, TimeSpan.FromSeconds(20));
        runtime.OnMain(() =>
        {
            Dispatcher.UIThread.RunJobs(); AboutSwap.Settle(runtime, source); Assert.True(checker.UpdateAvailable); Assert.Null(checker.LastError);
            Assert.Empty(toasts.Shown); Assert.DoesNotContain("updateChecker_notifiedVersion", runtime.Defaults.Writes);
            Assert.NotNull(runtime.Defaults.Object("updateChecker_release"));
            Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Nth 9.0.0 is ready to install. Checked " + SwiftFormatters.RelativeNamed(checker.LastCheckedAt!.Value) + ".");
            window.Close();
        });
    });

    [Fact]
    [Trait("Req", "PR-UPDATE-073")] // App/Views/Settings/AboutUpdateViews.swift:638-675; App/AppUpdater.swift:261-283
    [Trait("Req", "PR-SETTINGS-242")] // App/Views/Settings/AboutUpdateViews.swift:638-675
    [Trait("Req", "UI-SETTINGS-056")] // App/Views/Settings/AboutUpdateViews.swift:638-675
    [Trait("Decision", "ED-180")] // UI-UPDATE-005 partial: press renderer remains deferred, though the real pointer invokes installation.
    [Trait("Req", "UI-UPDATE-005")] // App/Views/Settings/AboutUpdateViews.swift:638-640,673-675: click starts the install, a second click is ignored, the button is gone once it runs
    public void Ready_pointer_click_runs_real_install_and_a_stale_second_click_reads_live_state_and_starts_nothing() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); var source = new ManualFrameSource(); using var frames = FrameClock.Source.Override(source);
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
        var progress = UpdateInstallProgressFixtures.NewProgress(); using var running = UpdateInstallProgress.Running.Override(progress);
        using var server = System.Threading.Tasks.Task.Run(() => new LoopbackFeedServer(_ => CannedResponse.Json("installer bytes"))).GetAwaiter().GetResult();
        var checker = runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], ""));
        runtime.OnMain(() => Set(checker, nameof(UpdateChecker.Update), Release(url: server.Url("/artifact")))); using var shared = UpdateChecker.OverrideShared(checker);
        var boundary = new FakeUpdateInstallBoundary { VersionToReturn = "9.0.0" }; var updater = new AppUpdater(boundary);
        var ambient = (Ambient<AppUpdater>)typeof(AppUpdater).GetField("running", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        using var updaterScope = ambient.Override(updater); var pill = new AboutVersionPill(); Avalonia.Controls.Window window = null!; Button original = null!;
        var finished = new TaskCompletionSource<bool>();
        using var observe = new Tracked<bool>(() => progress.UpdateInstallProgressPhase == UpdateInstallProgress.Phase.Handoff, done => { if (done) finished.TrySetResult(true); });
        runtime.OnMain(() =>
        {
            window = Show(pill); original = Assert.Single(pill.GetVisualDescendants().OfType<Button>()); Click(original, window);
            // A stale routed event reaches exactly the production click closure after the pointer button has already
            // disappeared. The round-1 flake (Ready_background_uses, Pill_faces_have_exact, Ready_pointer_click_runs,
            // 4-in-36) is consistent with the loopback download letting the first install reach Handoff and clear
            // installTask (AppUpdater.swift:264) inside Click's trailing RunJobs, so a closure reading the build-time
            // snapshot could start a second install and the test would only pass when that second GET had not yet
            // arrived; no isolated before-fix repro log exists for that exact mechanism. Swift :639 reads live state
            // (`guard let update = checker.update, isReady`), and a showing progress UI makes isReady false (:582): the
            // stale click must start nothing at all, which is what this test now proves regardless of the timing cause.
            var taskBefore = Field(updater, "installTask"); var phaseBefore = progress.UpdateInstallProgressPhase;
            original.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Same(taskBefore, Field(updater, "installTask")); Assert.Equal(phaseBefore, progress.UpdateInstallProgressPhase);
        });
        HeadlessSession.RunDispatcherUntil(finished.Task, TimeSpan.FromSeconds(20));
        runtime.OnMain(() =>
        {
            Dispatcher.UIThread.RunJobs(); AboutSwap.Settle(runtime, source); Assert.Single(server.Requests); // the ready button has faded out into the slider (OD-0076)
            Assert.Single(boundary.Calls, call => call.StartsWith("Stage(", StringComparison.Ordinal));
            Assert.Single(boundary.Calls, call => call.StartsWith("LaunchDetachedInstaller(", StringComparison.Ordinal));
            Assert.Empty(pill.GetVisualDescendants().OfType<Button>()); Assert.Single(pill.GetVisualDescendants().OfType<SmoothedUpdateProgressSlider>());
            window.Close();
        });
    });

    [Theory]
    [Trait("Req", "PR-SETTINGS-243")] // App/Views/Settings/AboutUpdateViews.swift:728-743,793-818 (with digest test)
    [Trait("Req", "PR-UPDATE-060")] // App/Views/Settings/AboutUpdateViews.swift:728-733; App/AppUpdater.swift:157-166
    [Trait("Req", "PR-UPDATE-063")] // App/Views/Settings/AboutUpdateViews.swift:737-743
    [Trait("Decision", "ED-084")] // PR-UPDATE-061 partial: inherited environment is not connected to the Windows setting on impl/b2.
    [Trait("Req", "PR-UPDATE-061")] // App/Views/Settings/AboutUpdateViews.swift:795-818: target 1.0 at 550 ms, reset at 2600 ms (1450 ms with Reduce Motion)
    [InlineData(false, false, 2600)] [InlineData(true, false, 1450)] [InlineData(false, true, 2600)]
    public void Page_consumes_cancels_old_work_mounts_caught_up_settles_and_never_replays(bool reduced, bool failureReset, int total) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); var frames = new ManualFrameSource(); using var frameScope = FrameClock.Source.Override(frames);
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
        var progress = UpdateInstallProgressFixtures.NewProgress(); using var running = UpdateInstallProgress.Running.Override(progress);
        var checker = runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], "")); using var shared = UpdateChecker.OverrideShared(checker);
        var pill = new AboutVersionPill(); var row = new AboutUpdateCheckRow(); var page = new AboutSoftwareUpdateSection();
        Avalonia.Controls.Window window = null!; TaskHandle oldWork = null!; TaskHandle cancelledSweep = null!;
        runtime.OnMain(() =>
        {
            progress.Begin(); if (failureReset) progress.Fail();
            oldWork = Assert.IsType<TaskHandle>(Field(progress, failureReset ? "failureReset" : "creepTask"));
            Assert.False(oldWork.IsCancelled); // real work running BEFORE consume, not merely a non-null field
            progress.ArmCelebration(); EnvironmentValues.SetAccessibilityReduceMotion(page, reduced);
            window = Show(new StackPanel { Children = { pill, row, page } });
        });
        runtime.OnMain(() =>
        {
            Dispatcher.UIThread.RunJobs(); Assert.False(progress.HasPendingCelebration);
            Assert.Null(Field(progress, "creepTask")); Assert.Null(Field(progress, "failureReset"));
            Assert.True(oldWork.IsCancelled); // AppUpdater.swift:162-163 stops the actual task, not just the stored handle
            Assert.Equal(UpdateInstallProgress.Phase.Celebrating, progress.UpdateInstallProgressPhase); Assert.Equal(0.86, progress.Fraction);
            Assert.Equal(0.86, AboutSwap.Live<UpdateProgressSlider>(pill).Fraction); // the face and the slider swap by a crossfade (OD-0076): the slider on show is the live one
            Assert.Equal("No update feed is set.", checker.LastError); Assert.NotNull(checker.LastCheckedAt);
        });
        runtime.AdvanceMilliseconds(549); runtime.OnMain(() => Assert.Equal(0.86, progress.Fraction));
        runtime.AdvanceMilliseconds(1); runtime.OnMain(() => Assert.Equal(1, progress.Fraction));
        runtime.AdvanceMilliseconds(total - 551); runtime.OnMain(() => Assert.Equal(UpdateInstallProgress.Phase.Celebrating, progress.UpdateInstallProgressPhase));
        runtime.AdvanceMilliseconds(1);
        runtime.OnMain(() =>
        {
            Dispatcher.UIThread.RunJobs(); Assert.Equal(UpdateInstallProgress.Phase.Idle, progress.UpdateInstallProgressPhase); Assert.Equal(0, progress.Fraction);
            AboutSwap.Settle(runtime, frames); // the slider fades out into the standing over 0.18 s (OD-0076); the phase and the fraction were read first
            Assert.Empty(pill.GetVisualDescendants().OfType<UpdateProgressSlider>());
            Assert.Contains(pill.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Check failed");
            // PR-SETTINGS-243 failure case: the page's own check failed, so the row's detail prefixes the error (:839, OD-0012 I1).
            Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(), t => t.Text!.StartsWith("No update feed is set. Checked ", StringComparison.Ordinal) && t.Text.EndsWith('.')); // the date was rendered at check time
            window.Close();
        });
        var checkedAt = runtime.OnMain(() => checker.LastCheckedAt); runtime.AdvanceMilliseconds(1000);
        runtime.OnMain(() => { window = Show(new StackPanel { Children = { new AboutVersionPill(), new AboutSoftwareUpdateSection() } }); });
        runtime.OnMain(() =>
        {
            Dispatcher.UIThread.RunJobs(); Assert.Equal(UpdateInstallProgress.Phase.Idle, progress.UpdateInstallProgressPhase); Assert.Equal(0, progress.Fraction);
            Assert.True(checker.LastCheckedAt > checkedAt); Assert.Empty(window.GetVisualDescendants().OfType<UpdateProgressSlider>()); window.Close();
            // Confirm that a NEW pending celebration really starts before exercising cancellation.
            progress.ArmCelebration(); window = Show(new StackPanel { Children = { new AboutVersionPill(), new AboutSoftwareUpdateSection() } });
        });
        runtime.OnMain(() =>
        {
            Dispatcher.UIThread.RunJobs(); Assert.False(progress.HasPendingCelebration); Assert.Equal(UpdateInstallProgress.Phase.Celebrating, progress.UpdateInstallProgressPhase);
            Assert.Single(window.GetVisualDescendants().OfType<UpdateProgressSlider>());
            cancelledSweep = Assert.IsType<TaskHandle>(Field(Assert.Single(window.GetVisualDescendants().OfType<AboutSoftwareUpdateSection>()), "celebration"));
            // Remove just the page, leaving the real accessory attached to observe the immediate standing.
            var stack = Assert.IsType<StackPanel>(window.Content); stack.Children.RemoveAt(1);
            Assert.Equal(UpdateInstallProgress.Phase.Idle, progress.UpdateInstallProgressPhase); Assert.Equal(0, progress.Fraction);
            Assert.True(cancelledSweep.IsCancelled); // :739 cancellation of the real sweep handle
        });
        runtime.OnMain(() =>
        {
            Dispatcher.UIThread.RunJobs(); AboutSwap.Settle(runtime, frames); // the slider has faded out into the standing (OD-0076); what is left of it is gone with it
            Assert.Empty(window.GetVisualDescendants().OfType<UpdateProgressSlider>()); Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Check failed");
            Assert.Equal(0, frames.Pending); // :737-743 no detached slider timeline survives
        });
        runtime.AdvanceMilliseconds(3000);
        runtime.OnMain(() => { Assert.Equal(UpdateInstallProgress.Phase.Idle, progress.UpdateInstallProgressPhase); Assert.Equal(0, progress.Fraction); window.Close(); });
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-243")] // App/Views/Settings/AboutUpdateViews.swift:734-736
    public void Page_parses_once_per_release_version_even_if_notes_or_other_render_inputs_change() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
        runtime.OnMain(() =>
        {
            var checker = new UpdateChecker(AppInfo.Current, [], ""); Set(checker, nameof(UpdateChecker.Update), Release("## New features\n- First version's bullet"));
            using var shared = UpdateChecker.OverrideShared(checker); using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
            var page = new AboutSoftwareUpdateSection(); var window = Show(page); var digest = Field(page, "digest");
            Set(checker, nameof(UpdateChecker.Update), Release("## New features\n- Changed notes, same version")); Dispatcher.UIThread.RunJobs();
            Assert.Same(digest, Field(page, "digest"));
            Set(checker, nameof(UpdateChecker.LastError), "Other input"); Dispatcher.UIThread.RunJobs(); Assert.Same(digest, Field(page, "digest"));
            Set(checker, nameof(UpdateChecker.Update), Release("## Bug fixes\n- New version's bullet") with { Version = "9.1.0" }); Dispatcher.UIThread.RunJobs();
            Assert.NotSame(digest, Field(page, "digest"));
            var changed = Assert.IsType<UpdateReleaseNotesDigest>(Field(page, "digest")); Assert.Equal("New version's bullet", Assert.Single(changed.Sections).Bullets[0]);
            window.Close();
        });
    });

    [Fact]
    [Trait("Decision", "ED-084")] // PR-UPDATE-061 partial: the phase guard at Swift :801, not system-setting integration
    [Trait("Req", "PR-SETTINGS-243")] // App/Views/Settings/AboutUpdateViews.swift:800-801 ("only if the progress is still in the celebrating phase")
    [Trait("Req", "PR-UPDATE-061")] // App/Views/Settings/AboutUpdateViews.swift:801: a phase change before the 550 ms guard aborts the sweep
    public void A_phase_change_before_the_550_millisecond_guard_ends_the_real_sweep() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var progress = UpdateInstallProgressFixtures.NewProgress(); using var running = UpdateInstallProgress.Running.Override(progress);
        using var checker = UpdateChecker.OverrideShared(runtime.OnMain(() => new UpdateChecker(ReleaseFeed.Running("1.7.0"), [], "")));
        var page = new AboutSoftwareUpdateSection(); Avalonia.Controls.Window window = null!; TaskHandle task = null!;
        runtime.OnMain(() => { progress.ArmCelebration(); window = Show(page); });
        runtime.OnMain(() => { Assert.Equal(UpdateInstallProgress.Phase.Celebrating, progress.UpdateInstallProgressPhase); task = Assert.IsType<TaskHandle>(Field(page, "celebration")); });
        runtime.AdvanceMilliseconds(100);
        runtime.OnMain(() => progress.CompleteCelebration());
        runtime.AdvanceMilliseconds(449); Assert.False(task.Completion.IsCompleted);
        runtime.AdvanceMilliseconds(1); Assert.True(task.Completion.IsCompleted);
        runtime.AdvanceMilliseconds(3000);
        runtime.OnMain(() => { Assert.Equal(UpdateInstallProgress.Phase.Idle, progress.UpdateInstallProgressPhase); Assert.Equal(0, progress.Fraction); window.Close(); });
    });
}
