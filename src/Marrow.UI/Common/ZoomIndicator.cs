// PORT-ONLY (ED-NEW-textsize-3, owner 2026-10-11: "zoom in and out must also be smooth"): a small glass capsule that tells the
// zoom while it changes ("110%"). It fades in at a change, counts with the scale as it moves, holds a moment once the scale
// rests, and fades out. It is drawn over the window at the app's own 100 % (it does not grow and shrink with the content
// under it) and never takes a click. Reduce Motion: the scale changes at once and the capsule shows and hides without a fade.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.UI.Common;

/// <summary>The zoom's indicator over a window root (ED-NEW-textsize-3).</summary>
public sealed class ZoomIndicator : Border
{
    /// <summary>How long it stays once the scale rests, before it fades out.</summary>
    public static readonly TimeSpan Hold = TimeSpan.FromSeconds(0.9);

    /// <summary>The fade in at a change.</summary>
    public static Animation FadeIn { get; } = Animation.EaseOut(0.15);

    /// <summary>The fade out after the hold.</summary>
    public static Animation FadeOut { get; } = Animation.EaseInOut(0.3);

    /// <summary>The capsule's distance below the window's top edge, in the app's points: under the toolbar.</summary>
    public const double TopInset = 46;

    private readonly TextBlock label;
    private readonly AnimatedScalar fade;
    private readonly Func<double, int> percentOf;
    private readonly Action<TimeSpan> hold;
    private IFrameSource? source;
    private Action<TimeSpan>? holding;
    private ContinuousInstant hideAt;

    /// <param name="motion">The window root's zoom motion.</param>
    /// <param name="percentOf">The zoom level, in percent, a scale is drawn at.</param>
    public ZoomIndicator(ZoomMotion motion, Func<double, int> percentOf)
    {
        this.percentOf = percentOf;
        hold = HoldFrame;
        IsHitTestVisible = false;
        Focusable = false;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(0, TopInset, 0, 0);
        MinWidth = 64;
        Height = 30;
        Padding = new Thickness(14, 0);
        label = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            FontFeatures = FontFeatureCollection.Parse("tnum"), // the digits keep their width as they count
        };
        SwiftFont.System(13, SwiftFont.Weight.Semibold).Apply(label);
        label.Foreground = Chrome.Chrome.PrimaryText.Brush(label);
        this.Capsule(RoundedCornerStyle.Continuous);
        Background = Brushes.Transparent;
        SwiftGlass.SetEffect(this, GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous)));
        IsVisible = false;
        fade = new AnimatedScalar(this, 0, value =>
        {
            Opacity = value;
            // The words hang in the capsule only while it shows: hidden, it holds no text to lay out or to draw.
            Child = value > 0 ? label : null;
            IsVisible = value > 0;
        });
        Show(motion.Value);
        motion.Retargeted += _ =>
        {
            StopHold();
            fade.Set(1, FadeIn);
        };
        motion.Moved += Show;
        motion.Rested += _ => HoldThenHide();
        DetachedFromVisualTree += (_, _) => StopHold();
    }

    /// <summary>The words shown ("110%").</summary>
    public string Text => label.Text ?? "";

    private void Show(double scale) => label.Text = percentOf(scale) + "%";

    private void HoldThenHide()
    {
        StopHold();
        if (!FrameClock.IsBound)
        {
            fade.Set(0, null);
            return;
        }
        hideAt = SwiftClock.Now + Hold;
        source = FrameClock.Source.Current;
        holding = hold;
        source.RequestFrame(hold);
    }

    private void HoldFrame(TimeSpan _)
    {
        holding = null;
        if (SwiftClock.Now < hideAt)
        {
            holding = hold;
            source!.RequestFrame(hold);
            return;
        }
        fade.Set(0, FadeOut);
    }

    private void StopHold()
    {
        if (holding is { } frame) source?.CancelFrame(frame);
        holding = null;
    }
}
