using System.Runtime.InteropServices;

namespace Marrow.App.Tests.Window.Native;

// Read-only WH_GETMESSAGE observer on the test window's own STA, never a global input hook.
// DefWindowProc may consume a disabled caption button's release in its internal tracking loop,
// without dispatching that release through the HWND procedure. Observe the real dequeue instead.
internal sealed class WindowManagerNativeQueueProbe : IDisposable
{
    private readonly nint window;
    private readonly NativeWindowQueueHook callback;
    private readonly nint hook;
    private int releases;
    private Exception? failure;

    internal WindowManagerNativeQueueProbe(nint window, uint thread)
    {
        WindowManagerNativeMethods.RequireOwner(window);
        if (WindowManagerNativeMethods.GetCurrentThreadId() != thread)
            throw new InvalidOperationException("Queue observation must be installed on the test-owned STA.");
        this.window = window;
        callback = Observe;
        hook = SetWindowsHookEx(3, callback, 0, thread);
        WindowManagerNativeMethods.Check(hook != 0, "Install test-thread message-queue observer");
    }

    internal int ReleaseCount => Volatile.Read(ref releases);

    private nint Observe(int code, nint removed, nint pointer)
    {
        try
        {
            if (code >= 0 && removed == 1 && pointer != 0)
            {
                var message = Marshal.PtrToStructure<NativeQueuedWindowMessage>(pointer);
                if (message.Window == window && message.Message is WindowManagerNativeMethods.WM_LBUTTONUP or WindowManagerNativeMethods.WM_NCLBUTTONUP)
                    Interlocked.Increment(ref releases);
            }
        }
        catch (Exception error) { Volatile.Write(ref failure, error); }
        return CallNextHookEx(hook, code, removed, pointer);
    }

    internal void Check()
    {
        if (Volatile.Read(ref failure) is { } error)
            throw new InvalidOperationException("The test-thread queue observer failed.", error);
    }

    public void Dispose()
    {
        WindowManagerNativeMethods.Check(UnhookWindowsHookEx(hook), "Remove test-thread message-queue observer");
        GC.KeepAlive(callback);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint SetWindowsHookEx(int kind, NativeWindowQueueHook callback, nint module, uint thread);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(nint hook);
}

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint NativeWindowQueueHook(int code, nint removed, nint message);

[StructLayout(LayoutKind.Sequential)]
internal struct NativeQueuedWindowMessage
{
    internal nint Window;
    internal uint Message;
    internal nuint WParam;
    internal nint LParam;
    internal uint Time;
    internal NativeWindowPoint Point;
    internal uint Private;
}
