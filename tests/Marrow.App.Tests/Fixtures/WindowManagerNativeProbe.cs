using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window.Native;

// Observer only. Every message continues through DefSubclassProc, including unhandled nonclient
// presses. This never installs or directly calls either production WndProc callback.
internal sealed class WindowManagerNativeProbe : IDisposable
{
    private readonly NativeWindowSubclass callback;
    private readonly AvaloniaWindow window;
    private readonly WindowManagerNativeContent content;
    private readonly Func<nint> readPending;
    private readonly ConcurrentQueue<NativeWindowMessageReading> messages = new();
    private readonly ConcurrentQueue<bool> resizeTransitions = new();
    private int active;
    private int nextFence;
    private int completedFence;
    private Exception? callbackFailure;

    internal WindowManagerNativeProbe(AvaloniaWindow window, WindowManagerNativeContent content, Func<nint> readPending)
    {
        Dispatcher.UIThread.VerifyAccess();
        this.window = window;
        this.content = content;
        this.readPending = readPending;
        Handle = window.TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException("No native factory HWND.");
        WindowManagerNativeMethods.RequireOwner(Handle);
        ThreadId = WindowManagerNativeMethods.GetWindowThreadProcessId(Handle, out _);
        if (!WindowManagerNativeMethods.ClassName(Handle).StartsWith("Avalonia", StringComparison.Ordinal))
            throw new InvalidOperationException("The factory did not create an Avalonia Win32 window.");
        callback = Observe;
        WindowManagerNativeMethods.Check(WindowManagerNativeMethods.SetWindowSubclass(Handle, callback, 0x574D, 0), "SetWindowSubclass");
        InputQueue = new WindowManagerNativeQueueProbe(Handle, ThreadId);
        window.PropertyChanged += ObserveResize;
    }

    internal nint Handle { get; }
    internal uint ThreadId { get; }
    internal WindowManagerNativeQueueProbe InputQueue { get; }
    internal bool IsResizing => Volatile.Read(ref active) != 0;
    internal bool[] ResizeTransitions => resizeTransitions.ToArray();
    internal NativeWindowMessageReading[] Messages => messages.ToArray();
    internal int Count(uint message, bool returned = false) => messages.Count(item => item.Message == message && item.Returned == returned);
    internal int CommandCount(uint command) => messages.Count(item => item.Message == WindowManagerNativeMethods.WM_SYSCOMMAND && !item.Returned && (unchecked((uint)item.WParam) & 0xFFF0) == command);

    // Call after an input message has RETURNED. A queued marker follows commands that the release
    // posted; a DispatcherPriority.Send callback alone could run before those queued commands.
    internal void Fence()
    {
        var sequence = Interlocked.Increment(ref nextFence);
        WindowManagerNativeMethods.Post(Handle, WindowManagerNativeMethods.WM_TEST_FENCE, sequence);
        WindowManagerNativeMethods.Until(() => Volatile.Read(ref completedFence) >= sequence, "Native message fence did not return.");
    }

    internal void CheckCallbacks()
    {
        InputQueue.Check();
        if (Volatile.Read(ref callbackFailure) is { } failure)
            throw new InvalidOperationException("A test observer failed inside a native callback.", failure);
    }

    private nint Observe(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data)
    {
        var tracked = message is WindowManagerNativeMethods.WM_NCLBUTTONDOWN or WindowManagerNativeMethods.WM_NCLBUTTONUP
            or WindowManagerNativeMethods.WM_LBUTTONUP or WindowManagerNativeMethods.WM_CANCELMODE
            or WindowManagerNativeMethods.WM_CAPTURECHANGED or WindowManagerNativeMethods.WM_SYSCOMMAND
            or WindowManagerNativeMethods.WM_CONTEXTMENU or WindowManagerNativeMethods.WM_INITMENUPOPUP
            or WindowManagerNativeMethods.WM_ENTERMENULOOP or WindowManagerNativeMethods.WM_EXITMENULOOP
            or WindowManagerNativeMethods.WM_ENTERSIZEMOVE or WindowManagerNativeMethods.WM_SIZING
            or WindowManagerNativeMethods.WM_EXITSIZEMOVE;
        if (tracked) messages.Enqueue(new(message, wParam, lParam, false));
        var result = WindowManagerNativeMethods.DefSubclassProc(hwnd, message, wParam, lParam);
        if (message == WindowManagerNativeMethods.WM_TEST_FENCE)
            Volatile.Write(ref completedFence, checked((int)wParam));
        if (tracked)
        {
            nint? pending = null;
            try { pending = readPending(); }
            catch (Exception error) { Volatile.Write(ref callbackFailure, error); }
            messages.Enqueue(new(message, wParam, lParam, true, pending));
        }
        return result;
    }

    private void ObserveResize(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property != WindowLiveResizeSignal.IsActiveProperty) return;
        try
        {
            var model = AppEnvironment.GetWindowLiveResize(content);
            var signal = change.GetNewValue<bool>();
            if (!ReferenceEquals(sender, window) || signal != model.IsActive ||
                WindowManagerNativeMethods.GetCurrentThreadId() != ThreadId)
                throw new InvalidOperationException("Resize signal/model/window/thread disagree.");
            Volatile.Write(ref active, signal ? 1 : 0);
            resizeTransitions.Enqueue(signal);
        }
        catch (Exception error)
        {
            Volatile.Write(ref callbackFailure, error); // Never throw through a native WndProc.
        }
    }

    public void Dispose()
    {
        Dispatcher.UIThread.VerifyAccess();
        window.PropertyChanged -= ObserveResize;
        InputQueue.Dispose();
        if (WindowManagerNativeMethods.IsWindow(Handle))
            _ = WindowManagerNativeMethods.RemoveWindowSubclass(Handle, callback, 0x574D);
        GC.KeepAlive(callback);
    }
}

internal readonly record struct NativeWindowMessageReading(uint Message, nint WParam, nint LParam, bool Returned, nint? PendingCaption = null);
