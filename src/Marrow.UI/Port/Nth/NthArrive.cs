// PORT-ONLY: OD-0136 amendment 2, steps 6, 7, 10, 11 and 13 (ED-NEW-nth-motion). Content arriving where a skeleton or a
// placeholder stood: rows rise 6 to 0 px and fade in over 0.36 s (.2,.8,.2,1), 40 ms apart (step 7); an image fades in and
// settles from 102 % to 100 % over 0.6 s (steps 6, 13); a button rises 6 to 0 px over 0.32 s (step 10). Opacity and a
// render transform only: no re-layout. Calm: the fade alone, with no movement.
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;

namespace Marrow.UI.Port.Nth;

public static class NthArrive
{
    private static readonly SplineEasing Out = new(.2, .8, .2, 1);

    /// <summary>Rows that replace a skeleton: row i rises 6 px and fades in, 0.36 s, 40 ms after row i - 1 (after <paramref name="delay"/>).
    /// The thread history hand-over: 10 px, 0.5 s, 45 ms apart, 150 ms after the loader starts to leave.</summary>
    public static void Rows(IReadOnlyList<Control> rows, double distance = 6, double seconds = .36, double stagger = .04, double delay = 0)
    {
        var risen = RisingRows(rows);
        for (var i = 0; i < rows.Count; i++)
        {
            if (i < risen) Rise(rows[i], distance, seconds, delay + i * stagger);
            else Arrived(rows[i]);
        }
    }

    // ED-NEW-lean-19: only the rows that can be on screen when they arrive rise; the rest stand at once in the state the rise ends in, with
    // no transition (300 rows made 600 transitions over 12 s). Rows 0-24 always rise with the 40 ms stagger. A taller window raises the
    // count to every row that can fit in it: a maximised Settings window is as tall as its screen, so the count follows the top level's
    // height at the shortest row an arrival lays out (the composer's model row, 34 px and 1 px of spacing) at the smallest app zoom (75 %).
    internal const int AlwaysRisingRows = 25;
    internal const double ShortestRowPitch = 35 * .75;

    internal static int RisingRows(IReadOnlyList<Control> rows)
    {
        if (rows.Count <= AlwaysRisingRows) return rows.Count;
        var height = TopLevel.GetTopLevel(rows[0])?.ClientSize.Height ?? 0;
        var fit = double.IsFinite(height) && height > 0 ? (int)Math.Min(int.MaxValue - 1, Math.Ceiling(height / ShortestRowPitch)) + 1 : 0;
        return Math.Min(rows.Count, Math.Max(AlwaysRisingRows, fit));
    }

    private static void Arrived(Control control)
    {
        var calm = EnvironmentValues.GetAccessibilityReduceMotion(control);
        control.Transitions = null;
        control.Opacity = 1;
        control.RenderTransform = calm ? null : TransformOperations.Identity;
    }

    /// <summary>One control rising <paramref name="distance"/> px and fading in over <paramref name="seconds"/>, after <paramref name="delay"/>.</summary>
    public static void Rise(Control control, double distance = 6, double seconds = .36, double delay = 0) =>
        Play(control, seconds, delay, $"translateY({Fmt(distance)}px)", "none");

    /// <summary>An image arriving over its placeholder: opacity 0 to 1 and scale 1.02 to 1, 0.6 s.</summary>
    public static void Settle(Control control, double seconds = .6) => Play(control, seconds, 0, "scale(1.02)", "none");

    private static string Fmt(double value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static void Play(Control control, double seconds, double delay, string from, string to)
    {
        var calm = EnvironmentValues.GetAccessibilityReduceMotion(control);
        var duration = TimeSpan.FromSeconds(seconds);
        var wait = TimeSpan.FromSeconds(delay);
        control.Transitions = null;
        control.Opacity = 0;
        control.RenderTransform = calm ? null : TransformOperations.Parse(from);
        control.Transitions =
        [
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = calm ? TimeSpan.FromSeconds(.2) : duration, Delay = wait, Easing = calm ? new LinearEasing() : Out },
            new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = duration, Delay = wait, Easing = Out },
        ];
        // the start values are drawn first; the next frame moves them to the end values under the transitions
        Dispatcher.UIThread.Post(() =>
        {
            control.Opacity = 1;
            if (!calm) control.RenderTransform = TransformOperations.Parse(to);
        }, DispatcherPriority.Render);
    }
}
