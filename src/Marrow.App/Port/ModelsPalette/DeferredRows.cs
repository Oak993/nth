// PORT-ONLY (ED-NEW-stutterfix-2): a long model list's rows past its first screens are put in a slice per frame instead of all
// in the frame the list opens (a list of ~480 rows took 50 ms to make and 20-70 ms to lay out on the UI thread). Each row is the
// same control the list made before, in the same place; until the last is in, one empty spacer stands for the rest at exactly
// their height, so the scroll extent, the scrollbar's thumb and every realised row's position are the final ones from the first
// frame. A reader that needs every row (a search, a key press) or a viewport reaching past them realises them at once.
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Marrow.UI.Port;

namespace Marrow.App.Port.ModelsPalette;

internal sealed class DeferredRows
{
    /// <summary>How far down a list its rows are put in at once: the 440-point frame twice over (a screen and a scroll's worth).</summary>
    internal const double EagerHeight = 2 * 440;

    // The UI thread's slice of a frame spent on rows (their layout follows in the same frame).
    private static readonly long Slice = Stopwatch.Frequency * 2 / 1000;

    private readonly Panel panel;
    private readonly ScrollViewer scroll;
    private readonly Control spacer;
    private readonly Func<int, Control> make;
    private readonly double[] below; // below[i]: the height of items i.. to the end, each with the spacing above it
    private readonly double spacing;
    private readonly Action<TimeSpan> frame;
    private int next, at;
    private bool requested;

    /// <summary>
    /// How many of the items of <paramref name="heights"/> (in a vertical stack with <paramref name="spacing"/>, starting
    /// <paramref name="above"/> points down) a list puts in at once: all of them when the rest would come to less than a screen
    /// (the list is then built whole, as before), or when nothing drives frames.
    /// </summary>
    internal static int EagerCount(IReadOnlyList<double> heights, double spacing, double above)
    {
        if (!FrameClock.IsBound) return heights.Count;
        var (count, reach) = (0, above);
        while (count < heights.Count && reach < EagerHeight) reach += heights[count++] + spacing;
        var rest = 0.0;
        for (var i = count; i < heights.Count; i++) rest += heights[i] + spacing;
        return rest < 440 ? heights.Count : count;
    }

    /// <summary>
    /// Takes over putting items <paramref name="first"/> to the last of <paramref name="heights"/> into <paramref name="panel"/> (a
    /// vertical stack whose spacing is <paramref name="spacing"/>), after the items already there; the panel's next child, added
    /// by the caller after this, stays after them. <paramref name="make"/> gives item i (made then, or before) and does whatever
    /// its owner does with it.
    /// </summary>
    internal DeferredRows(Panel panel, ScrollViewer scroll, int first, IReadOnlyList<double> heights, double spacing, Func<int, Control> make)
    {
        this.panel = panel; this.scroll = scroll; this.make = make; this.spacing = spacing;
        below = new double[heights.Count + 1];
        for (var i = heights.Count - 1; i >= 0; i--) below[i] = below[i + 1] + heights[i] + spacing;
        next = first;
        spacer = new Control { IsHitTestVisible = false, Height = Pending };
        at = panel.Children.Count;
        panel.Children.Add(spacer);
        frame = OnFrame;
        scroll.ScrollChanged += (_, _) => Reach();
        scroll.AttachedToVisualTree += (_, _) => Request();
        Request();
    }

    /// <summary>Whether every item is in.</summary>
    internal bool Complete => next >= below.Length - 1;

    // The items still to come; the spacer takes the spacing above the first of them as its own.
    private double Pending => below[next] - spacing;

    /// <summary>Puts in every item still to come, now. True when there were any (they are not laid out yet).</summary>
    internal bool RealiseAll()
    {
        if (Complete) return false;
        while (!Complete) Add();
        Settle();
        return true;
    }

    private void Request()
    {
        if (requested || Complete || TopLevel.GetTopLevel(scroll) is null) return;
        requested = true;
        FrameClock.Source.Current.RequestFrame(frame);
    }

    private void OnFrame(TimeSpan _)
    {
        requested = false;
        if (Complete || TopLevel.GetTopLevel(scroll) is null) return;
        if (!Reach())
        {
            var until = Stopwatch.GetTimestamp() + Slice;
            do Add(); while (!Complete && Stopwatch.GetTimestamp() < until);
            Settle();
        }
        Request();
    }

    // A viewport (with a screen below it) reaching the spacer gets the items it reaches before this frame's layout.
    private bool Reach()
    {
        if (Complete || !spacer.IsArrangeValid) return false;
        var needed = scroll.Offset.Y + scroll.Viewport.Height + 440;
        var top = spacer.Bounds.Y;
        if (needed <= top) return false;
        var reached = top - spacing;
        while (!Complete && reached < needed) { reached += below[next] - below[next + 1]; Add(); }
        Settle();
        return true;
    }

    private void Add() => panel.Children.Insert(at++, make(next++));

    private void Settle()
    {
        if (Complete) panel.Children.Remove(spacer);
        else spacer.Height = Pending;
    }
}
