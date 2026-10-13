using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.App.Window;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window;

// Source: DroppyCode/App/Window/WindowManager.swift (B3 52bc792), :1-222. The file is unchanged from B2 1fbf81e, so
// the B2 and B3 line numbers are the same. Parts 1 and 2 of 2 (work packets part1.md, part2.md).
// Every expectation is transcribed from the cited Swift lines (or, where the record says so, from its windows_path):
//   :19 main 1320 x 860; :20 main minimum 860 x 560; :21 settings 780 x 580; :24 frame name "DroppyCodeMainWindow"
//   :26-30 showMain: `mainWindow ?? makeMainWindow()`, cached, then present
//   :32-38 showSettings: the page first, then `settingsWindow ?? makeSettingsWindow()`, centred over main, present
//   :41-55 centre over the main window: guard visible and not miniaturized else window.center(); origin =
//          (parent.mid - size / 2).rounded(); clamped into the main window's screen's visible frame
//   :57-60 present: activate, then make key and order front
//   :62-72 the main window: resizable, title AppInfo.name, contentMinSize, setFrameUsingName else center, autosave
//   :80-84 the settings window: "Settings", not resizable, SecondaryWindow, centred
//   :92-108 makeWindow: titled, closable, miniaturizable, full-size content, not released when closed, one
//          container holding one FrameHostingView holding HostedRoot(model, liveResize, content)
//   :118-140 WindowLiveResize; :143-161 HostedRoot; :165-179 HostedEnvironment; :183-193 ThemeTint;
//   :197-222 FrameHostingView
// Forms (ED-095, packet part1.md): NSWindow is an Avalonia Window; a point is one DIP (ED-071); the headless screen
// is one 1920 x 1280 working area at scale 1 (Avalonia.Headless HeadlessScreensStub), so pixels and points agree.
// This original suite isolates content through WindowManager.MainContent and SettingsContent.
// WindowManagerNextTests also exercises the default real SettingsView; RootView still needs its owning chat.
// DEVIATION OD-0151 (ED-NEW-settingsinapp-1, -3): Settings is a panel inside the main window (Port/SettingsOverlay.cs), not a window, so
// :16, :21, :36, :40-55 and :80-84 are not ported as such (no Settings window to size, centre, title or style); and with no saved frame the
// main window opens at 69 % x 92 % of its monitor's work area, 1325 x 1178 on the headless one, instead of :19's 1320 x 860.
[Collection("Headless")]
public sealed class WindowManagerTests(HeadlessSession headless)
{
    // WindowsMessages the live-resize hook reads (winuser.h).
    private const uint WM_SIZING = 0x0214;
    private const uint WM_MOVING = 0x0216;
    private const uint WM_ENTERSIZEMOVE = 0x0231;
    private const uint WM_EXITSIZEMOVE = 0x0232;

    // One test body: a real AppModel store in a throwaway folder (AppModelHarness), a fresh WindowManager whose main
    // window and Settings panel host the given stand-ins, and the process-wide values a test here changes put back afterwards.
    private void WithManager(Action<AppModelHarness, WindowManager> body) => headless.OnUiThread(() =>
    {
        var application = SwiftTraps.Unwrap(Application.Current);
        var variant = application.RequestedThemeVariant;
        var theme = ThemeManager.Current;
        using var harness = new AppModelHarness();
        using var cursor = WindowCenteringTestHooks.OverrideCursor(new WindowCenteringCursor());
        var page = harness.Main.OnMain(() => SettingsNavigation.Shared.RequestedPage);
        WindowManager? manager = null;
        try
        {
            // OD-0151: Settings opens in the main window, so a test that opens only Settings still makes the main window; it hosts a
            // stand-in here unless the test gives it one.
            manager = harness.Main.OnMain(() => new WindowManager { MainContent = () => new Border() });
            body(harness, manager);
        }
        finally
        {
            if (manager is { } made) harness.Main.OnMain(made.CloseSettings);
            harness.Main.OnMain(() => SettingsNavigation.Shared.RequestedPage = page);
            ThemeManager.Current = theme;
            application.RequestedThemeVariant = variant;
            Dispatcher.UIThread.RunJobs();
        }
    });

