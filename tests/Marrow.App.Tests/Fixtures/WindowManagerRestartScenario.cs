using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.App.Window;
using Marrow.Core.Port;
using Marrow.Services.Port;
using Marrow.UI.Theme;
using W = Avalonia.Controls.Window;
using E = Marrow.App.Tests.Fixtures.WindowManagerRestartExpectations;
using P = Marrow.App.Tests.Fixtures.WindowManagerProcessProtocol;
using R = Marrow.App.Tests.Fixtures.WindowManagerRestartProtocol;

namespace Marrow.App.Tests.Fixtures;

// The only writer of the saved main frame is production WindowManager's move/resize subscription.
// A new SettingsStore is constructed in each child. No test seeds, copies or repairs the shared store.
// DEVIATION OD-0151 (ED-NEW-settingsinapp-1): Settings opens as the panel inside the main window, so it has no frame of its own to
// place, save or recompute; each phase opens it in the main window and proves the store unchanged.
internal static class WindowManagerRestartScenario
{
    internal static WindowManagerRestartResult Run(WindowManagerRestartReady identity, string directory)
    {
        var request = identity.Request;
        var storePath = R.StorePath(request.Root);
        void Record(string entry) => P.Log(directory, entry);
        byte[]? incoming = null;
        if (request.Phase == 1)
        {
            Assert.False(File.Exists(storePath));
        }
        else
        {
            incoming = File.ReadAllBytes(storePath);
            E.AssertSerializedFrame(incoming);
            Assert.Equal(request.ExpectedInputHash, R.Hash(incoming));
            R.WriteBytes(directory, "input-store.bin", incoming);
            Record("store-bytes-read");
        }

        // Bind safe model/library/home/credential scopes first, then shadow only its memory defaults
        // with the real persistent store BEFORE constructing WindowManager and AppSettings.
        // withRuntime:false keeps the existing headless main executor and normal I/O retry clock.
        using var harness = new AppModelHarness(withRuntime: false);
        var store = new SettingsStore(storePath);
        using var persistedDefaults = UserDefaults.Standard.Override(store);
        using var cursor = WindowCenteringTestHooks.OverrideCursor(new WindowCenteringCursor(), required: true);
        Assert.Same(store, UserDefaults.Standard.Current);
        if (request.Phase == 1)
        {
            Assert.Null(store.Object(E.FrameName));
            Assert.True(store.Synchronize());
            Assert.Null(store.LastWriteError);
            Assert.False(File.Exists(storePath));
            Record("store-empty");
        }
        else
        {
            E.AssertStoredFrame(store, E.SavedMain);
            Record("store-reloaded");
        }

        var application = Application.Current!;
        var appearance = application.RequestedThemeVariant;
        var theme = ThemeManager.Current;
        var roots = new List<Border>();
        var settingsRoots = new List<Border>();
        Border Content(bool settings)
        {
            var root = new Border();
            roots.Add(root);
            if (settings) settingsRoots.Add(root);
            return root;
        }
        var manager = new WindowManager { MainContent = () => Content(false), SettingsContent = () => Content(true) };
        WindowManagerRestartFrame mainReading;
        WindowManagerRestartFrame settingsReading;
        byte[] beforeSettings;
        try
        {
            Assert.Empty(manager.Model.Projects);
            Assert.Empty(manager.Model.Threads);
            if (request.Phase == 1) manager.Model.Settings.Theme = AppTheme.Dark;
            // The second process must obtain Dark from the production persistent store, not a setter.
            Assert.Equal(AppTheme.Dark, manager.Model.Settings.Theme);
            application.RequestedThemeVariant = ThemeVariant.Light;
            manager.ShowMain();
            var main = Assert.IsType<ThreadWindow>(manager.MainWindow);
            WindowManagerMutationScope.Settle(main);
            RequireHeadlessGeometry(main);
            Assert.True(main.IsVisible);
            Assert.Equal(WindowState.Normal, main.WindowState);
            Assert.Equal(ThemeVariant.Dark, application.RequestedThemeVariant);
            Assert.Equal(ThemeVariant.Dark, Assert.Single(LogicalQuery.SelfAndDescendants<HostedRoot>((Control)main.Content!)).RequestedThemeVariant);
            if (request.Phase == 1)
            {
                // Swift:67-69 plus ED-225 on the asserted headless work area, at ED-NEW-settingsinapp-3's first-launch size: 69 % x 92 %
                // of the 1920 x 1280 work area, rounded (1325 x 1178), centred both ways. The Mac bias remains outside this test's claim.
                // This cannot accidentally pass by loading a prior run.
                Assert.Equal(new WindowManagerRestartFrame(298, 51, 1325, 1178), ReadFrame(main));
                Record("main-shown");
                main.Position = new PixelPoint(2345, 6789);
                WindowManagerMutationScope.Settle(main);
                _ = FlushAndRead(store, storePath);
                E.AssertStoredFrame(store, E.MainAfterMove);
                Assert.Equal(E.MainAfterMove, ReadFrame(main));
                Record("main-position-saved");
                // Resize without another move, so a missing resize subscription cannot be rescued
                // by the later position setter. Both dimensions differ from the source defaults.
                main.Width = 1453;
                main.Height = 973;
                WindowManagerMutationScope.Settle(main);
                _ = FlushAndRead(store, storePath);
                E.AssertStoredFrame(store, E.SavedMain);
                Assert.Equal(E.SavedMain, ReadFrame(main));
                Record("main-size-saved");
            }
            else
            {
                // No position or size setter in this phase: the factory's actual restore must do it.
                Assert.Equal(E.SavedMain, ReadFrame(main));
                Assert.Equal(new Size(1453, 973), main.ClientSize);
                E.AssertStoredFrame(store, E.SavedMain);
                Record("main-restored");
            }
            Assert.True(main.Position.X > 1920 && main.Position.Y > 1280);
            beforeSettings = FlushAndRead(store, storePath);
            E.AssertSerializedFrame(beforeSettings);
            if (incoming is not null) Assert.Equal(incoming, beforeSettings);

            // OD-0151: Settings opens as the panel inside the main window, in the dark theme the store holds; no second window is made.
            manager.ShowSettings();
            var settingsRoot = Assert.Single(settingsRoots);
            Assert.Same(main, LaunchHarness.WindowOf(settingsRoot));
            var overlay = Assert.IsType<Marrow.App.Port.SettingsOverlay>(manager.SettingsOverlay);
            Assert.Same(main, overlay.Window);
            Assert.Same(settingsRoot, overlay.Content);
            Assert.True(manager.IsSettingsOpen);
            Assert.True(overlay.IsVisible);
            WindowManagerMutationScope.Settle(main);
            RequireHeadlessGeometry(main);
            Assert.Same(main, manager.MainWindow);
            Assert.True(main.IsVisible);
            Assert.Equal(WindowState.Normal, main.WindowState);
            Assert.Equal(ThemeVariant.Dark, application.RequestedThemeVariant);
            Assert.Equal(ThemeVariant.Dark, Assert.Single(LogicalQuery.SelfAndDescendants<HostedRoot>(overlay)).RequestedThemeVariant);
            // Opening it neither moves nor resizes the main window, in a first launch or a restored one.
            Assert.Equal(E.SavedMain, ReadFrame(main));
            Assert.Equal(beforeSettings, FlushAndRead(store, storePath));
            Record("settings-panel-shown");
            // Compare the entire serialized document, not a guessed Settings key. This detects
            // new Settings placement data and accidental writes under the main frame key.
            Assert.Equal(beforeSettings, FlushAndRead(store, storePath));
            E.AssertStoredFrame(store, E.SavedMain);
            Assert.Equal(E.SavedMain, ReadFrame(main));
            mainReading = ReadFrame(main);
            settingsReading = ReadFrame(LaunchHarness.WindowOf(settingsRoot)!); // the frame of the window Settings is in: the main one
            Record("settings-store-unchanged");
        }
        finally
        {
            // Only test-owned headless windows are hidden. No main.Close/AppShell binding is
            // substituted. The enclosing HeadlessSession is disposed before the child Fact returns.
            manager.CloseSettings();
            foreach (var root in roots)
            {
                if (LaunchHarness.WindowOf(root) is not { } window) continue;
                window.Content = null;
                window.Hide();
            }
            Dispatcher.UIThread.RunJobs();
            application.RequestedThemeVariant = appearance;
            ThemeManager.Current = theme;
            Record("windows-hidden");
        }
        var outgoing = FlushAndRead(store, storePath);
        E.AssertSerializedFrame(outgoing);
        Assert.Equal(beforeSettings, outgoing);
        R.WriteBytes(directory, "output-store.bin", outgoing);
        Record("store-synchronized");
        return new WindowManagerRestartResult(identity, storePath, mainReading, settingsReading,
            incoming is null ? null : R.Hash(incoming), R.Hash(outgoing));
    }

    private static byte[] FlushAndRead(SettingsStore store, string path)
    {
        Assert.True(store.Synchronize(), "Production SettingsStore.Synchronize reported a failed write.");
        Assert.Null(store.LastWriteError);
        Assert.True(File.Exists(path), "WindowManager autosave did not create the production settings document.");
        return File.ReadAllBytes(path);
    }

    private static WindowManagerRestartFrame ReadFrame(W window) =>
        new(window.Position.X, window.Position.Y, window.Width, window.Height);

    private static void RequireHeadlessGeometry(W window)
    {
        var screen = Assert.Single(window.Screens.All);
        Assert.True(screen.IsPrimary);
        Assert.Equal(new PixelRect(0, 0, 1920, 1280), screen.Bounds);
        Assert.Equal(new PixelRect(0, 0, 1920, 1280), screen.WorkingArea);
        Assert.Equal(1d, screen.Scaling);
        Assert.Equal(1d, window.DesktopScaling);
        Assert.True(window.FrameSize is null || window.FrameSize == window.ClientSize,
            "The restart oracle requires the existing headless platform with zero frame decorations.");
    }
}
