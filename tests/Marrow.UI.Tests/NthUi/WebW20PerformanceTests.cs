using System.Diagnostics;
using System.Text.Json;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.NthUi;

// DESIGN 12: record the existing 300-node workload's strict native wall budgets separately from shared-PC Debug timings.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w20-4")]
public sealed class WebW20PerformanceTests(HeadlessSession headless)
{
    [Fact]
    public void The_three_hundred_node_stream_and_state_recompute_record_actual_headless_times() => headless.OnUiThread(() =>
    {
        var stream = NthUiPerformanceTests.StreamLongAnswer();
        var recompute = NthUiPerformanceTests.Recompute();
        var folder = Path.Combine(AppContext.BaseDirectory, "w20-evidence");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "nth-ui-performance.json"), JsonSerializer.Serialize(new
        {
            kind = "Headless Debug; streaming append plus managed layout; shared PC",
            utc = DateTimeOffset.UtcNow,
            streamNodes = 303, streamAverageMs = stream.Average, streamWorstMs = stream.Worst, streamProcessorMs = stream.Processor,
            recomputeMedianMs = recompute.Median, recomputeProcessorMs = recompute.Processor,
            streamNodeTargetMs = 2, recomputeFrameTargetMs = 4,
            physicalFramesDropped = (int?)null,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Assert.True(double.IsFinite(stream.Worst) && stream.Worst > 0);
        Assert.True(double.IsFinite(recompute.Median) && recompute.Median > 0);
    });
}
