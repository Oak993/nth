using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

// PORT-ONLY (OD-0088, OD-0076, ED-254): the Settings window turns over when the theme's appearance changes. The locked
// board (accent-d3-locked.dc.html, SPEC 6.5, scenarios 08 and 09) eases every window colour over 0.32 s, CSS `ease`, per
// channel in sRGB premultiplied with alpha; the port's appearance is an Avalonia theme variant, which changes in one
// frame. A tree marked here eases instead: each SwiftColor brush inside it (SwiftColor.Brush) runs from the colour it
// shows to the new appearance's on the frame clock, and the parts that draw themselves follow Progress. Reduce Motion, or
// no frame clock, keeps the change at once.

/// <summary>The appearance turnover of a window's tree (SPEC 6.5).</summary>
public static class AppearanceTurn
{
    /// <summary>The curve and length of the turnover: 0.32 s, CSS <c>ease</c>.</summary>
    public static Animation Curve { get; } = Animation.TimingCurve(0.25, 0.1, 0.25, 1, duration: 0.32);

    /// <summary>Whether an appearance change inside this tree eases (inherited).</summary>
    public static readonly AttachedProperty<bool> TurnsOverProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("TurnsOver", typeof(AppearanceTurn), inherits: true);

    public static bool GetTurnsOver(Control control) => control.GetValue(TurnsOverProperty);

    /// <summary>Marks a tree; a window so marked turns over only once it has drawn a frame since it was last shown.</summary>
    public static void SetTurnsOver(Control control, bool value)
    {
        control.SetValue(TurnsOverProperty, value);
        if (value && control is TopLevel top && !Painted.TryGetValue(top, out _))
        {
            Watch(top);
            // ED-253: the text and glyphs that set no colour of their own draw in labelColor (PortControlThemes'
            // TopLevel default, a theme resource that changes in one frame); a marked window holds it as a SwiftColor brush,
            // which eases with the rest.
            top.SetValue(Avalonia.Controls.Documents.TextElement.ForegroundProperty, SwiftColor.LabelColor.Brush(top));
        }
    }

    /// <summary>
    /// Whether a change on <paramref name="control"/> runs as a transition now: in a marked tree, with a frame clock and
    /// without Reduce Motion, for a control in a window (a control being attached takes its tree's appearance before it
    /// reaches the window, at once), and in a marked window that is showing a frame already drawn (a window not yet drawn,
    /// or hidden, takes the new appearance at once: there is nothing on screen to turn over).
    /// </summary>
    public static bool Eases(Control control)
    {
        if (!GetTurnsOver(control) || !FrameClock.IsBound || EnvironmentValues.GetAccessibilityReduceMotion(control)) return false;
        return TopLevel.GetTopLevel(control) is { } top && (!Painted.TryGetValue(top, out var painted) || painted.Value);
    }

    // Each marked window: whether it has drawn a frame since it was last shown.
    private static readonly ConditionalWeakTable<TopLevel, StrongBox<bool>> Painted = new();

    private static void Watch(TopLevel top)
    {
        var painted = new StrongBox<bool>();
        Painted.Add(top, painted);
        void Arm()
        {
            painted.Value = false;
            if (top.IsVisible) top.RequestAnimationFrame(_ => painted.Value = top.IsVisible);
        }
        top.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.IsVisibleProperty) Arm();
        };
        Arm();
    }

    /// <summary>
    /// Runs <paramref name="apply"/> with the turnover's progress, 0 to 1, on each frame from now until it completes (at
    /// once with 1 when the control does not ease). The returned action stops it.
    /// </summary>
    public static Action Run(Control control, Action<double> apply)
    {
        if (!Eases(control))
        {
            apply(1);
            return () => { };
        }
        var start = SwiftClock.Now;
        var span = TimeSpan.FromSeconds(Curve.Duration);
        var source = FrameClock.Source.Current;
        var stopped = false;
        Action<TimeSpan>? tick = null;
        tick = _ =>
        {
            if (stopped) return;
            var now = SwiftClock.Now;
            var done = FrameClock.IsComplete(start, now, span);
            apply(done ? 1 : Curve.Progress((now - start).TotalSeconds));
            if (!done) source.RequestFrame(tick!);
            else stopped = true;
        };
        apply(0);
        source.RequestFrame(tick);
        return () =>
        {
            if (stopped) return;
            stopped = true;
            source.CancelFrame(tick);
        };
    }
}
