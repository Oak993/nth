using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.Setup.Motion;

namespace Marrow.Setup.Shell;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3; the canvas's "Screen change" row and <c>.scr</c>; unit U1, ED-NEW-inst-u1-1). The
/// window's body: one screen at a time. On a change the old screen fades (0.34 s) and lifts 6 DIP (0.42 s) and the new one
/// rises 8 DIP and fades in, 60 ms later (0.38 s and 0.46 s), on <c>cubic-bezier(.2,.8,.2,1)</c>. A screen asked back while
/// it is still leaving turns around from where it is. Calm: no lift and no rise, fades of 0.2 s. A screen is laid out in
/// its final state before it fades in, so entering never reveals. Its height is the tallest any screen it has shown
/// needed (it only grows while the window is open, until text size or DPI changes), so the window never jumps smaller.
/// </summary>
public sealed class ScreenHost : Panel
{
    public const double LeaveLift = -6, ArriveRise = 8, ArriveDelay = 0.06;
    public const double LeaveFade = 0.34, LeaveMove = 0.42, ArriveFade = 0.38, ArriveMove = 0.46, CalmFade = 0.2;

    private readonly Animator animator;
    private readonly Func<bool> calm;
    private readonly Dictionary<Control, ScreenMotion> motions = [];
    private double tallest;

    public ScreenHost(Animator animator, Func<bool> calm)
    {
        this.animator = animator ?? throw new ArgumentNullException(nameof(animator));
        this.calm = calm ?? throw new ArgumentNullException(nameof(calm));
        ClipToBounds = false;
    }

    /// <summary>The screen shown (or arriving).</summary>
    public Control? Current { get; private set; }

    /// <summary>Raised once a screen has faded out and been taken away.</summary>
    public event Action<Control>? Left;

    /// <summary>Shows <paramref name="screen"/>: the current one leaves and this one arrives. <paramref name="immediately"/> skips the motion (the first screen fades in with the window).</summary>
    public void Show(Control screen, bool immediately = false)
    {
        ArgumentNullException.ThrowIfNull(screen);
        if (ReferenceEquals(screen, Current)) return;
        var previous = Current;
        Current = screen;
        if (previous is not null && motions.TryGetValue(previous, out var leaving)) leaving.Leave(calm(), immediately);
        if (!motions.TryGetValue(screen, out var arriving))
        {
            arriving = new ScreenMotion(this, screen);
            motions.Add(screen, arriving);
            Children.Add(screen);
        }
        screen.IsHitTestVisible = true;
        arriving.Arrive(calm(), immediately, fresh: !arriving.HasShown);
        if (!immediately) animator.Start(arriving);
        if (previous is not null && motions.TryGetValue(previous, out leaving) && !immediately) animator.Start(leaving);
        if (immediately && previous is not null) Remove(previous);
    }

    /// <summary>Lets the height shrink again (after a text size or DPI change).</summary>
    public void ResetHeight()
    {
        tallest = 0;
        InvalidateMeasure();
    }

    /// <summary>The drawn opacity and vertical offset of <paramref name="screen"/> (for tests).</summary>
    public (double Opacity, double Offset) Pose(Control screen) =>
        motions.TryGetValue(screen, out var motion) ? (motion.Opacity.Value, motion.Offset.Value) : (0, 0);

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = new Size();
        foreach (var child in Children)
        {
            child.Measure(availableSize);
            size = new Size(Math.Max(size.Width, child.DesiredSize.Width), Math.Max(size.Height, child.DesiredSize.Height));
        }
        tallest = Math.Max(tallest, size.Height);
        return new Size(size.Width, tallest);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children) child.Arrange(new Rect(finalSize));
        return finalSize;
    }

    private void Remove(Control screen)
    {
        motions.Remove(screen);
        Children.Remove(screen);
        Left?.Invoke(screen);
    }

    private sealed class ScreenMotion : IMotion
    {
        private readonly ScreenHost host;
        private readonly Control screen;
        private readonly TranslateTransform shift = new();
        private bool leaving;

        public ScreenMotion(ScreenHost host, Control screen)
        {
            this.host = host;
            this.screen = screen;
            screen.RenderTransform = shift;
            Opacity = new Tween(0);
            Offset = new Tween(ArriveRise);
        }

        public Tween Opacity { get; }

        public Tween Offset { get; }

        public bool HasShown { get; private set; }

        public void Arrive(bool calm, bool immediately, bool fresh)
        {
            leaving = false;
            HasShown = true;
            if (immediately)
            {
                Opacity.Snap(1);
                Offset.Snap(0);
            }
            else if (calm)
            {
                if (fresh) Offset.Snap(0);
                Opacity.Retarget(1, CalmFade, CubicBezier.Ease);
                Offset.Retarget(0, CalmFade, CubicBezier.Ease);
            }
            else
            {
                if (fresh) Offset.Snap(ArriveRise);
                Opacity.Retarget(1, ArriveFade, CubicBezier.ScreenChange, ArriveDelay);
                Offset.Retarget(0, ArriveMove, CubicBezier.ScreenChange, ArriveDelay);
            }
            Draw();
        }

        public void Leave(bool calm, bool immediately)
        {
            leaving = true;
            screen.IsHitTestVisible = false;
            if (immediately)
            {
                Opacity.Snap(0);
                return;
            }
            Opacity.Retarget(0, calm ? CalmFade : LeaveFade, calm ? CubicBezier.Ease : CubicBezier.ScreenChange);
            Offset.Retarget(calm ? Offset.Value : LeaveLift, calm ? CalmFade : LeaveMove, CubicBezier.ScreenChange);
        }

        public bool Advance(double seconds)
        {
            var moving = Opacity.Advance(seconds);
            moving |= Offset.Advance(seconds);
            Draw();
            if (!moving && leaving) host.Remove(screen);
            return moving;
        }

        private void Draw()
        {
            screen.Opacity = Math.Clamp(Opacity.Value, 0, 1);
            shift.Y = Offset.Value;
        }
    }
}
