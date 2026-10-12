// PORT-ONLY: web surfaces W16a (DESIGN.md 7.4 "A new node fades in (opacity 0 → 1, 6 DIP rise, 180 ms) and its container
// height springs open. Earlier nodes never move except by growth below them"; 7.5 calm cards; 10 Reduce Motion;
// ED-NEW-web-w16a-1). No Swift counterpart. Every drawn node sits in a slot. The slot owns the node's motion: its opening
// (a spring from 0 to 1 that scales the height it reports, so the container it sits in grows smoothly and nothing above it
// moves), its fade and rise (opacity and a render transform only, never layout), its `when` hiding and showing, and the
// height its content grows to later (streamed text, a tab swap), which follows by spring unless the width changed (a resize
// tracks the pointer 1:1). A card's tint, rim and drop are drawn by its slot at the height shown, so the card's rounded
// edge grows with it and its content is clipped inside the padding, never over the edge.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;

namespace Marrow.UI.NthUi;

/// <summary>How a slot's reported height follows its content.</summary>
public enum NthUiSlotMode
{
    /// <summary>The content's height at once: a container whose own children animate their arrival.</summary>
    Exact,

    /// <summary>A spring toward the content's height: a leaf whose content grows (streamed text, a body, a flow of chips).</summary>
    Follow,
}

/// <summary>Which size the slot's opening scales.</summary>
public enum NthUiReveal
{
    /// <summary>In a column: the height opens, so the column grows downward.</summary>
    Vertical,

    /// <summary>In a row: the width opens when shown or hidden by `when` (an arrival at the end only fades).</summary>
    Horizontal,
}

/// <summary>A tinted surface a slot draws under its content: a card, a callout, a code well.</summary>
public sealed record NthUiSurface(SwiftColor Fill, SwiftColor? Rim, double Radius, bool Shadow, double ClipInset);

/// <summary>The place of one node in a live view, and its motion.</summary>
public sealed class NthUiSlot : Control, INthUiAnimated
{
    private readonly NthUiMotion motion;
    private readonly TranslateTransform shift = new();
    private Control? child;
    private NthSpring open = NthSpring.At(1);
    private NthSpring follow = NthSpring.At(0);
    private NthUiTween fade = NthUiTween.At(1);
    private bool following;
    private bool springNext;
    private bool rising;
    private bool jump = true;
    private bool shown = true;
    private bool held;
    private double lastWidth = double.NaN;
    private double full;
    private double fullWidth;
    private bool sizing;
    private NthUiSurface? surface;

