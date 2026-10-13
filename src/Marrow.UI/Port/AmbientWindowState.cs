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
    private readonly Window window;
    private readonly nint hwnd;
    private readonly AmbientWinEventCallback callback;
    private readonly Action refresh;
    private nint hook;
    private int users, available, seen, queued;
    private double lastInput = Now;
    internal event Action? Changed;
    internal bool Available => Volatile.Read(ref available) != 0;
    internal bool Seen => Volatile.Read(ref seen) != 0;
    internal double LastInput => Volatile.Read(ref lastInput);
    internal static double Now => TimeProvider.System.GetTimestamp() / (double)TimeProvider.System.TimestampFrequency;

    internal AmbientWindowState(Window window, bool test = false)
    {
        this.window = window;
        hwnd = test ? 0 : window.TryGetPlatformHandle()?.Handle ?? 0;
        callback = OnWindowEvent; refresh = Refresh;
        if (test) { available = seen = 1; return; }
        window.PropertyChanged += WindowChanged;
        window.AddHandler(InputElement.PointerMovedEvent, PointerInput, RoutingStrategies.Tunnel, true);
        window.AddHandler(InputElement.PointerPressedEvent, PointerInput, RoutingStrategies.Tunnel, true);
        window.AddHandler(InputElement.PointerWheelChangedEvent, PointerInput, RoutingStrategies.Tunnel, true);
        window.AddHandler(InputElement.KeyDownEvent, KeyInput, RoutingStrategies.Tunnel, true);
        hook = SetWinEventHook(3, 0x801b, 0, callback, 0, 0, 0);
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
        SetWindowFacts(new(value, false, false, false, true, true));
    }
    internal void SetWindowFacts(WindowVisibilityFacts facts)
    {
        var oldSeen = Interlocked.Exchange(ref seen, facts.Seen ? 1 : 0);
        var oldAvailable = Interlocked.Exchange(ref available, facts.Available ? 1 : 0);
        if (oldSeen == (facts.Seen ? 1 : 0) && oldAvailable == (facts.Available ? 1 : 0)) return;
        Changed?.Invoke();
    }

    private void PointerInput(object? sender, PointerEventArgs args) => Touch(Now);
    private void KeyInput(object? sender, KeyEventArgs args) => Touch(Now);
    private void WindowChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == Window.IsActiveProperty || args.Property == Window.WindowStateProperty || args.Property == Visual.IsVisibleProperty) Refresh();
    }
    private void OnWindowEvent(nint hook, uint kind, nint handle, int objectId, int childId, uint thread, uint time)
    {
        if (objectId != 0 || childId != 0 || (kind != 3 && kind != 0x16 && kind != 0x17 && kind != 0x8002 && kind != 0x8003 && kind != 0x8004 && kind != 0x800b && kind != 0x8017 && kind != 0x8018)) return;
        if (Interlocked.Exchange(ref queued, 1) == 0) Dispatcher.UIThread.Post(refresh, DispatcherPriority.Background);
    }
    private void Refresh()
    {
        Volatile.Write(ref queued, 0);
        if (hwnd == 0) return;
        var shown = window.IsVisible && window.WindowState != WindowState.Minimized;
        SetWindowFacts(new(window.IsVisible, window.WindowState == WindowState.Minimized,
            shown && Cloaked(hwnd), shown && FullyCovered(hwnd), window.IsActive, GetForegroundWindow() == hwnd));
    }
    private static bool Cloaked(nint handle) => DwmGetWindowAttribute(handle, 14, out int value, sizeof(int)) == 0 && value != 0;
    private static bool FullyCovered(nint handle)
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
        if (hook != 0) { UnhookWinEvent(hook); hook = 0; }
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
