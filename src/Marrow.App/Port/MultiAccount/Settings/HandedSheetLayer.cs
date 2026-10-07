// PORT-ONLY: T11 step 2. What the in-window sheet layer is given for the example sheet: the sheet and, behind it, a wash over the whole window, exactly as DialogSheet and ImportSheetLayer do it.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Marrow.App.Port.MultiAccount.Settings;

internal sealed class HandedSheetLayer : Panel
{
    private const double Reach = 3000;
    private readonly Border wash = new() { IsHitTestVisible = false, Background = new SolidColorBrush(Color.FromArgb(0x52, 0, 0, 0)) };

    internal HandedSheetLayer(HandedSheetView sheet)
    {
        Sheet = sheet;
        ClipToBounds = false;
        Children.Add(wash);
        Children.Add(sheet);
    }

    internal HandedSheetView Sheet { get; }

    protected override Size MeasureOverride(Size availableSize)
    {
        wash.Measure(new Size(Reach * 2, Reach * 2));
        Sheet.Measure(availableSize);
        return Sheet.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        wash.Arrange(new Rect(finalSize.Width / 2 - Reach, finalSize.Height / 2 - Reach, Reach * 2, Reach * 2));
        Sheet.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        return finalSize;
    }
}
