using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Marrow.Setup.Ui;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3b; the canvas's <c>.scr</c>, <c>.grow</c>; unit U2, ED-NEW-inst-u2-1). A screen's
/// stack: one column, its children centred (or stretched, for the 420 DIP blocks), with flexible spaces
/// (<see cref="Grow"/>, at least 10 DIP) that share whatever height is left, the canvas's <c>flex:1;min-height:10px</c>.
/// The column is the screen's whole body (padding 14 40 26), so a screen that grows taller than the window makes the
/// window grow (the shell's text-size sizing). Only <see cref="Reveal"/> moves anything inside it.
/// </summary>
public sealed class ScreenColumn : Panel
{
    public static readonly Thickness ScreenPadding = new(40, 14, 40, 26);

    /// <summary>A stand-in that keeps the height of a hidden line in the column's own height (it takes no place in the flow).</summary>
    public static readonly AttachedProperty<bool> IsGhostProperty = AvaloniaProperty.RegisterAttached<ScreenColumn, Control, bool>("IsGhost");

    /// <summary>
    /// A line that takes its height from the flexible space directly above it, so nothing above it moves as it comes or goes
    /// (the Close-paused line: "it fades in; nothing above it moves", PLAN.md 3b). When that space is too small it counts
    /// like any other line.
    /// </summary>
    public static readonly AttachedProperty<bool> FromSpaceAboveProperty = AvaloniaProperty.RegisterAttached<ScreenColumn, Control, bool>("FromSpaceAbove");

    private readonly Dictionary<Control, Control> ghosts = [];

    public ScreenColumn() => Padding = ScreenPadding;

    public static bool GetIsGhost(Control control) => control.GetValue(IsGhostProperty);

    public static bool GetFromSpaceAbove(Control control) => control.GetValue(FromSpaceAboveProperty);

    public static void SetFromSpaceAbove(Control control, bool value) => control.SetValue(FromSpaceAboveProperty, value);

    /// <summary>
    /// Counts <paramref name="ghost"/>'s height in the column's own height while <paramref name="twin"/> is hidden, so the
    /// window is already as tall as the screen with that line shown and its arrival only uses the flexible space: the
    /// buttons never move, at any text size (PLAN.md 3b). The ghost is invisible, not hit-testable and hidden from Narrator.
    /// </summary>
    public void AddGhost(Control twin, Control ghost)
    {
        ArgumentNullException.ThrowIfNull(twin);
        ArgumentNullException.ThrowIfNull(ghost);
        ghost.SetValue(IsGhostProperty, true);
        ghost.Opacity = 0;
        ghost.IsHitTestVisible = false;
        Avalonia.Automation.AutomationProperties.SetAccessibilityView(ghost, Avalonia.Automation.AccessibilityView.Raw);
        ghosts[ghost] = twin;
        Children.Insert(0, ghost);
    }

    public Thickness Padding { get; set; }

