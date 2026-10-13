using Avalonia;
using Avalonia.Controls;
using Marrow.UI.NthUi;
using Marrow.UI.NthUi.Charts;

namespace Marrow.UI.Tests.NthUi;

/// <summary>ED-NEW-web-w16b-1: finding a chart in a rig and putting a pointer on its marks.</summary>
internal static class NthUiChartRigExtensions
{
    /// <summary>The chart view <paramref name="index"/> of the rig's view.</summary>
    public static NthUiChartView Chart(this NthUiRig rig, int index = 0) => rig.ViewOf<NthUiChartView>(index);

    /// <summary>A point of <paramref name="control"/> in the window's space (what the headless mouse takes).</summary>
    public static Point InWindow(this NthUiRig rig, Control control, Point at) => control.TranslatePoint(at, rig.Window) ?? at;

    /// <summary>The height in the plot's space of <paramref name="value"/> on the axis drawn now.</summary>
    public static double HeightOf(this NthUiChartPlot plot, double value)
    {
        var area = plot.PlotArea;
        var (lo, hi) = plot.ShownRange;
        return area.Bottom - (value - lo) / (hi - lo) * area.Height;
    }
}
