using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Marrow.Core.Web;

namespace Marrow.UI.Tests.Web;

/// <summary>
/// W7 (DESIGN 13.3): the web view a <see cref="Marrow.UI.Web.WebViewHost"/> wraps, scripted by the test. It records
/// every call, lets the test hold the gesture capture and the frame ping, and runs <see cref="OnLiveVisible"/> at the
/// moment the host shows or hides the live view, so a test can check what was on screen right then.
/// </summary>
internal sealed class FakeWebViewSurface : IWebViewSurface
{
    public WebCreationOptions Creation { get; } = new(WebProfileKind.Browse, "browse", false, @"C:\nth-test\WebView2", "en-US", null, false);

    public WebLiveness Liveness { get; private set; } = WebLiveness.Live;

    public WebFailureCard? Failure { get; private set; }

    public WebSnapshot? LastSnapshot { get; set; }

    public bool IsLiveVisible { get; private set; } = true;

    /// <summary>Every call in order ("SetLiveVisible False", "Bounds 400x300@1", "Frames 2", "Activate", ...).</summary>
    public List<string> Log { get; } = [];

    /// <summary>The frame the next gesture capture returns at once; ignored while <see cref="HeldGesture"/> is set.</summary>
    public WebGestureFrame? GestureFrame { get; set; }

    public TaskCompletionSource<WebGestureFrame>? HeldGesture { get; set; }

    public TaskCompletionSource<bool>? HeldFrames { get; set; }

    public Action<bool>? OnLiveVisible { get; set; }

    public List<WebFailureAction> Actions { get; } = [];

    public event Action? Changed;

    public event Action<bool>? FocusExitRequested;

    public void Set(WebLiveness liveness)
    {
        Liveness = liveness;
        Changed?.Invoke();
    }

    public void Fail(WebFailureCard? card)
    {
        Failure = card;
        Changed?.Invoke();
    }

    /// <summary>The engine hid the live view by itself (a tab no longer shown).</summary>
    public void HideLive()
    {
        IsLiveVisible = false;
        Changed?.Invoke();
    }

    public void RaiseFocusExit(bool forward) => FocusExitRequested?.Invoke(forward);

    public Task<nint> AcquireEnvironmentAsync(CancellationToken cancellationToken) => Task.FromResult<nint>(0);

    public void AttachNative(nint coreWebView2, nint controller) => Log.Add("AttachNative");

    public void DetachNative() => Log.Add("DetachNative");

    public Task<WebGestureFrame> CaptureForGestureAsync(CancellationToken cancellationToken)
    {
        Log.Add("Capture");
        if (HeldGesture is { } held) return held.Task;
        return Task.FromResult(GestureFrame ?? new WebGestureFrame(LastSnapshot, null));
    }

    public void NoteIntent() => Log.Add("Intent");

    public void SetBounds(double width, double height, double scale) =>
        Log.Add(FormattableString.Invariant($"Bounds {width}x{height}@{scale}"));

    public void SetLiveVisible(bool visible)
    {
        OnLiveVisible?.Invoke(visible);
        IsLiveVisible = visible;
        Log.Add($"SetLiveVisible {visible}");
    }

    public Task<bool> WaitForFramesAsync(int frames, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Log.Add($"Frames {frames}");
        return HeldFrames?.Task ?? Task.FromResult(true);
    }

    public void Activate() => Log.Add("Activate");

    public void InvokeFailureAction(WebFailureAction action) => Actions.Add(action);

    /// <summary>A real PNG of one colour, made with Avalonia's own encoder.</summary>
    public static byte[] Png(int width, int height, Color color)
    {
        using var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormats.Bgra8888, AlphaFormat.Premul);
        using (var buffer = bitmap.Lock())
        {
            var row = new byte[width * 4];
            for (var x = 0; x < width; x++)
            {
                row[x * 4] = color.B;
                row[(x * 4) + 1] = color.G;
                row[(x * 4) + 2] = color.R;
                row[(x * 4) + 3] = color.A;
            }

            for (var y = 0; y < height; y++) Marshal.Copy(row, 0, buffer.Address + (y * buffer.RowBytes), row.Length);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        return stream.ToArray();
    }

    public static WebSnapshot Frame(int pixelWidth, int pixelHeight, double scale, Color color, uint? background = null) =>
        new(Png(pixelWidth, pixelHeight, color), pixelWidth, pixelHeight, scale, DateTimeOffset.UnixEpoch, background, WebSnapshotSource.Fake);

    /// <summary>The result of a task that must have finished by now.</summary>
    public static T Done<T>(Task<T> task)
    {
        if (!task.IsCompletedSuccessfully) throw new InvalidOperationException($"the task is {task.Status}");
        return task.Result;
    }

    /// <summary>Runs the headless dispatcher and render timer until <paramref name="task"/> ends (10 s at most).</summary>
    public static void Pump(Task task)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!task.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            if (task.IsCompleted) break;
            if (DateTime.UtcNow > until) throw new TimeoutException("the host did not finish its swap");
            Thread.Sleep(1);
        }

        task.GetAwaiter().GetResult();
    }

    /// <summary>Runs the headless dispatcher and render timer until <paramref name="done"/> holds (10 s at most).</summary>
    public static void PumpUntil(Func<bool> done)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!done())
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            if (done()) break;
            if (DateTime.UtcNow > until) throw new TimeoutException("the condition never held");
            Thread.Sleep(1);
        }
    }
}