    internal NthUiSlot(NthUiMotion motion)
    {
        this.motion = motion;
        RenderTransform = shift;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>The key of the node it holds.</summary>
    public string Key { get; internal set; } = "";

    public Control? Child
    {
        get => child;
        set
        {
            if (ReferenceEquals(child, value)) return;
            if (child is not null)
            {
                LogicalChildren.Remove(child);
                VisualChildren.Remove(child);
            }
            child = value;
            if (value is not null)
            {
                value.IsVisible = shown || !open.Settled(NthUiMotion.Now);
                LogicalChildren.Add(value);
                VisualChildren.Add(value);
            }
            InvalidateMeasure();
        }
    }

    public NthUiSlotMode Mode { get; set; } = NthUiSlotMode.Follow;

    public NthUiReveal Reveal { get; set; } = NthUiReveal.Vertical;

    public NthUiSurface? Surface
    {
        get => surface;
        set
        {
            surface = value;
            InvalidateVisual();
        }
    }

    /// <summary>Whether `when` lets it show (its target, not its motion).</summary>
    public bool IsShown => shown;

    /// <summary>Held back until its row closes (a centred or spread row; DESIGN 7.4 "no earlier node moves").</summary>
    public bool IsHeld => held;

    /// <summary>The opening (0 closed, 1 open) at the last measure; a column scales the gap above it by this.</summary>
    public double Openness { get; private set; } = 1;

    /// <summary>The content's own height at the last measure.</summary>
    public double ContentHeight => full;

    private bool Calm => NthUiCalm.For(this);

    /// <summary>The node has just been placed: open from nothing and fade in with a rise, or stay closed while `when` is false.</summary>
    internal void Arrive(bool visible, bool revealSize, bool hold = false)
    {
        shown = visible;
        held = hold;
        if (child is not null) child.IsVisible = visible;
        if (!visible)
        {
            open = NthSpring.At(0);
            fade = NthUiTween.At(0);
            Settle();
            return;
        }
        if (!motion.CanAnimate)
        {
            open = NthSpring.At(1);
            fade = NthUiTween.At(hold ? 0 : 1);
            Settle();
            return;
        }
        var now = NthUiMotion.Now;
        var calm = Calm;
        open = revealSize ? new NthSpring(0, 0, 1, now, calm) : NthSpring.At(1);
        fade = hold ? NthUiTween.At(0) : NthUiTween.At(0).Retarget(1, now, calm ? NthUiTokens.CalmFadeSeconds : NthUiTokens.FadeSeconds, NthEase.Out);
        rising = !calm && !hold;
        Paint(now);
        sizing = true;
        motion.Start(this);
        InvalidateMeasure();
    }

    /// <summary>A held node is let go: it fades in where it already sits.</summary>
    internal void Release()
    {
        if (!held) return;
        held = false;
        if (!shown) return;
        if (!motion.CanAnimate)
        {
            fade = NthUiTween.At(1);
            Settle();
            return;
        }
        var now = NthUiMotion.Now;
        var calm = Calm;
        fade = fade.Retarget(1, now, calm ? NthUiTokens.CalmFadeSeconds : NthUiTokens.FadeSeconds, NthEase.Out);
        rising = !calm;
        Paint(now);
        motion.Start(this);
    }

    /// <summary>`when` changed: open and fade in, or close and fade out; interruptible from wherever it is.</summary>
    internal void SetShown(bool visible)
    {
        if (visible == shown) return;
        shown = visible;
        if (child is not null && visible) child.IsVisible = true;
        jump = true;
        if (!motion.CanAnimate)
        {
            open = NthSpring.At(visible ? 1 : 0);
            fade = NthUiTween.At(visible && !held ? 1 : 0);
            Settle();
            InvalidateMeasure();
            return;
        }
        var now = NthUiMotion.Now;
        var calm = Calm;
        open = open.Retarget(visible ? 1 : 0, now, calm);
        if (!held) fade = fade.Retarget(visible ? 1 : 0, now, calm ? NthUiTokens.CalmFadeSeconds : NthUiTokens.FadeSeconds, NthEase.Out);
        rising = visible && !calm && !held;
        Paint(now);
        motion.Start(this);
        InvalidateMeasure();
    }

    /// <summary>The next change of the content's height springs even in <see cref="NthUiSlotMode.Exact"/> (a tab or step swap).</summary>
    internal void AnimateNextChange() => springNext = true;

    /// <summary>Shows <paramref name="next"/> in place of the current child (a tab or step swap): the height springs to the
    /// new content and the new content fades in where it sits, without a rise.</summary>
    internal void Swap(Control? next)
    {
        if (ReferenceEquals(child, next)) return;
        springNext = true;
        Child = next;
        if (next is not null) next.IsVisible = true;
        if (!motion.CanAnimate || !shown || held)
        {
            springNext = false;
            jump = true;
            return;
        }
        var now = NthUiMotion.Now;
        fade = NthUiTween.At(0).Retarget(1, now, NthUiTokens.CalmFadeSeconds, NthEase.Out);
        rising = false;
        Paint(now);
        motion.Start(this);
    }

    bool INthUiAnimated.Step(double now)
    {
        Paint(now);
        var resizing = !open.Settled(now) || (following && !follow.Settled(now));
        var moving = resizing || !fade.Settled(now);
        if (following && follow.Settled(now)) following = false;
        if (resizing || sizing) InvalidateMeasure();
        sizing = resizing;
        if (!moving) Settle();
        return moving;
    }

    private void Paint(double now)
    {
        var f = held ? 0 : Math.Clamp(fade.Value(now), 0, 1);
        Opacity = f;
        shift.Y = rising && f < 1 ? NthUiTokens.Rise * (1 - f) : 0;
        IsHitTestVisible = shown && !held;
    }

    private void Settle()
    {
        rising = false;
        shift.Y = 0;
        Opacity = held ? 0 : fade.Target;
        IsHitTestVisible = shown && !held;
        if (!shown && child is not null && child.IsVisible)
        {
            child.IsVisible = false;
            jump = true;
            InvalidateMeasure();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (child is null)
        {
            Openness = Math.Clamp(open.Value(NthUiMotion.Now), 0, 1);
            return default;
        }
        var now = NthUiMotion.Now;
        child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        var desired = child.DesiredSize;
        var widthChanged = !double.IsNaN(lastWidth) && Math.Abs(availableSize.Width - lastWidth) > .01;
        lastWidth = availableSize.Width;
        fullWidth = desired.Width;
        full = desired.Height;
        double height;
        if (Mode == NthUiSlotMode.Exact && !springNext && !following)
        {
            follow = NthSpring.At(full);
            height = full;
            jump = false;
        }
        else if (jump || widthChanged || !motion.CanAnimate)
        {
            follow = NthSpring.At(full);
            following = false;
            jump = false;
            springNext = false;
            height = full;
        }
        else
        {
            if (Math.Abs(follow.Target - full) > .01)
            {
                follow = follow.Retarget(full, now, Calm);
                following = true;
                springNext = false;
                motion.Start(this);
            }
            else if (!following) springNext = false;
            height = following ? Math.Max(0, follow.Value(now)) : full;
        }
        var o = Math.Clamp(open.Value(now), 0, 1);
        Openness = o;
        return Reveal == NthUiReveal.Vertical ? new Size(desired.Width, height * o) : new Size(desired.Width * o, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (child is null) return finalSize;
        var width = Reveal == NthUiReveal.Horizontal ? Math.Max(fullWidth, finalSize.Width) : finalSize.Width;
        child.Arrange(new Rect(0, 0, width, full));
        var clipped = finalSize.Height < full - .25 || finalSize.Width < width - .25;
        if (clipped)
        {
            var inset = Surface?.ClipInset ?? 0;
            var height = Math.Max(0, finalSize.Height - (finalSize.Height < full - .25 ? inset : 0));
            child.Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, finalSize.Width), height));
        }
        else child.Clip = null;
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        if (Surface is not { } surface || Bounds.Width <= 0 || Bounds.Height <= .5) return;
        var theme = ActualThemeVariant;
        var rect = new Rect(Bounds.Size);
        var radius = Math.Min(surface.Radius, rect.Height / 2);
        var fill = new ImmutableSolidColorBrush(surface.Fill.Resolve(theme));
        var shadows = surface.Shadow ? NthUiTokens.CardShadows(NthUiTokens.CardShadow.Resolve(theme)) : default;
        context.DrawRectangle(fill, null, new RoundedRect(rect, radius), shadows);
        if (surface.Rim is { } rim)
        {
            var edge = rect.Deflate(.5);
            context.DrawRectangle(null, new ImmutablePen(new ImmutableSolidColorBrush(rim.Resolve(theme)), 1), new RoundedRect(edge, Math.Max(0, radius - .5)));
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        motion.Remove(this);
        open = NthSpring.At(open.Target);
        fade = NthUiTween.At(fade.Target);
        following = false;
        Settle();
    }
}
