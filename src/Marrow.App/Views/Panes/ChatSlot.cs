using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Marrow.UI.Port;

// PORT-ONLY: web surfaces W3a, the chat column's place in the workspace (E:/Droppy/work/design/web-surfaces/DESIGN.md
// 2.6 "Fallback if (h) fails ... the chat column does not re-wrap during the spring. Its content keeps the start layout
// under a clip that springs to the end width (a compositor clip animation, so no layout per frame), then re-lays out once
// at the end with a 120 ms cross-fade, anchored so the line under the reading position stays put. Growing reveals the
// chat background; shrinking clips the trailing edge for 320 ms"; ED-NEW-web-w3a-2). No Swift counterpart. The slot holds
// the thread's chat columns. With a width reserve its content is laid out at the reserved width whatever the slot's own
// width, and the slot clips it: the spring then moves only the slot's edge, which costs an arrange of the slot and a
// clip, never a re-measure of the transcript. Ending the reserve lays the content out once at the slot's width under a
// snapshot of the last frame that fades out over 120 ms (rendered on the compositor, so the UI thread pays nothing).

namespace Marrow.App.Views.Panes;

/// <summary>The chat column's slot: its content, an optional width reserve, and the settle cross-fade.</summary>
public sealed class ChatSlot : Panel
{
    /// <summary>The cross-fade from the last reserved frame to the new layout (DESIGN 2.6).</summary>
    public const double CrossFadeSeconds = 0.12;

    /// <summary>How many frames the slot waits for the compositor's snapshot before it re-lays out without one.</summary>
    public const int SnapshotFrames = 3;

    private readonly Image overlay = new()
    {
        Stretch = Stretch.Fill,
        IsHitTestVisible = false,
        IsVisible = false,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
    };
    private Control? content;
    private double? reserved;
    private int generation;
    private Action<TimeSpan>? pendingFrame;
    private IFrameSource? frameSource;
    private TimeSpan? lastFrame;
    private double fadeElapsed = double.NaN;
    private Bitmap? fading;
    private Action? waiting;
    private int waitedFrames;

    public ChatSlot()
    {
        AutomationProperties.SetAccessibilityView(overlay, AccessibilityView.Raw);
        Children.Add(overlay);
        DetachedFromVisualTree += (_, _) => FinishNow();
    }

    /// <summary>The snapshot the cross-fade starts from; tests replace it. Null or a fault means no cross-fade.</summary>
    internal static Marrow.Core.Port.Ambient<Func<Visual, Size, Task<Bitmap?>?>> Snapshot { get; } = new(CompositorSnapshot);

    /// <summary>The chat columns.</summary>
    public Control? Content
    {
        get => content;
        set
        {
            if (ReferenceEquals(content, value)) return;
            if (content is not null) Children.Remove(content);
            content = value;
            if (value is not null) Children.Insert(0, value);
            InvalidateMeasure();
        }
    }

    /// <summary>The width the content is laid out at while a reflow spring runs (the start width), or null.</summary>
    public double? ReservedWidth => reserved;

    /// <summary>The settle cross-fade is showing.</summary>
    public bool IsCrossFading => overlay.IsVisible;

    /// <summary>The cross-fade's image, for tests.</summary>
    internal Image Overlay => overlay;

    /// <summary>Waiting for the snapshot that starts the cross-fade.</summary>
    public bool IsEnding => waiting is not null;

    /// <summary>Holds the content at <paramref name="width"/> under a clip (the start of a reflow spring).</summary>
    public void Reserve(double width)
    {
        generation++;
        waiting = null;
        if (reserved is not null) return; // a spring that turns mid-way keeps the layout it started from
        reserved = Math.Max(0, width);
        ClipToBounds = true;
        InvalidateMeasure();
    }

    /// <summary>
    /// Ends the reserve: the content is laid out once at the slot's width, under the last frame fading out over 120 ms.
    /// <paramref name="relaid"/> runs in the same turn as the new layout (the workspace's caption reserve and reading
    /// anchor go with it). Without a reserve it just runs <paramref name="relaid"/>.
    /// </summary>
    public void EndReserve(Action? relaid = null)
    {
        if (reserved is null)
        {
            relaid?.Invoke();
            return;
        }
        var token = ++generation;
        Task<Bitmap?>? shot = null;
        try { shot = Snapshot.Current(this, Bounds.Size); }
        catch (Exception) { shot = null; }
        if (shot is null || shot.IsCompleted)
        {
            Complete(shot is { IsCompletedSuccessfully: true } ? shot.Result : null, relaid);
            return;
        }
        waiting = () => Complete(null, relaid);
        waitedFrames = 0;
        RequestFrame();
        shot.ContinueWith(task =>
        {
            var bitmap = task.IsCompletedSuccessfully ? task.Result : null;
            Dispatcher.UIThread.Post(() =>
            {
                if (token != generation || waiting is null)
                {
                    bitmap?.Dispose();
                    return;
                }
                Complete(bitmap, relaid);
            });
        }, TaskScheduler.Default);
    }

