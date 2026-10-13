using System.Globalization;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.NthUi;

// DESIGN.md 12 (web surfaces W16b; ED-NEW-web-w16b-1): "each node under 2 ms; a 300-node view never takes more than 4 ms of a
// frame", held on the wall clock for a chart of three series and 200 points. The design's own budgets, stricter than the
// headless checks in NthUiChartPerformanceTests, for the native window: run them on the real machine in a Release build with
// nothing else heavy running (dotnet test tests/Marrow.UI.Tests -c Release --filter FullyQualifiedName~NthUiChartPerformanceNativeTests).
[Collection("Headless")]
public sealed class NthUiChartPerformanceNativeTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void A_chart_node_is_placed_in_under_two_milliseconds_and_costs_under_four_milliseconds_of_a_frame() => headless.OnUiThread(() =>
    {
        var t = NthUiChartPerformanceTests.Measure();
        NthUiChartPerformanceTests.Record(string.Create(CultureInfo.InvariantCulture,
            $"native wall ms: placed {t.PlaceWall:0.00}; a state change {t.ChangeWall:0.00}; a paint {t.PaintWall:0.00}; a readout frame {t.GlideWall:0.00}"));
        Assert.True(t.PlaceWall < 2, $"placing the chart took {t.PlaceWall:0.00} ms");
        Assert.True(t.ChangeWall < 4, $"a state change took {t.ChangeWall:0.00} ms");
        Assert.True(t.PaintWall < 4, $"a paint took {t.PaintWall:0.00} ms");
        Assert.True(t.GlideWall < 4, $"a readout frame took {t.GlideWall:0.00} ms");
    });
}
