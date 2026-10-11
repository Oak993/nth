// PORT-ONLY: ED-NEW-lean-6. One Windows signal for "this window can be seen at all": the decorative motion, the working line's
// clock and the hang watchdog stand still while nothing of the app can be seen, and pick up at the current time when it can.
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Marrow.Core.Support;

namespace Marrow.UI.Port;

/// <summary>
/// PORT-ONLY (ED-NEW-lean-6). Whether a window of the app can be seen: shown, not minimised, not cloaked (DWMWA_CLOAKED, which
/// also covers a window on another virtual desktop), not fully covered by the windows above it, the session not locked
/// (WTSRegisterSessionNotification) and the display on (RegisterPowerSettingNotification, GUID_CONSOLE_DISPLAY_STATE; dimmed
/// counts as on). Event-driven, no polling: the process's one window-event hook set (<see cref="AmbientWindowState"/>), the
/// window's own state and the session and power messages refresh it on the UI thread. Once any presence is asked for, every
/// window of the app is tracked, and the hang watchdog is quiet while none of them can be seen.
/// </summary>
public sealed class WindowPresence
{
    private static readonly ConditionalWeakTable<Window, WindowPresence> Windows = new();
    private static readonly Guid DisplayState = new("6fe69556-704a-47a0-8f24-c28d936fda47"); // GUID_CONSOLE_DISPLAY_STATE
    internal static Func<Window, WindowPresence>? TestFactory;
    private static int tracked, seenCount; // open tracked windows, and how many of them can be seen
    private static bool installed, locked, displayOff; // UI thread

    private readonly Window window;
    private readonly nint hwnd;
    private readonly Action onEvent, refresh;
    private readonly Win32Properties.CustomWndProcHookCallback? messages;
    private nint power;
    private int queued;
    private readonly bool live;
    private bool closed;
    private volatile bool seen = true;

    internal WindowPresence(Window window, bool test = false)
    {
        this.window = window;
        onEvent = OnWindowEvent; refresh = Refresh;
        Interlocked.Increment(ref tracked); Interlocked.Increment(ref seenCount);
        window.Closed += OnClosed;
        if (test) return;
        live = true;
        hwnd = window.TryGetPlatformHandle()?.Handle ?? 0;
        window.PropertyChanged += WindowChanged;
        AmbientWindowState.Events.Subscribe(onEvent);
        if (hwnd != 0 && OperatingSystem.IsWindows())
        {
            messages = OnMessage;
            NativeWindowHooks.Add(window, messages);
            try
            {
                WTSRegisterSessionNotification(hwnd, 0); // NOTIFY_FOR_THIS_SESSION
                var guid = DisplayState;
                power = RegisterPowerSettingNotification(hwnd, ref guid, 0); // DEVICE_NOTIFY_WINDOW_HANDLE; the current state comes at once
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
        Refresh();
    }

    /// <summary>Raised on the UI thread when <see cref="Seen"/> changes.</summary>
    public event Action? Changed;

    /// <summary>Whether the window can be seen now (any thread).</summary>
    public bool Seen => seen;

    /// <summary>Whether no tracked window of the app can be seen (any thread; the hang watchdog's quiet).</summary>
    internal static bool Quiet => Volatile.Read(ref tracked) > 0 && Volatile.Read(ref seenCount) == 0;

    /// <summary>
    /// The presence of the window <paramref name="view"/> is in (a popover's is its owner window's), or null when it has
    /// none (not in a window, or not a Win32 window: it then counts as always seen). UI thread.
    /// </summary>
    public static WindowPresence? For(Visual view) => WindowTransients.WindowOf(view) is Window window ? Track(window) : null;

    private static WindowPresence? Track(Window window)
    {
        if (Windows.TryGetValue(window, out var known)) return known;
        if (TestFactory is null && (!OperatingSystem.IsWindows() || window.TryGetPlatformHandle()?.HandleDescriptor != "HWND")) return null;
        Install();
        if (Windows.TryGetValue(window, out known)) return known; // Install tracked it already
        var presence = TestFactory?.Invoke(window) ?? new WindowPresence(window);
        Windows.AddOrUpdate(window, presence);
        return presence;
    }

    // Once per process: every window of the app is tracked from now on, and the hang watchdog asks this signal.
    private static void Install()
    {
        if (installed) return;
        installed = true;
        HangWatchdog.Quiet = static () => Quiet;
        Window.WindowOpenedEvent.AddClassHandler<Window>(static (window, _) => Track(window));
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            foreach (var open in desktop.Windows.ToArray()) Track(open);
    }

    /// <summary>Sets whether the window can be seen (the refresh, and tests' fake presence).</summary>
    internal void SetSeen(bool value)
    {
        if (closed || seen == value) return;
        seen = value;
        if (value) Interlocked.Increment(ref seenCount); else Interlocked.Decrement(ref seenCount);
        Changed?.Invoke();
    }

    private void WindowChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == Window.WindowStateProperty || args.Property == Visual.IsVisibleProperty) Refresh();
    }

