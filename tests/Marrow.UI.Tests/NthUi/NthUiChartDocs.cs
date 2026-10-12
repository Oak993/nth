using System.Globalization;
using Marrow.Core.NthUi;
using Marrow.UI.NthUi.Charts;

namespace Marrow.UI.Tests.NthUi;

/// <summary>ED-NEW-web-w16b-1: a chart's model, summary and table built from nth-ui text, with no window.</summary>
internal static class NthUiChartDocs
{
    public static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    /// <summary>The first chart of <paramref name="text"/> as the runtime reads it.</summary>
    public static NthUiChartModel Model(string text)
    {
        var document = NthUiParser.Parse(text);
        var runtime = new NthUiRuntime(document, English);
        return NthUiChartModel.From(runtime.Chart(document.Nodes.First(n => n.Kind == "chart" && !n.Degraded)));
    }
}
