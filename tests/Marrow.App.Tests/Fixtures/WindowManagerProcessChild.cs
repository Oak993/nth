using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.Services.Store;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

// Source: WindowManager.swift:15-16, :26-38, :98. Main-close termination is OD-0008 C1/C2. DEVIATION OD-0151: Settings is a panel
// inside the main window (Port/SettingsOverlay.cs), so "retained" is the one panel and its content kept through a close and a reopen.
// This is a disposable testhost scenario. The real application entrypoint, bootstrap and native
// pointer dispatch are outside its claim. Environment.Exit is the test-owned terminal dependency
// passed to AppShell; production FinishLaunching must install and reach it through main.Close().
internal static class WindowManagerProcessChild
{
    internal static readonly string[] ExpectedEvents =
    [
        "headless-body-entered",
        "finish-launching-requested",
        "finish-launching-returned",
        "main-reused",
        "main-represented",
        "settings-close-1-requested",
        "settings-close-1-returned",
        "settings-reopened-1",
        "settings-close-2-requested",
        "settings-close-2-returned",
        "settings-reopened-2",
        "main-close-requested",
        "main-closing-observed",
        "main-close-returned",
        "shell-exit-entered",
        "after-will-terminate",
        "process-exit-requested",
    ];

    internal static void Run(string token)
    {
        // Refuse invalid/global child markers by throwing normally, before installing any route
        // that can exit the current process. Both the nonce and this testhost identity are checked.
        var request = WindowManagerProcessProtocol.ValidateChild(token, out var run);
        var identity = new WindowManagerProcessReady(token, request.ParentProcessId, Environment.ProcessId,
            typeof(AppShell).Module.ModuleVersionId, typeof(WindowManagerProcessChild).Module.ModuleVersionId);
        try
        {
            WindowManagerProcessProtocol.Write(run, "ready.json", identity);
            WindowManagerProcessProtocol.Until(() => File.Exists(Path.Combine(run, "permit.json")),
                WindowManagerProcessProtocol.StartupBound, "Parent did not authorize the identified child to continue.");
            var permit = WindowManagerProcessProtocol.Read<WindowManagerProcessPermit>(run, "permit.json");
            Assert.Equal(token, permit.Token);
            Assert.Equal(Environment.ProcessId, permit.ChildProcessId);

            // A wall-clock test infrastructure guard, never a product delay. The timeout route
            // writes no success marker and cannot block on logging or a stuck UI thread.
            using var watchdog = new System.Threading.Timer(_ => Environment.Exit(WindowManagerProcessProtocol.WatchdogExitCode),
                null, WindowManagerProcessProtocol.ChildBound, Timeout.InfiniteTimeSpan);
            using var headless = new HeadlessSession();
            headless.OnUiThread(() => Scenario(identity, run));
            throw new InvalidOperationException("The child scenario returned without the shell exit dependency running.");
        }
        catch (Exception error)
        {
            try
            {
                WindowManagerProcessProtocol.Write(run, "failure.json", new
                {
                    Identity = identity,
                    Error = error.ToString(),
                    ExitCode = WindowManagerProcessProtocol.FailureExitCode,
                });
            }
            finally { Environment.Exit(WindowManagerProcessProtocol.FailureExitCode); }
        }
    }

