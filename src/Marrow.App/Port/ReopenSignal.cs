// PORT-ONLY: the second-launch signal of ED-002. The source contributes no single-instance lock; macOS Launch Services
// activates the running copy instead, and the running copy's AppDelegate.swift:36-39 applicationShouldHandleReopen then
// shows the main window when none is visible. ED-002 rules that a second launch on Windows runs exactly that path in the
// running instance. The first instance, holding the lock (DroppyCodeApp.TryAcquireSingleInstanceLock), keeps a named
// auto-reset event; a second launch that finds the lock taken lets the running instance take the foreground (a process
// in the background may not, AllowSetForegroundWindow) and sets the event; the running instance hears it on a background
// thread and runs the reopen on main. An auto-reset event keeps a signal that arrives before anyone listens (the running
// instance still starting), and several quick launches are one reopen.
using System.Runtime.InteropServices;
using Marrow.Core.Port;

namespace Marrow.App.Port;

/// <summary>PORT-ONLY. The second-launch signal (ED-002).</summary>
public sealed class ReopenSignal : IDisposable
{
    // winuser.h: ASFW_ANY, any process may set the foreground window next.
    private const int AsfwAny = -1;

    private readonly EventWaitHandle signal;
    private readonly ManualResetEvent stop = new(false);
    private Thread? listener;

    private ReopenSignal(EventWaitHandle signal) => this.signal = signal;

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);

    /// <summary>The running instance's signal, made right after it takes the single-instance lock.</summary>
    public static ReopenSignal Create(string name) =>
        new(new EventWaitHandle(initialState: false, EventResetMode.AutoReset, name));

    /// <summary>
    /// Runs <paramref name="onReopen"/> on the main actor of the caller (captured now) each time a second launch sets the
    /// signal. Once per instance.
    /// </summary>
    public void Listen(Action onReopen)
    {
        if (listener is not null) throw new InvalidOperationException("The reopen signal already has its listener.");
        var main = MainActor.Executor.Current;
        listener = new Thread(() =>
        {
            WaitHandle[] handles = [signal, stop];
            while (WaitHandle.WaitAny(handles) == 0) main.Enqueue(onReopen);
        })
        {
            IsBackground = true,
            Name = "reopen-signal",
        };
        listener.Start();
    }

    /// <summary>
    /// A second launch: lets the running instance take the foreground and sets its signal. False when no instance
    /// holds a signal of that name.
    /// </summary>
    public static bool Send(string name)
    {
        if (!EventWaitHandle.TryOpenExisting(name, out var running)) return false;
        using (running)
        {
            _ = AllowSetForegroundWindow(AsfwAny);
            return running.Set();
        }
    }

    public void Dispose()
    {
        stop.Set();
        listener?.Join();
        stop.Dispose();
        signal.Dispose();
    }
}
