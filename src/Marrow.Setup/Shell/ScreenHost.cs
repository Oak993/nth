using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.Setup.Motion;
using Marrow.Setup.Ui;

namespace Marrow.Setup.Shell;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3; the canvas's "Screen change" row and <c>.scr</c>; unit U1, ED-NEW-inst-u1-1). The
/// window's body: one screen at a time. On a change the old screen fades (0.34 s) and lifts 6 DIP (0.42 s) and the new one
/// rises 8 DIP and fades in, 60 ms later (0.38 s and 0.46 s), on <c>cubic-bezier(.2,.8,.2,1)</c>. A screen asked back while
/// it is still leaving turns around from where it is. Calm: no lift and no rise, fades of 0.2 s. A screen is laid out in
/// its final state before it fades in, so entering never reveals. Its height is the tallest any screen it has shown
/// needed (it only grows while the window is open, until text size or DPI changes), so the window never jumps smaller.
/// The screens a window can still reach are measured with the first one (<see cref="Probe"/>; VELOPACK_CHECK.md finding 11,
/// ED-NEW-inst-fix-7), so the window opens at the tallest of them and a taller screen arriving later does not grow it in one frame.
/// A screen taller than all of those (a Problem with a long reason) cannot be known before it arrives; the window then grows on
/// the same glide as every other move (<see cref="Reveal.Glide"/>: the canvas's spring, at most <see cref="Reveal.MaxGlideSpeed"/>
/// DIP a second), from the height it has with the speed it has, and a still taller screen turns the glide around where it is
/// (ED-NEW-inst-picks-1). The new screen keeps its own layout while the window opens to it; the screen leaving keeps the height
/// it had. A first layout, a text size or DPI change, Calm and <c>immediately</c> take the new height at once.
/// </summary>
public sealed class ScreenHost : Panel, IMotion
{
    public const double LeaveLift = -6, ArriveRise = 8, ArriveDelay = 0.06;
    public const double LeaveFade = 0.34, LeaveMove = 0.42, ArriveFade = 0.38, ArriveMove = 0.46, CalmFade = 0.2;

    private readonly Animator animator;
    private readonly Func<bool> calm;
    private readonly Dictionary<Control, ScreenMotion> motions = [];
    private readonly List<Control> probes = [];
    private Func<IEnumerable<Control>>? reachable;
    private bool probed;
    private double tallest;
    private double shown, velocity;
    private bool snap = true;

    public ScreenHost(Animator animator, Func<bool> calm)
    {
        this.animator = animator ?? throw new ArgumentNullException(nameof(animator));
        this.calm = calm ?? throw new ArgumentNullException(nameof(calm));
        ClipToBounds = false;
        LayoutUpdated += OnLayoutUpdated;
    }

    /// <summary>The height the body reports now, which glides up to <see cref="Tallest"/> (for tests).</summary>
    public double Shown => shown;