    /// <summary>Ends a reserve at once, with no cross-fade (a thread switch, a window resize, detaching).</summary>
    public void FinishNow()
    {
        generation++;
        var pending = waiting;
        waiting = null;
        if (reserved is not null)
        {
            reserved = null;
            InvalidateMeasure();
        }
        StopFade();
        ClipToBounds = false;
        _ = pending; // the relaid action of an interrupted end is the caller's to run again
    }

    private void Complete(Bitmap? bitmap, Action? relaid)
    {
        waiting = null;
        generation++;
        reserved = null;
        StopFade();
        if (bitmap is not null && Bounds.Width > 0 && Bounds.Height > 0)
        {
            fading = bitmap;
            overlay.Source = bitmap;
            overlay.Width = Bounds.Width;
            overlay.Height = Bounds.Height;
            overlay.Opacity = 1;
            overlay.IsVisible = true;
            fadeElapsed = 0;
            RequestFrame();
        }
        else
        {
            bitmap?.Dispose();
            ClipToBounds = false;
        }
        InvalidateMeasure();
        relaid?.Invoke();
    }

    private void StopFade()
    {
        fadeElapsed = double.NaN;
        overlay.IsVisible = false;
        overlay.Source = null;
        fading?.Dispose();
        fading = null;
        if (reserved is null) ClipToBounds = false;
    }

    private void RequestFrame()
    {
        if (pendingFrame is not null) return;
        if (!FrameClock.IsBound)
        {
            // No frame clock (a bare test host): nothing can animate, so land at once.
            waiting?.Invoke();
            if (!double.IsNaN(fadeElapsed)) StopFade();
            return;
        }
        frameSource = FrameClock.Source.Current;
        pendingFrame = OnFrame;
        frameSource.RequestFrame(pendingFrame);
    }

    private void OnFrame(TimeSpan now)
    {
        pendingFrame = null;
        var dt = lastFrame is { } last ? Math.Clamp((now - last).TotalSeconds, 0, 0.034) : 1.0 / 60;
        lastFrame = now;
        if (waiting is { } fallback && ++waitedFrames >= SnapshotFrames)
        {
            fallback();
        }
        if (!double.IsNaN(fadeElapsed))
        {
            fadeElapsed += dt;
            var progress = Math.Min(1, fadeElapsed / CrossFadeSeconds);
            overlay.Opacity = 1 - Marrow.Core.Models.SplitterMath.EaseOut(progress);
            if (progress >= 1) StopFade();
        }
        if (waiting is not null || !double.IsNaN(fadeElapsed)) RequestFrame();
        else lastFrame = null;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = reserved ?? availableSize.Width;
        content?.Measure(new Size(width, availableSize.Height));
        overlay.Measure(availableSize);
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : content?.DesiredSize.Width ?? 0,
            double.IsFinite(availableSize.Height) ? availableSize.Height : content?.DesiredSize.Height ?? 0);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        content?.Arrange(new Rect(0, 0, reserved ?? finalSize.Width, finalSize.Height));
        overlay.Arrange(new Rect(0, 0, double.IsNaN(overlay.Width) ? 0 : overlay.Width, double.IsNaN(overlay.Height) ? 0 : overlay.Height));
        return finalSize;
    }

    // The compositor renders the slot's own visual on the render thread; the UI thread does not draw it again.
    private static Task<Bitmap?>? CompositorSnapshot(Visual visual, Size size)
    {
        if (size.Width <= 0 || size.Height <= 0) return null;
        if (ElementComposition.GetElementVisual(visual) is not { } element) return null;
        var scaling = TopLevel.GetTopLevel(visual)?.RenderScaling ?? 1;
        var task = element.Compositor.CreateCompositionVisualSnapshot(element, scaling);
        return task.ContinueWith(t => t.IsCompletedSuccessfully ? (Bitmap?)t.Result : null, TaskScheduler.Default);
    }
}
