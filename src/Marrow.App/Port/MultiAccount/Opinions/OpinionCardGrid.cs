// PORT-ONLY: final .hyd.ops :898 wins: repeat(auto-fit,minmax(min(100%,200px),1fr)), gap 8.
using Avalonia;
using Avalonia.Controls;
namespace Marrow.App.Port.MultiAccount.Opinions;
internal sealed class OpinionCardGrid : Panel
{
    private int columns;
    private double cellWidth;
    private double[] heights = [];
    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsFinite(available.Width) ? available.Width : 200 * Children.Count + 8 * Math.Max(0, Children.Count - 1);
        columns = Math.Max(1, Math.Min(Children.Count, (int)Math.Floor((width + 8) / 208)));
        cellWidth = Math.Max(0, (width - (columns - 1) * 8) / columns);
        heights = new double[(Children.Count + columns - 1) / columns];
        for (var i = 0; i < Children.Count; i++) { Children[i].Measure(new Size(cellWidth, double.PositiveInfinity)); heights[i / columns] = Math.Max(heights[i / columns], Children[i].DesiredSize.Height); }
        return new Size(width, heights.Sum() + Math.Max(0, heights.Length - 1) * 8);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        double y = 0;
        for (var row = 0; row < heights.Length; row++)
        {
            for (var column = 0; column < columns && row * columns + column < Children.Count; column++)
                Children[row * columns + column].Arrange(new Rect(column * (cellWidth + 8), y, cellWidth, heights[row]));
            y += heights[row] + 8;
        }
        return finalSize;
    }
}