    private static void Settle(TopLevel window)
    {
        for (var pass = 0; pass < 5; pass++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }

    // The window a hosted control sits in, by its logical parents (content, ZoomLayout, HostedRoot, FrameHostingView,
    // the container, the window).
    private static AvaloniaWindow WindowOf(Control content)
    {
        StyledElement? element = content;
        while (element is not null and not AvaloniaWindow) element = element.Parent;
        return Assert.IsAssignableFrom<AvaloniaWindow>(element);
    }

    private static AvaloniaWindow ShowMain(AppModelHarness harness, WindowManager manager, Control content)
    {
        manager.MainContent = () => content;
        harness.Main.OnMain(manager.ShowMain);
        var window = WindowOf(content);
        harness.Main.OnMain(() => Settle(window));
        return window;
    }

    // OD-0151: the window Settings opens in, which is the main window (its panel holds the content).
    private static AvaloniaWindow ShowSettings(AppModelHarness harness, WindowManager manager, Control content)
    {
        manager.SettingsContent = () => content;
        harness.Main.OnMain(() => manager.ShowSettings());
        var window = WindowOf(content);
        harness.Main.OnMain(() => Settle(window));
        Assert.True(manager.IsSettingsOpen);
        Assert.Same(manager.MainWindow, window);
        Assert.Same(window, Assert.IsType<SettingsOverlay>(manager.SettingsOverlay).Window);
        return window;
    }

    // ---- PR-WIN-003: the main window is built at most once ----

    [Fact]
    [Trait("Req", "PR-WIN-003")] // App/Window/WindowManager.swift:15, :26-30
    public void ShowMain_builds_the_main_window_once_and_every_later_call_presents_that_same_window() => WithManager((harness, manager) =>
    {
        var content = new Border();
        var built = 0;
        manager.MainContent = () =>
        {
            built++;
            return content;
        };
        harness.Main.OnMain(manager.ShowMain);
        var first = WindowOf(content);
        harness.Main.OnMain(manager.ShowMain);
        harness.Main.OnMain(manager.ShowMain);
        Assert.Equal(1, built);
        Assert.Same(first, WindowOf(content));
        Assert.True(first.IsVisible);
    });

    // ---- PR-WIN-004, PR-WIN-005: main window size and minimum ----

    [Fact]
    [Trait("Req", "PR-WIN-004")] // App/Window/WindowManager.swift:19, :63, :95-96; DEVIATION OD-0151 (ED-NEW-settingsinapp-3)
    public void The_main_window_opens_at_69_by_92_percent_of_the_work_area() => WithManager((harness, manager) =>
    {
        var window = ShowMain(harness, manager, new Border());
        // the headless work area is 1920 x 1280: 1324.8 x 1177.6, rounded
        Assert.Equal(1325d, window.Width);
        Assert.Equal(1178d, window.Height);
        Assert.Equal(new Size(1325, 1178), window.ClientSize);
    });

    [Fact]
    [Trait("Req", "PR-WIN-005")] // App/Window/WindowManager.swift:20, :66
    public void The_main_window_content_minimum_is_860_by_560() => WithManager((harness, manager) =>
    {
        var window = ShowMain(harness, manager, new Border());
        Assert.Equal(860d, window.MinWidth);
        Assert.Equal(560d, window.MinHeight);
    });

    // ---- PR-WIN-006: the frame autosave name ----
    // With no saved frame the main window is centered by window.center() (:68), now bound by ED-225.

    [Fact]
    [Trait("Req", "PR-WIN-006")] // App/Window/WindowManager.swift:24, :67
    public void A_saved_frame_under_DroppyCodeMainWindow_is_applied_as_it_is_without_centring_or_clamping() => WithManager((harness, manager) =>
    {
        // The frame the previous main window wrote (see the next test), entirely off the screen: no clamp (the record's
        // failure case: setFrameUsingName applies it unconditionally).
        var first = harness.Main.OnMain(() => new WindowManager());
        var earlier = ShowMain(harness, first, new Border());
        harness.Main.OnMain(() =>
        {
            earlier.Width = 1000;
            earlier.Height = 700;
            Settle(earlier);
            earlier.Position = new PixelPoint(-5000, -3000);
            Settle(earlier);
            first.FlushPendingFrames(); // ED-NEW-perf-startup-structure-1: the app ending writes the held frame
        });

        var window = ShowMain(harness, manager, new Border());
        Assert.Equal(new PixelPoint(-5000, -3000), window.Position);
        Assert.Equal(1000d, window.Width);
        Assert.Equal(700d, window.Height);
    });

    [Fact]
    [Trait("Req", "PR-WIN-006")] // App/Window/WindowManager.swift:24, :67-70
    public void Every_later_move_or_resize_is_written_back_under_DroppyCodeMainWindow_and_restored_by_the_next_main_window() => WithManager((harness, manager) =>
    {
        var window = ShowMain(harness, manager, new Border());
        harness.Defaults.Writes.Clear();
        harness.Main.OnMain(() =>
        {
            window.Position = new PixelPoint(123, 45);
            Settle(window);
            manager.FlushPendingFrames(); // ED-NEW-perf-startup-structure-1: a frame is saved at most half a second late, or when the window hides or the app ends
        });
        Assert.Contains("DroppyCodeMainWindow", harness.Defaults.Writes);
        harness.Defaults.Writes.Clear();
        harness.Main.OnMain(() =>
        {
            window.Width = 1400;
            window.Height = 900;
            Settle(window);
            manager.FlushPendingFrames(); // ED-NEW-perf-startup-structure-1
        });
        Assert.Contains("DroppyCodeMainWindow", harness.Defaults.Writes);
        Assert.All(harness.Defaults.Writes, key => Assert.Equal("DroppyCodeMainWindow", key));

        var next = harness.Main.OnMain(() => new WindowManager());
        var restored = ShowMain(harness, next, new Border());
        Assert.Equal(new PixelPoint(123, 45), restored.Position);
        Assert.Equal(1400d, restored.Width);
        Assert.Equal(900d, restored.Height);
    });

    [Fact]
    [Trait("Req", "PR-WIN-006")] // App/Window/WindowManager.swift:70
    public void A_minimised_main_window_writes_no_frame() => WithManager((harness, manager) =>
    {
        // AppKit leaves a miniaturized window's frame where it was; Windows parks a minimised window at (-32000,
        // -32000) (platform), so a frame written then would bring the next window back off the screen.
        var window = ShowMain(harness, manager, new Border());
        harness.Defaults.Writes.Clear();
        harness.Main.OnMain(() =>
        {
            window.WindowState = WindowState.Minimized;
            window.Position = new PixelPoint(-32000, -32000);
            Settle(window);
            manager.FlushPendingFrames(); // ED-NEW-perf-startup-structure-1: a frame held from before would reach the store here; the parked one must not
        });
        Assert.DoesNotContain("DroppyCodeMainWindow", harness.Defaults.Writes);
    });

    // ---- PR-WIN-007, PR-WIN-010: style ----

    [Fact]
    [Trait("Req", "PR-WIN-007")] // App/Window/WindowManager.swift:63, :81, :93-94, :101
    [Trait("Req", "PR-WIN-010")] // App/Window/WindowManager.swift:81
    [Trait("Decision", "OD-0151")]
    public void The_main_window_is_resizable_and_extends_its_content_under_the_title_bar_and_Settings_opens_inside_it() => WithManager((harness, manager) =>
    {
        var main = ShowMain(harness, manager, new Border());
        var settings = ShowSettings(harness, manager, new Border());
        Assert.Same(main, settings); // OD-0151: no Settings window with a style of its own
        Assert.True(main.CanResize);
        Assert.True(main.CanMinimize); // .miniaturizable
        Assert.Equal(WindowDecorations.Full, main.WindowDecorations); // .titled, .closable (ED-063)
        Assert.True(main.ExtendClientAreaToDecorationsHint); // .fullSizeContentView
        Assert.Equal(44, main.ExtendClientAreaTitleBarHeightHint); // PR-WIN-007 windows_path, ED-063: a 44 px title bar band
    });

    // ---- PR-WIN-008, PR-SETTINGS-001: kept when closed ----

    [Fact]
    [Trait("Req", "PR-WIN-008")] // App/Window/WindowManager.swift:98
    [Trait("Req", "PR-SETTINGS-001")] // App/Window/WindowManager.swift:32-37, :98
    [Trait("Req", "PR-WIN-003")] // App/Window/WindowManager.swift:16, :34-35, :98
    [Trait("Decision", "OD-0151")]
    public void Closing_Settings_hides_its_panel_and_the_next_showSettings_presents_the_same_panel_and_content() => WithManager((harness, manager) =>
    {
        var content = new Border();
        var built = 0;
        manager.SettingsContent = () =>
        {
            built++;
            return content;
        };
        harness.Main.OnMain(() => manager.ShowSettings());
        var window = WindowOf(content);
        var overlay = Assert.IsType<SettingsOverlay>(manager.SettingsOverlay);
        Assert.Same(manager.MainWindow, window);
        Assert.True(overlay.IsVisible);
        harness.Main.OnMain(() =>
        {
            manager.CloseSettings();
            Settle(window);
        });
        Assert.False(manager.IsSettingsOpen);
        Assert.False(overlay.IsVisible);
        Assert.True(window.IsVisible); // the main window it was in stays
        harness.Main.OnMain(() => manager.ShowSettings());
        Assert.True(manager.IsSettingsOpen);
        Assert.True(overlay.IsVisible);
        Assert.Same(overlay, manager.SettingsOverlay);
        Assert.Same(window, WindowOf(content));
        Assert.Equal(1, built);
    });

    // ---- PR-WIN-009: the main window's title ----

    [Fact]
    [Trait("Req", "PR-WIN-009")] // App/Window/WindowManager.swift:63, :97; Core/Support/Text.swift:4
    public void The_main_window_title_is_the_app_display_name() => WithManager((harness, manager) =>
    {
        // OD-0002: the Windows build's CFBundleDisplayName is "Marrow".
        using var name = AppInfo.Running.Override(AppInfo.Read(new Dictionary<string, object?> { ["CFBundleDisplayName"] = "Marrow" }, isDevelopment: false));
        var window = ShowMain(harness, manager, new Border());
        Assert.Equal("Marrow", window.Title);
    });

    // ---- PR-WIN-010, PR-SETTINGS-001: Settings (OD-0151: a panel in the main window, not a window) ----
    // Deleted with OD-0151: the Settings window's frame autosave test (there is no Settings frame) and the centring of the Settings window
    // over the main window (:40-55: centred, rounded, clamped, re-centred on every show).

    [Fact]
    [Trait("Req", "PR-WIN-010")] // App/Window/WindowManager.swift:21, :81
    [Trait("Req", "PR-SETTINGS-001")] // App/Window/WindowManager.swift:21, :80-84, :97
    [Trait("Decision", "OD-0151")]
    [Trait("Decision", "ED-NEW-settingsinapp-2")]
    public void Settings_is_a_panel_named_Settings_in_the_main_window_and_takes_its_size_from_that_window() => WithManager((harness, manager) =>
    {
        var window = ShowSettings(harness, manager, new Border());
        Assert.IsType<ThreadWindow>(window); // the main window: Settings makes no SecondaryWindow
        var overlay = Assert.IsType<SettingsOverlay>(manager.SettingsOverlay);
        Assert.Equal("Settings", Avalonia.Automation.AutomationProperties.GetName(overlay.Card)); // :81's title, now the panel's name
        // ED-NEW-settingsinapp-4: the frame is the window's pixels less the owner's margins; the panel is laid out in points at the base scale (100 %).
        // The stage is the whole window in points (Avalonia's layout rounding takes an arranged size up to the next whole point), and the
        // panel is the stage's frame in pixels, laid out in points, rounded up the same way.
        var stage = overlay.Stage.Bounds.Size;
        Assert.InRange(stage.Width - window.ClientSize.Width / AppZoom.BaseScale, -0.01, 1);
        Assert.InRange(stage.Height - window.ClientSize.Height / AppZoom.BaseScale, -0.01, 1);
        var pixels = SettingsOverlay.SettingsStage.CardSize(new Size(stage.Width * AppZoom.BaseScale, stage.Height * AppZoom.BaseScale), AppZoom.BaseScale);
        var expected = new Size(Math.Ceiling(pixels.Width / AppZoom.BaseScale - 1e-6), Math.Ceiling(pixels.Height / AppZoom.BaseScale - 1e-6));
        // A one-point window: rounded up once, and once more where the stage's own size was rounded up.
        Assert.InRange(overlay.Card.Bounds.Width, expected.Width - 0.01, expected.Width + 1.01);
        Assert.InRange(overlay.Card.Bounds.Height, expected.Height - 0.01, expected.Height + 1.01);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-001")] // App/Window/WindowManager.swift:16, :32-37
    [Trait("Decision", "OD-0151")]
    public void The_Settings_panel_is_built_once_and_reused_by_every_showSettings() => WithManager((harness, manager) =>
    {
        var content = new Border();
        var built = 0;
        manager.SettingsContent = () =>
        {
            built++;
            return content;
        };
        harness.Main.OnMain(() => manager.ShowSettings());
        var window = WindowOf(content);
        var overlay = Assert.IsType<SettingsOverlay>(manager.SettingsOverlay);
        harness.Main.OnMain(() => manager.ShowSettings(SettingsPage.Hydra));
        harness.Main.OnMain(() => manager.ShowSettings());
        Assert.Equal(1, built);
        Assert.Same(window, WindowOf(content));
        Assert.Same(manager.MainWindow, window);
        Assert.Same(overlay, manager.SettingsOverlay);
        Assert.Same(overlay, Assert.Single(Assert.IsAssignableFrom<Panel>(window.Content).Children.OfType<SettingsOverlay>()));
        Assert.True(manager.IsSettingsOpen);
    });

    // ---- PR-WIN-011, PR-SETTINGS-002: the window Settings opens in ----
    // :42-45 guard a hidden or minimised main window. OD-0151: Settings opens inside the main window, so showSettings presents that window
    // (:37) whatever state it was in, and the panel opens in it.

    [Fact]
    [Trait("Req", "PR-WIN-011")] // App/Window/WindowManager.swift:42-45
    [Trait("Req", "PR-SETTINGS-002")] // App/Window/WindowManager.swift:42-45
    [Trait("Decision", "OD-0151")]
    public void With_the_main_window_hidden_or_minimised_showSettings_brings_it_back_with_the_Settings_panel_open() => WithManager((harness, manager) =>
    {
        var main = ShowMain(harness, manager, new Border());
        harness.Main.OnMain(() =>
        {
            main.Hide();
            Settle(main);
        });
        var settings = ShowSettings(harness, manager, new Border());
        Assert.Same(main, settings);
        Assert.True(main.IsVisible); // :42 `main.isVisible` failed: the window is shown again

        harness.Main.OnMain(() =>
        {
            manager.CloseSettings();
            main.WindowState = WindowState.Minimized;
            Settle(main);
            manager.ShowSettings();
            Settle(main);
        });
        Assert.True(manager.IsSettingsOpen); // :42 `!main.isMiniaturized` failed: the window is restored, Settings open in it
        Assert.Same(main, Assert.IsType<SettingsOverlay>(manager.SettingsOverlay).Window);
        Assert.True(main.IsVisible);
        Assert.Equal(WindowState.Normal, main.WindowState); // OD-0038: as it was before it was minimised
    });

    // ---- PR-WIN-012: the requested page ----

    [Fact]
    [Trait("Req", "PR-WIN-012")] // App/Window/WindowManager.swift:32-34
    public void showSettings_with_a_page_sets_the_requested_page_before_the_window_is_built() => WithManager((harness, manager) =>
    {
        SettingsPage? seen = null;
        var content = new Border();
        manager.SettingsContent = () =>
        {
            seen = SettingsNavigation.Shared.RequestedPage;
            return content;
        };
        harness.Main.OnMain(() => SettingsNavigation.Shared.RequestedPage = null);
        harness.Main.OnMain(() => manager.ShowSettings(SettingsPage.Hydra));
        Assert.Equal(SettingsPage.Hydra, seen);
        Assert.Equal(SettingsPage.Hydra, harness.Main.OnMain(() => SettingsNavigation.Shared.RequestedPage));
    });

    [Fact]
    [Trait("Req", "PR-WIN-012")] // App/Window/WindowManager.swift:33
    public void showSettings_without_a_page_leaves_the_requested_page_untouched() => WithManager((harness, manager) =>
    {
        harness.Main.OnMain(() => SettingsNavigation.Shared.RequestedPage = SettingsPage.General);
        ShowSettings(harness, manager, new Border());
        Assert.Equal(SettingsPage.General, harness.Main.OnMain(() => SettingsNavigation.Shared.RequestedPage));
        harness.Main.OnMain(() => SettingsNavigation.Shared.RequestedPage = null);
        harness.Main.OnMain(() => manager.ShowSettings());
        Assert.Null(harness.Main.OnMain(() => SettingsNavigation.Shared.RequestedPage));
    });

    // ---- PR-WIN-013: present ----

    [Fact]
    [Trait("Req", "PR-WIN-013")] // App/Window/WindowManager.swift:29, :37, :57-60
    [Trait("Req", "PR-SETTINGS-002")] // App/Window/WindowManager.swift:57-60
    [Trait("Decision", "OD-0151")]
    public void Presenting_a_window_shows_it_and_makes_it_the_active_window() => WithManager((harness, manager) =>
    {
        var main = ShowMain(harness, manager, new Border());
        Assert.True(main.IsVisible);
        Assert.True(main.IsActive);
        // OD-0151: showSettings presents the main window Settings opens in (:37).
        var settings = ShowSettings(harness, manager, new Border());
        Assert.Same(main, settings);
        Assert.True(main.IsVisible);
        Assert.True(main.IsActive);
        harness.Main.OnMain(() =>
        {
            main.Hide();
            Settle(main);
            manager.ShowMain();
            Settle(main); // the platform reports the activation on a later dispatcher turn
        });
        Assert.True(main.IsVisible);
        Assert.True(main.IsActive);
    });

    // ---- PR-WIN-014, PR-WIN-015, PR-WIN-016: the hosting view ----

    [Fact]
    [Trait("Req", "PR-WIN-014")] // App/Window/WindowManager.swift:103-108, :197-204
    public void A_window_content_is_one_container_holding_one_hosting_view_that_fills_the_window() => WithManager((harness, manager) =>
    {
        var window = ShowMain(harness, manager, new Border());
        var container = Assert.IsAssignableFrom<Panel>(window.Content);
        // :107 one hosting view, then the zoom's capsule over it (ED-NEW-textsize-3: drawn at the base scale, never hit), then (OD-0044) the caption
        // buttons' layer over both, filling the window too.
        Assert.Equal(3, container.Children.Count);
        var hosting = Assert.IsType<FrameHostingView>(container.Children[0]);
        var capsule = Assert.IsType<LayoutTransformControl>(container.Children[1]); Assert.IsType<ZoomIndicator>(capsule.Child); Assert.False(capsule.IsHitTestVisible);
        Assert.Same(WindowChrome.CaptionLayer(window), container.Children[2]);
        // ED-NEW-settingsinapp-3: the first-launch window, 69 % x 92 % of the headless 1920 x 1280 work area.
        Assert.Equal(new Rect(0, 0, 1325, 1178), hosting.Bounds);
        Assert.Equal(new Rect(0, 0, 1325, 1178), container.Bounds);
        Assert.Equal(new Rect(0, 0, 1325, 1178), container.Children[2].Bounds);
        Assert.Same(WindowChrome.CaptionLayer(window), WindowChrome.CaptionCluster(window).Parent); // the buttons live on it
    });

    [Fact]
    [Trait("Req", "PR-WIN-014")] // App/Window/WindowManager.swift:200-203
    [Trait("Decision", "ED-150")]
    public void The_hosting_view_takes_its_size_from_its_frame_and_never_from_its_content() => headless.OnUiThread(() =>
    {
        // sizingOptions = []: a content that wants more or less than the frame changes nothing about the host.
        var large = new Border { Width = 3000, Height = 2000 };
        var small = new Border { Width = 10, Height = 10 };
        var hostOfLarge = new FrameHostingView(large);
        var hostOfSmall = new FrameHostingView(small);
        hostOfLarge.Measure(new Size(500, 400));
        hostOfSmall.Measure(new Size(500, 400));
        Assert.Equal(new Size(0, 0), hostOfLarge.DesiredSize);
        Assert.Equal(new Size(0, 0), hostOfSmall.DesiredSize);
        hostOfLarge.Arrange(new Rect(0, 0, 500, 400));
        Assert.Equal(new Size(500, 400), hostOfLarge.Bounds.Size);
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Stretch, hostOfLarge.HorizontalAlignment); // .width
        Assert.Equal(Avalonia.Layout.VerticalAlignment.Stretch, hostOfLarge.VerticalAlignment); // .height
    });

    [Fact]
    [Trait("Req", "PR-WIN-015")] // App/Window/WindowManager.swift:210-214
    public void The_hosting_view_always_accepts_focus() => headless.OnUiThread(() =>
    {
        Assert.True(new FrameHostingView(new Border()).Focusable);
        Assert.True(new FrameHostingView(new TextBlock()).Focusable);
    });

    // The detail pane as ThreadColumns keeps it (App/Views/RootView.swift:224-243, :254-274): every chat column is a
    // FrameHostingView added to the same pane, the ones built first standing first.
    private static AvaloniaWindow Pane(params FrameHostingView[] columns)
    {
        var pane = new Panel();
        foreach (var column in columns) pane.Children.Add(column);
        var window = new AvaloniaWindow { Width = 400, Height = 300, Content = pane };
        window.Show();
        Settle(window);
        return window;
    }

    private static void Press(AvaloniaWindow window, Key key, RawInputModifiers modifiers)
    {
        window.KeyPress(key, modifiers, PhysicalKey.Enter, null);
        window.KeyRelease(key, modifiers, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Close(AvaloniaWindow window)
    {
        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    // A button that counts its clicks.
    private static Button Counted(Button button, Action clicked)
    {
        button.Click += (_, _) => clicked();
        return button;
    }

    // The approval card's approve button, trimmed (App/Views/Composer/RequestCards.swift:49-54): the card at the top of
    // its runtime's stack (`request.id == runtime.approvals.first?.id`, :49) carries
    // KeyboardShortcut(.return, modifiers: .command) (:54), which is the key gesture Ctrl+Enter in the port (ED-099 (3)
    // Command as Control, (6) a KeyboardShortcut as an Avalonia key gesture). A button's key gesture is Button.HotKey,
    // which Avalonia 12.1.2 answers from the window's own key bindings (HotKeyManager), asking only whether the button
    // is enabled. Each chat column has its own runtime, so every column whose thread waits on an approval holds it.
    private static Button ApproveButton(Action clicked) =>
        Counted(new Button { HotKey = new KeyGesture(Key.Enter, KeyModifiers.Control) }, clicked);

    [Fact]
    [Trait("Req", "PR-WIN-016")] // App/Window/WindowManager.swift:216-221; App/Views/Composer/RequestCards.swift:49-54
    public void A_hidden_hosting_view_keeps_the_approval_card_chord_to_itself() => headless.OnUiThread(() =>
    {
        // (1) Only a hidden column holds the chord: a column built ahead of a click, hidden from the start
        // (RootView.swift:254-269, `column.isHidden = !isShown` at :269), whose thread waits on an approval, beside the
        // shown column of a thread that does not. :219-220 a hidden host answers no without asking its subtree.
        var prebuiltClicks = 0;
        var prebuiltButton = ApproveButton(() => prebuiltClicks++);
        var prebuilt = new FrameHostingView(prebuiltButton) { IsVisible = false };
        var window = Pane(prebuilt, new FrameHostingView(new Border()));
        Press(window, Key.Enter, RawInputModifiers.Control);
        Assert.Equal(0, prebuiltClicks);
        Assert.True(prebuiltButton.IsVisible); // the column's own controls are untouched: only the host is hidden
        Assert.True(prebuiltButton.IsEnabled);
        Close(window);

        // (2) Both columns hold it. The earlier column was shown and is hidden by the switch to the later one
        // (RootView.swift:224-235: `previous.isHidden = true` at :227, then `kept.isHidden = false` at :235); only the
        // shown one answers.
        var earlierClicks = 0;
        var laterClicks = 0;
        var earlier = new FrameHostingView(ApproveButton(() => earlierClicks++));
        var later = new FrameHostingView(ApproveButton(() => laterClicks++)) { IsVisible = false };
        window = Pane(earlier, later);
        earlier.IsVisible = false;
        later.IsVisible = true;
        Settle(window);
        Press(window, Key.Enter, RawInputModifiers.Control);
        Assert.Equal(0, earlierClicks);
        Assert.Equal(1, laterClicks);

        // (3) Back to the earlier thread: its column, shown again, answers again, and the later one, now hidden, does not.
        later.IsVisible = false;
        earlier.IsVisible = true;
        Settle(window);
        Press(window, Key.Enter, RawInputModifiers.Control);
        Assert.Equal(1, earlierClicks);
        Assert.Equal(1, laterClicks);
        Close(window);
    });

    [Fact]
    [Trait("Req", "PR-WIN-016")] // App/Window/WindowManager.swift:216-221; App/Views/Composer/QuestionTab.swift:156-158
    public void A_hidden_hosting_view_keeps_its_default_button_chord_to_itself() => headless.OnUiThread(() =>
    {
        // The question tab's Send button, trimmed (QuestionTab.swift:156-158 `.keyboardShortcut(.defaultAction)`, Return
        // with no modifiers), in a hidden column beside a shown one that holds no chord. No decision yet fixes which
        // Avalonia form that chord takes (ED-099 (6) says a key gesture), so both forms are checked: Button.HotKey
        // Return, answered from the window's key bindings, and Button.IsDefault, answered from the window's KeyDown.
        foreach (var form in new Func<Button>[] { () => new Button { HotKey = new KeyGesture(Key.Enter) }, () => new Button { IsDefault = true } })
        {
            var hiddenClicks = 0;
            var hidden = new FrameHostingView(Counted(form(), () => hiddenClicks++)) { IsVisible = false };
            var shown = new FrameHostingView(new Border());
            var window = Pane(hidden, shown);
            Press(window, Key.Return, RawInputModifiers.None);
            Assert.Equal(0, hiddenClicks);

            // Shown in its turn (RootView.swift:224-235), the column's Return is its own again.
            shown.IsVisible = false;
            hidden.IsVisible = true;
            Settle(window);
            Press(window, Key.Return, RawInputModifiers.None);
            Assert.Equal(1, hiddenClicks);
            Close(window);
        }
    });

    // ---- PR-WIN-017, PR-WIN-018: live resize ----

    private static void Send(WindowLiveResize liveResize, uint message)
    {
        var handled = false;
        liveResize.WndProc(0, message, 0, 0, ref handled);
        Assert.False(handled); // an observer only: the window's own handling goes on
    }

    [Fact]
    [Trait("Req", "PR-WIN-017")] // App/Window/WindowManager.swift:118-140
    public void A_live_resize_sets_isActive_for_that_window_until_it_ends() => WithManager((harness, manager) =>
    {
        harness.Main.OnMain(() =>
        {
            var liveResize = new WindowLiveResize(new AvaloniaWindow());
            Assert.False(liveResize.IsActive);
            Send(liveResize, WM_ENTERSIZEMOVE);
            Send(liveResize, WM_SIZING);
            Assert.True(liveResize.IsActive); // :133-135 willStartLiveResize
            Send(liveResize, WM_SIZING);
            Assert.True(liveResize.IsActive);
            Send(liveResize, WM_EXITSIZEMOVE);
            Assert.False(liveResize.IsActive); // :136-138 didEndLiveResize
        });
    });

    [Fact]
    [Trait("Req", "PR-WIN-017")] // App/Window/WindowManager.swift:133-138 (windows_path: a move is not a live resize)
    public void A_window_move_is_not_a_live_resize() => WithManager((harness, manager) =>
    {
        harness.Main.OnMain(() =>
        {
            var liveResize = new WindowLiveResize(new AvaloniaWindow());
            Send(liveResize, WM_ENTERSIZEMOVE);
            Send(liveResize, WM_MOVING);
            Send(liveResize, WM_MOVING);
            Assert.False(liveResize.IsActive);
            Send(liveResize, WM_EXITSIZEMOVE);
            Assert.False(liveResize.IsActive);
        });
    });

    [Fact]
    [Trait("Req", "PR-WIN-017")] // App/Window/WindowManager.swift:113-117, :131-133 (`object: window`)
    public void A_live_resize_of_one_window_leaves_another_window_flag_alone() => WithManager((harness, manager) =>
    {
        harness.Main.OnMain(() =>
        {
            var resized = new WindowLiveResize(new AvaloniaWindow());
            var other = new WindowLiveResize(new AvaloniaWindow());
            Send(resized, WM_ENTERSIZEMOVE);
            Send(resized, WM_SIZING);
            Assert.True(resized.IsActive);
            Assert.False(other.IsActive);
            Send(resized, WM_EXITSIZEMOVE);
        });
    });

    [Fact]
    [Trait("Req", "PR-WIN-037")] // DroppyCode/UI/Chrome/WindowChrome.swift:28, :93; App/Window/WindowManager.swift:129-139; ED-224
    public void Window_resize_messages_publish_the_UI_signal_only_for_the_resizing_window() => WithManager((harness, manager) =>
    {
        harness.Main.OnMain(() =>
        {
            var window = new AvaloniaWindow();
            var other = new AvaloniaWindow();
            var liveResize = new WindowLiveResize(window);
            var transitions = new List<bool>();
            window.PropertyChanged += (_, e) =>
            {
                if (e.Property == WindowLiveResizeSignal.IsActiveProperty) transitions.Add(WindowLiveResizeSignal.GetIsActive(window));
            };
            Send(liveResize, WM_ENTERSIZEMOVE);
            Send(liveResize, WM_MOVING);
            Assert.False(WindowLiveResizeSignal.GetIsActive(window));
            Send(liveResize, WM_SIZING);
            Assert.True(WindowLiveResizeSignal.GetIsActive(window));
            Assert.False(WindowLiveResizeSignal.GetIsActive(other));
            Send(liveResize, WM_SIZING);
            Send(liveResize, WM_EXITSIZEMOVE);
            Assert.False(WindowLiveResizeSignal.GetIsActive(window));
            Send(liveResize, WM_EXITSIZEMOVE);
            Assert.Equal(new[] { true, false }, transitions);
            Send(liveResize, WM_ENTERSIZEMOVE);
            Send(liveResize, WM_MOVING);
            Send(liveResize, WM_EXITSIZEMOVE);
            Assert.Equal(new[] { true, false }, transitions); // A pure move never publishes resize transitions.
        });
    });

    [Fact]
    [Trait("Req", "PR-WIN-018")] // App/Window/WindowManager.swift:121-127
    public void isReshaping_is_true_while_either_the_window_or_a_pane_is_being_reshaped() => WithManager((harness, manager) =>
    {
        harness.Main.OnMain(() =>
        {
            var liveResize = new WindowLiveResize(new AvaloniaWindow());
            Assert.False(liveResize.IsReshaping);
            liveResize.IsPaneResizing = true;
            Assert.True(liveResize.IsReshaping);
            liveResize.IsPaneResizing = false;
            Assert.False(liveResize.IsReshaping);
            Send(liveResize, WM_ENTERSIZEMOVE);
            Send(liveResize, WM_SIZING);
            Assert.True(liveResize.IsReshaping);
            liveResize.IsPaneResizing = true;
            Send(liveResize, WM_EXITSIZEMOVE);
            Assert.True(liveResize.IsReshaping);
            liveResize.IsPaneResizing = false;
            Assert.False(liveResize.IsReshaping);
        });
    });

    [Fact]
    [Trait("Req", "PR-WIN-018")] // App/Window/WindowManager.swift:118, :125-127 (@Observable: a view reading isReshaping)
    public void A_view_reading_isReshaping_is_told_when_the_pane_drag_begins_and_ends() => WithManager((harness, manager) =>
    {
        var seen = new List<bool>();
        WindowLiveResize? liveResize = null;
        Tracked<bool>? tracked = null;
        harness.Main.OnMain(() =>
        {
            liveResize = new WindowLiveResize(new AvaloniaWindow());
            tracked = new Tracked<bool>(() => liveResize.IsReshaping, seen.Add);
        });
        harness.Main.OnMain(() => liveResize!.IsPaneResizing = true);
        harness.Main.OnMain(() => liveResize!.IsPaneResizing = false);
        harness.Main.OnMain(() =>
        {
            Send(liveResize!, WM_ENTERSIZEMOVE);
            Send(liveResize!, WM_SIZING);
        });
        harness.Main.OnMain(() => Send(liveResize!, WM_EXITSIZEMOVE));
        tracked!.Dispose();
        Assert.Equal([false, true, false, true, false], seen);
    });

    // ---- PR-WIN-019, PR-WIN-221: the hosted environment ----

    // The hosted content's ancestors, nearest first, as makeWindow builds them (:104-108).
    private static (ZoomLayout Zoom, HostedRoot Root, FrameHostingView Hosting) Hosts(Control content)
    {
        var zoom = Assert.IsType<ZoomLayout>(content.Parent);
        var root = Assert.IsType<HostedRoot>(zoom.Parent);
        var hosting = Assert.IsType<FrameHostingView>(root.Parent);
        return (zoom, root, hosting);
    }

    private static double ScaleOf(Control content) => Assert.IsType<ScaleTransform>(content.RenderTransform).ScaleX;

    [Fact]
    [Trait("Req", "PR-WIN-019")] // App/Window/WindowManager.swift:143-150, :165-179
    [Trait("Req", "PR-WIN-221")] // App/Window/WindowManager.swift:104, :143-150, :173-175
    [Trait("Decision", "OD-0151")]
    public void Every_window_root_carries_the_model_the_live_resize_object_the_app_zoom_the_capsule_shape_and_the_theme_tint() => WithManager((harness, manager) =>
    {
        harness.Main.OnMain(() =>
        {
            manager.Model.Settings.AppZoom = 125;
            manager.Model.Settings.Theme = AppTheme.Dracula;
        });
        var mainContent = new Border();
        var settingsContent = new Border();
        var main = ShowMain(harness, manager, mainContent);
        ShowSettings(harness, manager, settingsContent);
        // :172 one live-resize object per window (:103): the Settings panel is in the main window, so it has the main window's (OD-0151).
        Assert.Same(AppEnvironment.GetWindowLiveResize(mainContent), AppEnvironment.GetWindowLiveResize(settingsContent));
        Hosts(mainContent);
        // The panel's root is a hosted root of its own (HostedRoot over its stage), beside the main window's, so its tree is zoomed once, at
        // its stage, and not again inside the main root's zoom.
        var overlay = Assert.IsType<SettingsOverlay>(manager.SettingsOverlay);
        Assert.Same(main, overlay.Window);
        var zoom = Assert.IsType<ZoomLayout>(overlay.Stage.Parent);
        var root = Assert.IsType<HostedRoot>(zoom.Parent);
        Assert.Same(overlay, root.Parent);
        Assert.Equal(125 / 100.0 * AppZoom.BaseScale, ScaleOf(mainContent)); // :175, AppZoom.swift:22; the level over the base scale (ED-NEW-textsize-2)
        Assert.Equal(125 / 100.0 * AppZoom.BaseScale, ScaleOf(overlay.Stage));
        Assert.Null(settingsContent.RenderTransform);
        foreach (var content in new[] { mainContent, settingsContent })
        {
            Assert.Same(manager.Model, AppEnvironment.GetAppModel(content)); // :171
            Assert.True(Popovers.GetIsCapsuleButtonBorderShape(content)); // :176
            Assert.Equal(Color.FromRgb(0xBD, 0x93, 0xF9), SwiftTraps.Unwrap(Popovers.GetTint(content)).Resolve(ThemeVariant.Dark)); // :177, AppTheme.swift:119
        }
    });

    [Fact]
    [Trait("Req", "PR-WIN-019")] // App/Window/WindowManager.swift:163-179; App/Views/RootView.swift:276-282
    [Trait("Req", "PR-WIN-221")] // App/Window/WindowManager.swift:173-175; App/Views/RootView.swift:280
    public void A_chat_column_hosted_apart_carries_the_same_environment_when_given_HostedEnvironment() => WithManager((harness, manager) =>
    {
        harness.Main.OnMain(() =>
        {
            manager.Model.Settings.AppZoom = 150;
            manager.Model.Settings.Theme = AppTheme.Dracula;
            var liveResize = new WindowLiveResize(new AvaloniaWindow());
            var column = new Border();
            var hosting = new FrameHostingView(new HostedEnvironment(manager.Model, liveResize).Body(column));
            Assert.Same(manager.Model, AppEnvironment.GetAppModel(column));
            Assert.Same(liveResize, AppEnvironment.GetWindowLiveResize(column));
            Assert.Equal(150 / 100.0 * AppZoom.BaseScale, ScaleOf(column)); // ED-NEW-textsize-2
            Assert.IsType<ZoomLayout>(column.Parent);
            Assert.True(Popovers.GetIsCapsuleButtonBorderShape(column));
            Assert.Equal(Color.FromRgb(0xBD, 0x93, 0xF9), SwiftTraps.Unwrap(Popovers.GetTint(column)).Resolve(ThemeVariant.Dark));
            Assert.NotNull(hosting.Child);
        });
    });

    [Fact]
    [Trait("Req", "PR-WIN-221")] // App/Window/WindowManager.swift:173-175
    [Trait("Decision", "OD-0151")]
    public void Changing_the_app_zoom_rescales_every_hosted_tree_live_without_rebuilding_its_content() => WithManager((harness, manager) =>
    {
        // Layout rounding off for the stand-in, so its bounds show the unrounded division (as AppZoomTests does).
        var mainContent = new Border { UseLayoutRounding = false };
        var settingsContent = new Border();
        var attached = 0;
        mainContent.AttachedToVisualTree += (_, _) => attached++;
        ShowMain(harness, manager, mainContent);
        ShowSettings(harness, manager, settingsContent);
        var zoom = Hosts(mainContent).Zoom;
        var stage = Assert.IsType<SettingsOverlay>(manager.SettingsOverlay).Stage; // OD-0151: the Settings panel's tree is zoomed at its stage
        Assert.Equal(AppZoom.BaseScale, ScaleOf(mainContent)); // 100 %: the base scale alone (ED-NEW-textsize-2)
        Assert.Equal(AppZoom.BaseScale, ScaleOf(stage));
        var attachedBefore = attached;

        harness.Main.OnMain(() => manager.Model.Settings.AppZoom = 175);
        harness.Main.OnMain(() => Settle(WindowOf(mainContent)));
        var scale = 175 / 100.0 * AppZoom.BaseScale;
        Assert.Equal(scale, ScaleOf(mainContent));
        Assert.Equal(scale, ScaleOf(stage));
        Assert.Null(settingsContent.RenderTransform); // once, not again inside it
        Assert.Same(zoom, Hosts(mainContent).Zoom); // the same layout, the same content: nothing rebuilt
        Assert.Equal(attachedBefore, attached); // never detached and attached again
        // AppZoom.swift:40-46: laid out in the window's space divided by the scale (ED-NEW-settingsinapp-3: the window is 1325 x 1178).
        Assert.Equal(new Size(1325 / scale, 1178 / scale), mainContent.Bounds.Size);

        harness.Main.OnMain(() => manager.Model.Settings.AppZoom = 95); // not a level: normalized to 100
        harness.Main.OnMain(() => Settle(WindowOf(mainContent)));
        Assert.Equal(AppZoom.BaseScale, ScaleOf(mainContent));
    });

    // ---- PR-WIN-020, PR-THEME-039: colour scheme and process appearance ----

    [Fact]
    [Trait("Req", "PR-WIN-020")] // App/Window/WindowManager.swift:151-159
    [Trait("Req", "PR-THEME-039")] // App/Window/WindowManager.swift:151-159
    public void At_first_build_the_theme_sets_the_window_colour_scheme_and_the_process_appearance() => WithManager((harness, manager) =>
    {
        var application = SwiftTraps.Unwrap(Application.Current);
        application.RequestedThemeVariant = ThemeVariant.Light;
        harness.Main.OnMain(() => manager.Model.Settings.Theme = AppTheme.Dracula); // a dark palette (AppTheme.swift:119)
        var content = new Border();
        ShowMain(harness, manager, content);
        Assert.Equal(ThemeVariant.Dark, Hosts(content).Root.RequestedThemeVariant); // :151 preferredColorScheme
        Assert.Equal(ThemeVariant.Dark, application.RequestedThemeVariant); // :152-156 initial: true, .darkAqua
        Assert.Equal(ThemeVariant.Dark, content.ActualThemeVariant);
    });

    [Fact]
    [Trait("Req", "PR-WIN-020")] // App/Window/WindowManager.swift:151-159
    [Trait("Req", "PR-THEME-039")] // App/Window/WindowManager.swift:151-159
    public void Changing_the_theme_sets_both_the_colour_scheme_and_the_process_appearance() => WithManager((harness, manager) =>
    {
        var application = SwiftTraps.Unwrap(Application.Current);
        var content = new Border();
        ShowMain(harness, manager, content);
        var root = Hosts(content).Root;

        harness.Main.OnMain(() => manager.Model.Settings.Theme = AppTheme.CatppuccinLatte); // light (AppTheme.swift:19)
        harness.Main.OnMain(() => Settle(WindowOf(content)));
        Assert.Equal(ThemeVariant.Light, root.RequestedThemeVariant);
        Assert.Equal(ThemeVariant.Light, application.RequestedThemeVariant); // :155 .aqua

        harness.Main.OnMain(() => manager.Model.Settings.Theme = AppTheme.Dark); // AppTheme.swift:116
        harness.Main.OnMain(() => Settle(WindowOf(content)));
        Assert.Equal(ThemeVariant.Dark, root.RequestedThemeVariant);
        Assert.Equal(ThemeVariant.Dark, application.RequestedThemeVariant); // :156 .darkAqua
    });

    [Fact]
    [Trait("Req", "PR-THEME-039")] // App/Window/WindowManager.swift:151, :154
    [Trait("Req", "PR-WIN-020")] // App/Window/WindowManager.swift:151, :154
    public void The_System_theme_leaves_both_the_colour_scheme_and_the_process_appearance_to_Windows() => WithManager((harness, manager) =>
    {
        var application = SwiftTraps.Unwrap(Application.Current);
        harness.Main.OnMain(() => manager.Model.Settings.Theme = AppTheme.Dark);
        var content = new Border();
        ShowMain(harness, manager, content);
        Assert.Equal(ThemeVariant.Dark, application.RequestedThemeVariant);
        harness.Main.OnMain(() => manager.Model.Settings.Theme = AppTheme.System); // AppTheme.swift:114, scheme nil
        harness.Main.OnMain(() => Settle(WindowOf(content)));
        Assert.Equal(ThemeVariant.Default, Hosts(content).Root.RequestedThemeVariant); // preferredColorScheme(nil)
        Assert.Equal(ThemeVariant.Default, application.RequestedThemeVariant); // :154 case nil: nil
    });

    // ---- PR-WIN-021, PR-THEME-040: the tint ----

    [Fact]
    [Trait("Req", "PR-WIN-021")] // App/Window/WindowManager.swift:177, :183-193
    [Trait("Req", "PR-THEME-040")] // App/Window/WindowManager.swift:177, :183-193
    public void The_theme_accent_is_the_tint_of_the_whole_tree_and_a_nil_accent_is_a_nil_tint() => WithManager((harness, manager) =>
    {
        var content = new Border();
        var inner = new TextBlock();
        content.Child = inner;
        harness.Main.OnMain(() => manager.Model.Settings.Theme = AppTheme.System); // AppTheme.swift:114, no accent
        ShowMain(harness, manager, content);
        Assert.Null(Popovers.GetTint(inner)); // a nil tint keeps the system accent (:190-191)

        harness.Main.OnMain(() => manager.Model.Settings.Theme = AppTheme.Dracula);
        harness.Main.OnMain(() => Settle(WindowOf(content)));
        Assert.Equal(Color.FromRgb(0xBD, 0x93, 0xF9), SwiftTraps.Unwrap(Popovers.GetTint(inner)).Resolve(ThemeVariant.Dark));
    });

    [Fact]
    [Trait("Req", "PR-WIN-021")] // App/Window/WindowManager.swift:187-191
    [Trait("Req", "PR-THEME-040")] // App/Window/WindowManager.swift:183-193
    public void Changing_between_a_theme_with_an_accent_and_one_without_changes_the_tint_and_nothing_else() => WithManager((harness, manager) =>
    {
        var content = new Border();
        var attached = 0;
        var detached = 0;
        content.AttachedToVisualTree += (_, _) => attached++;
        content.DetachedFromVisualTree += (_, _) => detached++;
        harness.Main.OnMain(() => manager.Model.Settings.Theme = AppTheme.Light); // AppTheme.swift:115, no accent
        ShowMain(harness, manager, content);
        var (zoom, root, hosting) = Hosts(content);
        var attachedBefore = attached;

        harness.Main.OnMain(() => manager.Model.Settings.Theme = AppTheme.CatppuccinLatte); // an accent (AppTheme.swift:19)
        harness.Main.OnMain(() => Settle(WindowOf(content)));
        Assert.NotNull(Popovers.GetTint(content));
        harness.Main.OnMain(() => manager.Model.Settings.Theme = AppTheme.Light);
        harness.Main.OnMain(() => Settle(WindowOf(content)));
        Assert.Null(Popovers.GetTint(content));

        var after = Hosts(content);
        Assert.Same(zoom, after.Zoom);
        Assert.Same(root, after.Root);
        Assert.Same(hosting, after.Hosting);
        Assert.Equal(attachedBefore, attached);
        Assert.Equal(0, detached);
    });

    // ---- Part 2 (work packet part2.md): UI-SETTINGS-001, the settings window itself ----
    // The record's evidence in this file is :21, :80-84 and :92-99 (with :32-55 for how it opens, :98 for how it
    // closes and :101 for its clear window, WindowChrome.swift:111-117). Its evidence in SettingsView.swift:118-122
    // (the 26 pt clip, the backdrop at settings.backdropOpacity), the sidebar drag (SettingsView.swift:193), the page
    // hand-over (SettingsView.swift:123-127) and the openers (AppCommands.swift:12-14, CommandPalette.swift:176-178)
    // belong to other units: STOP App.Window.WindowManager #4 below. The Acrylic material OD-0014 names for the clear
    // window over the desktop is WindowChrome.Configure's (ED-236), closed STOP App.Window.WindowManager #5 below.
    // OD-0151: the Settings window's own tests (fixed 780 x 580 and titled, drawn from its top-left under its own title bar, centred over
    // the main window on every open) are gone with that window; the panel's size, shape and backdrop are in WindowManagerNextTests,
    // WindowManagerSettingsSurfaceTests and SettingsOverlayTests.

    // ED-236 (OD-0014, closes STOP #5): the clear window over the desktop is Acrylic under the app-drawn scrim, on the window
    // WindowChrome.configure (:101) reaches; OD-0151: Settings opens in that same window. Native pixels: GlassWindowNativeTests.
    [Fact]
    [Trait("Decision", "ED-236")]
    [Trait("Decision", "OD-0151")]
    [Trait("Req", "UI-SETTINGS-001")] // WindowChrome.swift:116-117 through WindowManager.swift:101; OD-0014
    public void The_main_window_asks_for_the_Acrylic_backdrop_over_the_clear_window_with_Settings_open_in_it() => WithManager((harness, manager) =>
    {
        var main = ShowMain(harness, manager, new Border());
        Assert.Equal([WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Transparent], main.TransparencyLevelHint);
        Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(main.Background).Color); // the clear window is kept
        var settings = ShowSettings(harness, manager, new Border());
        Assert.Same(main, settings);
        Assert.Equal([WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Transparent], main.TransparencyLevelHint);
        Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(main.Background).Color);
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-001")] // App/Window/WindowManager.swift:16, :34-35, :98
    [Trait("Decision", "OD-0151")]
    public void Closing_Settings_hides_its_panel_and_its_view_state_survives_so_the_next_open_shows_the_same_page_and_search_text() => WithManager((harness, manager) =>
    {
        // Stand-ins for SettingsView's @State page and search (SettingsView.swift:93-94), held by the hosted view.
        var search = new TextBox { Text = "zoom" };
        var page = new Border { Tag = SettingsPage.Appearance };
        var content = new StackPanel { Children = { search, page } };
        var built = 0;
        manager.SettingsContent = () =>
        {
            built++;
            return content;
        };
        harness.Main.OnMain(() => manager.ShowSettings());
        var window = WindowOf(content);
        var overlay = Assert.IsType<SettingsOverlay>(manager.SettingsOverlay);
        Assert.Same(manager.MainWindow, window);

        // The close button closes the panel (OD-0151; it was the window's performClose); :98 keeps it.
        harness.Main.OnMain(() =>
        {
            manager.CloseSettings();
            Settle(window);
        });
        Assert.False(manager.IsSettingsOpen);
        Assert.False(overlay.IsVisible);
        Assert.True(window.IsVisible); // the main window it is in stays
        Assert.Same(window, WindowOf(content));

        harness.Main.OnMain(() =>
        {
            manager.ShowSettings();
            Settle(window);
        });
        Assert.True(manager.IsSettingsOpen);
        Assert.True(overlay.IsVisible);
        Assert.Same(overlay, manager.SettingsOverlay);
        Assert.Equal(1, built); // :34 the kept view, not a new one
        Assert.Same(window, WindowOf(content));
        Assert.Equal("zoom", search.Text);
        Assert.Equal(SettingsPage.Appearance, page.Tag);
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-001")] // App/Window/WindowManager.swift:32-37
    [Trait("Decision", "OD-0151")]
    public void Reopening_Settings_with_a_page_requests_that_page_before_the_same_kept_view_is_shown_again() => WithManager((harness, manager) =>
    {
        var search = new TextBox { Text = "zoom" };
        var content = new StackPanel { Children = { search } };
        var window = ShowSettings(harness, manager, content);
        var overlay = Assert.IsType<SettingsOverlay>(manager.SettingsOverlay);
        harness.Main.OnMain(() =>
        {
            manager.CloseSettings();
            Settle(window);
            SettingsNavigation.Shared.RequestedPage = null;
        });
        Assert.False(manager.IsSettingsOpen);
        Assert.False(overlay.IsVisible);

        // OD-0151: Settings is shown when its panel becomes visible in the main window (it was the Settings window's show).
        SettingsPage? requestedWhenShown = null;
        var shown = 0;
        overlay.PropertyChanged += (_, e) =>
        {
            if (e.Property != Visual.IsVisibleProperty || !e.GetNewValue<bool>()) return;
            shown++;
            requestedWhenShown = SettingsNavigation.Shared.RequestedPage;
        };
        harness.Main.OnMain(() => manager.ShowSettings(SettingsPage.Mcp));

        Assert.Equal(1, shown);
        Assert.True(manager.IsSettingsOpen);
        Assert.Equal(SettingsPage.Mcp, requestedWhenShown); // :33 before :37: the view finds the page as it appears
        Assert.Equal(SettingsPage.Mcp, harness.Main.OnMain(() => SettingsNavigation.Shared.RequestedPage));
        Assert.Same(window, WindowOf(content)); // the same view state, which the requested page then overrides
        Assert.Same(overlay, manager.SettingsOverlay);
        Assert.Equal("zoom", search.Text);
    });
}

// CLOSED STOP App.Window.WindowManager #1 (2026-10-04): RootView and SettingsView construct (RootView and SettingsView are built):
// WindowManagerTitleMutationTests.The_unmodified_main_factory_hosts_one_real_RootView... and WindowManagerNextTests' real default-factory
// Settings tests run the two builders as written. The text below is the original STOP, kept as history.
// STOP App.Window.WindowManager #1 (build agent, 2026-09-26)
// Kind: missing dependency
// Swift: DroppyCode/App/Window/WindowManager.swift:63, :81 (B3 52bc792): `{ RootView() }`, `{ SettingsView() }`
// Requirements: PR-WIN-003, PR-WIN-009 (hosts RootView), PR-SETTINGS-001, PR-WIN-010
// What is unclear or missing: both content builders are written exactly as the Swift has them
// (WindowManager.MainContent is `() => new RootView()`, SettingsContent `() => new SettingsView()`), but RootView's
// body (App.Views.RootView, RootView.cs :8) and SettingsView's body (App.Views.Settings.SettingsView, :109) are still
// MARROW_STUB and throw on construction, so the real app's showMain() and showSettings() stop inside those callees.
// Readings or options: (a) leave the builders as written and test every window behaviour through the internal
// content seam -> this file; (b) substitute any other content in production -> an invented window content.
// Needed to proceed: units App.Views.RootView (body) and App.Views.Settings.SettingsView (body); then one end-to-end
// test per window with the real content.
// Done so far: every WindowManager member is implemented; the tests above host stand-ins and are green.

// CLOSED STOP App.Window.WindowManager #2 (2026-10-04): closed by the Boot unit's AppShell (the note under it), and the real exe exits
// on a main close (WindowManagerProcessTests, WindowManagerTerminationWaitTests). The text below is the original STOP, kept as history.
// STOP App.Window.WindowManager #2 (build agent, 2026-09-26)
// Kind: missing dependency
// Swift: DroppyCode/App/Window/WindowManager.swift:98 `window.isReleasedWhenClosed = false` (B3 52bc792)
// Requirements: PR-WIN-003, PR-WIN-008 (their OD-0008 C1 deviation); PR-LAUNCH-074
// What is unclear or missing: OD-0008 C1 makes closing the MAIN window quit the app through the termination
// sequence, with the upstream 250 ms / 4 minute wait while a Hydra merge runs (AppDelegate.swift:35-48,
// PR-LAUNCH-049, PR-LAUNCH-050, PR-LAUNCH-074). That sequence is App.AppDelegate's, not yet ported, and the port has
// no termination path yet (Program.cs runs no application lifetime). Quitting without the wait would break the ruling.
// Readings or options: (a) keep :98 for every window until AppDelegate lands: a closed window, main included, is
// hidden and kept, as upstream -> the Settings half of both records is met, the main-window quit is not yet;
// (b) quit on the main window's close without the Hydra wait -> contradicts OD-0008 C1.
// Needed to proceed: unit App.AppDelegate (applicationShouldTerminate and PR-LAUNCH-074), which intercepts the main
// window's close and starts the termination sequence.
// Done so far: (a) is implemented and tested for the Settings window; nothing is written for the main-window quit.

// Boot unit (2026-09-26): CLOSED. WindowManager.MainWindowClose is the seam the application shell (Port/AppShell.cs)
// sets; a close asked of the main window runs the terminate handshake instead of ordering it out (tested in
// Port/AppShellSecondTests.cs); every other window still hides as :98 has it.

// CLOSED STOP App.Window.WindowManager #3 (2026-09-28)
// Kind: implementation and real-Windows verification
// Swift: DroppyCode/App/Window/WindowManager.swift:101 `WindowChrome.configure(window)` (B3 52bc792)
// Requirements: PR-WIN-007, PR-SETTINGS-001, PR-THEME-039 (windows_path: the ED-063 caption hook); PR-WIN-017
// (the live-resize hook's wiring)
// Closed by WindowManager.MakeWindow's ED-063 WndProc bridge: it swallows valid app-drawn caption downs, posts the
// matching SC_* command on release, executes close/minimise/maximise/restore, and prepares the system menu for
// WM_CONTEXTMENU and SC_KEYMENU. WindowManagerHighRiskTests sends the real user32 messages through a native window,
// including the Settings zoom-inert case; the live-resize test sends real WM_ENTERSIZEMOVE/WM_SIZING/WM_EXITSIZEMOVE.
// OD-0013's separate WindowChrome live-resize edge-strip acceptance remains recorded under that owning unit.

// CLOSED STOP App.Window.WindowManager #4 (2026-10-04): the Settings view, the openers and the rest of UI-SETTINGS-001 are built and
// tested in their own units (WindowManagerSettingsSurfaceTests, WindowManagerSettingsOpenerTests, WindowManagerNextTests with the real
// SettingsView); the one part still open is the continuous corner curve (ED-181). The text below is the original STOP, kept as history.
// STOP App.Window.WindowManager #4 (build agent, 2026-09-26)
// Kind: missing dependency
// Swift: DroppyCode/App/Views/Settings/SettingsView.swift:118-122, :123-127, :193; App/AppCommands.swift:12-14;
// App/Views/Palette/CommandPalette.swift:176-178 (B3 52bc792)
// Requirements: UI-SETTINGS-001
// What is unclear or missing: this file builds, places, opens and keeps the Settings window (tested above with
// stand-in content). The rest of the record is drawn or done by the view the window hosts and by the commands that
// open it: the content clipped to Chrome.windowCornerRadius (26) continuous (SettingsView.swift:120), WindowBackdrop
// at model.settings.backdropOpacity behind the whole content (:118) and WindowChromeConfigurator (:119), the frame of
// 780 x 580 on the view itself (:122), the requested page taking over the kept view's page and being cleared
// (:123-127), the drag by the sidebar's WindowDragArea (:193), and the openers: the openSettings chord (Ctrl+, in
// Windows notation, OD-0008 B7; AppCommands.swift:12-14 without its menu, OD-0008 C3) and the palette's Settings item
// (CommandPalette.swift:176-178). SettingsView's body is still MARROW_STUB (STOP #1), App/AppCommands.cs does not
// exist yet, and the palette's items are still MARROW_STUB.
// Readings or options: (a) test this file's half of the record with stand-in content and leave the rest to the units
// that own those lines -> above; (b) draw any of it here -> another Swift file's view in this file (R-FILE-1).
// Needed to proceed: units App.Views.Settings.SettingsView (body), App.AppCommands and App.Views.Palette.CommandPalette;
// then one end-to-end test of UI-SETTINGS-001 with the real SettingsView and the real chord.
// Done so far: the five UI-SETTINGS-001 tests above (style and fixed size, clear window under a transparent title
// bar, placement without autosave, kept view state, the page requested on reopen); nothing is written for these sites.

// CLOSED STOP App.Window.WindowManager #5 (2026-10-02, ED-236)
// Kind: missing dependency (was)
// Swift: DroppyCode/UI/Chrome/WindowChrome.swift:116-117 `window.isOpaque = false` / `window.backgroundColor = .clear`,
// reached from DroppyCode/App/Window/WindowManager.swift:101 (B3 52bc792)
// Requirements: UI-SETTINGS-001 (colors, and its OD-0014 note); PR-WIN-058 and PR-WIN-059 draw over it
// Was: OD-0014 keeps the window clear, so until a wallpaper is set the glass sits over the desktop "drawn as Acrylic plus
// the app-drawn scrim" (OD-0005, probe P3), and nothing set the Acrylic hint, which left the main and the tour windows
// without a blur (native launch capture 2026-10-02: other windows' text showed through sharp).
// Closed by ED-236 (a decision naming the unit): WindowChrome.Configure, the twin of :116-117, sets
// WindowChrome.GlassBackdrop = [AcrylicBlur, Transparent] beside the transparent Background, so the main window and the
// Settings window made here have the Acrylic under the app-drawn scrim, and so does the tour window (OD-0151: Settings now opens in the
// main window, so that is the one window here). Proved by
// The_main_window_asks_for_the_Acrylic_backdrop_over_the_clear_window_with_Settings_open_in_it and on real screen pixels by
// GlassWindowNativeTests (stripes behind the real main window: contrast 42.9 bare, 0.0 through it, active and inactive).
// OD-0041's start-up gap is covered by the boot hand-over (BootStageWindowTests, GlassWindowNativeTests).
// Still open, named in ED-236 and not decided here: the corners outside the 26 pt shape show the Acrylic without the scrim
// (the window is a rectangle DWM rounds by 8 px; luma 126 beside 93 inside the shape), and a user-opened Settings window
// shows the 225 ms gap on its first show.

// ED-225 supplies WindowManager.swift:43, :68, :82's screen-centering binding. WindowManagerCenteringTests covers
// owner/pointer monitor selection, working area, horizontal center, one-third free height above, and clamping.
// The exact macOS vertical bias remains UNVERIFIED OD-0001.

// Review of STOPs on impl/b2 49eda0591 (2026-09-27); #3 and #5 are CLOSED since (see above):
// #1 is closed for SettingsView by WindowManagerNextTests' real default-factory tests; RootView's constructor
// still throws and belongs to another chat (current STOP #7). Neither dependency is modified here.
// #2 remains closed by AppShell; the new close test verifies the main close handoff and Settings-only hiding.
// #4's Settings body and requested-page/state retention are now tested with real SettingsView, not stand-ins.
// AppCommands.cs:57 and CommandPalette.cs:295 already call ShowSettings; the RootView opener/Sidebar path remains
// blocked by the concurrent Window unit. #3 and #5 remain open; see WindowManagerNextTests and the report.
