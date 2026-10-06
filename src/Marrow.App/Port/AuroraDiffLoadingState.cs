using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Port;

// PORT-ONLY: OD-0113 loader 5; ED-097 delay and the locked page's 200/150 ms entry/exit.
// Only the label's brief fades use the UI frame clock; continuous sheen motion stays on the compositor.
public sealed class AuroraDiffLoadingState : StackPanel
{
    private readonly Action<TimeSpan> frame;
    private TimeProvider? clock;
    private IFrameSource? source;
    private long started;
    private long? finished;
    private Action? removed;
    private bool pending;

    public AuroraDiffLoadingState()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Center;
        Spacing = 8;
        Loader = new(AuroraLoaderKind.AuroraSheen) { MinWidth = 44, Margin = new Thickness(10, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
        Label = new() { Text = "Reading the diff\u2026", HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0 };
        Children.Add(Loader); Children.Add(Label);
        frame = Tick;
        AttachedToVisualTree += (_, _) =>
        {
            if (clock is null) { clock = SwiftClock.Provider.Current; started = clock.GetTimestamp(); Loader.BeginWait(); }
            source = FrameClock.Source.Current; Update();
        };
        DetachedFromVisualTree += (_, _) => { if (pending) source?.CancelFrame(frame); pending = false; };
    }

    public AuroraLoader Loader { get; }
    public TextBlock Label { get; }

    public void Complete(Action remove)
    {
        if (clock is null) { remove(); return; }
        if (finished is not null) return;
        finished = clock.GetTimestamp(); removed = remove; Loader.Complete(); Update();
    }

    private void Tick(TimeSpan _) { pending = false; Update(); }
    private void Update()
    {
        if (clock is null) return;
        var elapsed = clock.GetElapsedTime(started).TotalSeconds;
        var exit = finished is { } end ? clock.GetElapsedTime(end).TotalSeconds : 0;
        Label.Opacity = AuroraLoaderMotion.Visibility(elapsed) * (finished is null ? 1 : 1 - AuroraLoaderMotion.Smooth(exit / .15));
        if (finished is not null && exit >= .15)
        {
            var remove = removed; removed = null; remove?.Invoke(); return;
        }
        if (!pending && (finished is not null || elapsed < .6)) { pending = true; source!.RequestFrame(frame); }
    }
}
