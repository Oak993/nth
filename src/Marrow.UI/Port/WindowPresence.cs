// PORT-ONLY: ED-NEW-lean-6. One Windows signal for "this window can be seen at all": the decorative motion, the working line's
// clock and the hang watchdog stand still while nothing of the app can be seen, and pick up at the current time when it can.
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Marrow.Core.Support;

namespace Marrow.UI.Port;

/// <summary>
/// PORT-ONLY (ED-NEW-lean-6). Whether a window of the app can be seen: shown, not minimised, not cloaked (DWMWA_CLOAKED, which
/// also covers a window on another virtual desktop), not fully covered by the windows above it, the session not locked
/// (WTSRegisterSessionNotification) and the display on (RegisterPowerSettingNotification, GUID_CONSOLE_DISPLAY_STATE; dimmed
/// counts as on). Event-driven, no polling: the process's one window-event hook set (<see cref="AmbientWindowState"/>; while
/// the window is fully covered it also hears the covering processes' windows move), the window's own state, pointer input
/// over it while unseen, and the session and power messages refresh it on the UI thread. Once any presence is asked for,
/// every window of the app is tracked, and the hang watchdog is quiet while none of them can be seen and none has stopped
/// answering.
/// </summary>
public sealed class WindowPresence
{
    private static readonly ConditionalWeakTable<Window, WindowPresence> Windows = new();
    private static readonly Guid DisplayState = new("6fe69556-704a-47a0-8f24-c28d936fda47"); // GUID_CONSOLE_DISPLAY_STATE
    internal static Func<Window, WindowPresence>? TestFactory;
    /// <summary>Whether a window's thread has stopped answering (IsHungAppWindow; tests fake it). Any thread.</summary>
    internal static Func<nint, bool> Hung { get; set; } = static handle => OperatingSystem.IsWindows() && IsHungAppWindow(handle);
    private static int tracked, seenCount; // open tracked windows, and how many of them can be seen
    private static nint[] handles = []; // the tracked windows' handles (copy on write; the watchdog's thread reads them)
    private static bool installed, locked, displayOff; // UI thread

    private readonly Window window;
    private readonly nint hwnd;
    private readonly AmbientWindowState.EventHub? events; // the hub this presence listens to (null: a test presence)
    private readonly Func<AmbientWindowState.WindowLook>? look; // the look at the window (null: none to take)
    private readonly Action refresh;
    private readonly Win32Properties.CustomWndProcHookCallback? messages;
    private nint power;
    private bool closed;
    private volatile bool seen = true;

    /// <summary>A window's presence; <paramref name="test"/>: a fake one whose <see cref="SetSeen"/> the test drives, with
    /// <paramref name="handle"/> (when not 0) the handle the watchdog's hang check asks about.</summary>
    internal WindowPresence(Window window, bool test = false, nint handle = 0)
        : this(window, test ? null : AmbientWindowState.Events, test ? handle : window.TryGetPlatformHandle()?.Handle ?? 0, null, !test) { }

    /// <summary>Tests: a presence wired as a live one (its window's state, pointer and the hub's flushes refresh it) that
    /// looks at its window through <paramref name="look"/>, under the fake handle <paramref name="handle"/>.</summary>
    internal WindowPresence(Window window, AmbientWindowState.EventHub events, nint handle, Func<AmbientWindowState.WindowLook> look)
        : this(window, events, handle, look, false) { }

