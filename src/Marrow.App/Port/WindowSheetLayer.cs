// PORT-ONLY (ED-333): a sheet drawn in its own window, over the window's content, for a sheet whose background is glass over
// the app (ChatView.swift:1456-1463, `.presentationBackground { WindowBackdrop(opacity:) }`). The port's glass material reads what its own
// top level drew under it (F.GlassMaterial), so the scripts sheet's glass can frost the chat under it only from inside the main window; a
// Popup is a top level of its own (a PopupRoot window), whose surface holds nothing under the sheet. The layer covers the window's content,
// so the window under a sheet takes no pointer while it shows (a sheet is window-modal), and it centres the sheet on its anchor, as the
// Popup's PlacementMode.Center did. Beside the sheet a press that would land on the window's own drag region (a WindowDragArea, or any region
// whose Windows hit test is the caption) passes through to it, so the window can still be dragged by it, as a Mac window with a sheet can be.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;
using Avalonia.VisualTree;

namespace Marrow.App.Port;

/// <summary>
/// ED-NEW-dlhonest-1: a sheet that dims the whole window (the skills pages' Download all dialog) is centred on the window, not on its anchor. A page's anchor can be taller than the window or scrolled (the Tools
/// library sits far down Settings › Updates), and its middle then put the dialog over whatever part of the page happened to be there.
/// </summary>
internal interface IWindowCentredSheet;

internal sealed class WindowSheetLayer : Panel, ICustomHitTest
{
    private readonly Control anchor;

    public WindowSheetLayer(Control anchor, Control sheet)
    {
        this.anchor = anchor;
        Sheet = sheet;
        Background = Brushes.Transparent; // hit-tested: a press beside the sheet reaches nothing under it
        Focusable = true; // it takes the keyboard when it shows (ChatSheetPresentation.ShowInWindow)
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        Children.Add(sheet);
        // the anchor moves without this layer being laid out again (the sidebar opens, the column narrows): follow it
        anchor.PropertyChanged += OnAnchorChanged;
        DetachedFromVisualTree += (_, _) => anchor.PropertyChanged -= OnAnchorChanged;
    }

    /// <summary>The sheet's content, as the presentation built it.</summary>
    public Control Sheet { get; }

    // Everywhere but where the content under the layer is the window's drag region (see the file note).
    public bool HitTest(Point point)
    {
        if (new Rect(Bounds.Size).Contains(point) is false) return false;
        if (Sheet.Bounds.Contains(point)) return true;
        if (Parent is not Panel root) return true;
        foreach (var under in root.Children.TakeWhile(child => !ReferenceEquals(child, this)).Reverse())
        {
            if (!under.IsEffectivelyVisible || this.TranslatePoint(point, under) is not { } at) continue;
            if (under.InputHitTest(at) is not Visual hit) continue;
            return !hit.GetSelfAndVisualAncestors().OfType<Control>().Any(control =>
                Win32Properties.GetNonClientHitTestResult(control) == Win32Properties.Win32HitTestValue.Caption);
        }
        return true;
    }

    private void OnAnchorChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BoundsProperty) InvalidateArrange();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Sheet.Measure(availableSize);
        return Sheet.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = Sheet.DesiredSize;
        var middle = new Point(finalSize.Width / 2, finalSize.Height / 2);
        var center = Sheet is IWindowCentredSheet ? middle : anchor.TranslatePoint(new Point(anchor.Bounds.Width / 2, anchor.Bounds.Height / 2), this) ?? middle;
        var x = Math.Clamp(center.X - size.Width / 2, 0, Math.Max(0, finalSize.Width - size.Width));
        var y = Math.Clamp(center.Y - size.Height / 2, 0, Math.Max(0, finalSize.Height - size.Height));
        Sheet.Arrange(new Rect(new Point(x, y), size));
        return finalSize;
    }
}
