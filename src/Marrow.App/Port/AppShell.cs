// PORT-ONLY: the Windows application shell that plays NSApplication's part for DroppyCode/App/AppDelegate.swift (B3
// 52bc792): it calls the delegate where AppKit does and turns the Windows ways to quit into AppKit's terminate handshake.
//   finishLaunching -> applicationDidFinishLaunching(_:) (:9-34), once, before the run loop (the desktop lifetime's Startup).
//   terminate(_:) -> applicationShouldTerminate(_:) (:44-54); .terminateNow, or a later reply(toApplicationShouldTerminate:
//     true) (:51), ends the app through the lifetime's shutdown, whose Exit runs applicationWillTerminate(_:) (:56-61).
//     OD-0008 C1, C2 (PR-LAUNCH-074): the main window's close control and Alt+F4 are the only ways to quit, so they are
//     terminate(_:). ED-004 (2): a session end (WM_QUERYENDSESSION, the lifetime's ShutdownRequested) asks the same
//     question; while the answer is later the request is refused and the 250 ms polling goes on; WM_ENDSESSION runs the
//     three termination steps at once, whatever is left of the four minutes.
//   A reopen (:36-39) runs when a second launch reaches this instance (ED-002, ReopenSignal.cs).
// ED-156: the settings store is synchronized after the termination steps (the afterWillTerminate step Program.cs passes).
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Marrow.App.Window;
using Marrow.Core.Port;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Port;

/// <summary>PORT-ONLY. The NSApplication that drives <see cref="AppDelegate"/> on Windows.</summary>
public sealed class AppShell : ITerminationSender
{
    // winuser.h
    private const uint WM_ENDSESSION = 0x0016;
    private const uint WM_ACTIVATEAPP = 0x001C;
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint WM_DPICHANGED = 0x02E0;

    /// <summary>PORT-ONLY seam: what puts a window procedure hook on the main window, Avalonia's own (a headless window has no
    /// Win32 window procedure, so a test records the hook and calls it as Windows would, to prove it is installed).</summary>
    internal static Ambient<Action<TopLevel, Win32Properties.CustomWndProcHookCallback>> AddWindowProcedureHook { get; } =
        new((window, hook) => NativeWindowHooks.Add(window, hook));

    private readonly Action exit;
    private readonly Action afterWillTerminate;
    private bool terminationPending;
    private bool terminated;
    private AvaloniaWindow? hookedWindow; // the main window the hooks are on (AS-10: a window closed to the background is made again)
    internal bool HasTerminated => terminated;

    /// <param name="appDelegate">The application's delegate (DroppyCodeApp.swift:5, @NSApplicationDelegateAdaptor).</param>
    /// <param name="exit">Ends the app: in the app, the desktop lifetime's Shutdown, which closes every window and raises
    /// Exit, whose handler is <see cref="WillTerminate"/>.</param>
    /// <param name="afterWillTerminate">Runs once, after applicationWillTerminate(_:): the settings store's Synchronize
    /// (ED-156).</param>
    public AppShell(AppDelegate appDelegate, Action exit, Action afterWillTerminate)
    {
        Delegate = appDelegate;
        this.exit = exit;
        this.afterWillTerminate = afterWillTerminate;
    }

    /// <summary>NSApp.delegate.</summary>
    public AppDelegate Delegate { get; }

    /// <summary>
    /// PORT-ONLY (AS-10, ED-NEW-dextra-as10-13): a hidden start (<c>nth --hidden</c>, <c>nth --duty id</c>) makes no main window and no boot
    /// stage; it only leaves the quit path (the updater's and the Quit command's) as in a normal start.
    /// </summary>
    internal void PrepareHidden()
    {
        WindowManager.Shared.MainWindowClose = () => MainActor.Post(Terminate);
        WindowManager.Shared.MainWindowMade = _ => AttachMainWindowHooks();
    }

    internal void PrepareBoot()
    {
        WindowManager.Shared.MainWindowClose = () => MainActor.Post(Terminate);
        WindowManager.Shared.MainWindowMade = _ => AttachMainWindowHooks();
        Delegate.PrepareBoot();
        AttachMainWindowHooks();
    }

    /// <summary>
    /// NSApplication.finishLaunching: from now on the main window's own close is terminate(_:) (OD-0008 C1, C2), then the
    /// delegate's applicationDidFinishLaunching(_:) (:9-34), then the session-end hook on the main window it showed.
    /// </summary>
    public void FinishLaunching()
    {
        var windows = WindowManager.Shared;
        // Posted, so terminate(_:) runs once the window's own close handling (WM_CLOSE) has returned and the lifetime's
        // shutdown never destroys the window from inside it (Window.HandleClosing, Avalonia 12.1.2).
        windows.MainWindowClose = () => MainActor.Post(Terminate);
        Delegate.ApplicationDidFinishLaunching();
        AttachMainWindowHooks();
    }

    private void AttachMainWindowHooks()
    {
        // ED-004 (2): WM_ENDSESSION comes to every top-level window; the main window is the app's one lasting window.
        // AS-10: a window closed to the background is made again at the next show, and the hooks go on the new one.
        if (WindowManager.Shared.MainWindow is { } main && !ReferenceEquals(hookedWindow, main))
        {
            hookedWindow = main;
            AddWindowProcedureHook.Current(main, WndProc);
            // ED-219: the badge computed while the delegate launched had no window to sit on yet.
            (TaskbarBadge.Overlay.Current as WindowsTaskbarOverlay)?.Reapply();
            // AppDelegate.swift:28 installs UpdateChecker.swift:141-149's didBecomeActive observer.
            // WM_ACTIVATEAPP, handled below, is the app-level boundary; Window.Activated also fires
            // when focus moves between two windows of this same app.
        }
    }

