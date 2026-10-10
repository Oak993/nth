namespace Marrow.App.Tests.Window.Native;

// Driver-thread SendInput. No RaiseEvent, direct handler call, WM_NCLBUTTONDOWN/UP injection,
// or synthetic replacement of a captured WM_LBUTTONUP with WM_NCLBUTTONUP.
internal sealed class WindowManagerNativePointer : IDisposable
{
    private readonly WindowManagerNativeSubject subject;
    private readonly NativeWindowPoint originalCursor;
    private readonly HashSet<nint> owned;
    private bool leftHeld;

    internal WindowManagerNativePointer(WindowManagerNativeSubject subject)
    {
        this.subject = subject;
        owned = [subject.MainProbe.Handle, subject.SettingsProbe.Handle];
        if ((WindowManagerNativeMethods.GetAsyncKeyState(0x01) & 0x8000) != 0 ||
            (WindowManagerNativeMethods.GetAsyncKeyState(0x02) & 0x8000) != 0)
            throw new InvalidOperationException("A mouse button is already held. Native input was not started.");
        WindowManagerNativeMethods.Check(WindowManagerNativeMethods.GetCursorPos(out originalCursor), "GetCursorPos");
    }

    internal void Down(WindowManagerNativeProbe probe, NativeWindowPoint point, int expectedHit)
    {
        if (leftHeld) throw new InvalidOperationException("A test mouse press is already active.");
        GuardStart(probe, point, expectedHit);
        Move(probe, point);
        GuardStart(probe, point, expectedHit);
        WindowManagerNativeMethods.Mouse(WindowManagerNativeMethods.MOUSE_LEFTDOWN);
        leftHeld = true;
    }

    internal void Up(WindowManagerNativeProbe probe, NativeWindowPoint point)
    {
        if (!leftHeld) throw new InvalidOperationException("No test mouse press is active.");
        Move(probe, point);
        GuardDelivery(point);
        WindowManagerNativeMethods.Mouse(WindowManagerNativeMethods.MOUSE_LEFTUP);
        leftHeld = false;
    }

    internal void RightClick(WindowManagerNativeProbe probe, NativeWindowPoint point, int expectedHit)
    {
        GuardStart(probe, point, expectedHit);
        Move(probe, point);
        GuardStart(probe, point, expectedHit);
        WindowManagerNativeMethods.Mouse(WindowManagerNativeMethods.MOUSE_RIGHTDOWN, WindowManagerNativeMethods.MOUSE_RIGHTUP);
    }

    internal void Move(WindowManagerNativeProbe probe, NativeWindowPoint point)
    {
        WindowManagerNativeMethods.RequireOwner(probe.Handle);
        GuardDelivery(point);
        WindowManagerNativeMethods.Check(WindowManagerNativeMethods.SetCursorPos(point.X, point.Y), "SetCursorPos on test window");
        GuardDelivery(point);
        WindowManagerNativeMethods.Mouse(WindowManagerNativeMethods.MOUSE_MOVE);
    }

    private void GuardStart(WindowManagerNativeProbe probe, NativeWindowPoint point, int expectedHit)
    {
        WindowManagerNativeMethods.RequireOwner(probe.Handle);
        if (WindowManagerNativeMethods.GetForegroundWindow() != probe.Handle || WindowManagerNativeMethods.RootAt(point) != probe.Handle)
            throw new InvalidOperationException("Native pointer target is covered or is no longer the foreground test window." +
                $" target=0x{probe.Handle:X}, foreground=0x{WindowManagerNativeMethods.GetForegroundWindow():X}, rootAtPoint=0x{WindowManagerNativeMethods.RootAt(point):X}, point={point.X},{point.Y}," +
                $" main=0x{subject.MainProbe.Handle:X}, settings=0x{subject.SettingsProbe.Handle:X}, targetRect={Describe(WindowManagerNativeMethods.Rect(probe.Handle))}, mainRect={Describe(WindowManagerNativeMethods.Rect(subject.MainProbe.Handle))}, settingsRect={Describe(WindowManagerNativeMethods.Rect(subject.SettingsProbe.Handle))}.");
        if (WindowManagerNativeMethods.Hit(probe.Handle, point) != expectedHit)
            throw new InvalidOperationException("The actual WM_NCHITTEST result does not match the requested test hit region.");
    }

    private static string Describe(NativeWindowRect rectangle) => $"{rectangle.Left},{rectangle.Top},{rectangle.Right},{rectangle.Bottom}";

    private void GuardDelivery(NativeWindowPoint point)
    {
        var info = WindowManagerNativeMethods.Gui(subject.Session.ThreadId);
        if (info.Capture != 0)
        {
            if (owned.Contains(info.Capture)) return;
            throw new InvalidOperationException("A foreign capture target would receive the injected pointer input.");
        }
        if (owned.Contains(WindowManagerNativeMethods.RootAt(point))) return;
        throw new InvalidOperationException("Injected pointer input would leave the test-owned windows without capture.");
    }

    // Locate a real native edge, accounting for DWM's invisible frame. Never replace the hit result.
    internal static NativeWindowPoint RightEdge(WindowManagerNativeProbe probe, bool resizable)
    {
        var rectangle = WindowManagerNativeMethods.Rect(probe.Handle);
        for (var inset = 1; inset <= 20; inset++)
        {
            var point = new NativeWindowPoint(rectangle.Right - inset, rectangle.Top + rectangle.Height / 2);
            if (WindowManagerNativeMethods.RootAt(point) != probe.Handle) continue;
            var hit = WindowManagerNativeMethods.Hit(probe.Handle, point);
            if (resizable ? hit == WindowManagerNativeMethods.HTRIGHT : hit is < 10 or > 17) return point;
        }
        throw new InvalidOperationException("No test-owned native edge has the expected resize capability.");
    }

    public void Dispose()
    {
        if (leftHeld)
        {
            WindowManagerNativeMethods.Check(WindowManagerNativeMethods.GetCursorPos(out var current), "GetCursorPos during input cleanup");
            GuardDelivery(current);
            WindowManagerNativeMethods.Mouse(WindowManagerNativeMethods.MOUSE_LEFTUP);
            leftHeld = false;
        }
        // Release any capture left by a failing bridge before closing its windows.
        subject.Session.OnUi(() => { _ = WindowManagerNativeMethods.ReleaseCapture(); });
        WindowManagerNativeMethods.Check(WindowManagerNativeMethods.SetCursorPos(originalCursor.X, originalCursor.Y), "Restore test cursor position");
    }
}
