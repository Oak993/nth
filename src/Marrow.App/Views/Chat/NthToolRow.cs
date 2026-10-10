// PORT-ONLY: OD-0136 amendment 2, step 8 "Tool call" (ED-NEW-nth-motion). A running tool row shows the 14 px spinner in its status
// slot and the light passing through its label (--text); when the call completes the spinner fades, a tick draws itself
// (NthStatusMark) and the label steps back to --text2 over 0.25 s. A ToolRow builds its body again whenever the call changes,
// so the parts that must live across the running -> done change (the mark, the shining label) live in the row, in
// <see cref="NthToolRowParts"/>, and each new body takes them over. A row built already done gets a plain mark and label: its
// tick is in place and nothing animates (a row scrolled in, or made again, does not replay).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;

namespace Marrow.App.Views.Chat;

/// <summary>What a tool row keeps from one body to the next (only a row first seen running keeps a label).</summary>
internal sealed class NthToolRowParts
{
    /// <summary>The status slot (the tick draws in when this one changes from working to done while on screen).</summary>
    public NthStatusMark Mark { get; } = NthToolRow.NewMark();

    /// <summary>The shining label, once the row has been seen running.</summary>
    public NthToolLabel? Label { get; set; }
}

internal static class NthToolRow
{
    /// <summary>A status slot for a tool row: the tick as the done look, a 14 px spinner.</summary>
    public static NthStatusMark NewMark()
    {
        var mark = new NthStatusMark { DoneMark = NthDoneMark.Tick, MarkSize = 14 };
        TextBlock.SetBaselineOffset(mark, 12); // the 16 px slot sits on the callout text's x-height centre in a row that aligns on baselines
        return mark;
    }

    /// <summary>Takes a control out of the body it was last in (so the next body can hold it).</summary>
    public static void TakeOut(Control control)
    {
        switch (control.Parent)
        {
            case Decorator decorator: decorator.Child = null; break;
            case Panel panel: panel.Children.Remove(control); break;
        }
    }
}

/// <summary>A tool row's label: the light passes through it while the call runs; it steps from --text to --text2 when the call is done. Long text is elided from its middle, as the row's plain label does.</summary>
internal sealed class NthToolLabel : NthShineText
{
    private const string Ellipsis = "…";
    private readonly AnimatedColor ink;
    private string full = "";

    public NthToolLabel()
    {
        ink = new AnimatedColor(this);
        Foreground = ink.Brush;
    }

    /// <summary>The untruncated text (MiddleTruncatingTextBlock.FullText).</summary>
    public string FullText
    {
        get => full;
        set { if (full == value) return; full = value; InvalidateMeasure(); }
    }

    /// <summary>Running: shining, in the primary text colour. Done (or failed): still, in the secondary colour, stepping there over 0.25 s when <paramref name="animate"/>.</summary>
    public void Rest(bool running, bool animate)
    {
        IsShining = running;
        ink.Set(running ? Chrome.PrimaryText : SwiftColor.Secondary, animate && FrameClock.IsBound ? Animation.EaseOut(duration: 0.25) : null);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Text = Trimmed(availableSize.Width);
        return base.MeasureOverride(availableSize);
    }

    // MiddleTruncatingTextBlock's single-line path (Marrow.UI/Port/MiddleTruncatingText.cs): the longest head + ellipsis + tail that fits.
    private string Trimmed(double maxWidth)
    {
        if (double.IsInfinity(maxWidth) || full.Length == 0 || TextWidth(full) <= maxWidth) return full;
        int lo = 0, hi = full.Length;
        var best = Ellipsis;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var candidate = Truncated(mid);
            if (TextWidth(candidate) <= maxWidth) { best = candidate; lo = mid + 1; }
            else hi = mid - 1;
        }
        return best;
    }

    private string Truncated(int kept)
    {
        if (kept >= full.Length) return full;
        var head = kept / 2;
        return string.Concat(full.AsSpan(0, head), Ellipsis, full.AsSpan(full.Length - (kept - head)));
    }

    private double TextWidth(string text) =>
        new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize, null).Width;
}
