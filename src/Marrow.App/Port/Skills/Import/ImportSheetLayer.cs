// PORT-ONLY: first-release feature unit SK-18 (OD-0118). What the in-window sheet layer is given for "Import skills": the sheet and, behind it, a wash over the whole window, exactly as DialogSheet does it for Download all.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Marrow.App.Port.Skills.Import;

internal sealed class ImportSheetLayer : Panel
{
    private const double Reach = 3000;
    private readonly Border wash = new() { IsHitTestVisible = false, Background = new SolidColorBrush(Color.FromArgb(0x52, 0, 0, 0)) };

    public ImportSheetLayer(ImportSheetView sheet)
    {
        Sheet = sheet;
        ClipToBounds = false;
        Children.Add(wash);
        Children.Add(sheet);
    }

    public ImportSheetView Sheet { get; }

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
