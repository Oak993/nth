namespace Marrow.App.Tests.Window.Native;

// The timer is armed on the owning STA BEFORE the triggering command/input. TIMERPROC dispatch
// also occurs inside a native menu's message loop. A broken assertion never needs a person to
// dismiss the menu; normal disposal posts WM_CANCELMODE and the independent deadline calls EndMenu.
internal sealed class WindowManagerNativeMenu : IDisposable
{
    private readonly WindowManagerNativeSession session;
    private readonly WindowManagerNativeProbe probe;
    private readonly NativeWindowTimer timer;
    private readonly nuint timerId;
    private int deadlineFired;
    private bool disposed;

    internal WindowManagerNativeMenu(WindowManagerNativeSession session, WindowManagerNativeProbe probe)
    {
        this.session = session;
        this.probe = probe;
        timer = (_, _, id, _) =>
        {
            Interlocked.Exchange(ref deadlineFired, 1);
            _ = WindowManagerNativeMethods.KillTimer(probe.Handle, id);
            _ = WindowManagerNativeMethods.EndMenu();
        };
        timerId = session.OnUi(() => WindowManagerNativeMethods.SetTimer(probe.Handle, 0x574D4E55, 4000, timer));
        WindowManagerNativeMethods.Check(timerId != 0, "SetTimer for native menu deadline");
    }

    internal bool DeadlineFired => Volatile.Read(ref deadlineFired) != 0;

    internal NativeMenuReading WaitForMenu()
    {
        nint popup = 0;
        WindowManagerNativeMethods.Until(() => (popup = WindowManagerNativeMethods.MenuOn(probe.ThreadId)) != 0,
            "No native system-menu popup appeared on the factory window's STA.", 1800);
        var menu = WindowManagerNativeMethods.GetSystemMenu(probe.Handle, false);
        if (menu == 0) throw new InvalidOperationException("The factory HWND has no system menu.");
        var states = new Dictionary<uint, uint>();
        foreach (var command in new[] { WindowManagerNativeMethods.SC_SIZE, WindowManagerNativeMethods.SC_MINIMIZE,
            WindowManagerNativeMethods.SC_MAXIMIZE, WindowManagerNativeMethods.SC_RESTORE, WindowManagerNativeMethods.SC_CLOSE })
        {
            var state = WindowManagerNativeMethods.GetMenuState(menu, command, 0);
            if (state == uint.MaxValue) throw new InvalidOperationException("A required native system-menu item is absent.");
            states.Add(command, state);
        }
        return new NativeMenuReading(WindowManagerNativeMethods.Rect(popup), states);
    }

    internal bool AppearsWithin(int milliseconds) =>
        WindowManagerNativeMethods.Wait(() => WindowManagerNativeMethods.MenuOn(probe.ThreadId) != 0, milliseconds);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        WindowManagerNativeMethods.Post(probe.Handle, WindowManagerNativeMethods.WM_CANCELMODE);
        try
        {
            WindowManagerNativeMethods.Until(() => WindowManagerNativeMethods.MenuOn(probe.ThreadId) == 0,
                "Native system menu survived cancellation and its four-second EndMenu watchdog.", 5500);
            session.OnUi(() => { _ = WindowManagerNativeMethods.KillTimer(probe.Handle, timerId); });
        }
        finally { GC.KeepAlive(timer); }
        if (DeadlineFired)
            throw new TimeoutException("The system-menu watchdog was needed. Treat this as a fixture/teardown failure, not a pass.");
    }
}

internal sealed record NativeMenuReading(NativeWindowRect Bounds, IReadOnlyDictionary<uint, uint> States)
{
    internal bool Enabled(uint command) => (States[command] & 0x0003) == 0;
}
