// PORT-ONLY (ED-NEW-stutterfix-2): a long model list's rows past its first screens are made a slice per frame instead of all
// in the frame the list opens (486 OpenRouter-sized rows took 50 ms to make and 20-70 ms to lay out on the UI thread). Each row
// is the same control the list made before, in the same place; until the last is in, one empty spacer stands for the rest at
// exactly their height, so the scroll extent, the scrollbar's thumb and every realised row's position are the final ones from
// the first frame. A reader that needs every row (a search, a key press) or a viewport reaching past them realises them at once.
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Marrow.UI.Port;

namespace Marrow.App.Port.ModelsPalette;

internal sealed class DeferredRows
{
    /// <summary>How far down a list its rows are made at once: the 440-point frame twice over (a screen and a scroll's worth).</summary>
    internal const double EagerHeight = 2 * 440;

    // The UI thread's slice of a frame spent making rows (their layout follows in the same frame).
    private static readonly long Slice = Stopwatch.Frequency * 2 / 1000;

    private readonly Panel panel;
    private readonly ScrollViewer scroll;
    private readonly Control spacer;
    private readonly Func<int, Control> make;
    private readonly int total;
    private readonly double pitch, spacing;
    private readonly Action<TimeSpan> frame;
    private int next;
    private bool requested;

    /// <summary>
    /// How many of <paramref name="count"/> rows of <paramref name="pitch"/> (a row and its spacing) a list starting
    /// <paramref name="above"/> points down makes at once: all of them when the rest would be fewer than a screen's worth
    /// (the list is then built whole, as before), or when nothing drives frames.
    /// </summary>
    internal static int EagerCount(int count, double pitch, double above)
    {
        if (!FrameClock.IsBound || pitch <= 0) return count;
        var eager = (int)Math.Ceiling(Math.Max(0, EagerHeight - above) / pitch);
        return count - eager < Math.Ceiling(440 / pitch) ? count : eager;
    }

    /// <summary>
    /// Takes over making rows <paramref name="first"/> to <paramref name="total"/> − 1 of <paramref name="panel"/> (a vertical
    /// stack whose spacing is <paramref name="spacing"/>; every row is <paramref name="pitch"/> − spacing high), appended after
    /// the rows already there. <paramref name="make"/> makes row i and does whatever its owner does with a new row.
    /// </summary>
    internal DeferredRows(Panel panel, ScrollViewer scroll, int first, int total, double pitch, double spacing, Func<int, Control> make)
    {
        this.panel = panel; this.scroll = scroll; this.make = make; this.total = total; this.pitch = pitch; this.spacing = spacing;
        next = first;
        spacer = new Control { IsHitTestVisible = false, Height = Pending };
        panel.Children.Add(spacer);
        frame = OnFrame;
        scroll.ScrollChanged += (_, _) => Reach();
        scroll.AttachedToVisualTree += (_, _) => Request();
        Request();
    }

    /// <summary>Whether every row is in.</summary>
    internal bool Complete => next >= total;

    // The rows still to come, with the spacing above each (the spacer takes the first of them as its own spacing).
    private double Pending => (total - next) * pitch - spacing;

    /// <summary>Makes every row still to come, now. True when there were any (they are not laid out yet).</summary>
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

    // A viewport (with a screen below it) reaching the spacer gets the rows it reaches before this frame's layout.
    private bool Reach()
    {
        if (Complete || !spacer.IsArrangeValid) return false;
        var needed = scroll.Offset.Y + scroll.Viewport.Height + 440;
        var top = spacer.Bounds.Y;
        if (needed <= top) return false;
        var rows = (int)Math.Ceiling((needed - top) / pitch);
        for (var i = 0; i < rows && !Complete; i++) Add();
        Settle();
        return true;
    }

    private void Add() => panel.Children.Insert(panel.Children.Count - 1, make(next++));

    private void Settle()
    {
        if (Complete) panel.Children.Remove(spacer);
        else spacer.Height = Pending;
    }
}
