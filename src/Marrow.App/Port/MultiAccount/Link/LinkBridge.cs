// PORT-ONLY: locked .lkj :649-652, gradient with 2px bloom and 2.4s opacity pulse, still .5 under RM.
// ED-NEW-perf-idle-3: the pulse is a composition key-frame animation the compositor plays by itself (opacity .25 to .9 and back, each
// leg 1.2 s along cubic-bezier(.42, 0, .58, 1)), not a frame request at every display refresh for as long as the bridge is on screen.
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.Core.Port;
using Marrow.UI.Port;
namespace Marrow.App.Port.MultiAccount.Link;
internal sealed class LinkBridge : Decorator
{
    internal const double Rest = .5, Low = .25, High = .9;
    internal static readonly TimeSpan Cycle = TimeSpan.FromSeconds(2.4);
    /// <summary>The cycle's key frames: low at the start, high at half way, low again at the end.</summary>
    internal static readonly IReadOnlyList<OpacityKey> Keys = [new(0f, (float)Low), new(.5f, (float)High), new(1f, (float)Low)];
    private static readonly Easing Ease = new SplineEasing(.42, 0, .58, 1);

    private readonly Border glow;
    private readonly OpacityLoopRunner pulse;
    internal LinkBridge(Color outgoing, Color incoming)
    {
        Width = 14; Height = 1.5; Margin = new Thickness(3, 0, 3, 0); VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center; IsHitTestVisible = false;
        var gradient = new LinearGradientBrush { StartPoint = new RelativePoint(0, .5, RelativeUnit.Relative), EndPoint = new RelativePoint(1, .5, RelativeUnit.Relative), GradientStops = [new(outgoing, 0), new(incoming, 1)] };
        glow = new Border { Background = gradient, Effect = new BlurEffect { Radius = 2 }, Opacity = Rest }.RoundedRectangle(2, RoundedCornerStyle.Continuous);
        pulse = new OpacityLoopRunner(glow, Cycle, Keys, Ease, Rest);
        Child = new Panel { ClipToBounds = false, Children = { glow, new Border { Background = gradient }.RoundedRectangle(1, RoundedCornerStyle.Continuous) } };
        AttachedToVisualTree += (_, _) => Apply();
        DetachedFromVisualTree += (_, _) => pulse.Stop();
        PropertyChanged += (_, e) => { if (e.Property == EnvironmentValues.AccessibilityReduceMotionProperty && VisualRoot is not null) Apply(); };
    }
    /// <summary>The pulse is running (tests).</summary>
    internal bool IsPulsing => pulse.Running;
    // Reduce Motion (or no frame clock): still at .5. Otherwise the compositor plays the cycle from the start.
    private void Apply()
    {
        pulse.Stop();
        glow.Opacity = Rest;
        if (FrameClock.IsBound && !CapsuleMotion.Reduce(this)) pulse.Start();
    }
}
