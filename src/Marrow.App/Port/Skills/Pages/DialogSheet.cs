// PORT-ONLY: first-release feature unit SK-15 (DL-17). What the in-window sheet layer is given: the dialog and, behind it, a wash over the whole window (the design's dimmed window). The panel measures to the dialog alone, so the
// layer centres the dialog, and the wash is arranged far past the panel's own edges (nothing clips it), taking no pointer: the layer itself keeps the window under it from the pointer while the sheet shows.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Marrow.App.Port.Skills.Pages;

internal sealed class DialogSheet : Panel, IWindowCentredSheet // centred on the window it dims, whatever part of the page is in view
{
    private const double Reach = 3000;
    private readonly Border wash = new() { IsHitTestVisible = false, Background = new SolidColorBrush(Color.FromArgb(0x52, 0, 0, 0)) };

    public DialogSheet(DownloadAllDialog dialog)
    {
        Dialog = dialog;
        ClipToBounds = false;
        Children.Add(wash);
        Children.Add(dialog);
    }

    public DownloadAllDialog Dialog { get; }

    public Control Wash => wash;

    protected override Size MeasureOverride(Size availableSize)
    {
        wash.Measure(new Size(Reach * 2, Reach * 2));
        Dialog.Measure(availableSize);
        return Dialog.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        wash.Arrange(new Rect(finalSize.Width / 2 - Reach, finalSize.Height / 2 - Reach, Reach * 2, Reach * 2));
        Dialog.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        return finalSize;
    }
}
