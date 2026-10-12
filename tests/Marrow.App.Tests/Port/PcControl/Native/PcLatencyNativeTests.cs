// PORT-ONLY: Tactus native probe NP-8, latency and overhead (unit 15, P19; ED-NEW-pcctl-43; DESIGN.md 2.11 targets, 5.4
// item 11, critiques C21 to C23). NATIVE: (1) the ten-field form plus Save as one batch, five times, against the 0.6 s
// p50 and 1.5 s p95 tool-time targets, with the cold observe, the warm diff and a screenshot observe timed alongside; (2)
// the user-side cost of Tactus in Chrome, Edge, VS Code and Slack when the owner opened them first (CPU and memory of all
// the app's processes for 8 s with Tactus idle in it, against 8 s before it looked; budget 3 % CPU and 50 MB); (3) a
// screenshot every second for 30 s of a video window the owner plays in Edge, while the owner reads the GPU column in
// Task Manager. Only RESULT lines decide. Runs only from the smoke script.
using System.Diagnostics;
using System.Globalization;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl;

namespace Marrow.App.Tests.Port.PcControl.Native;

[Trait("Decision", "ED-NEW-pcctl-43")]
public sealed class PcLatencyNativeTests
{
    private const string FormFields =
        "for ($i = 1; $i -le 10; $i++) { $t = New-Object System.Windows.Forms.TextBox; $t.Name = 'f' + $i; $t.AccessibleName = 'Field ' + $i; $t.Top = 30 * $i; $t.Left = 20; $t.Width = 300; $f.Controls.Add($t) }; "
        + "$b = New-Object System.Windows.Forms.Button; $b.Text = 'Save'; $b.Top = 340; $b.Left = 20; "
        + "$b.Add_Click({ ($f.Controls | Where-Object { $_ -is [System.Windows.Forms.TextBox] } | ForEach-Object { $_.Text }) -join ',' | Set-Content ($env:TEMP + '\\tactus-smoke\\np8-form.txt') }); $f.Controls.Add($b);";

    private static string Ms(double ms) => ms.ToString("F0", CultureInfo.InvariantCulture) + " ms";

