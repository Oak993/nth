// PORT-ONLY, first-release UE-4 (OD-0100 place A, OD-0076). The bar under a running row: a 4 pt track with an accent fill. Marrow's download has a percentage (Velopack reports one), so its fill eases to it
// (.25 s linear); a CLI's own updater reports none, so its row shows a sheen gliding along the track (34 % of it, 1.25 s on cubic-bezier(.45,.05,.55,.95)) and no invented percentage. Under Reduce
// Motion the sheen stands still at 33 % (the design's reduced-motion rule). Driven by the port's frame clock; with none bound it is still.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

internal sealed class UpdatesProgressBar : Control
{
    public const double BarHeight = 4;
    private const double SheenWidth = .34;
    private const double SheenSeconds = 1.25;

    private static readonly Animation SheenCurve = Animation.TimingCurve(.45, .05, .55, .95, SheenSeconds);

    private readonly CzMotion motion;
    private readonly CzNumber fraction;
    private readonly Action<TimeSpan> frame;
    private bool indeterminate = true;
    private double target;
    private bool looping, attached, active;
    private IFrameSource? source;
    private ContinuousInstant epoch;

    public UpdatesProgressBar()
    {
        Height = BarHeight;
        IsHitTestVisible = false;
        motion = new CzMotion(this);
        fraction = new CzNumber(motion, 0, _ => InvalidateVisual());
        frame = Tick;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        AttachedToVisualTree += (_, _) => { attached = true; Restart(); };
        DetachedFromVisualTree += (_, _) => { attached = false; Stop(); };
    }

    /// <summary>True while the bar has no amount to show and so glides a sheen.</summary>
    public bool Indeterminate => indeterminate;

    /// <summary>The fill the bar is easing to (0 to 1) while it is not indeterminate.</summary>
    public double TargetFraction => target;

    /// <summary>The fill drawn now.</summary>
    public double Fraction => fraction.Value;

    /// <summary>True while the sheen is being driven by the frame clock (false when still, detached, or showing an amount).</summary>
    public bool Moves => looping && !motion.Still;

    /// <summary>Whether the bar is on show. A bar folded shut does not ask for frames.</summary>
    public bool Active
    {
        get => active;
        set
        {
            if (active == value) return;
            active = value;
            Restart();
        }
    }

    /// <summary>The amount, or null when the updater reports none.</summary>
    public void Set(double? progress)
    {
        if (progress is null)
        {
            if (indeterminate) return;
            indeterminate = true;
            Restart();
            InvalidateVisual();
            return;
        }
        var clamped = Math.Clamp(progress.Value, 0, 1);
        var was = indeterminate;
        indeterminate = false;
        target = clamped;
        Stop();
        if (was) fraction.Jump(0);
        fraction.To(clamped, Animation.TimingCurve(0, 0, 1, 1, .25));
        InvalidateVisual();
    }

    private void Restart()
    {
        Stop();
        if (!attached || !active || !indeterminate || motion.Still) return;
        source = FrameClock.Source.Current;
        epoch = SwiftClock.Now;
        looping = true;
        source.RequestFrame(frame);
    }

    private void Stop()
    {
        if (looping) source?.CancelFrame(frame);
        looping = false;
    }

    private void Tick(TimeSpan _)
    {
        if (!looping) return;
        if (motion.Still)
        {
            looping = false;
            InvalidateVisual();
            return;
        }
        InvalidateVisual();
        source!.RequestFrame(frame);
    }

    public override void Render(DrawingContext context)
    {
        var theme = ActualThemeVariant;
        var rect = new Rect(Bounds.Size);
        if (rect.Width <= 0) return;
        context.DrawRectangle(new SolidColorBrush(Chrome.Overlay(.10).Resolve(theme)), null, rect, BarHeight / 2, BarHeight / 2);
        var accent = new SolidColorBrush(Chrome.Accent.Resolve(theme));
        using var clip = context.PushClip(new RoundedRect(rect, BarHeight / 2));
        if (!indeterminate)
        {
            context.DrawRectangle(accent, null, new Rect(0, 0, rect.Width * fraction.Value, rect.Height), BarHeight / 2, BarHeight / 2);
            return;
        }
        var left = looping
            ? (-SheenWidth + (SheenWidth + 1) * SheenCurve.Progress(((SwiftClock.Now - epoch).TotalSeconds) % SheenSeconds)) * rect.Width
            : .33 * rect.Width;
        context.DrawRectangle(accent, null, new Rect(left, 0, SheenWidth * rect.Width, rect.Height), BarHeight / 2, BarHeight / 2);
    }
}
