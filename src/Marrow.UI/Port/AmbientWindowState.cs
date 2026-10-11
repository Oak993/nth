using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Marrow.UI.Port;

// One event-driven visibility/input source per native window. No idle polling timer.
internal sealed class AmbientWindowState : IDisposable
{
    private static readonly ConditionalWeakTable<Window, AmbientWindowState> Windows = new();
    internal static Func<Window, AmbientWindowState>? TestFactory;
    /// <summary>Installs a WinEvent hook for one range of events (tests record the ranges).</summary>
    internal static Func<uint, uint, AmbientWinEventCallback, nint> SetHook { get; set; } = static (first, last, callback) => SetWinEventHook(first, last, 0, callback, 0, 0, 0);
    /// <summary>Removes a hook <see cref="SetHook"/> installed (tests: a recorder's handles are 0 and never removed).</summary>
    internal static Action<nint> Unhook { get; set; } = static handle => UnhookWinEvent(handle);
    private readonly Window window;
    private readonly nint hwnd;
    private readonly Action onEvent, refresh;
    private bool subscribed;
    private int users, available, queued;
    private double lastInput = Now;
    internal event Action? Changed;
    internal bool Available => Volatile.Read(ref available) != 0;
    internal double LastInput => Volatile.Read(ref lastInput);
    internal static double Now => TimeProvider.System.GetTimestamp() / (double)TimeProvider.System.TimestampFrequency;

    internal AmbientWindowState(Window window, bool test = false)
    {
        this.window = window;
        hwnd = test ? 0 : window.TryGetPlatformHandle()?.Handle ?? 0;
        onEvent = OnWindowEvent; refresh = Refresh;
        if (test) { available = 1; return; }
        window.PropertyChanged += WindowChanged;
        window.AddHandler(InputElement.PointerMovedEvent, PointerInput, RoutingStrategies.Tunnel, true);
        window.AddHandler(InputElement.PointerPressedEvent, PointerInput, RoutingStrategies.Tunnel, true);
        window.AddHandler(InputElement.PointerWheelChangedEvent, PointerInput, RoutingStrategies.Tunnel, true);
        window.AddHandler(InputElement.KeyDownEvent, KeyInput, RoutingStrategies.Tunnel, true);
        Events.Subscribe(onEvent); subscribed = true;
        Refresh();
    }

    internal static AmbientWindowState? Acquire(Control view)
    {
        if (WindowTransients.WindowOf(view) is not Window window) return null;
        if (TestFactory is null && (!OperatingSystem.IsWindows() || window.TryGetPlatformHandle()?.HandleDescriptor != "HWND")) return null;
        var state = Windows.GetValue(window, w => TestFactory?.Invoke(w) ?? new(w));
        state.users++; return state;
    }

    internal void Touch(double now) { Volatile.Write(ref lastInput, now); Changed?.Invoke(); }
    internal void SetAvailable(bool value)
    {
        if (Interlocked.Exchange(ref available, value ? 1 : 0) == (value ? 1 : 0)) return;
        Changed?.Invoke();
    }

