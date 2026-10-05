using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Views;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using AvaloniaWindow = Avalonia.Controls.Window;

// Source: DroppyCode/App/Window/WindowManager.swift (B3 52bc792), :1-222. Unchanged from B2 1fbf81e (same lines).
// Forms (ED-095): NSWindow is an Avalonia Window (ThreadWindow.cs's `AvaloniaWindow` alias, since this namespace is
// Marrow.App.Window); NSSize is Avalonia.Size; a point is one DIP (ED-071); a ViewModifier is a sealed record with
// `Control Body(Control content)`; the generic SwiftUI View HostedRoot<Content> is a ThemeVariantScope (a Decorator)
// taking its content as a Control; NSHostingView<AnyView> (FrameHostingView) is a Decorator holding the root view. The
// windowClass parameter of makeWindow (:90) is a factory for the window. DCDisplayCycleGuardedWindow is macOS-only and
// not re-implemented (dead_code.md 41; ThreadWindow.cs), so the default window class is the Avalonia Window.
// `@Environment(AppModel.self)` and `@Environment(WindowLiveResize.self)` are Port/AppEnvironment's inherited attached
// properties; `.tint` and `.buttonBorderShape(.capsule)` are UI/Chrome/Popovers.cs's (R-VIEW-6).
// NOT PORTED: makeCaptureWindow(size:) (:74-78) is reachable only from the website capture run (dead_code.md 39;
// ED-001), so it is not declared.
// The OD-0008 C1 quit on the main window's close (PR-LAUNCH-074) is the application shell's (Port/AppShell.cs): it sets
// MainWindowClose below, which the close handler of makeWindow (:98) runs for the main window in place of ordering it
// out (closes STOP App.Window.WindowManager #2). The ED-063 caption WndProc hook that sits beside WindowChrome.configure
// (:101) is installed by makeWindow below and is tested through a real user32 message window. UI-SETTINGS-001 (the Settings window, :21, :80-84, :92-99) is this file's as far as building, placing,
// opening and keeping the window go. SettingsView's real body, preserved state and requested-page handling are now
// exercised by WindowManagerNextTests (closes the Settings portion of STOP #1 and #4). The real main body still
// depends on the concurrent RootView/SidebarView repairs (STOP #7). The Acrylic backdrop OD-0014 names for the clear
// window (WindowChrome.swift:116-117, reached from :101) was STOP #5; ED-236 closed it: WindowChrome.Configure sets
// WindowChrome.GlassBackdrop ([AcrylicBlur, Transparent]) on the main and Settings windows made here (the tour window and
// the guide panels carry it too), proved on real screen pixels by GlassWindowNativeTests (the stripes behind the main
// window read 42.9 bare and 0.0 through it, active and inactive), and the boot hand-over (Port/BootStageWindow.cs,
// WindowCloak) keeps the ~230 ms between the Acrylic's tint and its blur (probes P13, P14; OD-0041) out of sight at launch.
// What ED-236 leaves open is its UNRESOLVED list: the wedge between the 26 pt shape and the rectangular window (closed since: OD-0075 and the owner's intent of
// 2026-10-05 extend the glass to the window's rectangle, UI.Chrome.WindowBackdrop's layers in RootView, SettingsView, the two guide panels and the tour card, so the corner is the body
// beside it; ED-338, proved on real screen pixels by MainWindowCornerWedgeNativeTests and GlassWindowNativeTests), and the same
// gap on a user-opened Settings window, which ED-249 (OD-0074) closes: every open of Settings is shown cloaked and revealed at
// glass-ready with Rise (Port/SettingsRise.cs). The three
// window.center() calls (:43, :68, :82) use ED-225; Apple's exact bias remains UNVERIFIED OD-0001.
// makeWindow also carries the port's stand-in for the menu bar DroppyCodeApp.swift:24 installs: each window it makes
// dispatches the AppCommands chords itself (OD-0008 C3, Port/MenuKeyEquivalents.cs; STOP App.AppCommands #6).

namespace Marrow.App.Window;

/// <summary>:9-111 <c>@MainActor final class WindowManager</c>: builds the app's windows.</summary>
public sealed class WindowManager
{
    // :11 static let shared = WindowManager(). R-LIFE-2: made on first use, as a Swift static is, because making it
    // makes the AppModel (:13), which reads the library from disk.
    private static readonly Ambient<WindowManager> SharedAmbient = new(() => new WindowManager());

    public static WindowManager Shared => SharedAmbient.Current;

    /// <summary>For tests only: replaces <see cref="Shared"/> for the calling async flow.</summary>
    internal static IDisposable OverrideShared(WindowManager manager) => SharedAmbient.Override(manager);

    // :13 let model = AppModel()
    public AppModel Model { get; }

    private AvaloniaWindow? mainWindow; // :15
    private AvaloniaWindow? settingsWindow; // :16

    // ED-063: WindowChrome declares the app-drawn caption controls with Win32 hit-test values; this App-layer hook owns
    // their native actions because Avalonia 12.1.2 does not perform them for app-drawn controls by itself.
    private readonly Dictionary<AvaloniaWindow, CaptionState> captionStates = new();

    private sealed class CaptionState
    {
        public nint PendingHitTest;
        public bool TrackingLeave;
    }

    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint WM_NCLBUTTONDOWN = 0x00A1;
    private const uint WM_NCLBUTTONUP = 0x00A2;
    private const uint WM_NCMOUSEMOVE = 0x00A0;
    private const uint WM_NCMOUSELEAVE = 0x02A2;
    private const uint TME_LEAVE = 0x00000002;
    private const uint TME_NONCLIENT = 0x00000010;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_NCHITTEST = 0x0084;
    private const uint WM_SYSCOMMAND = 0x0112;
    private const uint WM_CANCELMODE = 0x001F;
    private const uint WM_CAPTURECHANGED = 0x0215;
    private const uint SC_SIZE = 0xF000;
    private const uint SC_MINIMIZE = 0xF020;
    private const uint SC_MAXIMIZE = 0xF030;
    private const uint SC_CLOSE = 0xF060;
    private const uint SC_KEYMENU = 0xF100;
    private const uint SC_RESTORE = 0xF120;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint MF_BYCOMMAND = 0x00000000;
    private const uint MF_ENABLED = 0x00000000;
    private const uint MF_GRAYED = 0x00000001;