    private static void Scenario(WindowManagerProcessReady identity, string run)
    {
        var events = new List<string>();
        void Record(string value)
        {
            events.Add(value);
            WindowManagerProcessProtocol.Log(run, value);
        }
        Record("headless-body-entered");
        using var launch = new LaunchHarness(h => h.Defaults.Set("hasSeenTour", true));
        // LaunchHarness supplies the established no-bootstrap/no-tour delegates and safe model
        // roots. Delegate installation must also be bound before calling the actual launch body.
        using var toast = ToastNotifications.Center.Override(new RecordingAppToastCenter());
        var ledger = launch.Harness.Main.OnMain(FreshTokenLedger.Make);
        using var ledgerScope = TokenLedger.Shared.Override(ledger);
        // Fresh content on every factory call avoids making a cache mutant fail merely because
        // LaunchHarness's fixed Border cannot be parented by two windows. Count actual builds.
        var mainRoots = new List<Border>();
        var settingsRoots = new List<Border>();
        launch.Manager.MainContent = () =>
        {
            launch.Events.Add("main window built");
            var root = new Border();
            mainRoots.Add(root);
            return root;
        };
        launch.Manager.SettingsContent = () =>
        {
            launch.Events.Add("settings window built");
            var root = new Border();
            settingsRoots.Add(root);
            return root;
        };

        AvaloniaWindow? main = null;
        var mainCloseRequested = false;
        var mainCloseReturned = false;
        var mainClosed = false;
        var settingsClosed = 0;
        AppShell shell = null!;
        void ExitFromShell()
        {
            // These checks also distinguish S01, inline termination, wrong-window termination,
            // destruction-before-shutdown and a hidden main from the required bound exit path.
            Assert.True(mainCloseRequested);
            Assert.True(mainCloseReturned);
            Assert.NotNull(main);
            Assert.True(main.IsVisible);
            Assert.False(mainClosed);
            Assert.Equal(0, settingsClosed);
            Assert.Equal(1, launch.Events.Count(value => value == "main window built"));
            Assert.Equal(1, launch.Events.Count(value => value == "settings window built"));
            Record("shell-exit-entered");
            shell.WillTerminate(); // production delegate save/flush/empty process-list teardown
            UpdateCheckerLoop.Stop(launch.Checker);
            Record("process-exit-requested");
            Assert.Equal(ExpectedEvents, events);
            WindowManagerProcessProtocol.Write(run, "exit-path.json", new WindowManagerProcessExit(identity,
                WindowManagerProcessProtocol.SuccessExitCode, main.IsVisible, mainCloseReturned, mainClosed,
                launch.Events.Count(value => value == "main window built"),
                launch.Events.Count(value => value == "settings window built"), settingsClosed, events.ToArray()));
            Environment.Exit(WindowManagerProcessProtocol.SuccessExitCode);
        }

        shell = new AppShell(launch.Delegate, ExitFromShell, () => Record("after-will-terminate"));
        try
        {
            Record("finish-launching-requested");
            launch.Harness.Main.OnMain(shell.FinishLaunching);
            Record("finish-launching-returned");
            main = Assert.IsAssignableFrom<AvaloniaWindow>(launch.Manager.MainWindow);
            Assert.Same(launch.Manager.Model, shell.Delegate.Model);
            Assert.Empty(launch.Manager.Model.Projects);
            Assert.Empty(launch.Manager.Model.Threads);
            Assert.Single(launch.BootstrapModels); // established recorder ran, not AppModel.Bootstrap
            Assert.Empty(launch.TourModels);
            Assert.True(main.IsVisible);
            var mainContent = main.Content;
            main.Closed += (_, _) => mainClosed = true;

            launch.Harness.Main.OnMain(launch.Manager.ShowMain);
            Assert.Same(main, launch.Manager.MainWindow);
            Assert.Same(mainContent, main.Content);
            Assert.Equal(1, launch.Events.Count(value => value == "main window built"));
            Record("main-reused");
            launch.Harness.Main.OnMain(main.Hide);
            Assert.False(main.IsVisible);
            launch.Harness.Main.OnMain(launch.Manager.ShowMain);
            Assert.Same(main, launch.Manager.MainWindow);
            Assert.True(main.IsVisible);
            Record("main-represented");

            launch.Harness.Main.OnMain(() => launch.Manager.ShowSettings());
            var settingsRoot = Assert.Single(settingsRoots);
            // OD-0151: the Settings content is in the main window, inside its one panel; no second window exists.
            Assert.Same(main, LaunchHarness.WindowOf(settingsRoot));
            var overlay = Assert.IsType<SettingsOverlay>(launch.Manager.SettingsOverlay);
            Assert.Same(main, overlay.Window);
            Assert.Same(settingsRoot, overlay.Content);
            Assert.True(launch.Manager.IsSettingsOpen);
            Assert.True(overlay.IsVisible);
            // A panel taken down (its content out of the window) would be the closed-and-gone window of before.
            settingsRoot.DetachedFromVisualTree += (_, _) => settingsClosed++;
            for (var index = 1; index <= 2; index++)
            {
                var number = WindowManagerProcessProtocol.Invariant(index);
                Record("settings-close-" + number + "-requested");
                launch.Harness.Main.OnMain(overlay.Close); // what the panel's close button, Escape and Ctrl+W run
                Assert.False(launch.Manager.IsSettingsOpen);
                Assert.False(overlay.IsVisible);
                Assert.Same(main, LaunchHarness.WindowOf(settingsRoot)); // kept in the main window
                Assert.Equal(0, settingsClosed);
                Assert.True(main.IsVisible);
                Record("settings-close-" + number + "-returned");
                launch.Harness.Main.OnMain(() => launch.Manager.ShowSettings());
                Assert.Same(overlay, launch.Manager.SettingsOverlay);
                Assert.Same(main, LaunchHarness.WindowOf(Assert.Single(settingsRoots)));
                Assert.Same(settingsRoot, overlay.Content);
                Assert.True(launch.Manager.IsSettingsOpen);
                Assert.True(overlay.IsVisible);
                Assert.True(main.IsVisible);
                Assert.Equal(1, launch.Events.Count(value => value == "settings window built"));
                Record("settings-reopened-" + number);
            }

            main.Closing += (_, e) =>
            {
                Assert.Equal(WindowCloseReason.WindowClosing, e.CloseReason);
                Assert.True(e.Cancel);
                Assert.True(main.IsVisible);
                Record("main-closing-observed");
            };
            launch.Harness.Main.OnMain(() =>
            {
                mainCloseRequested = true;
                Record("main-close-requested");
                main.Close(); // production Closing handler -> FinishLaunching-installed MainWindowClose
                mainCloseReturned = true;
                Record("main-close-returned");
            }); // pumps the production MainActor.Post(Terminate), after Close returns
            throw new InvalidOperationException("S01: main.Close returned and the production shell exit was never reached.");
        }
        finally
        {
            // Only failure reaches this cleanup. Success actually exited this disposable process.
            launch.Harness.Main.OnMain(() =>
            {
                UpdateCheckerLoop.Stop(launch.Checker);
                foreach (var root in mainRoots.Concat(settingsRoots))
                {
                    if (LaunchHarness.WindowOf(root) is { } window)
                    {
                        window.Content = null;
                        window.Hide();
                    }
                }
                Dispatcher.UIThread.RunJobs();
            });
        }
    }
}
