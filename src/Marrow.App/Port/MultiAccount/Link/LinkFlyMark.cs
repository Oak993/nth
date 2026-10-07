// PORT-ONLY: locked flyMark :2741-2749, 22px mark, exact duration/curve. RM uses 260ms opacity, no transform.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Port.Skills.Pages;
using Marrow.Core.Port;
using Marrow.UI.Port;
namespace Marrow.App.Port.MultiAccount.Link;
internal sealed class LinkFlyMark : Decorator
{
    private readonly Action complete;
    private readonly Action cancel;
    private readonly Point start, end;
    private ContinuousInstant origin;
    private Action<TimeSpan>? frame;
    private IFrameSource? frames;
    internal Point FromPosition => start;
    internal Point ToPosition => end;
    internal bool Running { get; private set; }
    internal bool Crossfade { get; }
    internal double DurationSeconds => Crossfade ? .260 : .560;
    internal (double, double, double, double) Curve => (.45, 0, .2, 1);
    internal LinkFlyMark(string mono, Color hue, Point start, Point end, bool still, Action complete, Action cancel)
    {
        this.start = start; this.end = end; Crossfade = still; this.complete = complete; this.cancel = cancel;
        Width = Height = 22; IsHitTestVisible = false; HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left; VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        Child = new Border { Padding = new Thickness(3), Background = new SolidColorBrush(Color.FromArgb(66, hue.R, hue.G, hue.B)), BorderBrush = new SolidColorBrush(Color.FromArgb(179, hue.R, hue.G, hue.B)), BorderThickness = new Thickness(1), Child = LibraryUi.Text(mono, 9.5, weight: SwiftFont.Weight.Bold) }.RoundedRectangle(11, RoundedCornerStyle.Continuous);
        DetachedFromVisualTree += (_, _) => Stop();
    }
    internal void Start()
    {
        Running = true; origin = SwiftClock.Now; Apply(0);
        if (!FrameClock.IsBound) { Finish(); return; }
        frames = FrameClock.Source.Current; frame = Tick; frames.RequestFrame(frame);
    }
    private void Tick(TimeSpan _)
    {
        if (!Running) return;
        var seconds = (SwiftClock.Now - origin).TotalSeconds;
        if (seconds >= DurationSeconds) { Finish(); return; }
        var curve = Crossfade ? Animation.TimingCurve(.25, .1, .25, 1, duration: DurationSeconds) : Animation.TimingCurve(.45, 0, .2, 1, duration: DurationSeconds);
        Apply(curve.Progress(seconds)); frames!.RequestFrame(frame!);
    }
    private void Apply(double p)
    {
        if (Crossfade) { Margin = new Thickness(start.X, start.Y, 0, 0); RenderTransform = null; Opacity = 1 - p; return; }
        var mid = new Point((start.X + end.X) / 2, Math.Min(start.Y, end.Y) - 30);
        var q = p < .5 ? p * 2 : (p - .5) * 2; var a = p < .5 ? start : mid; var b = p < .5 ? mid : end;
        var scale = p < .5 ? 1 + .1 * q : 1.1 - .3 * q;
        RenderTransform = new TransformGroup { Children = { new ScaleTransform(scale, scale), new TranslateTransform(a.X + (b.X - a.X) * q, a.Y + (b.Y - a.Y) * q) } };
        Opacity = p < .5 ? 1 : 1 - .6 * q;
    }
    private void Finish() { Running = false; frame = null; Apply(1); complete(); }
    internal void Stop()
    {
        if (!Running) return; Running = false;
        if (frame is { } tick) frames?.CancelFrame(tick); frame = null; cancel();
    }
}
