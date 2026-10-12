using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Marrow.Core.Web;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// A web view the pane workspace can hold (web W3b): the live view is a plain control, the gesture capture can be held by
/// the test, and every call is logged. The workspace must have this view show its snapshot before a pane lifts over it.
/// </summary>
internal sealed class PaneWebSurface : IWebViewSurface
{
    public WebCreationOptions Creation { get; } = new(WebProfileKind.Browse, "browse", false, @"C:\nth-test\WebView2", "en-US", null, false);

    public WebLiveness Liveness => WebLiveness.Live;

    public WebFailureCard? Failure => null;

    public WebSnapshot? LastSnapshot { get; set; } = Frame(40, 30, Color.FromRgb(0x20, 0x40, 0x80));

    public bool IsLiveVisible { get; private set; } = true;

    public List<string> Log { get; } = [];

    /// <summary>Held: the capture does not return until the test completes it.</summary>
    public TaskCompletionSource<WebGestureFrame>? HeldGesture { get; set; }

    public int Captures { get; private set; }

#pragma warning disable CS0067 // part of the interface; this view never raises them
    public event Action? Changed;

    public event Action<bool>? FocusExitRequested;
#pragma warning restore CS0067

    public Task<nint> AcquireEnvironmentAsync(CancellationToken cancellationToken) => Task.FromResult<nint>(0);

    public void AttachNative(nint coreWebView2, nint controller) => Log.Add("AttachNative");

    public void DetachNative() => Log.Add("DetachNative");

    public Task<WebGestureFrame> CaptureForGestureAsync(CancellationToken cancellationToken)
    {
        Captures++;
        Log.Add("Capture");
        return HeldGesture?.Task ?? Task.FromResult(new WebGestureFrame(LastSnapshot, null));
    }

    public void NoteIntent() => Log.Add("Intent");

    public void SetBounds(double width, double height, double scale) { }

    public void SetLiveVisible(bool visible)
    {
        IsLiveVisible = visible;
        Log.Add($"SetLiveVisible {visible}");
    }

    public Task<bool> WaitForFramesAsync(int frames, TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(true);

    public void Activate() => Log.Add("Activate");

    public void InvokeFailureAction(WebFailureAction action) { }

    /// <summary>A real PNG of one colour, made with Avalonia's own encoder.</summary>
    public static WebSnapshot Frame(int width, int height, Color color)
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
        return new WebSnapshot(stream.ToArray(), width, height, 1, DateTimeOffset.UnixEpoch, 0xFF102030, WebSnapshotSource.Fake);
    }
}
