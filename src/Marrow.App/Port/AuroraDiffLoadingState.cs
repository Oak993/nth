using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;

namespace Marrow.App.Port;

// PORT-ONLY: OD-0113 loader 5; ED-097 delay (nothing shows for 400 ms). OD-0136 amendment 2, step 5: the sheen loader is replaced by the diff
// skeleton (bars in the shape of a diff, the light across them as one band) and the header label has the light through its words; the hand-over fades
// both over 0.2 s (the skeleton's Finish). Only the label's brief fades use the UI frame clock; the skeleton's motion stays on the render thread.
public sealed class AuroraDiffLoadingState : StackPanel
{
    private readonly Action<TimeSpan> frame;
    private TimeProvider? clock;
    private IFrameSource? source;
    private long started;
    private long? finished;
    private bool shown;
    private readonly Panel place;
    private Action? removed;
    private bool pending;

    public AuroraDiffLoadingState()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Center;
        Spacing = 8;
        // The skeleton's place keeps the loader's: 88 tall, stretched, 10 in at the sides; the skeleton itself comes into it after the 400 ms wait, so its rows fade in then.
        Skeleton = new(NthSkeletonKind.Diff);
        place = new Panel { Height = 88, MinWidth = 44, Margin = new Thickness(10, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
        Label = new() { Text = "Reading the diff\u2026", HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0, IsShining = true };
        Label.Foreground = Chrome.SecondaryText.Brush(Label);
        Children.Add(place); Children.Add(Label);
        frame = Tick;
        AttachedToVisualTree += (_, _) =>
        {
            if (clock is null) { clock = SwiftClock.Provider.Current; started = clock.GetTimestamp(); }
            source = FrameClock.Source.Current; Update();
        };
        DetachedFromVisualTree += (_, _) => { if (pending) source?.CancelFrame(frame); pending = false; };
    }

    public NthSkeleton Skeleton { get; }
    public NthShineText Label { get; }

    /// <summary>How long the hand-over takes: the label fades and the skeleton fades out (NthSkeleton.Finish), 0.2 s.</summary>
    public const double ExitSeconds = .2;

    public void Complete(Action remove)
    {
        if (clock is null) { remove(); return; }
        if (finished is not null) return;
        finished = clock.GetTimestamp(); removed = remove; if (shown) Skeleton.Finish(); Update();
    }

    private void Tick(TimeSpan _) { pending = false; Update(); }
    private void Update()
    {
        if (clock is null) return;
        var elapsed = clock.GetElapsedTime(started).TotalSeconds;
        var exit = finished is { } end ? clock.GetElapsedTime(end).TotalSeconds : 0;
        if (!shown && finished is null && elapsed >= .4) { shown = true; place.Children.Add(Skeleton); }
        Label.Opacity = AuroraLoaderMotion.Visibility(elapsed) * (finished is null ? 1 : 1 - AuroraLoaderMotion.Smooth(exit / ExitSeconds));
        if (finished is not null && exit >= ExitSeconds)
        {
            var remove = removed; removed = null; remove?.Invoke(); return;
        }
        if (!pending && (finished is not null || elapsed < .6)) { pending = true; source!.RequestFrame(frame); }
    }
}
