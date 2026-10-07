// PORT-ONLY: locked .lkj :649-652, gradient with 2px bloom and 2.4s opacity pulse, still .5 under RM.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.Core.Port;
using Marrow.UI.Port;
namespace Marrow.App.Port.MultiAccount.Link;
internal sealed class LinkBridge : Decorator
{
    private readonly Border glow;
    private ContinuousInstant origin;
    private IFrameSource? frames;
    private Action<TimeSpan>? frame;
    internal LinkBridge(Color outgoing, Color incoming)
    {
        Width = 14; Height = 1.5; Margin = new Thickness(3, 0, 3, 0); VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center; IsHitTestVisible = false;
        var gradient = new LinearGradientBrush { StartPoint = new RelativePoint(0, .5, RelativeUnit.Relative), EndPoint = new RelativePoint(1, .5, RelativeUnit.Relative), GradientStops = [new(outgoing, 0), new(incoming, 1)] };
        glow = new Border { Background = gradient, Effect = new BlurEffect { Radius = 2 }, Opacity = .5 }.RoundedRectangle(2, RoundedCornerStyle.Continuous);
        Child = new Panel { ClipToBounds = false, Children = { glow, new Border { Background = gradient }.RoundedRectangle(1, RoundedCornerStyle.Continuous) } };
        AttachedToVisualTree += (_, _) => { origin = SwiftClock.Now; Tick(default); };
        DetachedFromVisualTree += (_, _) => { if (frame is not null) frames?.CancelFrame(frame); frame = null; };
    }
    private void Tick(TimeSpan _)
    {
        frame = null;
        if (!FrameClock.IsBound || CapsuleMotion.Reduce(this)) { glow.Opacity = .5; return; }
        var seconds = (SwiftClock.Now - origin).TotalSeconds % 2.4;
        var half = seconds < 1.2 ? seconds : 2.4 - seconds;
        var p = Animation.TimingCurve(.42, 0, .58, 1, duration: 1.2).Progress(half);
        glow.Opacity = .25 + .65 * p;
        frames = FrameClock.Source.Current; frame = Tick; frames.RequestFrame(frame);
    }
}
