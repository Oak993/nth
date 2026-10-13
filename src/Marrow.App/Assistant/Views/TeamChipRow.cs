// PORT-ONLY: Dextra AS-17 (DESIGN.md rev 5, 9.4 "Mission chips hang under each node, one per active mission"; ED-NEW-dextra-as17-4). No Swift twin.
// The line of chips under a node. It is always one line of one height, so a task that starts or ends never changes a node's size: the chips that fit are drawn, newest first, and the rest are
// counted in one chip ("+2"), which is left out only when every chip fits. A node is 152 px wide inside its padding, so it usually shows the newest task and a count; the narrow list, with room,
// shows three. A chip that is alone and still too wide is cut by the line (its label already ends in an ellipsis).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Assistant.Views;

public sealed class TeamChipRow : Panel
{
    public const double Gap = 4;
    /// <summary>The line's height: the chips'.</summary>
    public const double Line = TeamChips.Height;

    private static readonly Dictionary<int, double> MoreWidths = [];

    private readonly List<Button> chips = [];
    private readonly List<double> widths = [];
    private readonly TeamMoreChip more = new() { IsVisible = false };
    private int shown;
    private int hidden;

    public TeamChipRow()
    {
        ClipToBounds = true;
        AutomationProperties.SetControlTypeOverride(this, AutomationControlType.List);
        AutomationProperties.SetName(this, "Tasks");
        Children.Add(more);
    }

    /// <summary>The chips drawn now, in order.</summary>
    public IReadOnlyList<Button> Shown => [.. chips.Take(shown)];

    /// <summary>How many chips did not fit and are counted in the "+N" chip.</summary>
    public int Hidden => hidden;

    /// <summary>The "+N" chip's words, or null when every chip fits.</summary>
    public string? MoreText => hidden > 0 ? OrgChartWords.MoreChip(hidden) : null;

    public void Set(IReadOnlyList<Button> next)
    {
        foreach (var old in chips) Children.Remove(old);
        chips.Clear();
        chips.AddRange(next);
        foreach (var chip in chips) Children.Insert(Children.Count - 1, chip);   // the count chip stays last
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        widths.Clear();
        foreach (var chip in chips)
        {
            chip.Measure(new Size(double.PositiveInfinity, Line));
            widths.Add(chip.DesiredSize.Width);
        }
        var room = availableSize.Width;
        shown = 0;
        var used = 0.0;
        for (var i = 0; i < chips.Count; i++)
        {
            var next = used + (i > 0 ? Gap : 0) + widths[i];
            var rest = chips.Count - (i + 1);
            if (next + (rest > 0 ? Gap + MoreWidth(rest) : 0) > room) break;
            shown = i + 1;
            used = next;
        }
        // not even the first chip fits with its count: it is drawn alone, and the line cuts it
        var alone = false;
        if (shown == 0 && chips.Count > 0)
        {
            shown = 1;
            used = Math.Min(widths[0], room);
            alone = true;
        }
        hidden = chips.Count - shown;
        var counted = hidden > 0 && !alone;
        more.IsVisible = counted;
        if (counted)
        {
            more.Count = hidden;
            more.Measure(new Size(double.PositiveInfinity, Line));
            used += (shown > 0 ? Gap : 0) + more.DesiredSize.Width;
        }
        for (var i = 0; i < chips.Count; i++) chips[i].IsVisible = i < shown;
        return new Size(double.IsInfinity(room) ? used : Math.Min(used, room), Line);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        for (var i = 0; i < shown; i++)
        {
            chips[i].Arrange(new Rect(x, 0, Math.Min(widths[i], Math.Max(0, finalSize.Width - x)), Line));
            x += widths[i] + Gap;
        }
        if (more.IsVisible) more.Arrange(new Rect(x, 0, more.DesiredSize.Width, Line));
        return finalSize;
    }

    // the width a "+N" chip takes: measured once for each count, so a measure allocates nothing for it
    private static double MoreWidth(int rest)
    {
        if (MoreWidths.TryGetValue(rest, out var known)) return known;
        var chip = TeamChips.More(rest);
        chip.Measure(new Size(double.PositiveInfinity, Line));
        return MoreWidths[rest] = chip.DesiredSize.Width;
    }
}
