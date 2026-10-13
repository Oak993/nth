using System.Diagnostics;
using System.Text.Json;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;

namespace Marrow.App.Tests.Views.Panes;

// DESIGN 12: measured headless where possible. These timings include the managed frame and layout of fake pane bodies.
// They do not measure the compositor, display deadlines, real pages or the 2,000-turn transcript (W0 h and W21).
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w20-4")]
public sealed class WebW20PerformanceTests(HeadlessSession headless)
{
    [Fact]
    public void Twenty_open_close_expand_and_redock_runs_record_the_headless_frame_cost_and_keep_valid_geometry() => headless.OnUiThread(() =>
    {
        var results = new List<object>();
        foreach (var refreshHz in new[] { 60, 120 })
        {
            using var rig = new PaneWorkspaceRig();
            rig.Workspace.Open(PaneKind.Browser);
            rig.Workspace.Open(PaneKind.Artifacts);
            rig.Settle();
            var timings = new List<double>();
            var allocations = new List<long>();
            void Change(Action change)
            {
                var from = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                change();
                rig.Layout();
                timings.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                allocations.Add(GC.GetAllocatedBytesForCurrentThread() - from);
                for (var frame = 0; frame < 120 && rig.Workspace.IsMoving; frame++)
                {
                    from = GC.GetAllocatedBytesForCurrentThread();
                    start = Stopwatch.GetTimestamp();
                    rig.Frame(1000.0 / refreshHz);
                    timings.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    allocations.Add(GC.GetAllocatedBytesForCurrentThread() - from);
                    Assert.True(rig.Workspace.ChatBounds.Width >= PaneKinds.ChatMinWidth);
                    foreach (var pane in new[] { PaneKind.Browser, PaneKind.Artifacts })
                        if (rig.Workspace.CardOf(pane) is { IsVisible: true } card)
                        {
                            Assert.True(double.IsFinite(card.Bounds.Width) && double.IsFinite(card.Bounds.Height));
                            Assert.True(card.Bounds.Width >= 0 && card.Bounds.Height >= 0);
                        }
                }
                Assert.False(rig.Workspace.IsMoving, "each change settles within the bounded 120-frame sample");
            }
            for (var run = 0; run < 20; run++)
            {
                Change(() => rig.Workspace.ToggleExpanded(PaneKind.Browser));
                Change(rig.Workspace.RestoreExpanded);
                Change(() => rig.Workspace.MoveEarlier(PaneKind.Artifacts));
                Change(() => rig.Workspace.MoveLater(PaneKind.Artifacts));
                Change(() => rig.Workspace.Close(PaneKind.Artifacts));
                Change(() => rig.Workspace.Open(PaneKind.Artifacts));
            }
            timings.Sort();
            double Percentile(double rank) => timings[(int)Math.Ceiling(rank * timings.Count) - 1];
            results.Add(new
            {
                kind = "Headless Debug managed frame and layout; fake bodies; warm; no physical paint or display",
                utc = DateTimeOffset.UtcNow, refreshHz, runs = 20, operations = 120, samples = timings.Count,
                p50Ms = Percentile(.5), p95Ms = Percentile(.95), p99Ms = Percentile(.99), maxMs = timings[^1],
                over8Ms = timings.Count(time => time >= 8), targetMs = 8,
                meanFrameAllocationBytes = allocations.Average(),
                physicalFramesDropped = (int?)null,
            });
            Assert.True(timings.Count > 1000, "all twenty runs must contain their actual motion frames");
        }
        var folder = Path.Combine(AppContext.BaseDirectory, "w20-evidence");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "workspace-performance.json"), JsonSerializer.Serialize(new { schema = 1, results }, new JsonSerializerOptions { WriteIndented = true }));
    });
}