    private WindowPresence(Window window, AmbientWindowState.EventHub? events, nint handle, Func<AmbientWindowState.WindowLook>? look, bool native)
    {
        this.window = window;
        hwnd = handle;
        refresh = Refresh;
        Interlocked.Increment(ref tracked); Interlocked.Increment(ref seenCount);
        if (handle != 0) Volatile.Write(ref handles, [.. Volatile.Read(ref handles), handle]);
        window.Closed += OnClosed;
        if (events is null) return;
        this.events = events;
        this.look = look ?? (handle != 0 ? () => events.Look(handle) : null);
        window.PropertyChanged += WindowChanged;
        window.AddHandler(InputElement.PointerMovedEvent, PointerMoved, RoutingStrategies.Tunnel, true);
        events.Subscribe(refresh);
        if (native && handle != 0 && OperatingSystem.IsWindows())
        {
            messages = OnMessage;
            NativeWindowHooks.Add(window, messages);
            try
            {
                WTSRegisterSessionNotification(handle, 0); // NOTIFY_FOR_THIS_SESSION
                var guid = DisplayState;
                power = RegisterPowerSettingNotification(handle, ref guid, 0); // DEVICE_NOTIFY_WINDOW_HANDLE; the current state comes at once
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

    /// <summary>
    /// Whether no tracked window of the app can be seen (any thread; the hang watchdog's quiet). Not quiet while a tracked
    /// window's thread has stopped answering (<see cref="Hung"/>, which asks Windows and never wakes main): a hang that begins
    /// while the app is unseen is still reported, though the stuck main thread cannot refresh the presence.
    /// </summary>
    internal static bool Quiet => IsQuiet(Volatile.Read(ref tracked), Volatile.Read(ref seenCount), Volatile.Read(ref handles));

    /// <summary><see cref="Quiet"/> for <paramref name="open"/> tracked windows, <paramref name="seenWindows"/> of them seen, with
    /// <paramref name="windows"/> their handles.</summary>
    internal static bool IsQuiet(int open, int seenWindows, nint[] windows)
    {
        if (open == 0 || seenWindows != 0) return false;
        foreach (var handle in windows) if (Hung(handle)) return false;
        return true;
    }

    /// <summary>The open tracked windows and how many of them can be seen (tests).</summary>
    internal static (int Open, int Seen) Counts => (Volatile.Read(ref tracked), Volatile.Read(ref seenCount));

    /// <summary>Whether the hang check asks about <paramref name="handle"/> (tests).</summary>
    internal static bool Watches(nint handle) => Array.IndexOf(Volatile.Read(ref handles), handle) >= 0;

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
        HangWatchdog.Quiet.Use(static () => Quiet);
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

    // Pointer input reaches the window only where it can be seen: an unseen window that hears it looks again (the cheap
    // recovery should a covering window's change have gone unheard).
    private void PointerMoved(object? sender, PointerEventArgs args)
    {
        if (!seen) Refresh();
    }

    // Runs once per batch of window events (the hub's one flush, whose look at the window the ambient state shares), when
    // the window's own state changes, and on the session and display messages.
    private void Refresh()
    {
        if (closed || look is null) return;
        var shown = window.IsVisible && window.WindowState != WindowState.Minimized && !locked && !displayOff;
        var at = shown ? look() : default;
        // ED-NEW-lean-6 (cloak rule): only the shell's cloak (another virtual desktop) hides a window, as FrameClock's HiddenBy; a
        // window the app cloaks itself (warm-up, hand-over) is being composed to be shown and is shown by an uncloak, so it is seen
        SetSeen(shown && !(at.Cloaked && !at.AppCloakOnly) && !at.Covered);
        // fully covered: the covering windows' processes are heard moving, so their maximise, restore or snap is noticed
        events!.Cover(hwnd, shown && at.Covered ? at.Coverers : []);
    }

    /// <summary>The window's messages (its window procedure hook; tests send the session and power messages).</summary>
    internal nint OnMessage(nint handle, uint message, nint wParam, nint lParam, ref bool handled)
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
        if (hwnd != 0) Volatile.Write(ref handles, [.. Volatile.Read(ref handles).Where(handle => handle != hwnd)]);
        if (events is null) return;
        window.PropertyChanged -= WindowChanged;
        window.RemoveHandler(InputElement.PointerMovedEvent, PointerMoved);
        events.Cover(hwnd, []);
        events.Unsubscribe(refresh);
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
    [DllImport("user32.dll")] private static extern bool IsHungAppWindow(nint handle);
}
