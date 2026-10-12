using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.NthUi;

// DESIGN.md 12 (web surfaces W16a; ED-NEW-web-w16a-1): "each node under 2 ms; a 300-node view never takes more than 4 ms of a
// frame". The design's own budgets, stricter than the headless checks in NthUiPerformanceTests, for the native window: run
// them on the real machine in a Release build with nothing else heavy running
// (dotnet test tests/Marrow.UI.Tests -c Release --filter FullyQualifiedName~NthUiPerformanceNativeTests).
[Collection("Headless")]
public sealed class NthUiPerformanceNativeTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void Each_streamed_node_is_placed_in_under_two_milliseconds_on_average() => headless.OnUiThread(() =>
    {
        var (average, worst) = NthUiPerformanceTests.StreamLongAnswer();
        NthUiPerformanceTests.Record("native stream average", average);
        Assert.True(average < 2, $"average {average:0.00} ms per node, worst {worst:0.0} ms");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void A_state_change_across_a_three_hundred_node_view_takes_under_four_milliseconds() => headless.OnUiThread(() =>
    {
        var median = NthUiPerformanceTests.RecomputeMedian();
        NthUiPerformanceTests.Record("native recompute median", median);
        Assert.True(median < 4, $"median {median:0.00} ms per change");
    });
}