    /// <summary>Adds <paramref name="children"/> in order; returns the column.</summary>
    public ScreenColumn With(params Control[] children)
    {
        foreach (var child in children) Children.Add(child);
        return this;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = Math.Max(0, availableSize.Width - Padding.Left - Padding.Right);
        double height = 0, widest = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(width, double.PositiveInfinity));
            if (ghosts.TryGetValue(child, out var twin))
            {
                if (!twin.IsEffectivelyVisible) height += child.DesiredSize.Height;
                continue;
            }
            if (!child.IsVisible) continue;
            height += child is Grow grow ? grow.MinHeight + grow.Margin.Top + grow.Margin.Bottom : child.DesiredSize.Height;
            widest = Math.Max(widest, child.DesiredSize.Width);
        }
        return new Size(double.IsInfinity(availableSize.Width) ? widest + Padding.Left + Padding.Right : availableSize.Width, height + Padding.Top + Padding.Bottom);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = Math.Max(0, finalSize.Width - Padding.Left - Padding.Right);
        double fixedHeight = 0;
        var grows = 0;
        // the lines that take their height from the space above them, by that space
        var taken = new Dictionary<Grow, double>();
        Grow? above = null;
        foreach (var child in Children)
        {
            if (!child.IsVisible || ghosts.ContainsKey(child)) continue;
            if (child is Grow grow)
            {
                grows++;
                fixedHeight += grow.MinHeight + grow.Margin.Top + grow.Margin.Bottom;
                above = grow;
            }
            else
            {
                fixedHeight += child.DesiredSize.Height;
                if (above is not null && GetFromSpaceAbove(child)) taken[above] = taken.GetValueOrDefault(above) + child.DesiredSize.Height;
            }
        }
        var extra = Math.Max(0, finalSize.Height - Padding.Top - Padding.Bottom - fixedHeight);
        var share = grows > 0 ? extra / grows : 0;
        var shares = new Dictionary<Grow, double>();
        if (taken.Count > 0)
        {
            // the share each space would have without those lines, less what they take from it
            var before = (extra + taken.Values.Sum()) / grows;
            if (taken.Values.All(t => t <= before))
                foreach (var child in Children)
                    if (child is Grow g && g.IsVisible) shares[g] = before - taken.GetValueOrDefault(g);
        }
        var y = Padding.Top;
        foreach (var child in Children)
        {
            if (ghosts.ContainsKey(child))
            {
                child.Arrange(new Rect(Padding.Left, 0, width, child.DesiredSize.Height));
                continue;
            }
            if (!child.IsVisible)
            {
                child.Arrange(new Rect(Padding.Left, y, width, 0));
                continue;
            }
            var height = child is Grow grow ? grow.MinHeight + grow.Margin.Top + grow.Margin.Bottom + shares.GetValueOrDefault(grow, share) : child.DesiredSize.Height;
            child.Arrange(new Rect(Padding.Left, y, width, height));
            y += height;
        }
        return finalSize;
    }
}

/// <summary>A flexible space in a <see cref="ScreenColumn"/>: at least 10 DIP, plus its share of what is left.</summary>
public sealed class Grow : Control
{
    public Grow()
    {
        MinHeight = 10;
        IsHitTestVisible = false;
        Focusable = false;
    }
}

/// <summary>
/// The canvas's <c>.acts</c>: the buttons in one centred row, 10 DIP apart, wrapping to more rows only when they do not fit
/// (a very large text size at a narrow width). Hidden buttons take no place.
/// </summary>
public sealed class ButtonRow : Panel
{
    public const double Gap = 10;

    public ButtonRow()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    private List<List<Control>> Lines(double width)
    {
        var lines = new List<List<Control>>();
        var line = new List<Control>();
        double used = 0;
        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;
            var w = child.DesiredSize.Width;
            if (line.Count > 0 && used + Gap + w > width + 0.01)
            {
                lines.Add(line);
                line = [];
                used = 0;
            }
            used += (line.Count > 0 ? Gap : 0) + w;
            line.Add(child);
        }
        if (line.Count > 0) lines.Add(line);
        return lines;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        var width = double.IsInfinity(availableSize.Width) ? double.MaxValue : availableSize.Width;
        double height = 0, widest = 0;
        foreach (var line in Lines(width))
        {
            height += (height > 0 ? Gap : 0) + line.Max(c => c.DesiredSize.Height);
            widest = Math.Max(widest, line.Sum(c => c.DesiredSize.Width) + Gap * (line.Count - 1));
        }
        return new Size(double.IsInfinity(availableSize.Width) ? widest : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double y = 0;
        foreach (var line in Lines(finalSize.Width))
        {
            var lineWidth = line.Sum(c => c.DesiredSize.Width) + Gap * (line.Count - 1);
            var lineHeight = line.Max(c => c.DesiredSize.Height);
            var x = (finalSize.Width - lineWidth) / 2;
            foreach (var child in line)
            {
                child.Arrange(new Rect(x, y, child.DesiredSize.Width, lineHeight));
                x += child.DesiredSize.Width + Gap;
            }
            y += lineHeight + Gap;
        }
        foreach (var child in Children)
            if (!child.IsVisible) child.Arrange(new Rect(0, 0, 0, 0));
        return finalSize;
    }
}
