using System.Diagnostics;
using System.Globalization;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl.Latency;

// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-8; DESIGN.md 2.11 budgets, 5.5 latency suite). On fakes, so only
// Tactus's own work is timed: engine overhead per action at most 0.5 ms at p50 and 2 ms at p95 over 1,000 runs after a
// warm-up, a warm observe diff within 25 ms at p95, a cold look at about 1,500 elements within 200 ms at p95, each settle
// within 150 ms of the app going quiet, and no fixed sleep anywhere: the fake clock records every delay, and every wait
// must end on a change event or a timeout instead.
[Trait("Decision", "ED-NEW-pcctl-8")]
[Collection(PcLatencyCollection.Name)]
public sealed class PcLatencyTests
{
    private const string Look = "{\"window\":\"w:1a\",\"screenshot\":\"no\"}";

    private static double Percentile(List<double> samples, double share)
    {
        var sorted = samples.Order().ToList();
        return sorted[Math.Max(0, (int)Math.Ceiling(share * sorted.Count) - 1)];
    }

    private static string Ms(double ms) => ms.ToString("0.000", CultureInfo.InvariantCulture) + " ms";

    private static async Task<List<double>> Time(int warmUp, int runs, Func<int, Task> run)
    {
        for (var i = 0; i < warmUp; i++) await run(i);
        var samples = new List<double>(runs);
        for (var i = 0; i < runs; i++)
        {
            var watch = Stopwatch.StartNew();
            await run(warmUp + i);
            samples.Add(watch.Elapsed.TotalMilliseconds);
        }
        return samples;
    }

    [Fact]
    public async Task Engine_overhead_per_action_is_half_a_millisecond_at_p50_and_two_at_p95()
    {
        var harness = new PcEngineHarness();
        harness.Notepad();
        var engine = harness.Engine();
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", Look)).Ok);
        var samples = await Time(200, 1000, async i =>
        {
            var result = i % 2 == 0
                ? await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"element\":4}")
                : await PcEngineHarness.Call(engine, "pc_set_value", "{\"window\":\"w:1a\",\"element\":2,\"value\":\"v" + i.ToString(CultureInfo.InvariantCulture) + "\"}");
            Assert.True(result.Ok, result.Render());
        });
        var p50 = Percentile(samples, 0.50);
        var p95 = Percentile(samples, 0.95);
        Assert.True(p50 <= 0.5 && p95 <= 2.0, "p50 " + Ms(p50) + ", p95 " + Ms(p95));
    }

    [Fact]
    public async Task A_warm_observe_diff_is_within_25_ms_and_a_cold_look_at_1500_elements_within_200_ms_at_p95()
    {
        var harness = new PcEngineHarness();
        var app = harness.Desktop.AddApp(0x1a, "Big - notepad", "notepad.exe", 101, new PcRect(100, 100, 1200, 800));
        // 30 groups of 50 rows: about 1,500 elements.
        for (var g = 0; g < 30; g++)
        {
            var group = app.Root.Add(new PcFakeNode("1.26." + g.ToString(CultureInfo.InvariantCulture), "group", "Section " + g.ToString(CultureInfo.InvariantCulture)) { Bounds = new PcRect(110, 110, 1100, 700) });
            for (var r = 0; r < 49; r++)
                group.Add(new PcFakeNode("1.26." + g.ToString(CultureInfo.InvariantCulture) + "." + r.ToString(CultureInfo.InvariantCulture), "button", "Row " + r.ToString(CultureInfo.InvariantCulture)) { Actions = PcActions.Invoke, Bounds = new PcRect(120, 120 + r, 200, 12) });
        }
        Assert.InRange(app.Root.All().Count(), 1450, 1600);
        var engine = harness.Engine();
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", Look)).Ok);
        var warm = await Time(50, 1000, async _ => Assert.True((await PcEngineHarness.Call(engine, "pc_observe", Look)).Ok));
        Assert.True(Percentile(warm, 0.95) <= 25, "warm p95 " + Ms(Percentile(warm, 0.95)));
        var cold = await Time(5, 50, async i =>
        {
            app.Root.Children[i % 30].Name = "Section " + i.ToString(CultureInfo.InvariantCulture);
            harness.Desktop.Bump(0x1a, structure: true);
            Assert.True((await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:1a\",\"screenshot\":\"no\",\"full\":true}")).Ok);
        });
        Assert.True(Percentile(cold, 0.95) <= 200, "cold p95 " + Ms(Percentile(cold, 0.95)));
    }

    [Fact]
    public async Task Every_action_settles_within_150_ms_of_the_app_going_quiet_and_nothing_sleeps()
    {
        var harness = new PcEngineHarness();
        var app = harness.Notepad();
        app.Root.Add(new PcFakeNode("1.26.5", "list", "Files") { Actions = PcActions.Scroll, ScrollPercent = 0, Bounds = new PcRect(700, 200, 300, 400) });
        app.Root.Children[3].OnScroll = (_, node, _) => node.ScrollPercent = Math.Min(100, (node.ScrollPercent ?? 0) + 25);
        app.Root.Children[1].OnInvoke = (_, _) => app.Title = "Saved - notepad";
        var engine = harness.Engine();
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", Look)).Ok);
        var steps = new[]
        {
            ("pc_click", "{\"window\":\"w:1a\",\"element\":4}"),
            ("pc_set_value", "{\"window\":\"w:1a\",\"element\":2,\"value\":\"hello\"}"),
            ("pc_type", "{\"window\":\"w:1a\",\"element\":2,\"text\":\" there\"}"),
            ("pc_scroll", "{\"window\":\"w:1a\",\"element\":5,\"direction\":\"down\"}"),
            ("pc_key", "{\"window\":\"w:1a\",\"keys\":\"ctrl+s\"}"),
            ("pc_observe", Look),
        };
        foreach (var (tool, json) in steps)
        {
            var start = harness.Desktop.Clock.NowMs;
            var result = await PcEngineHarness.Call(engine, tool, json);
            Assert.True(result.Ok, tool + ": " + result.Render());
            Assert.InRange(harness.Desktop.Clock.NowMs - start, 0, 150);
        }
        // A wait wakes on the app's own change event, not on a poll.
        var wait = harness.Desktop.Clock.NowMs;
        harness.Desktop.Schedule(400, () =>
        {
            app.Root.Add(new PcFakeNode("1.26.9", "text", "Upload done") { Bounds = new PcRect(300, 700, 120, 20) });
            harness.Desktop.Bump(0x1a, structure: true);
        });
        Assert.True((await PcEngineHarness.Call(engine, "pc_wait", "{\"window\":\"w:1a\",\"until\":\"text\",\"text\":\"Upload done\",\"timeout_ms\":2000}")).Ok);
        Assert.InRange(harness.Desktop.Clock.NowMs - wait, 400, 400 + PcFakeDesktop.EventLatencyMs);
        Assert.Empty(harness.Desktop.Clock.Delays);
    }
}