    // A batch of window events: one refresh (one walk of the windows above) however many came before it runs.
    private void OnWindowEvent()
    {
        if (Interlocked.Exchange(ref queued, 1) == 0) Dispatcher.UIThread.Post(refresh, DispatcherPriority.Background);
    }

    private void Refresh()
    {
        Volatile.Write(ref queued, 0);
        if (closed || hwnd == 0) return;
        SetSeen(window.IsVisible && window.WindowState != WindowState.Minimized && !locked && !displayOff
            && !AmbientWindowState.Cloaked(hwnd) && !AmbientWindowState.FullyCovered(hwnd));
    }

    private nint OnMessage(nint handle, uint message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x02B1) // WM_WTSSESSION_CHANGE
        {
            if (wParam == 7) SetSession(true, displayOff); // WTS_SESSION_LOCK
            else if (wParam == 8) SetSession(false, displayOff); // WTS_SESSION_UNLOCK
        }
        else if (message == 0x0218 && wParam == 0x8013 && lParam != 0 // WM_POWERBROADCAST, PBT_POWERSETTINGCHANGE
            && Marshal.PtrToStructure<Guid>(lParam) == DisplayState && Marshal.ReadInt32(lParam, 16) >= 4)
            SetSession(locked, Marshal.ReadInt32(lParam, 20) == 0); // POWERBROADCAST_SETTING.Data: 0 off, 1 on, 2 dimmed
        return 0; // observed only: never handled
    }

    private static void SetSession(bool nowLocked, bool nowOff)
    {
        if (nowLocked == locked && nowOff == displayOff) return;
        locked = nowLocked; displayOff = nowOff;
        foreach (var (_, presence) in Windows.ToArray()) presence.Refresh();
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        if (closed) return;
        if (seen) Interlocked.Decrement(ref seenCount);
        closed = true;
        Interlocked.Decrement(ref tracked);
        window.Closed -= OnClosed;
        Windows.Remove(window);
        if (!live) return;
        window.PropertyChanged -= WindowChanged;
        AmbientWindowState.Events.Unsubscribe(onEvent);
        if (messages is not null)
        {
            NativeWindowHooks.Remove(window, messages);
            try
            {
                WTSUnRegisterSessionNotification(hwnd);
                if (power != 0) { UnregisterPowerSettingNotification(power); power = 0; }
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
    }

    [DllImport("wtsapi32.dll")] private static extern bool WTSRegisterSessionNotification(nint hwnd, uint flags);
    [DllImport("wtsapi32.dll")] private static extern bool WTSUnRegisterSessionNotification(nint hwnd);
    [DllImport("user32.dll")] private static extern nint RegisterPowerSettingNotification(nint recipient, ref Guid setting, uint flags);
    [DllImport("user32.dll")] private static extern bool UnregisterPowerSettingNotification(nint handle);
}
