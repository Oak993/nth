using System.Text;
using Marrow.Core.Support;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Support;

public sealed class HangWatchdogTests
{
    [Fact]
    [Trait("Rule", "R-GCD-3")] // Core/Support/HangWatchdog.swift:62-81, before the capture site
    public void Pong_is_timed_when_main_runs_and_unlocks_one_report_for_the_next_stall()
    {
        using var runtime = new TestRuntime();
        var state = new HangWatchdog.State(runtime.Time.GetUtcNow());
        runtime.AdvanceMilliseconds(3999);
        Assert.Null(state.Check(runtime.Time.GetUtcNow()));
        runtime.AdvanceMilliseconds(1);
        Assert.Equal(4, state.Check(runtime.Time.GetUtcNow()));
        Assert.Null(state.Check(runtime.Time.GetUtcNow()));

        runtime.Loop.Enqueue(() => state.Pong(runtime.Time.GetUtcNow()));
        runtime.Time.Advance(TimeSpan.FromSeconds(2));
        Assert.Null(state.Check(runtime.Time.GetUtcNow()));
        runtime.RunUntilIdle();
        runtime.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(4, state.Check(runtime.Time.GetUtcNow()));
    }

    [Fact]
    [Trait("Decision", "ED-003")] // Core/Support/HangWatchdog.swift:24,28,52; no stack capture is asserted
    public void Watchdog_cadences_and_capture_window_keep_the_source_numbers()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(500), HangWatchdog.PingInterval);
        Assert.Equal(TimeSpan.FromSeconds(4), HangWatchdog.StallThreshold);
        Assert.Equal(TimeSpan.FromSeconds(2), HangWatchdog.SampleWindow);
    }

    [Fact]
    [Trait("Decision", "ED-003")] // Core/Support/HangWatchdog.swift:86-93,118-121; not the complete report
    public void Report_path_and_utf8_header_preserve_the_approved_windows_identity_and_line_order()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var date = new DateTimeOffset(2026, 9, 23, 10, 20, 30, TimeSpan.Zero);
        var folder = HangWatchdog.ReportsFolder(scratch.FullPath);
        Assert.Equal(Path.Combine(scratch.FullPath, "Marrow Code", "Logs"), folder);
        Assert.Equal("hang-2026-09-23T10-20-30Z.txt", HangWatchdog.ReportFileName(date));
        var report = HangWatchdog.ReportText(4.26, "1.6.2", "37", date, "thread 1\n");
        var expected =
            "Marrow main thread unresponsive for 4.3s\n" +
            "Version 1.6.2 (37)\n" +
            "Sampled at 2026-09-23 10:20:30 +0000\n\n" +
            "thread 1\n";
        Assert.Equal(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(report));
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-019")] // Core/Support/HangWatchdog.swift:30-32,123-130
    public void Report_trim_keeps_ten_newest_matching_names_and_leaves_other_files()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        for (var i = 0; i < 12; i++)
            File.WriteAllText(scratch.PathOf($"hang-2026-09-23T10-00-{i:00}Z.txt"), "stack");
        File.WriteAllText(scratch.PathOf("other.txt"), "keep");
        File.WriteAllText(scratch.PathOf("hang-not-a-report.log"), "keep");
        HangWatchdog.TrimReports(scratch.FullPath);
        Assert.Equal(10, Directory.GetFiles(scratch.FullPath, "hang-*.txt").Length);
        Assert.False(File.Exists(scratch.PathOf("hang-2026-09-23T10-00-00Z.txt")));
        Assert.False(File.Exists(scratch.PathOf("hang-2026-09-23T10-00-01Z.txt")));
        Assert.True(File.Exists(scratch.PathOf("hang-2026-09-23T10-00-11Z.txt")));
        Assert.True(File.Exists(scratch.PathOf("other.txt")));
        Assert.True(File.Exists(scratch.PathOf("hang-not-a-report.log")));
    }

    [Fact]
    [Trait("Decision", "ED-003")] // Core/Support/HangWatchdog.swift:18; actual logging is not asserted
    public void Logging_channel_preserves_the_two_part_identity_with_the_approved_rename()
    {
        Assert.Equal("iordv.marrow", HangWatchdog.LogSubsystem);
        Assert.Equal("hang", HangWatchdog.LogCategory);
    }
}
