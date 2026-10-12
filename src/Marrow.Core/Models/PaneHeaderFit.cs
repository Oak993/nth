// PORT-ONLY: web surfaces W3a, the pane header's overflow rule (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.2 "Header
// overflow", 6.3 "moves Export, Open in Visus, Save and Copy into the ⋯ menu, in that order", 13.3 "Header overflow";
// ED-NEW-web-w3a-1). No Swift counterpart. A pure function of measured widths: the header measures its parts after a
// layout and asks this which of them stay in the row; everything else goes into the one More menu.

namespace Marrow.Core.Models;

/// <summary>The measured parts of a pane header, in DIP.</summary>
/// <param name="Width">The header's width.</param>
/// <param name="Fixed">What is always there besides Close: the leading and trailing padding, the kind glyph and its gap.</param>
/// <param name="Gap">The space between two items.</param>
/// <param name="Button">One square header button (Close, Expand or Restore, More).</param>
/// <param name="Title">The title's (or the tab group's) natural width.</param>
/// <param name="Actions">The pane's own actions' widths, in reading order.</param>
/// <param name="Chip">The "Chat is working" chip's width, or 0 when it is not shown.</param>
/// <param name="HasExpand">Whether the pane can be expanded (every grid pane can; a pane alone in narrow mode cannot).</param>
public sealed record PaneHeaderParts(
    double Width,
    double Fixed,
    double Gap,
    double Button,
    double Title,
    IReadOnlyList<double> Actions,
    double Chip = 0,
    bool HasExpand = true)
{
    /// <summary>The title is truncated, never under this (DESIGN 3.2).</summary>
    public const double TitleMinimum = 80;
}

/// <summary>Which header parts stay in the row.</summary>
/// <param name="VisibleActions">The first this many actions stay; the rest go into More, in their order.</param>
/// <param name="ShowsMore">The More button (⋯) shows.</param>
/// <param name="ShowsExpand">Expand (or Restore) stays in the row; otherwise it is the More menu's last item.</param>
/// <param name="ShowsChip">The working chip stays in the row.</param>
/// <param name="TitleWidth">The width the title gets (at least <see cref="PaneHeaderParts.TitleMinimum"/>, at most its natural width).</param>
public readonly record struct PaneHeaderFit(int VisibleActions, bool ShowsMore, bool ShowsExpand, bool ShowsChip, double TitleWidth)
{
    /// <summary>
    /// DESIGN 3.2: the header keeps, in order of priority, Close, the title (truncated, never under 80 DIP), Expand, then the
    /// pane's own actions. Actions leave the row from the last one back (6.3: Export, then Open in Visus, Save and Copy), so
    /// a version chip or Ask to change, which lead the row, stay longest; what leaves goes into one More menu at the
    /// trailing end, in the original order. The working chip is a status, not an action: it is the first to go, and the
    /// Restore button does what it does.
    /// </summary>
    public static PaneHeaderFit Solve(PaneHeaderParts parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var n = parts.Actions.Count;
        var chip = parts.Chip > 0;
        if (Fits(parts, n, more: false, parts.HasExpand, chip, out var title)) return new(n, false, parts.HasExpand, chip, title);
        if (chip && Fits(parts, n, more: false, parts.HasExpand, chip: false, out title)) return new(n, false, parts.HasExpand, false, title);
        for (var k = n - 1; k >= 0; k--)
        {
            if (Fits(parts, k, more: true, parts.HasExpand, chip: false, out title)) return new(k, true, parts.HasExpand, false, title);
        }
        // Not even Expand beside the minimum title. With actions in More, Expand joins them there (one button fewer).
        // Without any, a More holding only Expand would be the same width, so Expand stays and the title gives way.
        if (n == 0)
        {
            Fits(parts, 0, more: false, parts.HasExpand, chip: false, out title);
            return new(0, false, parts.HasExpand, false, title);
        }
        Fits(parts, 0, more: true, expand: false, chip: false, out title);
        return new(0, true, false, false, title);
    }

    /// <summary>The trailing items' width: each is a gap and its own width; Close is always one of them.</summary>
    public static double Trailing(PaneHeaderParts parts, int actions, bool more, bool expand, bool chip)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var width = parts.Gap + parts.Button; // Close
        for (var i = 0; i < actions && i < parts.Actions.Count; i++) width += parts.Gap + parts.Actions[i];
        if (more) width += parts.Gap + parts.Button;
        if (expand) width += parts.Gap + parts.Button;
        if (chip) width += parts.Gap + parts.Chip;
        return width;
    }

    private static bool Fits(PaneHeaderParts parts, int actions, bool more, bool expand, bool chip, out double title)
    {
        var room = parts.Width - parts.Fixed - Trailing(parts, actions, more, expand, chip);
        var natural = Math.Max(0, parts.Title);
        title = Math.Max(Math.Min(natural, room), Math.Min(natural, PaneHeaderParts.TitleMinimum));
        return room >= Math.Min(natural, PaneHeaderParts.TitleMinimum) - 1e-6;
    }
}
