// PORT-ONLY: owner request 2026-10-11 (ED-NEW-mcpimport-1). What the in-window sheet layer is given for "Import servers" and "Import plugins": the sheet and, behind it, a wash over the whole window, exactly as
// ImportSheetLayer (SK-18) and HandedSheetLayer do it.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Marrow.App.Port.Settings;

internal sealed class ImportWashLayer : Panel
{
    private const double Reach = 3000;
    private readonly Border wash = new() { IsHitTestVisible = false, Background = new SolidColorBrush(Color.FromArgb(0x52, 0, 0, 0)) };

    public ImportWashLayer(Control sheet)
    {
        Sheet = sheet;
        ClipToBounds = false;
        Children.Add(wash);
        Children.Add(sheet);
    }

    public Control Sheet { get; }

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