    /// <summary>
    /// NSApplication.terminate(_:): the main window's close control and Alt+F4 (OD-0008 C1, C2). Asks the delegate
    /// (:44-54); .terminateNow ends the app at once, .terminateLater waits for <see cref="Reply"/>. A close asked while
    /// the quit already waits asks nothing more.
    /// </summary>
    public void Terminate()
    {
        if (terminationPending || terminated) return;
        switch (Delegate.ApplicationShouldTerminate(this))
        {
            case NSApplication.TerminateReply.TerminateNow:
                exit();
                break;
            case NSApplication.TerminateReply.TerminateLater:
                terminationPending = true;
                break;
        }
    }

    /// <summary>
    /// The desktop lifetime's ShutdownRequested: a session end (WM_QUERYENDSESSION, ED-004 (2)). The same question as
    /// <see cref="Terminate"/>: with .terminateNow the request goes on and the lifetime closes the app itself (its Exit
    /// runs <see cref="WillTerminate"/>); with .terminateLater, or while an earlier quit still waits, it is refused and
    /// the waiting goes on.
    /// </summary>
    public void ShutdownRequested(ShutdownRequestedEventArgs request)
    {
        if (terminated) return;
        if (terminationPending)
        {
            request.Cancel = true;
            return;
        }
        switch (Delegate.ApplicationShouldTerminate(this))
        {
            case NSApplication.TerminateReply.TerminateNow:
                break;
            case NSApplication.TerminateReply.TerminateLater:
                terminationPending = true;
                request.Cancel = true;
                break;
        }
    }

    /// <summary><c>reply(toApplicationShouldTerminate:)</c> (:51): true ends the app.</summary>
    public void Reply(bool toApplicationShouldTerminate)
    {
        if (!terminationPending) return;
        terminationPending = false;
        if (toApplicationShouldTerminate) exit();
    }

    /// <summary>
    /// NSApplicationWillTerminateNotification: the delegate's applicationWillTerminate(_:) (:56-61), then the settings
    /// flush (ED-156). Once: the lifetime's Exit and a session end (WM_ENDSESSION) can both reach it.
    /// </summary>
    public void WillTerminate()
    {
        if (terminated) return;
        terminated = true;
        Delegate.ApplicationWillTerminate();
        afterWillTerminate();
    }

    /// <summary>
    /// A second launch reached this instance (ED-002): AppKit's reopen. The delegate is asked with hasVisibleWindows as
    /// AppKit computes it (:36-39); when it answers true the app is activated, as AppKit's standard reopen does: the
    /// main window comes to the front, or another visible window when the main window is not up. With no visible window
    /// the delegate's showMain() has already activated it (WindowManager.swift:57-60).
    /// </summary>
    public void Reopen(IReadOnlyList<AvaloniaWindow> windows)
    {
        var hasVisibleWindows = HasVisibleWindows(windows);
        if (!Delegate.ApplicationShouldHandleReopen(hasVisibleWindows) || !hasVisibleWindows) return;
        var front = WindowManager.Shared.MainWindow is { } main && IsVisibleWindow(main)
            ? main
            : windows.FirstOrDefault(IsVisibleWindow);
        front?.Activate();
    }

    /// <summary>AppKit's hasVisibleWindows: whether any of the app's windows is on screen. NSWindow.isVisible is false
    /// for a miniaturized window, so a minimised window is not a visible one.</summary>
    public static bool HasVisibleWindows(IEnumerable<AvaloniaWindow> windows) => windows.Any(IsVisibleWindow);

    private static bool IsVisibleWindow(AvaloniaWindow window) =>
        window.IsVisible && window.WindowState != WindowState.Minimized;

    /// <summary>The main window's procedure hook: WM_ACTIVATEAPP with wParam TRUE reaches the application activation
    /// observer (UpdateChecker.swift:141-149). ED-004 (2): WM_ENDSESSION with wParam TRUE runs the termination steps
    /// immediately. These are observers only: the window's own handling goes on.</summary>
    internal nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_ACTIVATEAPP && wParam != 0)
        {
            UpdateChecker.Shared.HandleActivation();
            Delegate.Model?.ResumePaletteChecks(); // OD-0064 / AC-3: same captured transport and debounced automatic path.
            // HydraButton.swift:475 the charge views observe the same didBecomeActiveNotification (PR-HYDRA-241).
            Marrow.App.Views.Chat.HydraChargeLayerView.PostApplicationDidBecomeActive();
        }
        if (msg == WM_ENDSESSION && wParam != 0) WillTerminate();
        // ED-084: WM_SETTINGCHANGE (broadcast to every top-level window) re-reads Windows' "Animation effects" setting.
        if (msg == WM_SETTINGCHANGE) SystemReduceMotion.Shared.Current.Refresh();
        // ED-219: Explorer made the taskbar button (again), or the DPI, and so the small-icon size, changed: the unread
        // badge's overlay icon is set again.
        if (msg == WM_DPICHANGED || msg == WindowsTaskbarOverlay.TaskbarButtonCreated)
            (TaskbarBadge.Overlay.Current as WindowsTaskbarOverlay)?.Reapply();
        return 0;
    }
}
