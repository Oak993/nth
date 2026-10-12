// Test-owned settings resources use the ordinary gate's ambient disposable data directory.
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using Xunit;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window;

// Existing isolation pattern: WindowManagerNextTests.cs:71-93; input: SettingsRestFixture.cs:33-41.
// The main content is a stand-in. Settings uses the production default factory unless explicitly isolated.
public abstract class SettingsWindowFixture(HeadlessSession headless)
{
    protected void WithManager(Action<AppModelHarness, WindowManager> check, bool realSettings = true)
    {
        headless.OnUiThread(() =>
        {
            var application = Application.Current!;
            var appearance = application.RequestedThemeVariant;
            var theme = ThemeManager.Current;
            using var harness = new AppModelHarness(withRuntime: false);
            using var cursor = WindowCenteringTestHooks.OverrideCursor(new WindowCenteringCursor());
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var wallpaper = WallpaperStore.Shared.Override((WallpaperStore)Activator.CreateInstance(typeof(WallpaperStore), nonPublic: true)!);
            using var updates = UpdateChecker.OverrideShared(new UpdateChecker(AppInfo.Current, [], ""));
            using var shortcuts = ShortcutStore.Shared.Override((ShortcutStore)Activator.CreateInstance(typeof(ShortcutStore), nonPublic: true)!);
            var requested = SettingsNavigation.Shared.RequestedPage;
            SettingsNavigation.Shared.RequestedPage = null;
            var manager = new WindowManager { MainContent = () => new Border { Background = Brushes.Transparent } };
            if (!realSettings) manager.SettingsContent = () => new Border();
            using var shared = WindowManager.OverrideShared(manager);
            try { check(harness, manager); }
            finally
            {
                manager.SettingsOverlay?.Close();
                if (manager.MainWindow is { } main) { main.Content = null; main.Hide(); }
                SettingsNavigation.Shared.RequestedPage = requested;
                ThemeManager.Current = theme;
                application.RequestedThemeVariant = appearance;
                Dispatcher.UIThread.RunJobs();
            }
        });
    }

    // OD-0151: Settings is a panel inside the main window (SettingsOverlay), no window of its own.
    /// <summary>The window Settings is open in: the main window, once Settings has been opened; null before.</summary>
    protected static HostWindow? SettingsOrNull(WindowManager manager) => manager.SettingsOverlay?.Window;

    /// <summary>The window the open Settings panel is in (the main window); fails when Settings is not open there.</summary>
    protected static HostWindow Settings(WindowManager manager)
    {
        var overlay = Overlay(manager);
        Assert.True(overlay.IsOpen, "Settings is open");
        Assert.Same(manager.MainWindow, overlay.Window);
        return overlay.Window;
    }

    /// <summary>The Settings panel of the main window.</summary>
    private protected static SettingsOverlay Overlay(WindowManager manager) => Assert.IsType<SettingsOverlay>(manager.SettingsOverlay);

    protected static HostWindow MainOf(WindowManager manager) => Assert.IsAssignableFrom<HostWindow>(manager.MainWindow);

    protected static T[] Nodes<T>(Control root) where T : class => LogicalQuery.SelfAndDescendants<T>(root).ToArray();

    protected static SettingsView View(WindowManager manager) =>
        Assert.Single(Nodes<SettingsView>(Overlay(manager).Content));

    protected static void Drain(HostWindow window)
    {
        for (var pass = 0; pass < 5; pass++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// ED-NEW-textsize-3: frames at <paramref name="hertz"/> for <paramref name="seconds"/>, <paramref name="time"/> (the test's SwiftClock)
    /// moved before each, with <paramref name="each"/> after each frame: a zoom change runs out its motion.
    /// </summary>
    protected static void RunFrames(ManualTimeProvider time, HostWindow window, double seconds = 1, double hertz = 60, Action? each = null)
    {
        Drain(window);
        var frames = Assert.IsType<ManualFrameSource>(FrameClock.Source.Current);
        var step = TimeSpan.FromSeconds(1 / hertz);
        for (var at = 0.0; at < seconds; at += 1 / hertz)
        {
            time.Advance(step);
            frames.Advance(step);
            Dispatcher.UIThread.RunJobs();
            each?.Invoke();
        }
        Drain(window);
    }

    protected static void Deactivate(HostWindow window)
    {
        // Existing input-boundary setup: AppCommandsProofTests.cs:98-105.
        var callback = (Action?)typeof(IWindowBaseImpl).GetProperty("Deactivated")!.GetValue(window.PlatformImpl);
        Assert.NotNull(callback);
        callback();
        Assert.False(window.IsActive);
    }

    protected static void Press(HostWindow window, Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.Activate();
        window.KeyPress(key, modifiers, physical, keySymbol: null);
        window.KeyRelease(key, modifiers, physical, keySymbol: null);
        Drain(window);
    }

    protected static Point At(Control control, HostWindow window, Point? local = null) =>
        control.TranslatePoint(local ?? new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    protected static void AssertHitWithin(HostWindow window, Control control, Point point)
    {
        var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(point));
        Assert.True(ReferenceEquals(hit, control) || hit.GetVisualAncestors().Contains(control),
            $"Expected the input hit inside {control.GetType().Name}; actual hit was {hit.GetType().Name}.");
    }

    protected static void Click(HostWindow window, Control control, Point? local = null)
    {
        Drain(window);
        var point = At(control, window, local);
        AssertHitWithin(window, control, point);
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Drain(window);
    }
}