    /// <summary>:18-22 <c>private enum Size</c>.</summary>
    private static class Size
    {
        public static readonly Avalonia.Size Main = new(1320, 860); // :19
        public static readonly Avalonia.Size MainMinimum = new(860, 560); // :20
        public static readonly Avalonia.Size Settings = new(780, 580); // :21
    }

    // :24 private static let mainFrameName = "DroppyCodeMainWindow". ED-006: the port keeps the frame in its own
    // settings store under this exact name (PR-WIN-006 windows_path).
    private const string MainFrameName = "DroppyCodeMainWindow";

    // :63 `{ RootView() }` and :81 `{ SettingsView() }`, each window's content, built by makeWindow (:104). Internal and
    // settable only for headless content substitution. SettingsContent's default is now exercised with the real
    // SettingsView; MainContent's real RootView is blocked by its owned dependency (STOP #7). The app sets neither.
    internal Func<Control> MainContent { get; set; } = () => new RootView();
    internal Func<Control> SettingsContent { get; set; } = () => new SettingsView();

    // PORT-ONLY (OD-0008 C1, C2; PR-LAUNCH-074): what a close asked of the main window itself (its close control, Alt+F4)
    // does. The application shell sets it to its terminate request (Port/AppShell.cs), so the main window stays up while
    // the quit waits for a Hydra merge (AppDelegate.swift:44-54) and the lifetime's shutdown then closes it. Unset (no
    // shell), the main window is ordered out like every other window (:98).
    internal Action? MainWindowClose { get; set; }

    /// <summary>PORT-ONLY: the main window once <see cref="ShowMain"/> has made it (:15), for the application shell's
    /// reopen (ED-002) and session-end hook (ED-004); null before.</summary>
    internal AvaloniaWindow? MainWindow => mainWindow;

    // The implicit init of the Swift singleton (module-internal in Swift): `model` (:13) is made here.
    internal WindowManager() => Model = new AppModel();

    /// <summary>
    /// PORT-ONLY (OD-0018, ED-103): makes the main window as showMain would (:27-28), frame restored, without showing
    /// it, so the launch's boot sequence can be laid out on it before it appears.
    /// </summary>
    internal AvaloniaWindow PrepareMain() => mainWindow ??= MakeMainWindow();

    // :26-30 func showMain()
    public void ShowMain()
    {
        var window = mainWindow ?? MakeMainWindow(); // :27
        mainWindow = window; // :28
        Present(window); // :29
    }

    // :32-38 func showSettings(page: SettingsPage? = nil)
    public void ShowSettings(SettingsPage? page = null)
    {
        if (page is { } requested) SettingsNavigation.Shared.RequestedPage = requested; // :33
        var window = settingsWindow ?? MakeSettingsWindow(); // :34
        settingsWindow = window; // :35
        CenterOverMainWindow(window); // :36
        // DEVIATION OD-0074 (ED-249): an open of the hidden window is Rise (Port/SettingsRise.cs): shown cloaked, revealed
        // at glass-ready, then activated, the glass fading in and the items rising in a 0.26 s wave. A window already up is
        // brought forward as before, except while it is still cloaked for its reveal (the reveal activates it).
        if (!window.IsVisible && settingsView is { } view)
        {
            settingsRise?.Stop();
            settingsRise = SettingsRise.Open(window, view, SettingsCloak, () => ShowRestoring(window), window.Activate);
            return;
        }
        if (settingsRise is { Waiting: true }) return;
        Present(window); // :37
    }

    /// <summary>PORT-ONLY (OD-0074, ED-249): how the Settings window is cloaked for its open; DWM cloaking
    /// (<see cref="WindowCloak"/>) unless a test stands in for it.</summary>
    internal Func<AvaloniaWindow, bool, bool> SettingsCloak { get; set; } = WindowCloak.Set;

    // PORT-ONLY (OD-0074, ED-249): the Settings window's current open; null when it could not be cloaked.
    private SettingsRise? settingsRise;

    // PORT-ONLY (OD-0074, ED-249): the Settings window's content when it is the real SettingsView (:81).
    private SettingsView? settingsView;

    /// <summary>PORT-ONLY: the Settings window's current open, for tests.</summary>
    internal SettingsRise? SettingsOpening => settingsRise;