    private static double Percentile(List<double> values, double p)
    {
        var sorted = values.Order().ToList();
        return sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(p * sorted.Count) - 1)];
    }

    private static async Task<(PcResult Result, double Ms)> Timed(PcSmokeRig rig, string tool, object arguments)
    {
        var watch = Stopwatch.StartNew();
        var result = await rig.Call(tool, arguments);
        return (result, watch.Elapsed.TotalMilliseconds);
    }

    [Fact]
    public async Task NP8_The_ten_field_form_plus_Save_is_one_batch_within_the_tool_time_targets()
    {
        using var rig = new PcSmokeRig("np8-form");
        var handle = await rig.Form("Tactus smoke form", FormFields);
        var window = PcWindowRef.Format(handle);
        var saved = Path.Combine(Path.GetTempPath(), "tactus-smoke", "np8-form.txt");

        var cold = await Timed(rig, "pc_observe", new { window, screenshot = "no", full = true });
        var warm = await Timed(rig, "pc_observe", new { window, screenshot = "no" });
        var shot = await Timed(rig, "pc_observe", new { window, screenshot = "yes" });
        Assert.True(cold.Result.Ok, cold.Result.Render());
        rig.Result("observe cold " + Ms(cold.Ms) + " (80/200), warm diff " + Ms(warm.Ms) + " (8/25), with screenshot " + Ms(shot.Ms));

        var times = new List<double>();
        for (var run = 1; run <= 5; run++)
        {
            File.Delete(saved);
            var actions = Enumerable.Range(1, 10)
                .Select(i => (object)new { tool = "pc_set_value", target = new { name = "Field " + i }, value = "run " + run + " value " + i })
                .Append(new { tool = "pc_click", target = new { name = "Save", type = "button" } })
                .ToArray();
            var batch = await Timed(rig, "pc_batch", new { window, actions });
            Assert.True(batch.Result.Ok, "run " + run + ": " + batch.Result.Render());
            times.Add(batch.Ms);
            var deadline = Environment.TickCount64 + 5_000;
            while (!File.Exists(saved) && Environment.TickCount64 < deadline) await Task.Delay(50);
            Assert.Contains("run " + run + " value 10", File.ReadAllText(saved), StringComparison.Ordinal);
        }

        rig.Result("ten fields plus Save, one batch: p50 " + Ms(Percentile(times, 0.5)) + " (target 600), p95 " + Ms(Percentile(times, 0.95)) + " (target 1500); runs " + string.Join(", ", times.Select(Ms)));
        PcSmokeRig.Close(handle);
    }

    [Fact]
    public async Task NP8_Tactus_idle_in_a_Chromium_or_Electron_app_stays_within_the_overhead_budget()
    {
        var apps = new[] { "chrome.exe", "msedge.exe", "code.exe", "slack.exe" };
        using var rig = new PcSmokeRig("np8-overhead", alsoApprove: apps);
        var measured = 0;
        foreach (var exe in apps)
        {
            if (PcSmokeRig.WindowsOf(exe) is not [var first, ..])
            {
                rig.Result(exe + " not open: skipped");
                continue;
            }
            var before = await Usage(exe, 8_000);
            var look = await rig.Call("pc_observe", new { window = PcWindowRef.Format(first.Handle), screenshot = "no" });
            var during = await Usage(exe, 8_000);
            measured++;
            var extraCpu = during.CpuPercent - before.CpuPercent;
            var extraMb = (during.Bytes - before.Bytes) / (1024.0 * 1024.0);
            rig.Result(exe + " (observe " + (look.Error?.ToString() ?? look.Status.ToString()) + "): CPU " + before.CpuPercent.ToString("F2", CultureInfo.InvariantCulture) + "% then "
                + during.CpuPercent.ToString("F2", CultureInfo.InvariantCulture) + "% (extra " + extraCpu.ToString("F2", CultureInfo.InvariantCulture) + "%, budget 3%), memory extra "
                + extraMb.ToString("F1", CultureInfo.InvariantCulture) + " MB (budget 50) -> " + (extraCpu <= 3 && extraMb <= 50 ? "within" : "OVER: poll on demand for this app"));
        }
        rig.Result(measured + " apps measured");
    }

    [Fact]
    public async Task NP8_A_capture_pool_on_a_playing_video_is_timed_while_the_owner_reads_the_GPU()
    {
        using var rig = new PcSmokeRig("np8-video", alsoApprove: ["msedge.exe"]);
        var video = PcSmokeRig.WindowsOf("msedge.exe").FirstOrDefault();
        Assert.NotEqual(0, video.Handle);
        var window = PcWindowRef.Format(video.Handle);
        rig.Result("watching \"" + video.Title + "\" for 30 s: read Task Manager's GPU column for Edge and for the test host now");

        var times = new List<double>();
        for (var i = 0; i < 30; i++)
        {
            var (result, ms) = await Timed(rig, "pc_observe", new { window, screenshot = "yes", tree = false });
            if (result.Image is not null) times.Add(ms);
            await Task.Delay(Math.Max(0, 1_000 - (int)ms));
        }
        rig.Result(times.Count + "/30 screenshots; p50 " + (times.Count > 0 ? Ms(Percentile(times, 0.5)) : "-") + ", p95 " + (times.Count > 0 ? Ms(Percentile(times, 0.95)) : "-") + " (grab plus JPEG 15/35, plus the tree read)");
        Assert.NotEmpty(times);
    }

    // The CPU (share of one machine, all cores) and the working set of every process of an exe over a window of time.
    private static async Task<(double CpuPercent, long Bytes)> Usage(string exe, int ms)
    {
        var name = Path.GetFileNameWithoutExtension(exe);
        static TimeSpan Cpu(Process[] processes) => processes.Aggregate(TimeSpan.Zero, (sum, p) =>
        {
            try { return sum + p.TotalProcessorTime; }
            catch (InvalidOperationException) { return sum; }
            catch (System.ComponentModel.Win32Exception) { return sum; }
        });
        var start = Process.GetProcessesByName(name);
        var cpuStart = Cpu(start);
        var watch = Stopwatch.StartNew();
        await Task.Delay(ms);
        var end = Process.GetProcessesByName(name);
        var cpu = (Cpu(end) - cpuStart).TotalMilliseconds / (watch.Elapsed.TotalMilliseconds * Environment.ProcessorCount) * 100;
        long bytes = 0;
        foreach (var process in end)
        {
            try { bytes += process.WorkingSet64; }
            catch (InvalidOperationException) { }
        }
        foreach (var process in start.Concat(end)) process.Dispose();
        return (cpu, bytes);
    }
}
