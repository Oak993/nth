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
    /// <summary>The process's window-event hub (tests swap in one of their own, with recording hooks, for their run).</summary>
    internal static EventHub Events { get; set; } = new();
    private readonly Window window;
    private readonly nint hwnd;
    private readonly EventHub? events; // the hub this state listens to (null: a test state)
    private readonly Action refresh;
    private int users, available;
    private double lastInput = Now;
    internal event Action? Changed;
    internal bool Available => Volatile.Read(ref available) != 0;
    internal double LastInput => Volatile.Read(ref lastInput);
    internal static double Now => TimeProvider.System.GetTimestamp() / (double)TimeProvider.System.TimestampFrequency;

    internal AmbientWindowState(Window window, bool test = false)
    {
        this.window = window;
        hwnd = test ? 0 : window.TryGetPlatformHandle()?.Handle ?? 0;
        refresh = Refresh;
        if (test) { available = 1; return; }
        window.PropertyChanged += WindowChanged;
        window.AddHandler(InputElement.PointerMovedEvent, PointerInput, RoutingStrategies.Tunnel, true);
        window.AddHandler(InputElement.PointerPressedEvent, PointerInput, RoutingStrategies.Tunnel, true);
        window.AddHandler(InputElement.PointerWheelChangedEvent, PointerInput, RoutingStrategies.Tunnel, true);
        window.AddHandler(InputElement.KeyDownEvent, KeyInput, RoutingStrategies.Tunnel, true);
        events = Events;
        events.Subscribe(refresh);
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
    // Runs once per batch of window events (the hub's one flush), and when the window's own state changes.
    private void Refresh()
    {
        if (hwnd == 0 || events is null) return;
        SetAvailable(window.IsVisible && window.IsActive && window.WindowState != WindowState.Minimized
            && GetForegroundWindow() == hwnd && events.Look(hwnd) is { Cloaked: false, Covered: false });
    }
    internal static bool Cloaked(nint handle) => Cloak(handle) != 0;

    /// <summary>The window's DWMWA_CLOAKED flags (0: not cloaked).</summary>
    internal static int Cloak(nint handle) => DwmGetWindowAttribute(handle, 14, out int value, sizeof(int)) == 0 ? value : 0;
    internal static bool FullyCovered(nint handle) => FullyCovered(handle, null);

    /// <summary>
    /// Whether the windows above <paramref name="handle"/> cover all of it; <paramref name="coverers"/> (when given) gets the
    /// process of every window above that covers part of it.
    /// </summary>
    internal static bool FullyCovered(nint handle, HashSet<uint>? coverers) =>
        GetWindowRect(handle, out var bounds) && Covers(bounds.Rect, WindowsAbove(handle), coverers is { } processes ? above =>
        {
            if (GetWindowThreadProcessId(above, out var process) != 0) processes.Add(process);
        } : null);

    /// <summary>
    /// Whether the windows <paramref name="above"/> (nearest first; their bounds in the same physical pixels as
    /// <paramref name="bounds"/>, as GetWindowRect gives them, so no scale enters) cover all of <paramref name="bounds"/>;
    /// <paramref name="covering"/> hears every window that covers part of what was still showing. Stops asking for windows
    /// once nothing is left showing.
    /// </summary>
    internal static bool Covers(Rect bounds, IEnumerable<(nint Window, Rect Bounds)> above, Action<nint>? covering)
    {
        var remaining = new List<Rect> { bounds };
        foreach (var (window, cover) in above)
        {
            var covers = false;
            for (var i = remaining.Count - 1; i >= 0; i--)
            {
                var r = remaining[i]; var overlap = r.Intersect(cover);
                if (overlap.Width <= 0 || overlap.Height <= 0) continue;
                covers = true;
                remaining.RemoveAt(i);
                if (r.Left < overlap.Left) remaining.Add(new(r.Left, r.Top, overlap.Left - r.Left, r.Height));
                if (overlap.Right < r.Right) remaining.Add(new(overlap.Right, r.Top, r.Right - overlap.Right, r.Height));
                if (r.Top < overlap.Top) remaining.Add(new(overlap.Left, r.Top, overlap.Width, overlap.Top - r.Top));
                if (overlap.Bottom < r.Bottom) remaining.Add(new(overlap.Left, overlap.Bottom, overlap.Width, r.Bottom - overlap.Bottom));
            }
            if (covers) covering?.Invoke(window);
            if (remaining.Count == 0) break;
        }
        return remaining.Count == 0;
    }

    // The windows above handle, nearest first, that can hide it: shown, not cloaked, neither click-through nor layered.
    private static IEnumerable<(nint Window, Rect Bounds)> WindowsAbove(nint handle)
    {
        for (var above = GetWindow(handle, 3); above != 0; above = GetWindow(above, 3))
        {
            if (!IsWindowVisible(above) || Cloaked(above) || (GetWindowLong(above, -20) & (0x20 | 0x80000)) != 0 || !GetWindowRect(above, out var rect)) continue;
            yield return (above, rect.Rect);
        }
    }
    public void Dispose()
    {
        if (--users != 0) return;
        Windows.Remove(window);
        if (events is null) return;
        window.PropertyChanged -= WindowChanged;
        window.RemoveHandler(InputElement.PointerMovedEvent, PointerInput);
        window.RemoveHandler(InputElement.PointerPressedEvent, PointerInput);
        window.RemoveHandler(InputElement.PointerWheelChangedEvent, PointerInput);
        window.RemoveHandler(InputElement.KeyDownEvent, KeyInput);
        events.Unsubscribe(refresh);
    }

    /// <summary>What one look at a window found: cloaked, fully covered, and the processes of the windows covering it.
    /// <paramref name="AppCloakOnly"/>: the cloak is not the shell's (no DWM_CLOAKED_SHELL, 0x2): the app's own (DWM_CLOAKED_APP:
    /// WindowManager.SettingsCloak, the boot stage, ED-236), a window being composed to be shown (ED-NEW-lean-6 (cloak rule)).</summary>
    internal readonly record struct WindowLook(bool Cloaked, bool Covered, uint[] Coverers, bool AppCloakOnly = false);

    /// <summary>
    /// ED-NEW-lean-6: the process's one set of window-event hooks, shared by every window's ambient state and presence. It
    /// hears only what can change whether a window is in front or can be seen: the foreground (3), the end of a move or
    /// resize (0x0B), minimise start and end (0x16-0x17), show, hide and reorder (0x8002-0x8004) and cloak and uncloak
    /// (0x8017-0x8018); never the desktop's location changes (0x800B, every caret and cursor move). Only events of a window
    /// itself (OBJID_WINDOW, CHILDID_SELF) count. A batch of events is one flush: the listeners run once, and every window is
    /// looked at (cloak, then the walk of the windows above) at most once per flush, however many listeners ask.
    /// While a window of the app is fully covered, the location changes of the processes covering it are heard too (one
    /// 0x800B hook per such process, top-level windows only), so a covering window maximised, restored or snapped with the
    /// keyboard (which raises nothing else) is noticed; the hooks go when nothing of the app is covered. UI thread (the hooks
    /// call back on its message loop).
    /// </summary>
    internal sealed class EventHub
    {
        internal static readonly (uint First, uint Last)[] Ranges = [(0x0003, 0x0003), (0x000B, 0x000B), (0x0016, 0x0017), (0x8002, 0x8004), (0x8017, 0x8018)];
        internal const uint LocationChange = 0x800B;
        private readonly Func<uint, uint, uint, AmbientWinEventCallback, nint> setHook;
        private readonly Action<nint> unhook;
        private readonly Func<nint, bool> topLevel;
        private readonly Func<nint, WindowLook> look;
        private readonly AmbientWinEventCallback callback; // rooted while hooked
        private readonly Action flush;
        private readonly Dictionary<nint, uint[]> covered = [];
        private readonly Dictionary<uint, nint> locationHooks = [];
        private Action[] subscribers = [];
        private nint[]? hooks;
        private Dictionary<nint, WindowLook>? looks; // this flush's looks (null outside a flush)
        private bool queued;

        /// <param name="setHook">Installs a hook for events first..last of one process (0: every process); tests record.</param>
        /// <param name="unhook">Removes a hook <paramref name="setHook"/> installed.</param>
        /// <param name="topLevel">Whether a window is top level (a location change of a child window is not heard).</param>
        /// <param name="look">Looks at a window (tests count the looks).</param>
        internal EventHub(Func<uint, uint, uint, AmbientWinEventCallback, nint>? setHook = null, Action<nint>? unhook = null,
            Func<nint, bool>? topLevel = null, Func<nint, WindowLook>? look = null)
        {
            this.setHook = setHook ?? (static (first, last, process, callback) => SetWinEventHook(first, last, 0, callback, process, 0, 0));
            this.unhook = unhook ?? (static handle => UnhookWinEvent(handle));
            this.topLevel = topLevel ?? (static handle => GetAncestor(handle, 2) == handle); // GA_ROOT
            this.look = look ?? LookAt;
            callback = OnEvent; flush = Flush;
        }

        /// <summary>Whether the hooks are installed (tests).</summary>
        internal bool Hooked => hooks is not null;

        /// <summary>The processes whose location changes are heard (tests).</summary>
        internal IReadOnlyCollection<uint> Watched => locationHooks.Keys;

        internal void Subscribe(Action handler)
        {
            subscribers = [.. subscribers, handler];
            if (hooks is not null) return;
            hooks = new nint[Ranges.Length];
            for (var i = 0; i < Ranges.Length; i++) hooks[i] = setHook(Ranges[i].First, Ranges[i].Last, 0, callback);
        }

        internal void Unsubscribe(Action handler)
        {
            var index = Array.IndexOf(subscribers, handler);
            if (index < 0) return;
            subscribers = [.. subscribers[..index], .. subscribers[(index + 1)..]];
            if (subscribers.Length != 0 || hooks is null) return;
            foreach (var hook in hooks) if (hook != 0) unhook(hook);
            hooks = null;
            covered.Clear(); Watch();
        }

        /// <summary>
        /// The window <paramref name="window"/> is fully covered by windows of <paramref name="processes"/> (empty: it is not
        /// covered, or it is gone): their location changes are heard while any window of the app is covered by them.
        /// </summary>
        internal void Cover(nint window, uint[] processes)
        {
            if (processes.Length == 0) { if (!covered.Remove(window)) return; }
            else if (covered.TryGetValue(window, out var known) && known.AsSpan().SequenceEqual(processes)) return;
            else covered[window] = processes;
            Watch();
        }

        private void Watch()
        {
            var wanted = new HashSet<uint>();
            if (hooks is not null) foreach (var processes in covered.Values) wanted.UnionWith(processes);
            foreach (var process in locationHooks.Keys.Where(process => !wanted.Contains(process)).ToArray())
            {
                if (locationHooks.Remove(process, out var hook) && hook != 0) unhook(hook);
            }
            foreach (var process in wanted)
                if (!locationHooks.ContainsKey(process)) locationHooks[process] = setHook(LocationChange, LocationChange, process, callback);
        }

        /// <summary>The look at <paramref name="window"/>: taken once per flush and shared by every listener (outside a flush, fresh).</summary>
        internal WindowLook Look(nint window)
        {
            if (looks is not null && looks.TryGetValue(window, out var known)) return known;
            var seen = look(window);
            looks?.Add(window, seen);
            return seen;
        }

        private static WindowLook LookAt(nint window)
        {
            if (Cloak(window) is var cloak and not 0) return new(true, false, [], (cloak & 0x2) == 0);
            var coverers = new HashSet<uint>();
            var fully = FullyCovered(window, coverers);
            return new(false, fully, fully ? [.. coverers.Order()] : []);
        }

        /// <summary>A window event (the hooks' callback; tests raise it).</summary>
        internal void OnEvent(nint hook, uint kind, nint handle, int objectId, int childId, uint thread, uint time)
        {
            if (objectId != 0 || childId != 0) return; // OBJID_WINDOW, CHILDID_SELF
            if (kind == LocationChange && !topLevel(handle)) return;
            if (queued) return; // a flush is already on its way: it sees this event too
            queued = true;
            Dispatcher.UIThread.Post(flush, DispatcherPriority.Background);
        }

        private void Flush()
        {
            queued = false;
            looks = [];
            try { foreach (var subscriber in subscribers) subscriber(); }
            finally { looks = null; }
        }
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetWindow(nint handle, uint command);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint handle, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle, out uint process);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint handle, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint handle, out AmbientWindowRect rect);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint handle, int attribute, out int value, int size);
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint first, uint last, nint module, AmbientWinEventCallback callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
}