    /// <summary>:40-55 Opens a window centered over the main window, kept on the screen the main window is on.</summary>
    private void CenterOverMainWindow(AvaloniaWindow window)
    {
        // :42-45
        if (mainWindow is not { } main || !main.IsVisible || main.WindowState == WindowState.Minimized)
        {
            WindowCentering.Center(window, FrameSizeOf(window)); // :43 window.center(), ED-225
            return; // :44
        }
        // In points: the main window's position (pixels) over its own scale, as NSWindow.frame is in points.
        var scale = main.DesktopScaling;
        // An NSWindow frame's origin is its bottom-left corner, in one global space whose y grows upward from the
        // bottom edge of the primary screen (NSScreen.screens[0]). Windows' primary monitor has its top-left corner at
        // the virtual screen's origin and y grows downward, so AppKit's y is `up - y` for a point `y` down, where `up`
        // is the primary screen's bottom edge; :49 rounds, and :52 clamps, in AppKit's y, then :54 converts back. With
        // no screen at all (nothing a window could be shown on) there is no primary screen to measure from, and the
        // virtual screen's origin line stands in for its bottom edge.
        var up = main.Screens.Primary is { } primary ? primary.Bounds.ToRect(scale).Bottom : 0;
        var parentSize = main.FrameSize ?? FrameSizeOf(main); // :46 main.frame, not the requested content size.
        var parent = new Rect(main.Position.X / scale, up - (main.Position.Y / scale + parentSize.Height), parentSize.Width, parentSize.Height); // :46
        // :47 window.frame, for the HWND the visible frame and the invisible strip around it (WindowInvisibleBorder), in points.
        var strip = WindowInvisibleBorder.Pixels.Current(window, scale) / scale;
        var frame = FrameSizeOf(window) + new Avalonia.Size(2 * strip, strip);
        // The Settings HWND is centred on the HWND centre of the main window moved to the visible centre of the main window
        // (+ strip / 2 for the bottom strip the Settings frame carries). A normal main HWND keeps its strip on the left, the
        // right and the bottom, so its visible centre lies strip / 2 above its HWND centre; a MAXIMISED one has the strip on
        // all four sides (Avalonia's WM_NCCALCSIZE maximise branch insets the client by the border on every side), so its HWND
        // centre is its visible centre. The shift is in AppKit's y (up): a lower HWND centre is a smaller y.
        var mainStrip = WindowInvisibleBorder.Pixels.Current(main, scale) / scale;
        var mainTop = main.WindowState == WindowState.Maximized ? mainStrip : 0;
        var shiftDown = (mainTop - mainStrip + strip) / 2;
        parent = parent.WithY(parent.Y - shiftDown);
        Rect? visible = null;
        // :50 (main.screen ?? NSScreen.main)?.visibleFrame: the working area (the taskbar and app bars left out) of the
        // screen the main window is on, else the primary screen; with no screen at all the clamp is skipped.
        if ((main.Screens.ScreenFromWindow(main) ?? main.Screens.Primary) is { } screen)
        {
            var working = screen.WorkingArea.ToRect(scale);
            // visibleFrame in AppKit's y: minY is the working area's bottom edge, maxY its top edge. The window's invisible
            // strip (left, right, bottom) may lie outside it: the clamp is of the visible window, as AppKit's frame is.
            visible = new Rect(working.Left - strip, up - working.Bottom - strip, working.Width + 2 * strip, working.Height + strip);
        }
        var centered = CenteredFrame(parent, frame, visible); // :46-54
        // :54 window.setFrame(frame, display: false): only the origin moves (the size is the window's own). Back to
        // Windows' downward y (the top edge is `up` less the bottom edge and the height), and, a position being whole
        // pixels, to pixels at the main window's scale.
        var top = up - centered.Bottom;
        window.Position = new PixelPoint(checked((int)SwiftNumber.Rounded(centered.X * scale)), checked((int)SwiftNumber.Rounded(top * scale)));
    }

    // :46-54 The source's point-space calculation, shared with the headless edge-case tests. A missing screen
    // leaves the rounded origin alone; a smaller working area still uses the outer min, without shrinking the
    // frame. This centers over a visible main window; NSWindow.center() uses the separate ED-225 binding.
    private static Rect CenteredFrame(Rect parent, Avalonia.Size frame, Rect? visible)
    {
        var x = SwiftNumber.Rounded(parent.Center.X - frame.Width / 2); // :48
        var y = SwiftNumber.Rounded(parent.Center.Y - frame.Height / 2); // :49
        if (visible is { } working) // :50
        {
            x = SwiftMin(SwiftMax(x, working.Left), working.Right - frame.Width); // :51
            y = SwiftMin(SwiftMax(y, working.Top), working.Bottom - frame.Height); // :52 minY and maxY in upward point space
        }
        return new Rect(x, y, frame.Width, frame.Height); // :54 only the origin changes
    }

    // :57-60 private func present(_ window: NSWindow)
    private void Present(AvaloniaWindow window)
    {
        // :58 NSApp.activate() then :59 makeKeyAndOrderFront(nil). Windows has no activation of an application apart
        // from one of its windows (PR-WIN-013 windows_path): the window is shown (ordered front; a hidden window comes
        // back) and then activated, which asks Windows for the foreground (SetForegroundWindow); when Windows refuses
        // it, the window's taskbar button flashes instead.
        ShowRestoring(window);
        window.Activate();
    }

    // :59 the ordering front of makeKeyAndOrderFront, without its activation (Present; the Settings open, ED-249).
    private void ShowRestoring(AvaloniaWindow window)
    {
        window.Show();
        // OD-0038: a minimised window is restored to the state it had before it was minimised (normal or maximised),
        // for the second launch and the reopen alike. Whether makeKeyAndOrderFront alone deminiaturizes is UNVERIFIED
        // (OD-0001); the owner ruled it does here.
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = restoredStates.TryGetValue(window, out var restored) ? restored.Value : WindowState.Normal;
    }

    // OD-0038: each window's last state other than minimised, which a restore returns it to.
    private readonly ConditionalWeakTable<AvaloniaWindow, StrongBox<WindowState>> restoredStates = new();

    // :62-72 private func makeMainWindow() -> NSWindow
    private AvaloniaWindow MakeMainWindow()
    {
        var window = MakeWindow(AppInfo.Current.Name, Size.Main, resizable: true, MainContent, windowClass: () => new ThreadWindow()); // :63
        var model = new WeakReference<AppModel>(Model); // [weak model]
        (window as ThreadWindow)?.SelectThread = offset => // :64
        {
            if (model.TryGetTarget(out var target)) target.SelectThread(offset);
        };
        (window as ThreadWindow)?.SelectThreadNumber = number => // :65
        {
            if (model.TryGetTarget(out var target)) target.SelectThreadNumber(number);
        };
        // :66 window.contentMinSize: Avalonia reports it to Windows as the minimum tracking size (WM_GETMINMAXINFO).
        window.MinWidth = Size.MainMinimum.Width;
        // ENGINEERING DECISION (ED-332 (1)): the minimum HEIGHT of the CLIENT is 560, but Avalonia adds the standard
        // frame's caption to the minimum tracking height of a window whose client area is extended under it (WindowInvisibleBorder.
        // CaptionAboveClient); that caption is taken off, per DPI, so the HWND stops at the source's 560 and not at 591.
        void ApplyMinimumHeight() => window.MinHeight = Size.MainMinimum.Height - WindowInvisibleBorder.CaptionAboveClient.Current(window, window.RenderScaling) / window.RenderScaling;
        ApplyMinimumHeight();
        window.ScalingChanged += (_, _) => ApplyMinimumHeight();
        // :67-69 if !window.setFrameUsingName(Self.mainFrameName) { window.center() }
        if (!SetFrameUsingName(window, MainFrameName)) WindowCentering.Center(window, FrameSizeOf(window)); // :68, ED-225
        SetFrameAutosaveName(window, MainFrameName); // :70
        return window; // :71
    }

