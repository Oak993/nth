using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;
using Avalonia.VisualTree;

namespace Marrow.UI.Port;

/// <summary>
/// PORT-ONLY (ED-NEW-gate-fixes-3): the surface that catches an outside press where the window itself has nothing to hit. Avalonia routes a press to the element it hits and to nothing
/// when it hits none (a window whose template draws no background, a bare corner of its content), so with Avalonia's own light dismiss off (PopoverHost.BeginPresentation, so that the
/// close animation runs) such a press never reached the popover's owner-window handler and the popover stayed open. Avalonia's own light dismiss was a transparent layer over the whole
/// window; this one is hit only where nothing else under the pointer is, so a press on a control still reaches the control (PopoverHost's handler sees every press either way and
/// closes the popover through its own close transition). It lives in the owner's overlay layer while the popover is presented.
/// </summary>
internal sealed class PopoverDismissSurface : Control, ICustomHitTest
{
    private readonly TopLevel owner;

    internal PopoverDismissSurface(TopLevel owner)
    {
        this.owner = owner;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        Focusable = false;
    }

    public override void Render(DrawingContext context) => context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

    /// <summary>Hit where no other element is: the pointer is over the window's bare surface.</summary>
    public bool HitTest(Point point) =>
        this.TranslatePoint(point, owner) is { } inOwner && owner.InputHitTest(inOwner, visual => visual is not PopoverDismissSurface) is null;
}
