using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Marrow.Core.Models;
using Marrow.UI.Panes;

// PORT-ONLY: web surfaces W3b, the outlines a lifted pane shows where it can be dropped (E:/Droppy/work/design/web-surfaces/
// DESIGN.md 3.6 "Drop targets appear as outlines ... The target under the pointer fills softly", 2.4 "In-tree overlays use the
// snapshot swap ... the workspace's drop targets"; the canvas's .pane.dz::before (a 1.5 DIP dashed outline, white .22, 9 DIP
// radius, 6 DIP in) and .pane.drop::before (a solid .6 rim and a .07 fill); ED-NEW-web-w3b-2). No Swift counterpart. Decoration
// only: it takes no pointer, and it moves nothing but its opacity and its fill.

namespace Marrow.App.Views.Panes;

/// <summary>The layer over the cards that holds the drop outlines of a lifted pane.</summary>
public sealed class PaneDropLayer : Canvas
{
    public PaneDropLayer()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }
}

/// <summary>One drop target's outline: dashed at rest, solid and softly filled while the pointer is over it.</summary>
public sealed class PaneDropVisual : Control
{
    /// <summary>The outline's distance from the target's edge (canvas inset: 6 on a body, 4 on a header).</summary>
    public const double BodyInset = 6;

    public const double HeaderInset = 4;

    public const double Radius = 9;

    public const double Stroke = 1.5;

    private static readonly IReadOnlyList<double> Dashes = [4, 3];

    private double hot;

    public PaneDropVisual(PaneDropTarget target)
    {
        Target = target;
        IsHitTestVisible = false;
        Focusable = false;
        Opacity = 0;
        Width = target.Bounds.Width;
        Height = target.Bounds.Height;
    }

    public PaneDropTarget Target { get; }

    /// <summary>0 at rest, 1 while the pointer is over it.</summary>
    public double Hot
    {
        get => hot;
        set
        {
            value = Math.Clamp(value, 0, 1);
            if (hot == value) return;
            hot = value;
            InvalidateVisual();
        }
    }

    private double Inset => Target.Kind == PaneDropKind.Header ? HeaderInset : BodyInset;

    public override void Render(DrawingContext context)
    {
        var theme = ActualThemeVariant;
        var box = new Rect(Inset, Inset, Math.Max(0, Bounds.Width - 2 * Inset), Math.Max(0, Bounds.Height - 2 * Inset));
        if (box.Width < 2 || box.Height < 2) return;
        var radius = Math.Min(Radius, Math.Min(box.Width, box.Height) / 2);
        var rest = PaneTokens.DropOutline.Resolve(theme);
        var rim = PaneTokens.DropRim.Resolve(theme);
        var fill = PaneTokens.DropFill.Resolve(theme);
        // The canvas switches from a dashed to a solid outline at once; the fill and the rim's strength come in softly.
        var solid = hot >= 0.5;
        var colour = Mix(rest, rim, hot);
        var pen = new ImmutablePen(new ImmutableSolidColorBrush(colour), Stroke, solid ? null : new ImmutableDashStyle(Dashes, 0));
        var body = hot > 0 ? new ImmutableSolidColorBrush(Color.FromArgb((byte)Math.Round(fill.A * hot), fill.R, fill.G, fill.B)) : null;
        context.DrawRectangle(body, pen, box, radius, radius);
    }

    private static Color Mix(Color from, Color to, double amount) => Color.FromArgb(
        (byte)Math.Round(from.A + (to.A - from.A) * amount),
        (byte)Math.Round(from.R + (to.R - from.R) * amount),
        (byte)Math.Round(from.G + (to.G - from.G) * amount),
        (byte)Math.Round(from.B + (to.B - from.B) * amount));
}
