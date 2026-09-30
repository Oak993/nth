using System.Runtime.InteropServices;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Window.Native;

// Called on the HWND's own STA before any test input. A denied foreground request may briefly
// share the current foreground input queue; it is detached again before pointer injection.
// No activation click, simulated key, global foreground-policy change or foreign-window edit.
internal static class WindowManagerNativeForeground
{
    internal static void Acquire(nint window, uint expectedThread)
    {
        N.RequireOwner(window);
        var ownerThread = N.GetWindowThreadProcessId(window, out var ownerProcess);
        var callerThread = N.GetCurrentThreadId();
        if (ownerThread != expectedThread || callerThread != ownerThread)
            throw new InvalidOperationException($"NATIVE_PRECONDITION: Native activation called off the owned HWND thread: target=0x{window:X}, owner={ownerThread}, expected={expectedThread}, caller={callerThread}, pid={ownerProcess}.");

        var previousForeground = N.GetForegroundWindow();
        var foregroundAccepted = N.SetForegroundWindow(window);
        var attached = false;
        var attachError = 0;
        var foregroundOwner = N.GetWindowThreadProcessId(previousForeground, out _);
        if (N.GetForegroundWindow() != window && previousForeground != 0 && foregroundOwner != 0 && foregroundOwner != ownerThread)
        {
            // AttachThreadInput resets queue key state. Never attach while the user holds a
            // mouse button/modifier, while a menu/capture is active, or to an unresponsive queue.
            for (var key = 1; key < 256; key++)
                if ((N.GetAsyncKeyState(key) & 0x8000) != 0)
                    throw new InvalidOperationException("NATIVE_PRECONDITION: an input button/modifier is held before native activation.");
            var foregroundGui = N.Gui(foregroundOwner);
            if (foregroundGui.Capture != 0 || foregroundGui.MenuOwner != 0 ||
                SendMessageTimeout(previousForeground, 0, 0, 0, 0x0002, 200, out _) == 0)
                throw new InvalidOperationException("NATIVE_PRECONDITION: foreground queue has active capture/menu or is unresponsive; no input was sent.");
            if (N.GetForegroundWindow() != previousForeground ||
                N.GetWindowThreadProcessId(previousForeground, out _) != foregroundOwner)
                throw new InvalidOperationException("NATIVE_PRECONDITION: foreground identity changed during the activation preflight; no input was sent.");
            // USER calls after attachment can block on the other queue. The parent owns the
            // child process/job deadline; a driver timeout is not proof of successful detachment.
            attached = AttachThreadInput(ownerThread, foregroundOwner, true);
            attachError = attached ? 0 : Marshal.GetLastWin32Error();
            if (attached)
            {
                try
                {
                    _ = BringWindowToTop(window);
                    _ = SetActiveWindow(window);
                    foregroundAccepted = N.SetForegroundWindow(window);
                }
                finally
                {
                    if (!AttachThreadInput(ownerThread, foregroundOwner, false))
                        throw new InvalidOperationException("NATIVE_PRECONDITION: failed to detach the temporary foreground input queue, error=" + Marshal.GetLastWin32Error());
                }
            }
        }
        var previousActive = SetActiveWindow(window);
        var currentForeground = N.GetForegroundWindow();
        var currentActive = GetActiveWindow();
        var gui = N.Gui(ownerThread);
        var visible = N.IsWindowVisible(window);
        var minimized = N.IsIconic(window);
        var foregroundThread = N.GetWindowThreadProcessId(currentForeground, out var foregroundProcess);
        var diagnostics = $"target=0x{window:X}, ownerPid={ownerProcess}, ownerThread={ownerThread}, callerThread={callerThread}, " +
            $"SetForegroundWindow={foregroundAccepted}, temporaryAttach={attached}, attachError={attachError}, previousForeground=0x{previousForeground:X}, previousActive=0x{previousActive:X}, " +
            $"foreground=0x{currentForeground:X}, foregroundPid={foregroundProcess}, foregroundThread={foregroundThread}, " +
            $"active=0x{currentActive:X}, guiActive=0x{gui.Active:X}, capture=0x{gui.Capture:X}, menuOwner=0x{gui.MenuOwner:X}, " +
            $"visible={visible}, minimized={minimized}";
        if (!visible || minimized || currentForeground != window || currentActive != window || gui.Active != window ||
            gui.Capture != 0 || gui.MenuOwner != 0)
            throw new InvalidOperationException("NATIVE_PRECONDITION: Windows denied native foreground/activation before pointer injection. " + diagnostics);
        Console.WriteLine("Native foreground verified: " + diagnostics);
    }

    internal static void RequireStillForeground(nint window, uint expectedThread)
    {
        N.RequireOwner(window);
        var current = N.GetForegroundWindow();
        var active = N.Gui(expectedThread).Active;
        if (current != window || active != window)
            throw new InvalidOperationException($"NATIVE_PRECONDITION: Native foreground changed after activation and before pointer injection: target=0x{window:X}, foreground=0x{current:X}, ownedActive=0x{active:X}, ownedThread={expectedThread}.");
    }

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint SetActiveWindow(nint window);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint GetActiveWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool AttachThreadInput(uint from, uint to, bool attach);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool BringWindowToTop(nint window);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint SendMessageTimeout(nint window, uint message, nint wParam, nint lParam,
        uint flags, uint timeout, out nuint result);
}
