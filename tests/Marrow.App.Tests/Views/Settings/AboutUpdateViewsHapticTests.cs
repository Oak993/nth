using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/AboutUpdateViews.swift (B3 52bc792), :728-743 (the page's task and its
// disappearance) and :795-818 (the celebration sweep and its two haptics); App/AppUpdater.swift:157-176.
// The haptic itself is a no-op on Windows (port_impacts X6); the sink behind Haptics.Perform lets a test see which
// pattern the sweep asked for and when, on the same manual clock the sweep sleeps on.
[Collection("Headless")]
public sealed class AboutUpdateViewsHapticTests(HeadlessSession headless)
{
    private static Avalonia.Controls.Window Show(Control content)
    {
        var window = new Avalonia.Controls.Window { Content = content, Width = 600, Height = 500 };
        window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); return window;
    }

    private static void Seen(List<HapticPattern> haptics, params HapticPattern[] expected) => Assert.Equal(expected, haptics);

    // The sweep starts when the page's task runs: 550 ms, finish the sweep, then the branch of :803-814.
    [Theory]
    [Trait("Req", "PR-UPDATE-061")] // App/Views/Settings/AboutUpdateViews.swift:795-818: two haptics at 1450 and 1900 ms, reset at 2600 ms; one haptic at 550 ms and reset at 1450 ms under Reduce Motion
    [Trait("Req", "PR-SETTINGS-243")] // App/Views/Settings/AboutUpdateViews.swift:793-818: the haptics and their times (with the lifecycle proofs of 550/900/450/700 ms)
    [InlineData(false)] [InlineData(true)]
    public void The_sweep_fires_its_haptics_at_the_source_times(bool reduced) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
        var progress = UpdateInstallProgressFixtures.NewProgress(); using var running = UpdateInstallProgress.Running.Override(progress);
        using var checker = UpdateChecker.OverrideShared(runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], "")));
        var haptics = new List<HapticPattern>(); using var sink = Haptics.Sink.Override(haptics.Add);
        var page = new AboutSoftwareUpdateSection(); Avalonia.Controls.Window window = null!;
        runtime.OnMain(() => { progress.ArmCelebration(); EnvironmentValues.SetAccessibilityReduceMotion(page, reduced); window = Show(page); });
        runtime.OnMain(() => { Assert.Equal(UpdateInstallProgress.Phase.Celebrating, progress.UpdateInstallProgressPhase); Seen(haptics); }); // :729-731 consumed, no haptic yet
        runtime.AdvanceMilliseconds(549); runtime.OnMain(() => { Seen(haptics); Assert.Equal(0.86, progress.Fraction); }); // still in the 550 ms sleep (:800)
        runtime.AdvanceMilliseconds(1);   // t = 550 ms: :802 finishCelebrationSweep, apply(1, firesHaptics: false)
        runtime.OnMain(() =>
        {
            Assert.Equal(1, progress.Fraction);
            if (reduced) Seen(haptics, HapticPattern.Alignment); // :804 right after the target is set, before the 900 ms sleep
            else Seen(haptics); // finishCelebrationSweep fires none of its own, and :807's sleep comes first
        });
        if (reduced)
        {
            runtime.AdvanceMilliseconds(899); // t = 1449
            runtime.OnMain(() => { Assert.Equal(UpdateInstallProgress.Phase.Celebrating, progress.UpdateInstallProgressPhase); Seen(haptics, HapticPattern.Alignment); });
            runtime.AdvanceMilliseconds(1);   // t = 1450: :816 completeCelebration
            runtime.OnMain(() => { Assert.Equal(UpdateInstallProgress.Phase.Idle, progress.UpdateInstallProgressPhase); Assert.Equal(0, progress.Fraction); });
            runtime.AdvanceMilliseconds(3000); runtime.OnMain(() => Seen(haptics, HapticPattern.Alignment)); // never a level change under Reduce Motion
        }
        else
        {
            runtime.AdvanceMilliseconds(899); runtime.OnMain(() => Seen(haptics)); // t = 1449
            runtime.AdvanceMilliseconds(1);   // t = 1450: :809
            runtime.OnMain(() => Seen(haptics, HapticPattern.Alignment));
            runtime.AdvanceMilliseconds(449); runtime.OnMain(() => Seen(haptics, HapticPattern.Alignment)); // t = 1899
            runtime.AdvanceMilliseconds(1);   // t = 1900: :812
            runtime.OnMain(() => Seen(haptics, HapticPattern.Alignment, HapticPattern.LevelChange));
            runtime.AdvanceMilliseconds(699); runtime.OnMain(() => Assert.Equal(UpdateInstallProgress.Phase.Celebrating, progress.UpdateInstallProgressPhase)); // t = 2599
            runtime.AdvanceMilliseconds(1);   // t = 2600: :816
            runtime.OnMain(() => { Assert.Equal(UpdateInstallProgress.Phase.Idle, progress.UpdateInstallProgressPhase); Assert.Equal(0, progress.Fraction); });
            runtime.AdvanceMilliseconds(3000); runtime.OnMain(() => Seen(haptics, HapticPattern.Alignment, HapticPattern.LevelChange));
        }
        runtime.OnMain(() => window.Close());
    });

    // PR-UPDATE-061 windows_path: "Respect the Windows 'animation effects off' system setting as the Reduce Motion
    // analogue and take the shortened branch when it is off." The installed SystemReduceMotion (ED-084) is what puts
    // that setting on every window the app opens; the page below reads it from its own window's root, never from a value
    // set on the page itself.
    [Theory]
    [Trait("Req", "PR-UPDATE-061")] // App/Views/Settings/AboutUpdateViews.swift:797,803 + windows_path: the installed Windows setting selects the shortened sweep
    [Trait("Req", "PR-SETTINGS-243")] // App/Views/Settings/AboutUpdateViews.swift:803: the Reduce Motion branch follows the window's environment
    [Trait("Decision", "ED-084")] // SystemReduceMotion: the window's root carries the system setting
    [InlineData(true)] [InlineData(false)]
    public void The_installed_system_setting_selects_the_shortened_or_the_full_sweep(bool systemReducesMotion) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
        var progress = UpdateInstallProgressFixtures.NewProgress(); using var running = UpdateInstallProgress.Running.Override(progress);
        using var checker = UpdateChecker.OverrideShared(runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], "")));
        using var system = SystemReduceMotion.Shared.Override(new SystemReduceMotion(() => systemReducesMotion));
        var haptics = new List<HapticPattern>(); using var sink = Haptics.Sink.Override(haptics.Add);
        var page = new AboutSoftwareUpdateSection(); Avalonia.Controls.Window window = null!;
        runtime.OnMain(() =>
        {
            progress.ArmCelebration();
            window = new Avalonia.Controls.Window { Width = 600, Height = 500 };
            SystemReduceMotion.Shared.Current.Follow(window); // what the app does for every window it opens
            window.Content = page; window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(systemReducesMotion, EnvironmentValues.GetAccessibilityReduceMotion(page));
        });
        runtime.AdvanceMilliseconds(550);
        runtime.OnMain(() => { Assert.Equal(1, progress.Fraction); if (systemReducesMotion) Seen(haptics, HapticPattern.Alignment); else Seen(haptics); });
        runtime.AdvanceMilliseconds(899); // t = 1449: not over yet either way
        runtime.OnMain(() => Assert.Equal(UpdateInstallProgress.Phase.Celebrating, progress.UpdateInstallProgressPhase));
        runtime.AdvanceMilliseconds(1);   // t = 1450: the shortened sweep is over; the full one has just fired its first haptic
        runtime.OnMain(() =>
        {
            Assert.Equal(systemReducesMotion ? UpdateInstallProgress.Phase.Idle : UpdateInstallProgress.Phase.Celebrating, progress.UpdateInstallProgressPhase);
            Seen(haptics, HapticPattern.Alignment);
        });
        runtime.AdvanceMilliseconds(1150); // t = 2600
        runtime.OnMain(() =>
        {
            Assert.Equal(UpdateInstallProgress.Phase.Idle, progress.UpdateInstallProgressPhase);
            if (systemReducesMotion) Seen(haptics, HapticPattern.Alignment); else Seen(haptics, HapticPattern.Alignment, HapticPattern.LevelChange);
            window.Close();
        });
    });

    // PR-SETTINGS-243 side effect: "A network check on every open of the About page (while the update feed is empty, its
    // default, no network request is made; OD-0012 I1)". The probe watches .NET's real HTTP transport.
    [Fact]
    [Trait("Req", "PR-SETTINGS-243")] // App/Views/Settings/AboutUpdateViews.swift:732; OD-0012 I1: the open-time check with the empty feed makes no request, then "Check failed" and the note
    [Trait("Req", "PR-UPDATE-060")] // App/Views/Settings/AboutUpdateViews.swift:728-733: a check is issued every time the page appears
    public void Opening_the_page_checks_every_time_and_with_the_empty_feed_sends_nothing() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0")); using var probe = new AboutUpdateHttpProbe();
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        var checker = runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], UpdateChecker.DefaultFeed)); using var shared = UpdateChecker.OverrideShared(checker);
        Avalonia.Controls.Window window = null!;
        runtime.OnMain(() => window = Show(new AboutSoftwareUpdateSection()));
        runtime.RunUntilIdle();
        DateTimeOffset firstCheck = default;
        runtime.OnMain(() => { Dispatcher.UIThread.RunJobs(); Assert.Equal("No update feed is set.", checker.LastError); firstCheck = checker.LastCheckedAt!.Value; window.Close(); });
        runtime.AdvanceMilliseconds(1000);
        runtime.OnMain(() => window = Show(new AboutSoftwareUpdateSection())); // the second visit asks again
        runtime.RunUntilIdle();
        DateTimeOffset secondCheck = default;
        runtime.OnMain(() => { Dispatcher.UIThread.RunJobs(); Assert.True(checker.LastCheckedAt > firstCheck); secondCheck = checker.LastCheckedAt!.Value; window.Close(); });
        Assert.Equal(0, probe.Requests); // two checks, no request: the empty feed fails before the transport
        // Positive control: the same checker class does reach the observer when a feed is set.
        using var server = System.Threading.Tasks.Task.Run(() => new LoopbackFeedServer(_ => CannedResponse.Json("[]"))).GetAwaiter().GetResult();
        var positive = runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], server.Url("/releases"))); using var positiveShared = UpdateChecker.OverrideShared(positive);
        runtime.AdvanceMilliseconds(1000); // the checker above stored its stamp; the control's own answer must be a later one
        var answered = new TaskCompletionSource<bool>();
        using var observe = new Tracked<bool>(() => positive.LastCheckedAt > secondCheck && !positive.IsChecking, done => { if (done) answered.TrySetResult(true); });
        runtime.OnMain(() => window = Show(new AboutSoftwareUpdateSection()));
        HeadlessSession.RunDispatcherUntil(answered.Task, TimeSpan.FromSeconds(20));
        Assert.Single(server.Requests); Assert.Equal(1, probe.Requests);
        runtime.OnMain(() => window.Close());
    });

    // The record's "Every step re-checks cancellation and the phase / A phase change mid-sweep aborts it at the next guard" is stronger than
    // the pinned Swift (the Swift wins): :801 checks the phase once, after the first 550 ms; every later step checks only cancellation, and
    // the final completeCelebration() guards its own phase (AppUpdater.swift:174-176). A phase change after :801 therefore does not stop
    // the sweep: its haptics still fire and its last step changes nothing.
    [Fact]
    [Trait("Req", "PR-UPDATE-061")] // App/Views/Settings/AboutUpdateViews.swift:801-816; App/AppUpdater.swift:168-176: the phase is checked once (:801); a later phase change leaves the haptics and the guarded last step
    public void A_phase_change_after_the_first_guard_does_not_stop_the_sweep_and_its_last_step_changes_nothing() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
        var progress = UpdateInstallProgressFixtures.NewProgress(); using var running = UpdateInstallProgress.Running.Override(progress);
        using var checker = UpdateChecker.OverrideShared(runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], "")));
        var haptics = new List<HapticPattern>(); using var sink = Haptics.Sink.Override(haptics.Add);
        var page = new AboutSoftwareUpdateSection(); Avalonia.Controls.Window window = null!;
        runtime.OnMain(() => { progress.ArmCelebration(); EnvironmentValues.SetAccessibilityReduceMotion(page, false); window = Show(page); });
        runtime.AdvanceMilliseconds(550); // the :801 guard has passed, the target is 1
        runtime.OnMain(() => { Assert.Equal(1, progress.Fraction); progress.CompleteCelebration(); Assert.Equal(UpdateInstallProgress.Phase.Idle, progress.UpdateInstallProgressPhase); });
        runtime.AdvanceMilliseconds(3000);
        runtime.OnMain(() =>
        {
            Seen(haptics, HapticPattern.Alignment, HapticPattern.LevelChange); // :809 and :812 ran although the phase was no longer celebrating
            Assert.Equal(UpdateInstallProgress.Phase.Idle, progress.UpdateInstallProgressPhase); Assert.Equal(0, progress.Fraction); // :816's guarded complete changed nothing
            window.Close();
        });
    });
}
