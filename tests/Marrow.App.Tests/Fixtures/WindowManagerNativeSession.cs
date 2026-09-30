using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window.Native;

// Parent collection construction is inert, including when Headless already owns Avalonia.
// Each Run selects the same Fact in a fresh child before creating this fixture's native STA.
// In that child the root ExecutionContext keeps AppModelHarness overrides alive while Windows
// dispatches messages outside a posted delegate (notably frame autosave).
public sealed class WindowManagerNativeSession : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<AvaloniaWindow> windows = [];
    private Thread? uiThread;
    private ExecutionContext? nativeContext;
    private Exception? loopFailure;
    private bool disposed;

    public WindowManagerNativeSession() { }

    // Called only after the exact child Fact's nonce and owned-job permit have been checked.
    private void StartNativeChild()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || !Environment.UserInteractive)
            throw new InvalidOperationException("Native window tests require an interactive Windows x64 testhost.");
        if (uiThread is not null) throw new InvalidOperationException("Each native child executes exactly one Fact.");
        uiThread = new Thread(NativeLoop) { IsBackground = true, Name = "WindowManager native fixture STA" };
        uiThread.SetApartmentState(ApartmentState.STA);
        using (ExecutionContext.SuppressFlow()) uiThread.Start();
        ready.Task.WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
    }

    internal uint ThreadId { get; private set; }

    private void NativeLoop()
    {
        try
        {
            if (Application.Current is not null)
                throw new InvalidOperationException("The selected native child already initialized an Avalonia application.");
            AppBuilder.Configure<Application>().UsePlatformDetect().WithSwiftFonts().SetupWithoutStarting();
            MainActor.Bind(AvaloniaMainExecutor.Instance);
            using var harness = new AppModelHarness(withRuntime: false);
            ThreadId = WindowManagerNativeMethods.GetCurrentThreadId();
            nativeContext = ExecutionContext.Capture() ?? throw new InvalidOperationException("Native execution context was suppressed.");
            ready.TrySetResult();
            try { Dispatcher.UIThread.MainLoop(stop.Token); }
            finally { CloseWindows(); }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception error)
        {
            Volatile.Write(ref loopFailure, error);
            ready.TrySetException(error);
        }
    }

    internal T OnUi<T>(Func<T> body)
    {
        if (disposed) throw new ObjectDisposedException(nameof(WindowManagerNativeSession));
        if (Volatile.Read(ref loopFailure) is { } failure) ExceptionDispatchInfo.Capture(failure).Throw();
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = nativeContext ?? throw new InvalidOperationException("Native UI is not ready.");
        Dispatcher.UIThread.Post(() => ExecutionContext.Run(context.CreateCopy(), _ =>
        {
            try { completion.TrySetResult(body()); }
            catch (Exception error) { completion.TrySetException(error); }
        }, null), DispatcherPriority.Send);
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(4)).GetAwaiter().GetResult();
    }

    internal void OnUi(Action body) => OnUi(() => { body(); return true; });

    internal void Register(AvaloniaWindow window)
    {
        Dispatcher.UIThread.VerifyAccess();
        windows.Add(window);
    }

    internal void Run(Action<WindowManagerNativeSubject> body, [CallerMemberName] string member = "", Type? factType = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(body);
        var fact = WindowManagerNativeChildProtocol.FactName(member, factType);
        if (!WindowManagerNativeChildProtocol.IsChild)
        {
            WindowManagerNativeChildHost.RunParent(fact);
            return;
        }

        var identity = WindowManagerNativeChildProtocol.EnterChild(fact);
        Exception? failure = null;
        var bodyPassed = false;
        var nativeThreadJoined = false;
        try
        {
            StartNativeChild();
            RunNative(body);
            bodyPassed = true;
        }
        catch (Exception error) { failure = error; }
        finally
        {
            try
            {
                Dispose(); // Finish owned HWNDs and join the STA before this child Fact reports success.
                nativeThreadJoined = uiThread is not null && !uiThread.IsAlive;
            }
            catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
            WindowManagerNativeChildProtocol.Write(identity.Request.RunDirectory, "child-result.json",
                new WindowManagerNativeChildResult(identity, bodyPassed, nativeThreadJoined, failure?.ToString()));
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private void RunNative(Action<WindowManagerNativeSubject> body)
    {
        WindowManagerNativeSubject? subject = null;
        Exception? failure = null;
        try
        {
            subject = OnUi(() => new WindowManagerNativeSubject(this));
            body(subject); // Driver stays OFF the STA, including waits and native mouse injection.
            subject.MainProbe.CheckCallbacks();
            subject.SettingsProbe.CheckCallbacks();
        }
        catch (Exception error)
        {
            failure = error;
            if (subject is not null)
            {
                foreach (var probe in new[] { subject.MainProbe, subject.SettingsProbe })
                {
                    Console.WriteLine($"Test-owned HWND 0x{probe.Handle:X}, UI thread {probe.ThreadId}");
                    foreach (var item in probe.Messages)
                        Console.WriteLine($"message=0x{item.Message:X4} wParam=0x{item.WParam:X} lParam=0x{item.LParam:X} returned={item.Returned} pendingCaption={item.PendingCaption}");
                    Console.WriteLine("Resize transitions: " + string.Join(",", probe.ResizeTransitions));
                }
            }
        }
        finally
        {
            // Input and menu scopes in the test unwind before this. Do not destroy a still-pumped window
            // from another thread, and do not leave the production retained-close handler as teardown.
            try
            {
                OnUi(() =>
                {
                    try { subject?.Dispose(); }
                    finally { CloseWindows(); }
                });
            }
            catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private void CloseWindows()
    {
        Dispatcher.UIThread.VerifyAccess();
        _ = WindowManagerNativeMethods.EndMenu();
        _ = WindowManagerNativeMethods.ReleaseCapture();
        foreach (var window in windows.AsEnumerable().Reverse())
        {
            var handle = window.TryGetPlatformHandle()?.Handle ?? 0;
            if (handle == 0 || !WindowManagerNativeMethods.IsWindow(handle)) continue;
            window.Closing += AllowFixtureTeardown;
            window.Close();
            if (WindowManagerNativeMethods.IsWindow(handle))
                throw new InvalidOperationException("Test-owned window survived fixture teardown.");
        }
        windows.Clear();
    }

    private static void AllowFixtureTeardown(object? sender, WindowClosingEventArgs args) => args.Cancel = false;

    public void Dispose()
    {
        if (disposed) return;
        if (uiThread is null)
        {
            // Ordinary parent disposal must not even touch Dispatcher.UIThread: Headless may own it.
            disposed = true;
            stop.Dispose();
            return;
        }
        Exception? cleanupFailure = null;
        try
        {
            if (nativeContext is not null && uiThread.IsAlive && Volatile.Read(ref loopFailure) is null)
                OnUi(CloseWindows);
        }
        catch (Exception error) { cleanupFailure = error; }
        finally
        {
            stop.Cancel();
            // A failed platform startup may not have created a dispatcher at all.
            if (nativeContext is not null && uiThread.IsAlive)
                Dispatcher.UIThread.Post(() => { }, DispatcherPriority.Send);
            disposed = true;
        }
        if ((uiThread.ThreadState & ThreadState.Unstarted) == 0 && !uiThread.Join(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("Native child STA did not stop after cancellation; the parent owns bounded job cleanup.");
        stop.Dispose();
        if (Volatile.Read(ref loopFailure) is { } failure)
            cleanupFailure = cleanupFailure is null ? failure : new AggregateException(failure, cleanupFailure);
        if (cleanupFailure is not null) ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
    }
}
