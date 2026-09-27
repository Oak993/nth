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
    [Trait("Req", "UI-SETTINGS-056")] // App/Views/Settings/AboutUpdateViews.swift:617-621,693-703 (with geometry/modifier tests)
    [Trait("Req", "UI-UPDATE-003")] // App/Views/Settings/AboutUpdateViews.swift:617-621,693-703
    [Trait("Req", "PR-UPDATE-075")] // App/Views/Settings/AboutUpdateViews.swift:678-703
    public void Empty_feed_click_is_observed_to_send_zero_requests_and_updates_the_real_pill_and_persisted_row() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0")); using var probe = new AboutUpdateHttpProbe();
        var checker = runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], UpdateChecker.DefaultFeed)); using var shared = UpdateChecker.OverrideShared(checker);
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        var row = new AboutUpdateCheckRow(); var pill = new AboutVersionPill(); Avalonia.Controls.Window window = null!;
        runtime.OnMain(() => { window = Show(new StackPanel { Children = { pill, row } }); Click(Assert.Single(row.GetVisualDescendants().OfType<Button>()), window); });
        runtime.RunUntilIdle();
        runtime.OnMain(() =>
        {
            Dispatcher.UIThread.RunJobs(); Assert.Equal(0, probe.Requests);
            Assert.Equal("No update feed is set.", checker.LastError); Assert.NotNull(checker.LastCheckedAt);
            Assert.Equal(checker.LastCheckedAt, runtime.Defaults.Object("updateChecker_lastChecked"));
            var stamp = "Checked " + SwiftFormatters.RelativeNamed(checker.LastCheckedAt!.Value) + "."; // :837; ED-096 named relative formatter
            Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "No update feed is set. " + stamp);
            Assert.Contains(pill.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Check failed");
            var button = Assert.Single(pill.GetVisualDescendants().OfType<Button>());
            Assert.Equal("No update feed is set.", ToolTip.GetTip(button)); Assert.Equal("Marrow 1.7.0, check failed", AutomationProperties.GetName(button));
            window.Close();
        });
        // Positive control: same production checker does reach the HTTP observer for a real loopback request.
        using var server = System.Threading.Tasks.Task.Run(() => new LoopbackFeedServer(_ => CannedResponse.Json("[]"))).GetAwaiter().GetResult();
        var positive = runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], server.Url("/releases")));
        var request = positive.Check(); HeadlessSession.RunDispatcherUntil(request, TimeSpan.FromSeconds(20)); request.GetAwaiter().GetResult(); Assert.Single(server.Requests); Assert.Equal(1, probe.Requests);
    });

    [Fact]
    [Trait("Decision", "ED-071")] // App/Views/Settings/AboutUpdateViews.swift:826-829; UpdateChecker.swift:178-180; partial row proof.
    public void Successful_pointer_check_is_foreground_and_never_toasts_a_newer_release() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); using var frames = FrameClock.Source.Override(new ManualFrameSource());
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
            Dispatcher.UIThread.RunJobs(); Assert.True(checker.UpdateAvailable); Assert.Null(checker.LastError);
            Assert.Empty(toasts.Shown); Assert.DoesNotContain("updateChecker_notifiedVersion", runtime.Defaults.Writes);
            Assert.NotNull(runtime.Defaults.Object("updateChecker_release"));
            Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Marrow 9.0.0 is ready to install. Checked " + SwiftFormatters.RelativeNamed(checker.LastCheckedAt!.Value) + ".");
            window.Close();
        });
    });

    [Fact]
    [Trait("Req", "PR-UPDATE-073")] // App/Views/Settings/AboutUpdateViews.swift:638-675; App/AppUpdater.swift:261-283
    [Trait("Req", "PR-SETTINGS-242")] // App/Views/Settings/AboutUpdateViews.swift:638-675
    [Trait("Req", "UI-SETTINGS-056")] // App/Views/Settings/AboutUpdateViews.swift:638-675
    [Trait("Decision", "ED-180")] // UI-UPDATE-005 partial: press renderer remains deferred, though the real pointer invokes installation.
    public void Ready_pointer_click_runs_real_install_and_a_stale_second_click_is_single_flight() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); using var frames = FrameClock.Source.Override(new ManualFrameSource());
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
            // A stale routed event reaches exactly the production click closure. This catches removing install()'s
            // :262 single-flight guard, even though the actual pointer button has already disappeared.
            original.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        HeadlessSession.RunDispatcherUntil(finished.Task, TimeSpan.FromSeconds(20));
        runtime.OnMain(() =>
        {
            Dispatcher.UIThread.RunJobs(); Assert.Single(server.Requests);
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
    [InlineData(false, false, 2600)] [InlineData(true, false, 1450)] [InlineData(false, true, 2600)]
    public void Page_consumes_cancels_old_work_mounts_caught_up_settles_and_never_replays(bool reduced, bool failureReset, int total) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); var frames = new ManualFrameSource(); using var frameScope = FrameClock.Source.Override(frames);
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
        var progress = UpdateInstallProgressFixtures.NewProgress(); using var running = UpdateInstallProgress.Running.Override(progress);
        var checker = runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], "")); using var shared = UpdateChecker.OverrideShared(checker);
        var pill = new AboutVersionPill(); var row = new AboutUpdateCheckRow(); var page = new AboutSoftwareUpdateSection();
        Avalonia.Controls.Window window = null!;
        runtime.OnMain(() =>
        {
            progress.Begin(); if (failureReset) progress.Fail();
            Assert.NotNull(Field(progress, failureReset ? "failureReset" : "creepTask")); // real work running BEFORE consume
            progress.ArmCelebration(); EnvironmentValues.SetAccessibilityReduceMotion(page, reduced);
            window = Show(new StackPanel { Children = { pill, row, page } });
        });
        runtime.OnMain(() =>
        {
            Dispatcher.UIThread.RunJobs(); Assert.False(progress.HasPendingCelebration);
            Assert.Null(Field(progress, "creepTask")); Assert.Null(Field(progress, "failureReset"));
            Assert.Equal(UpdateInstallProgress.Phase.Celebrating, progress.UpdateInstallProgressPhase); Assert.Equal(0.86, progress.Fraction);
            Assert.Equal(0.86, Assert.Single(pill.GetVisualDescendants().OfType<UpdateProgressSlider>()).Fraction);
            Assert.Equal("No update feed is set.", checker.LastError); Assert.NotNull(checker.LastCheckedAt);
        });
        runtime.AdvanceMilliseconds(549); runtime.OnMain(() => Assert.Equal(0.86, progress.Fraction));
        runtime.AdvanceMilliseconds(1); runtime.OnMain(() => Assert.Equal(1, progress.Fraction));
        runtime.AdvanceMilliseconds(total - 551); runtime.OnMain(() => Assert.Equal(UpdateInstallProgress.Phase.Celebrating, progress.UpdateInstallProgressPhase));
        runtime.AdvanceMilliseconds(1);
        runtime.OnMain(() =>
        {
            Dispatcher.UIThread.RunJobs(); Assert.Equal(UpdateInstallProgress.Phase.Idle, progress.UpdateInstallProgressPhase); Assert.Equal(0, progress.Fraction);
            Assert.Empty(pill.GetVisualDescendants().OfType<UpdateProgressSlider>());
            Assert.Contains(pill.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Check failed");
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
            // Remove just the page, leaving the real accessory attached to observe the immediate standing.
            var stack = Assert.IsType<StackPanel>(window.Content); stack.Children.RemoveAt(1);
            Assert.Equal(UpdateInstallProgress.Phase.Idle, progress.UpdateInstallProgressPhase); Assert.Equal(0, progress.Fraction);
        });
        runtime.OnMain(() =>
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(window.GetVisualDescendants().OfType<UpdateProgressSlider>()); Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Check failed");
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
