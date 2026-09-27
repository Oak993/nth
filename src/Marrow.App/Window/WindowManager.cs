using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Styling;
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
// out (closes STOP App.Window.WindowManager #2). Not written here (STOP #3 in the tests): the ED-063 caption WndProc
// hook that sits beside WindowChrome.configure (:101). UI-SETTINGS-001 (the Settings window, :21, :80-84, :92-99) is this file's as far as building, placing,
// opening and keeping the window go; its SettingsView.swift half and its openers are STOP #4, and the Acrylic hint
// OD-0014 names for the clear window (WindowChrome.swift:116-117, reached from :101) is STOP #5. The three
// window.center() calls (:43, :68, :82) are not written: AppKit's placement is unsettled (STOP #6).

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
    // settable only so the tests can host a stand-in while RootView's and SettingsView's bodies are still MARROW_STUB
    // (STOP App.Window.WindowManager #1); nothing in the app sets them.
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
        Present(window); // :37
    }

    /// <summary>:40-55 Opens a window centered over the main window, kept on the screen the main window is on.</summary>
    private void CenterOverMainWindow(AvaloniaWindow window)
    {
        // :42-45
        if (mainWindow is not { } main || !main.IsVisible || main.WindowState == WindowState.Minimized)
        {
            // :43 window.center(): not written. Where AppKit's center() puts a window is not in the source and no
            // decision or oracle item settles it (STOP App.Window.WindowManager #6).
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
        var parentSize = FrameSizeOf(main);
        var parent = new Rect(main.Position.X / scale, up - (main.Position.Y / scale + parentSize.Height), parentSize.Width, parentSize.Height); // :46
        var frame = FrameSizeOf(window); // :47
        var x = SwiftNumber.Rounded(parent.Center.X - frame.Width / 2); // :48
        var y = SwiftNumber.Rounded(parent.Center.Y - frame.Height / 2); // :49, the bottom edge, measured upward
        // :50 (main.screen ?? NSScreen.main)?.visibleFrame: the working area (the taskbar and app bars left out) of the
        // screen the main window is on, else the primary screen; with no screen at all the clamp is skipped.
        if ((main.Screens.ScreenFromWindow(main) ?? main.Screens.Primary) is { } screen)
        {
            var working = screen.WorkingArea.ToRect(scale);
            // visibleFrame in AppKit's y: minY is the working area's bottom edge, maxY its top edge.
            var visibleMinY = up - working.Bottom;
            var visibleMaxY = up - working.Top;
            x = SwiftMin(SwiftMax(x, working.Left), working.Right - frame.Width); // :51
            y = SwiftMin(SwiftMax(y, visibleMinY), visibleMaxY - frame.Height); // :52
        }
        // :54 window.setFrame(frame, display: false): only the origin moves (the size is the window's own). Back to
        // Windows' downward y (the top edge is `up` less the bottom edge and the height), and, a position being whole
        // pixels, to pixels at the main window's scale.
        var top = up - (y + frame.Height);
        window.Position = new PixelPoint(checked((int)SwiftNumber.Rounded(x * scale)), checked((int)SwiftNumber.Rounded(top * scale)));
    }

    // :57-60 private func present(_ window: NSWindow)
    private void Present(AvaloniaWindow window)
    {
        // :58 NSApp.activate() then :59 makeKeyAndOrderFront(nil). Windows has no activation of an application apart
        // from one of its windows (PR-WIN-013 windows_path): the window is shown (ordered front; a hidden window comes
        // back) and then activated, which asks Windows for the foreground (SetForegroundWindow); when Windows refuses
        // it, the window's taskbar button flashes instead.
        window.Show();
        window.Activate();
    }

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
        window.MinHeight = Size.MainMinimum.Height;
        // :67-69 if !window.setFrameUsingName(Self.mainFrameName) { window.center() }: the saved frame is applied when
        // there is one. The fallback, :68 window.center(), is not written (STOP App.Window.WindowManager #6).
        _ = SetFrameUsingName(window, MainFrameName);
        SetFrameAutosaveName(window, MainFrameName); // :70
        return window; // :71
    }

    // :80-84 private func makeSettingsWindow() -> NSWindow
    private AvaloniaWindow MakeSettingsWindow()
    {
        var window = MakeWindow("Settings", Size.Settings, resizable: false, SettingsContent, windowClass: () => new SecondaryWindow()); // :81
        // :82 window.center(): not written (STOP App.Window.WindowManager #6).
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

        var liveResize = new WindowLiveResize(window); // :103
        var hosting = new FrameHostingView(new HostedRoot(Model, liveResize, content())); // :104
        // :105 hosting.frame = frame, :106 NSView(frame: frame): the plain container fills the client area and lays
        // its one child out over the whole of it.
        var container = new Panel();
        container.Children.Add(hosting); // :107
        window.Content = container; // :108
        return window; // :109
    }

    // NSWindow.frame's size, in points: the content size plus the decorations around it, which the platform reports
    // once the window has been shown (Window.FrameSize). Avalonia measures a window the same way for its own centring.
    private static Avalonia.Size FrameSizeOf(AvaloniaWindow window) =>
        new Avalonia.Size(window.Width, window.Height) + (window.FrameSize is { } frame ? frame - window.ClientSize : default);

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

    // :120 private(set) var isActive = false
    public bool IsActive
    {
        get => Read(isActive);
        private set => Set(ref isActive, value);
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
    public WindowLiveResize(AvaloniaWindow window) => Win32Properties.AddWndProcHookCallback(window, WndProc);

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
        var tinted = new ThemeTint(Model.Settings.Theme).Body(zoomed); // :177
        // The body reads model.settings.appZoom (:175) and model.settings.theme (:177), so SwiftUI evaluates it again
        // when either changes; AppZoomModifier keeps its layout and ThemeTint its view when they are evaluated again.
        tinted.Track(() => Model.Settings.AppZoom, percent => new AppZoomModifier(Percent(percent)).Body(content));
        tinted.Track(() => Model.Settings.Theme, theme => new ThemeTint(theme).Body(zoomed));
        return tinted;
    }

    // model.settings.appZoom is a Swift Int (a C# long, R-NUM-4) and the port's AppZoomModifier takes an int. A value
    // beyond an int is no zoom level, and so is the int bound it is clamped to: both normalize to 100 (AppZoom.swift:8-10).
    private static int Percent(long appZoom) => (int)Math.Clamp(appZoom, int.MinValue, int.MaxValue);
}

/// <summary>:181-193 <c>private struct ThemeTint: ViewModifier</c>: tints prominent glass buttons, toggles and progress
/// views with the theme's accent. System/Light/Dark keep the system control accent.</summary>
/// <param name="Theme">:184 <c>let theme: AppTheme</c>.</param>
internal sealed record ThemeTint(AppTheme Theme)
{
    // :186-192 func body(content: Content) -> some View
    public Control Body(Control content)
    {
        // :187-191 One branch, never two: an `if` here made the whole window's content a different view once a theme
        // with an accent replaced one without, so every page was built afresh and Settings jumped back to its top on a
        // theme change. A nil tint keeps the system accent.
        Popovers.SetTint(content, Theme.Spec().Accent);
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