    /// <summary>The tallest height any screen shown or probed has needed (for tests).</summary>
    public double Tallest => tallest;

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
            if (probes.Remove(screen)) screen.ClearValue(AutomationProperties.AccessibilityViewProperty); // a probe is already a child
            else Children.Add(screen);
        }
        screen.IsHitTestVisible = true;
        if (immediately) snap = true;
        arriving.Arrive(calm(), immediately, fresh: !arriving.HasShown);
        if (!immediately) animator.Start(arriving);
        if (previous is not null && motions.TryGetValue(previous, out leaving) && !immediately) animator.Start(leaving);
        if (immediately && previous is not null) Remove(previous);
    }

    /// <summary>Lets the height shrink again (after a text size or DPI change); the reachable screens are measured again.</summary>
    public void ResetHeight()
    {
        tallest = 0;
        shown = velocity = 0;
        snap = true;
        AddProbes();
        InvalidateMeasure();
    }

    /// <summary>
    /// The screens this window can still show. Each one not shown is added for one layout pass, drawn at opacity 0, not
    /// hit-testable and hidden from Narrator, so the body measures it with the screen shown, and is taken away again once that
    /// pass is done. Asked again after <see cref="ResetHeight"/>, since text size and DPI change every height.
    /// </summary>
    public void Probe(Func<IEnumerable<Control>> screens)
    {
        reachable = screens ?? throw new ArgumentNullException(nameof(screens));
        AddProbes();
    }

    /// <summary>The screens measured as probes and not yet taken away (for tests).</summary>
    public IReadOnlyList<Control> Probes => probes;

    private void AddProbes()
    {
        if (reachable is null) return;
        foreach (var screen in reachable())
        {
            if (motions.ContainsKey(screen) || probes.Contains(screen) || screen.Parent is not null) continue;
            screen.Opacity = 0;
            screen.IsHitTestVisible = false;
            AutomationProperties.SetAccessibilityView(screen, AccessibilityView.Raw);
            probes.Add(screen);
            Children.Add(screen);
        }
        if (probes.Count == 0) return;
        probed = false;
        LayoutUpdated -= OnProbed;
        LayoutUpdated += OnProbed;
        InvalidateMeasure();
    }

    // The first layout pass, and the pass after a reset or an immediate show, take their height at once; later growth glides.
    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (snap && Bounds.Height > 0 && shown >= tallest - .01) snap = false;
    }

    bool IMotion.Advance(double seconds)
    {
        var offset = Math.Min(0, shown - tallest);
        var steps = Math.Max(1, (int)Math.Ceiling(seconds / Reveal.SubStep));
        var h = seconds / steps;
        for (var i = 0; i < steps; i++) Reveal.Glide(ref offset, ref velocity, h);
        if (Reveal.Rest(ref offset, ref velocity, 0, .01, .05) || offset > 0)
        {
            offset = 0;
            velocity = 0;
        }
        shown = tallest + offset;
        InvalidateMeasure();
        return offset < 0;
    }

    private void OnProbed(object? sender, EventArgs e)
    {
        if (!probed) return; // a layout pass that did not measure the probes yet
        LayoutUpdated -= OnProbed;
        foreach (var probe in probes.ToArray()) TakeProbe(probe);
    }

    private void TakeProbe(Control probe)
    {
        probes.Remove(probe);
        Children.Remove(probe);
        probe.ClearValue(AutomationProperties.AccessibilityViewProperty);
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
        probed |= probes.Count > 0;
        tallest = Math.Max(tallest, size.Height);
        if (snap || shown <= 0 || calm())
        {
            shown = tallest;
            velocity = 0;
        }
        else if (shown < tallest)
        {
            // a screen taller than any measured: the window opens to it on the glide, beginning at the height it is drawn at now
            if (velocity == 0) shown = Math.Max(shown, Math.Min(Bounds.Height, tallest));
            if (tallest - shown > .5) animator.Start(this);
            else
            {
                shown = tallest; // half a DIP is not worth a glide, and a measure that differs by a rounding must not leave the glide short of its end
                velocity = 0;
            }
        }
        return new Size(size.Width, shown);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            // A screen keeps its own layout while the window is still opening to it, so nothing in it moves; one that is leaving
            // keeps the height it had, so its lines do not stretch away while it fades.
            var height = Math.Max(finalSize.Height, child.DesiredSize.Height);
            if (child is Control control && motions.TryGetValue(control, out var motion))
            {
                if (motion.IsLeaving) height = Math.Max(motion.ArrangedHeight, child.DesiredSize.Height);
                else motion.ArrangedHeight = height;
            }
            child.Arrange(new Rect(0, 0, finalSize.Width, height));
        }
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

        /// <summary>True from the moment the screen is asked to leave until it is shown again.</summary>
        public bool IsLeaving => leaving;

        /// <summary>The height the screen was last arranged at while it was the one shown.</summary>
        public double ArrangedHeight { get; set; }

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