    // :80-84 private func makeSettingsWindow() -> NSWindow
    private AvaloniaWindow MakeSettingsWindow()
    {
        var window = MakeWindow("Settings", Size.Settings, resizable: false, () => { var content = SettingsContent(); settingsView = content as SettingsView; return content; }, windowClass: () => new SecondaryWindow()); // :81
        WindowCentering.Center(window, FrameSizeOf(window)); // :82 window.center(), ED-225
        // DEVIATION OD-0088, OD-0076 (ED-254): the Settings window turns over on a change of appearance, every colour in it
        // easing 0.32 s as the locked accent board's window does (SPEC 6.5), where the source changes it in one frame.
        AppearanceTurn.SetTurnsOver(window, true);
        return window; // :83
    }

    // :86-110 private func makeWindow<Content: View>(title:size:resizable:windowClass:content:) -> NSWindow
    private AvaloniaWindow MakeWindow(string title, Avalonia.Size size, bool resizable, Func<Control> content, Func<AvaloniaWindow>? windowClass = null)
    {
        // :95-96 windowClass.init(contentRect: frame, styleMask: style, backing: .buffered, defer: false): an Avalonia
        // window makes its platform window in its constructor (not deferred) and draws through its own buffer, so
        // .buffered and defer: false need nothing more. The content rect is the window's client size.
        var window = (windowClass ?? (() => new AvaloniaWindow()))();
        window.Width = size.Width;
        window.Height = size.Height;
        // :93-94 [.titled, .closable, .miniaturizable, .fullSizeContentView], .resizable only when asked. Titled,
        // closable and the full-size content view are WindowChrome.configure's (:101: WindowDecorations.Full with the
        // client area extended under the title bar, ED-063); a window that is not resizable has no resize border and
        // Avalonia takes its maximise control with it (CanMaximize follows CanResize), as AppKit disables zoom.
        window.CanMinimize = true;
        window.CanResize = resizable;
        var inView = false;
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == AvaloniaWindow.WindowStateProperty && e.GetNewValue<WindowState>() is var state and not WindowState.Minimized)
                restoredStates.AddOrUpdate(window, new StrongBox<WindowState>(state)); // OD-0038
            // HydraButton.swift:476 NSWindow.didChangeOcclusionStateNotification (PR-HYDRA-242). ENGINEERING DECISION:
            // Windows posts no occlusion change to an app, so a window counts as coming into or going out of view when it
            // is shown or hidden and when it is restored or minimised; a change is posted only when that state flips.
            if (e.Property == AvaloniaWindow.WindowStateProperty || e.Property == Visual.IsVisibleProperty)
            {
                var now = window.IsVisible && window.WindowState != WindowState.Minimized;
                if (now == inView) return;
                inView = now;
                Marrow.App.Views.Chat.HydraChargeLayerView.PostWindowDidChangeOcclusionState(window, now);
            }
        };
        window.Title = title; // :97
        // :98 isReleasedWhenClosed = false: closing orders the window out and keeps it, so the next show brings back the
        // same window with its content. An Avalonia window that closes is disposed and can never be shown again, so a
        // close asked of the window itself (its close control, performClose, Alt+F4) hides it instead; the
        // application's own shutdown still closes it.
        window.Closing += (_, e) =>
        {
            if (e.CloseReason != WindowCloseReason.WindowClosing) return;
            e.Cancel = true;
            // DEVIATION OD-0008 C1: the main window's own close quits the app through the shell (PR-LAUNCH-074).
            if (ReferenceEquals(window, mainWindow) && MainWindowClose is { } quit)
            {
                quit();
                return;
            }
            window.Hide();
        };
        // :99 animationBehavior = .documentWindow: the system's own window animation, which Windows applies to every
        // top-level window (and honours "Animate windows when minimizing and maximizing"); nothing to set.
        // :100 tabbingMode = .disallowed: Windows has no window tabs (PR-WIN-008 windows_path, not applicable).
        WindowChrome.Configure(window); // :101
        // ED-063: SetNonClientHitTestResult declares the app-drawn caption controls, but Windows does not execute
        // their commands for an Avalonia full-decoration window. Keep the hook with the window for its full lifetime.
        Win32Properties.AddWndProcHookCallback(window, (nint hWnd, uint message, nint wParam, nint lParam, ref bool handled) =>
            HandleNativeWindowMessage(window, hWnd, message, wParam, lParam, ref handled));

        var liveResize = new WindowLiveResize(window); // :103
        var hosting = new FrameHostingView(new HostedRoot(Model, liveResize, content())); // :104
        // :105 hosting.frame = frame, :106 NSView(frame: frame): the plain container fills the client area and lays
        // its one child out over the whole of it.
        var container = new Panel();
        container.Children.Add(hosting); // :107
        container.Children.Add(WindowChrome.CaptionLayer(window)); // OD-0044: the caption buttons over the content
        window.Content = container; // :108
        // PORT-ONLY (OD-0008 C3): DroppyCodeApp.swift:21-25 installs `AppCommands(model: WindowManager.shared.model)` as
        // the menu bar, which answers the key equivalents of whichever window of the app is key. With no menu bar, each
        // window this factory makes (main and Settings) carries the port's dispatch of those chords
        // (Port/MenuKeyEquivalents.cs) for as long as it lives (:98 keeps it). The command table is read afresh at each
        // key-down, as SwiftUI re-reads the Commands body, over this manager's model (:13). Views that monitor the window's
        // keys register when they attach, after this, so they still see a key-down first, as a local monitor does.
        _ = MenuKeyEquivalents.Attach(window, (positional, typed) => new AppCommands(Model).PerformKeyDown(positional, typed));
        return window; // :109
    }

    /// <summary>
    /// ED-063's Windows caption bridge. WindowChrome declares the three app-drawn controls through native hit-test
    /// values, but Avalonia does not execute those values for a full-decoration window. The native down is swallowed,
    /// the matching command is posted on release, and the command is applied on the window's UI thread. The same hook
    /// owns the system menu entry points so every window made by this factory has the same chrome behaviour.
    /// </summary>
    internal nint HandleNativeWindowMessage(
        AvaloniaWindow window,
        nint hWnd,
        uint message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (!captionStates.TryGetValue(window, out var state))
        {
            state = new CaptionState();
            captionStates.Add(window, state);
        }

        switch (message)
        {
            // OD-0044: the caption buttons' hover (Turn pulse) follows the non-client pointer: WM_NCMOUSEMOVE names the
            // hit-test value under it, and WM_NCMOUSELEAVE (asked for once per entry with TrackMouseEvent) ends it.
            case WM_NCMOUSEMOVE:
                WindowChrome.SetCaptionHover(window, IsCaptionButton(wParam, window) ? (int)wParam : 0);
                if (!state.TrackingLeave && OperatingSystem.IsWindows() && hWnd != 0)
                {
                    var track = new TrackMouseEventInfo { Size = (uint)Marshal.SizeOf<TrackMouseEventInfo>(), Flags = TME_LEAVE | TME_NONCLIENT, Window = hWnd };
                    state.TrackingLeave = TrackMouseEvent(ref track);
                }
                break;

            case WM_NCMOUSELEAVE:
                state.TrackingLeave = false;
                if (state.PendingHitTest == 0) WindowChrome.SetCaptionHover(window, 0);
                break;

            case WM_NCLBUTTONDOWN when IsCaptionButton(wParam, window):
                state.PendingHitTest = wParam;
                WindowChrome.SetCaptionPressed(window, (int)wParam);
                if (OperatingSystem.IsWindows() && hWnd != 0) _ = SetCapture(hWnd);
                handled = true;
                return 0;

            case WM_NCLBUTTONUP when state.PendingHitTest != 0:
            case WM_LBUTTONUP when state.PendingHitTest != 0:
            {
                // SetCapture routes the physical release through WM_LBUTTONUP in client coordinates.
                // Re-hit-test that release against this HWND; a release over another control cancels.
                if (message == WM_LBUTTONUP) wParam = CaptionHitAtClientPoint(hWnd, lParam);
                var hitTest = state.PendingHitTest;
                state.PendingHitTest = 0;
                WindowChrome.SetCaptionPressed(window, 0);
                WindowChrome.SetCaptionHover(window, IsCaptionButton(wParam, window) ? (int)wParam : 0);
                if (OperatingSystem.IsWindows() && hWnd != 0) _ = ReleaseCapture();
                if (hitTest == wParam && IsCaptionButton(hitTest, window))
                {
                    var command = CommandForCaptionButton(hitTest, window);
                    if (OperatingSystem.IsWindows() && hWnd != 0)
                        _ = PostMessage(hWnd, WM_SYSCOMMAND, (nint)command, 0);
                    else
                        Dispatcher.UIThread.Post(() => ExecuteSystemCommand(window, command), DispatcherPriority.Input);
                }
                handled = true;
                return 0;
            }

            case WM_CANCELMODE:
            case WM_CAPTURECHANGED:
                if (state.PendingHitTest != 0) WindowChrome.SetCaptionPressed(window, 0);
                state.PendingHitTest = 0;
                break;

            case WM_CONTEXTMENU:
                if (TryShowSystemMenu(window, hWnd, lParam))
                {
                    handled = true;
                    return 0;
                }
                break;

            case WM_SYSCOMMAND:
            {
                var rawCommand = unchecked((uint)wParam.ToInt64());
                if ((rawCommand & 0xFFF0) == SC_KEYMENU)
                {
                    // ED-063/P7: only Space requests the window menu. Other mnemonics belong to
                    // normal platform handling; lParam is a character here, not packed coordinates.
                    if (lParam == (nint)' ' && TryShowSystemMenu(window, hWnd, lParam, keyboard: true))
                    {
                        handled = true;
                        return 0;
                    }
                    break;
                }

                var command = rawCommand & 0xFFF0;
                if (ExecuteSystemCommand(window, command))
                {
                    handled = true;
                    return 0;
                }
                break;
            }
        }

        return 0;
    }

    private static bool IsCaptionButton(nint hitTest, AvaloniaWindow window) =>
        hitTest.ToInt64() switch
        {
            20 => true, // Win32HitTestValue.Close
            8 => window.CanMinimize, // Win32HitTestValue.MinButton
            9 => window.CanMaximize, // Win32HitTestValue.MaxButton
            _ => false,
        };

    private static uint CommandForCaptionButton(nint hitTest, AvaloniaWindow window) =>
        hitTest.ToInt64() switch
        {
            20 => SC_CLOSE,
            8 => SC_MINIMIZE,
            9 when window.WindowState == WindowState.Maximized => SC_RESTORE,
            9 => SC_MAXIMIZE,
            _ => 0,
        };

    private static bool ExecuteSystemCommand(AvaloniaWindow window, uint command) =>
        command switch
        {
            SC_CLOSE => CloseWindow(window),
            SC_MINIMIZE when window.CanMinimize => SetWindowState(window, WindowState.Minimized),
            // Consume a denied maximize too: falling through lets DefWindowProc maximize a fixed Settings HWND.
            SC_MAXIMIZE => !window.CanMaximize || SetWindowState(window, WindowState.Maximized),
            SC_RESTORE when window.WindowState != WindowState.Normal => SetWindowState(window, WindowState.Normal),
            _ => false,
        };

    private static bool CloseWindow(AvaloniaWindow window)
    {
        window.Close();
        return true;
    }

    private static bool SetWindowState(AvaloniaWindow window, WindowState state)
    {
        window.WindowState = state;
        return true;
    }

    private static nint CaptionHitAtClientPoint(nint hWnd, nint lParam)
    {
        if (!OperatingSystem.IsWindows() || hWnd == 0) return 0;
        var packed = unchecked((int)lParam.ToInt64());
        var point = new NativePoint { X = (short)(packed & 0xFFFF), Y = (short)(packed >> 16) };
        if (!ClientToScreen(hWnd, ref point)) return 0;
        var screen = unchecked((nint)(uint)((ushort)(short)point.X | ((uint)(ushort)(short)point.Y << 16)));
        return SendMessage(hWnd, WM_NCHITTEST, 0, screen);
    }

    private static bool TryShowSystemMenu(AvaloniaWindow window, nint hWnd, nint lParam, bool keyboard = false)
    {
        if (!OperatingSystem.IsWindows() || hWnd == 0) return false;
        var menu = GetSystemMenu(hWnd, false);
        if (menu == 0) return false;

        SetMenuItemState(menu, SC_SIZE, window.CanResize && window.WindowState == WindowState.Normal);
        SetMenuItemState(menu, SC_MINIMIZE, window.CanMinimize && window.WindowState != WindowState.Minimized);
        SetMenuItemState(menu, SC_MAXIMIZE, window.CanMaximize && window.WindowState != WindowState.Maximized);
        SetMenuItemState(menu, SC_RESTORE, window.WindowState != WindowState.Normal);
        SetMenuItemState(menu, SC_CLOSE, true);

        var point = ContextMenuPoint(lParam);
        if (keyboard)
        {
            // Preserve the proven P7 keyboard-menu anchor at the leading caption, not (' ', 0).
            if (!GetWindowRect(hWnd, out var frame)) return false;
            point = new ScreenPoint(frame.Left + 8, frame.Top + 44);
        }
        var command = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, point.X, point.Y, hWnd, 0);
        if (command != 0) _ = PostMessage(hWnd, WM_SYSCOMMAND, (nint)command, 0);
        return true;
    }

    private static void SetMenuItemState(nint menu, uint command, bool enabled) =>
        _ = EnableMenuItem(menu, command, MF_BYCOMMAND | (enabled ? MF_ENABLED : MF_GRAYED));

    private static ScreenPoint ContextMenuPoint(nint lParam)
    {
        if (lParam == -1 && GetCursorPos(out var cursor)) return new ScreenPoint(cursor.X, cursor.Y);
        var packed = unchecked((int)lParam.ToInt64());
        return new ScreenPoint((short)(packed & 0xFFFF), (short)(packed >> 16));
    }

    private readonly record struct ScreenPoint(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetCapture(nint hWnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool TrackMouseEvent(ref TrackMouseEventInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct TrackMouseEventInfo
    {
        public uint Size;
        public uint Flags;
        public nint Window;
        public uint HoverTime;
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool ReleaseCapture();
    [DllImport("user32.dll", SetLastError = true)] private static extern nint GetSystemMenu(nint hWnd, bool revert);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint EnableMenuItem(nint menu, uint item, uint enable);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint TrackPopupMenuEx(
        nint menu, uint flags, int x, int y, nint hWnd, nint reserved);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool ClientToScreen(nint hWnd, ref NativePoint point);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(nint hWnd, out NativeRect frame);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessage(nint hWnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessage(
        nint hWnd, uint message, nint wParam, nint lParam);

    // NSWindow.frame's size, in points: the content size plus the decorations around it, which the platform reports
    // once the window has been shown (Window.FrameSize). Avalonia measures a window the same way for its own centring.
    // An HWND (the factory's windows, ED-063) is the exception: its title bar is drawn inside the client area, so the frame
    // is the content rect (:93-96) and nothing is added; before the first show the platform reports the standard overlapped
    // frame instead (+39 px of caption), which centred the window 11 to 15 px too high (WindowInvisibleBorder).
    private static Avalonia.Size FrameSizeOf(AvaloniaWindow window) =>
        window.TryGetPlatformHandle() is { Handle: not 0 } handle && handle.HandleDescriptor == "HWND"
            ? new Avalonia.Size(window.Width, window.Height)
            : new Avalonia.Size(window.Width, window.Height) + (window.FrameSize is { } frame ? frame - window.ClientSize : default);

    // :67 window.setFrameUsingName(_:): applies the frame saved under the name, as it is (no clamp to any screen), and
    // answers whether there was one. A frame the port did not write whole is no frame.
    private static bool SetFrameUsingName(AvaloniaWindow window, string name)
    {
        if (UserDefaults.Standard.Current.Dictionary(name) is not { } saved) return false;
        if (!saved.TryGetValue(FrameKey.X, out var xValue) || Plist.AsInt(xValue) is not { } x ||
            !saved.TryGetValue(FrameKey.Y, out var yValue) || Plist.AsInt(yValue) is not { } y ||
            !saved.TryGetValue(FrameKey.Width, out var widthValue) || Plist.AsDouble(widthValue) is not { } width ||
            !saved.TryGetValue(FrameKey.Height, out var heightValue) || Plist.AsDouble(heightValue) is not { } height ||
            x is < int.MinValue or > int.MaxValue || y is < int.MinValue or > int.MaxValue ||
            !double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) return false;
        window.Position = new PixelPoint((int)x, (int)y);
        window.Width = width;
        window.Height = height;
        return true;
    }

    // :70 window.setFrameAutosaveName(_:): from now on every move and resize writes the frame under the name (ED-006:
    // the window's position in pixels and its content size in points, in the port's settings store). Windows parks a
    // minimised window at (-32000, -32000) and gives a maximised one the whole working area, neither of which is a
    // frame AppKit would write (a miniaturized window keeps its frame), so only a window in its normal state writes.
    private static void SetFrameAutosaveName(AvaloniaWindow window, string name)
    {
        void Save()
        {
            if (window.WindowState != WindowState.Normal) return;
            UserDefaults.Standard.Current.Set(name, new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [FrameKey.X] = (long)window.Position.X,
                [FrameKey.Y] = (long)window.Position.Y,
                [FrameKey.Width] = window.Width,
                [FrameKey.Height] = window.Height,
            });
        }
        window.PositionChanged += (_, _) => Save();
        window.Resized += (_, _) => Save();
    }

    // The fields of the saved frame (ED-006: the port's own format).
    private static class FrameKey
    {
        public const string X = "x";
        public const string Y = "y";
        public const string Width = "width";
        public const string Height = "height";
    }

    // Swift.min and Swift.max on CGFloat (R-NUM-4).
    private static double SwiftMin(double x, double y) => y < x ? y : x;

    private static double SwiftMax(double x, double y) => y >= x ? y : x;
}

/// <summary>:113-140 <c>@MainActor @Observable final class WindowLiveResize</c> (R-OBS-6): whether the window is
/// being dragged to a new size right now. Views read it from the environment to hold their settle animations and
/// heavier layout work until the drag ends.</summary>
public sealed class WindowLiveResize : MainActorModel
{
    // The window messages that bracket a size or move drag and step a size drag (winuser.h).
    private const uint WM_SIZING = 0x0214;
    private const uint WM_ENTERSIZEMOVE = 0x0231;
    private const uint WM_EXITSIZEMOVE = 0x0232;

    private bool isActive; // :120
    private bool isPaneResizing; // :125
    private bool inSizeMove;
    private bool resizing;
    private readonly AvaloniaWindow window; // ED-224: publish to the UI signal on this window only.

    // :120 private(set) var isActive = false
    public bool IsActive
    {
        get => Read(isActive);
        private set
        {
            Set(ref isActive, value);
            WindowLiveResizeSignal.SetIsActive(window, value); // WindowChrome.swift:28, :93; ED-224
        }
    }

    // :121-125 var isPaneResizing = false. Written by the chat view, where the sidebar-edge and terminal-divider drags
    // are observed.
    public bool IsPaneResizing
    {
        get => Read(isPaneResizing);
        set => Set(ref isPaneResizing, value);
    }

    // :126-127 var isReshaping: Bool { isActive || isPaneResizing }
    public bool IsReshaping => Computed(() => IsActive || IsPaneResizing);

    // :129-139 init(window: NSWindow). NSWindow.willStartLiveResizeNotification and didEndLiveResizeNotification for
    // this window only (`object: window`) are the messages of this window's own procedure. Windows brackets a size
    // drag and a move drag alike with WM_ENTERSIZEMOVE and WM_EXITSIZEMOVE; only a size drag sends WM_SIZING, so a
    // live resize starts at the first WM_SIZING inside the loop and ends at the loop's WM_EXITSIZEMOVE, and a move is
    // not one (PR-WIN-017 windows_path). The window keeps the hook for as long as it exists: like the observations
    // (:129-130), it is never taken down.
    public WindowLiveResize(AvaloniaWindow window)
    {
        this.window = window;
        Win32Properties.AddWndProcHookCallback(window, WndProc);
    }

    // :133-138 the two observations, on the window's own (main) thread: MainActor.assumeIsolated.
    internal nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_ENTERSIZEMOVE:
                inSizeMove = true;
                break;
            case WM_SIZING when inSizeMove && !resizing:
                resizing = true;
                MainActor.AssumeIsolated(() => IsActive = true); // :133-135
                break;
            case WM_EXITSIZEMOVE:
                inSizeMove = false;
                if (!resizing) break;
                resizing = false;
                MainActor.AssumeIsolated(() => IsActive = false); // :136-138
                break;
        }
        return 0; // an observer only: `handled` stays false and the window's own handling goes on
    }
}

/// <summary>:142-161 <c>private struct HostedRoot&lt;Content: View&gt;: View</c>: the shared environment for every
/// hosted window, plus the app-wide appearance choice.</summary>
internal sealed class HostedRoot : ThemeVariantScope
{
    /// <param name="model">:144 <c>let model: AppModel</c>.</param>
    /// <param name="liveResize">:145 <c>let liveResize: WindowLiveResize</c>.</param>
    /// <param name="content">:146 <c>let content: Content</c>.</param>
    // :148-160 body
    public HostedRoot(AppModel model, WindowLiveResize liveResize, Control content)
    {
        Child = new HostedEnvironment(model, liveResize).Body(content); // :149-150
        // :151 .preferredColorScheme(model.settings.theme.spec.scheme): this tree's colour scheme; nil states no
        // preference, so the tree takes the window's, which follows the process appearance below.
        this.Track(() => model.Settings.Theme.Spec().Scheme, scheme => RequestedThemeVariant = scheme ?? ThemeVariant.Default);
        // :152-159 .onChange(of: model.settings.theme, initial: true): once when the tree appears, then on every change.
        OnChange<AppTheme>? appearance = null;
        AttachedToVisualTree += (_, _) => appearance ??= new OnChange<AppTheme>(() => model.Settings.Theme, (_, theme) => SetAppearance(theme), initial: true);
        DetachedFromVisualTree += (_, _) =>
        {
            appearance?.Dispose();
            appearance = null;
        };
    }

    // :153-158 NSApp.appearance = switch theme.spec.scheme: the process-wide appearance that framework-drawn chrome
    // follows. Its Avalonia counterpart is Application.RequestedThemeVariant: every window follows it, and Avalonia sets
    // each window's immersive dark title bar (DWMWA_USE_IMMERSIVE_DARK_MODE) from the variant the window ends up with.
    private static void SetAppearance(AppTheme theme)
    {
        var application = SwiftTraps.Unwrap(Application.Current); // NSApp
        var scheme = theme.Spec().Scheme;
        application.RequestedThemeVariant =
            scheme is null ? ThemeVariant.Default // :154 case nil: nil, the system decides
            : scheme == ThemeVariant.Light ? ThemeVariant.Light // :155 NSAppearance(named: .aqua)
            : scheme == ThemeVariant.Dark ? ThemeVariant.Dark // :156 NSAppearance(named: .darkAqua)
            : ThemeVariant.Default; // :157 @unknown default: nil
    }
}

/// <summary>:163-179 <c>struct HostedEnvironment: ViewModifier</c>: the environment every hosted view tree starts
/// from: a window's root, and a chat column hosted on its own inside the main window (see <c>ThreadColumns</c>).</summary>
/// <param name="Model">:166 <c>let model: AppModel</c>.</param>
/// <param name="LiveResize">:167 <c>let liveResize: WindowLiveResize</c>.</param>
public sealed record HostedEnvironment(AppModel Model, WindowLiveResize LiveResize)
{
    // :169-178 func body(content: Content) -> some View
    public Control Body(Control content)
    {
        AppEnvironment.SetAppModel(content, Model); // :171 .environment(model)
        AppEnvironment.SetWindowLiveResize(content, LiveResize); // :172 .environment(liveResize)
        // :173-175 The zoom in Settings scales the whole app: every window root and every chat column carries it, so the
        // sidebar, settings and composer move together.
        var zoomed = new AppZoomModifier(Percent(Model.Settings.AppZoom)).Body(content);
        Popovers.SetIsCapsuleButtonBorderShape(zoomed, true); // :176 .buttonBorderShape(.capsule)
        var tinted = new ThemeTint(Model.Settings.Theme, Model.Settings.Accent).Body(zoomed); // :177
        // The body reads model.settings.appZoom (:175) and model.settings.theme (:177), so SwiftUI evaluates it again
        // when either changes; AppZoomModifier keeps its layout and ThemeTint its view when they are evaluated again.
        tinted.Track(() => Model.Settings.AppZoom, percent => new AppZoomModifier(Percent(percent)).Body(content));
        tinted.Track(() => (Model.Settings.Theme, Model.Settings.Accent), pair => new ThemeTint(pair.Theme, pair.Accent).Body(zoomed)); // and the accent (OD-0088)
        return tinted;
    }

    // model.settings.appZoom is a Swift Int (a C# long, R-NUM-4) and the port's AppZoomModifier takes an int. A value
    // beyond an int is no zoom level, and so is the int bound it is clamped to: both normalize to 100 (AppZoom.swift:8-10).
    private static int Percent(long appZoom) => (int)Math.Clamp(appZoom, int.MinValue, int.MaxValue);
}

/// <summary>:181-193 <c>private struct ThemeTint: ViewModifier</c>: tints prominent glass buttons, toggles and progress
/// views with the theme's accent. System/Light/Dark keep the system control accent.</summary>
/// <param name="Theme">:184 <c>let theme: AppTheme</c>.</param>
/// <param name="Accent">PORT-ONLY (OD-0088, ED-250): the user's accent (AppSettings.Accent).</param>
internal sealed record ThemeTint(AppTheme Theme, string Accent = AccentPalette.WindowsId)
{
    // :186-192 func body(content: Content) -> some View
    public Control Body(Control content)
    {
        // :187-191 One branch, never two: an `if` here made the whole window's content a different view once a theme
        // with an accent replaced one without, so every page was built afresh and Settings jumped back to its top on a
        // theme change. A nil tint keeps the system accent.
        // DEVIATION OD-0088 (ED-250): a picked accent tints the window; the default keeps theme.spec.accent, nil for
        // System, Light and Dark as in the source.
        Popovers.SetTint(content, AccentPalette.Pick(Accent) ?? Theme.Spec().Accent);
        return content;
    }
}

/// <summary>:195-222 <c>final class FrameHostingView: NSHostingView&lt;AnyView&gt;</c>: lets the frame it is given
/// define the hosting view's size instead of the content hugging it: a window's content view, or a chat column
/// filling the detail pane (ED-150).</summary>
public sealed class FrameHostingView : Decorator
{
    // :198-204 required init(rootView: AnyView)
    public FrameHostingView(Control rootView)
    {
        Child = rootView; // :199
        // :200 sizingOptions = []: see MeasureOverride. :201 safeAreaRegions = []: a Windows desktop window has no safe
        // area and the port never pads content for one, so there is nothing to clear.
        // :202-203 translatesAutoresizingMaskIntoConstraints = true, autoresizingMask = [.width, .height]: the view
        // follows its superview's size on both axes.
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        // :210-214 acceptsFirstResponder { true }: every window here holds focusable text (the composer, a search
        // field), so the answer is always yes, as a stored value rather than a search of the view tree.
        Focusable = true;
    }

    // :216-221 override func performKeyEquivalent(with event: NSEvent) -> Bool { !isHidden && super... }: a hidden chat
    // column keeps its buttons' shortcuts to itself, answering no before its subtree is asked. Avalonia 12.1.2 has no
    // per-view chord pass to override: a button's key gesture (Button.HotKey, ED-099 (6)) is a key binding on the
    // window itself (HotKeyManager), matched before any view sees the key and asking only whether the button
    // IsEffectivelyEnabled, whatever its visibility (KeyboardDevice.ProcessRawEvent, HotkeyCommandWrapper.CanExecute).
    // So the host is the gate for its whole subtree: while the host itself is hidden (its own flag, as isHidden) it is
    // not enabled, which makes every control under it not IsEffectivelyEnabled, and no chord reaches one, however it is
    // bound (a key binding on the window or in the subtree, Button.IsDefault or IsCancel). The subtree's own IsEnabled
    // values are untouched; a hidden host already draws nothing, takes no pointer and holds no focus.
    protected override bool IsEnabledCore => IsVisible && base.IsEnabledCore;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // Hidden or shown again (RootView.swift:224-235, B3 52bc792): the gate follows at once.
        if (change.Property == IsVisibleProperty) UpdateIsEffectivelyEnabled();
    }

    // :200 sizingOptions = []: the content never sizes the host. It is laid out in the space the host is offered, and
    // the host asks for none of its own, so a window, a minimum size or a column is never sized from the content
    // (ED-150's condition for the zoom inside it).
    protected override Avalonia.Size MeasureOverride(Avalonia.Size availableSize)
    {
        Child?.Measure(availableSize);
        return default;
    }

    // :105 hosting.frame = frame, :202-203: the content fills the host's frame.
    protected override Avalonia.Size ArrangeOverride(Avalonia.Size finalSize)
    {
        Child?.Arrange(new Rect(finalSize));
        return finalSize;
    }
}