    private void PointerInput(object? sender, PointerEventArgs args) => Touch(Now);
    private void KeyInput(object? sender, KeyEventArgs args) => Touch(Now);
    private void WindowChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == Window.IsActiveProperty || args.Property == Window.WindowStateProperty || args.Property == Visual.IsVisibleProperty) Refresh();
    }
    // A batch of window events: one refresh (one walk of the windows above) however many came before it runs.
    private void OnWindowEvent()
    {
        if (Interlocked.Exchange(ref queued, 1) == 0) Dispatcher.UIThread.Post(refresh, DispatcherPriority.Background);
    }
    private void Refresh()
    {
        Volatile.Write(ref queued, 0);
        if (hwnd == 0) return;
        SetAvailable(window.IsVisible && window.IsActive && window.WindowState != WindowState.Minimized
            && GetForegroundWindow() == hwnd && !Cloaked(hwnd) && !FullyCovered(hwnd));
    }
    internal static bool Cloaked(nint handle) => DwmGetWindowAttribute(handle, 14, out int value, sizeof(int)) == 0 && value != 0;
    internal static bool FullyCovered(nint handle)
    {
        if (!GetWindowRect(handle, out var bounds)) return false;
        var remaining = new List<Rect> { bounds.Rect };
        for (var above = GetWindow(handle, 3); above != 0 && remaining.Count != 0; above = GetWindow(above, 3))
        {
            if (!IsWindowVisible(above) || Cloaked(above) || (GetWindowLong(above, -20) & (0x20 | 0x80000)) != 0 || !GetWindowRect(above, out var rect)) continue;
            var cover = rect.Rect;
            for (var i = remaining.Count - 1; i >= 0; i--)
            {
                var r = remaining[i]; var overlap = r.Intersect(cover);
                if (overlap.Width <= 0 || overlap.Height <= 0) continue;
                remaining.RemoveAt(i);
                if (r.Left < overlap.Left) remaining.Add(new(r.Left, r.Top, overlap.Left - r.Left, r.Height));
                if (overlap.Right < r.Right) remaining.Add(new(overlap.Right, r.Top, r.Right - overlap.Right, r.Height));
                if (r.Top < overlap.Top) remaining.Add(new(overlap.Left, r.Top, overlap.Width, overlap.Top - r.Top));
                if (overlap.Bottom < r.Bottom) remaining.Add(new(overlap.Left, overlap.Bottom, overlap.Width, r.Bottom - overlap.Bottom));
            }
        }
        return remaining.Count == 0;
    }
    public void Dispose()
    {
        if (--users != 0) return;
        Windows.Remove(window);
        window.PropertyChanged -= WindowChanged;
        window.RemoveHandler(InputElement.PointerMovedEvent, PointerInput);
        window.RemoveHandler(InputElement.PointerPressedEvent, PointerInput);
        window.RemoveHandler(InputElement.PointerWheelChangedEvent, PointerInput);
        window.RemoveHandler(InputElement.KeyDownEvent, KeyInput);
        if (subscribed) { Events.Unsubscribe(onEvent); subscribed = false; }
    }

    /// <summary>
    /// ED-NEW-lean-6: the process's one set of window-event hooks, shared by every window's ambient state and presence. It
    /// hears only what can change whether a window is in front or can be seen: the foreground (3), the end of a move or
    /// resize (0x0B), minimise start and end (0x16-0x17), show, hide and reorder (0x8002-0x8004) and cloak and uncloak
    /// (0x8017-0x8018); never a location change (0x800B, every caret and cursor move on the desktop). Only events of a window
    /// itself (OBJID_WINDOW, CHILDID_SELF) reach the subscribers. UI thread (the hooks call back on its message loop).
    /// </summary>
    internal static class Events
    {
        internal static readonly (uint First, uint Last)[] Ranges = [(0x0003, 0x0003), (0x000B, 0x000B), (0x0016, 0x0017), (0x8002, 0x8004), (0x8017, 0x8018)];
        private static readonly AmbientWinEventCallback Callback = OnEvent; // rooted while hooked
        private static Action[] subscribers = [];
        private static nint[]? hooks;

        /// <summary>Whether the hooks are installed (tests).</summary>
        internal static bool Hooked => hooks is not null;

        internal static void Subscribe(Action handler)
        {
            subscribers = [.. subscribers, handler];
            if (hooks is not null) return;
            hooks = new nint[Ranges.Length];
            for (var i = 0; i < Ranges.Length; i++) hooks[i] = SetHook(Ranges[i].First, Ranges[i].Last, Callback);
        }

        internal static void Unsubscribe(Action handler)
        {
            var index = Array.IndexOf(subscribers, handler);
            if (index < 0) return;
            subscribers = [.. subscribers[..index], .. subscribers[(index + 1)..]];
            if (subscribers.Length != 0 || hooks is null) return;
            foreach (var hook in hooks) if (hook != 0) Unhook(hook);
            hooks = null;
        }

        private static void OnEvent(nint hook, uint kind, nint handle, int objectId, int childId, uint thread, uint time)
        {
            if (objectId != 0 || childId != 0) return; // OBJID_WINDOW, CHILDID_SELF
            foreach (var subscriber in subscribers) subscriber();
        }
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetWindow(nint handle, uint command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint handle, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint handle, out AmbientWindowRect rect);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint handle, int attribute, out int value, int size);
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint first, uint last, nint module, AmbientWinEventCallback callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
}
