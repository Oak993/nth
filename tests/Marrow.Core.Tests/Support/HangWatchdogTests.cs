using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using Marrow.Core.Port;
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
        Assert.Equal(Path.Combine(scratch.FullPath, "nth", "Logs"), folder);
        Assert.Equal("hang-2026-09-23T10-20-30Z.txt", HangWatchdog.ReportFileName(date));
        var report = HangWatchdog.ReportText(4.26, "1.6.2", "37", date, "thread 1\n");
        var expected =
            "nth main thread unresponsive for 4.3s\n" +
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
    [Trait("Decision", "ED-163")] // Core/Support/HangWatchdog.swift:18; actual logging is not asserted
    public void Logging_channel_preserves_the_two_part_identity_with_the_approved_rename()
    {
        Assert.Equal("iordv.marrow", HangWatchdog.LogSubsystem);
        Assert.Equal("hang", HangWatchdog.LogCategory);
    }

    // The watchdog thread below is a real thread; the manual clock drives its 0.5 s sleep (R-GCD-3) and the test
    // thread plays the main loop. ActiveTimers == 1 means the watchdog is asleep again after a ping and a check.

    [Fact]
    [Trait("Req", "PR-LAUNCH-015")] // Core/Support/HangWatchdog.swift:50-56,62-69
    [Trait("Req", "PR-LAUNCH-080")] // Core/Support/HangWatchdog.swift:52, the 0.5 s cadence
    public void Pings_go_to_main_every_half_second_and_the_pong_is_stamped_when_main_runs_it()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var runner = NewRunner(scratch, (window, main) => "");
        var started = runtime.Time.GetUtcNow();
        runner.Start();
        Assert.Equal(started, runner.State.LastPong);

        Poll.Until(() => runtime.Time.ActiveTimers == 1);
        runtime.Time.Advance(TimeSpan.FromMilliseconds(499));
        Assert.Equal(0, runtime.Loop.Pending);
        runtime.Time.Advance(TimeSpan.FromMilliseconds(1));
        Poll.Until(() => runtime.Loop.Pending == 1);
        Poll.Until(() => runtime.Time.ActiveTimers == 1);
        Assert.Equal(started, runner.State.LastPong);
        Assert.Null(runner.State.MainThreadOsId);

        runtime.Time.Advance(TimeSpan.FromMilliseconds(200));
        Assert.Equal(1, runtime.RunUntilIdle());
        Assert.Equal(started.AddMilliseconds(700), runner.State.LastPong);
        Assert.Equal(StackSampler.CurrentOsThreadId(), runner.State.MainThreadOsId);

        runtime.Time.Advance(TimeSpan.FromMilliseconds(299));
        Assert.Equal(0, runtime.Loop.Pending);
        runtime.Time.Advance(TimeSpan.FromMilliseconds(1));
        Poll.Until(() => runtime.Loop.Pending == 1);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-016")] // Core/Support/HangWatchdog.swift:24,71-81
    [Trait("Req", "PR-LAUNCH-017")] // Core/Support/HangWatchdog.swift:28,83-84, the capture on the watchdog thread
    [Trait("Req", "PR-LAUNCH-018")] // Core/Support/HangWatchdog.swift:86-93,113-114
    [Trait("Req", "PR-LAUNCH-020")] // Core/Support/HangWatchdog.swift:18
    [Trait("Req", "UI-LAUNCH-008")] // Core/Support/HangWatchdog.swift:85-121
    public void A_four_second_stall_writes_one_report_and_two_log_lines_and_the_next_stall_gets_its_own()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var lines = new ConcurrentQueue<(string Message, string Channel)>();
        using var sink = Logger.Sink.Override((message, channel) => lines.Enqueue((message, channel)));
        var calls = new ConcurrentQueue<(TimeSpan Window, uint? Main, string? Thread, bool Background)>();
        var runner = NewRunner(scratch, (window, main) =>
        {
            calls.Enqueue((window, main, Thread.CurrentThread.Name, Thread.CurrentThread.IsBackground));
            return "STACKS\n";
        });
        runner.Start();

        for (var tick = 1; tick <= 7; tick++) Step(runtime);
        Poll.Until(() => runtime.Time.ActiveTimers == 1);
        Assert.Empty(lines);
        Step(runtime);
        Poll.Until(() => lines.Count == 2);
        Poll.Until(() => runtime.Time.ActiveTimers == 1);

        var logs = Path.Combine(scratch.FullPath, "nth", "Logs");
        var first = Path.Combine(logs, "hang-2026-09-22T12-00-04Z.txt");
        Assert.Equal(
            new (string, string)[] { ("Main thread unresponsive for 4.0s, sampling", "iordv.marrow/hang"), ("Hang report written to " + first, "iordv.marrow/hang") },
            lines.ToArray());
        Assert.Equal(Encoding.UTF8.GetBytes(
            "nth main thread unresponsive for 4.0s\n" +
            "Version 1.6.2 (20)\n" +
            "Sampled at 2026-09-22 12:00:04 +0000\n" +
            "\n" +
            "STACKS\n"), File.ReadAllBytes(first));
        Assert.Equal(new (TimeSpan, uint?, string?, bool)[] { (TimeSpan.FromSeconds(2), null, "hang-watchdog", true) }, calls.ToArray());

        // The same stall: further checks return early.
        Step(runtime);
        Step(runtime);
        Poll.Until(() => runtime.Time.ActiveTimers == 1);
        Assert.Equal(2, lines.Count);

        // Main answers at 5 s; the next report comes 4 s later, with the main thread named.
        runtime.RunUntilIdle();
        for (var tick = 1; tick <= 7; tick++) Step(runtime);
        Poll.Until(() => runtime.Time.ActiveTimers == 1);
        Assert.Equal(2, lines.Count);
        Step(runtime);
        Poll.Until(() => lines.Count == 4);
        var second = Path.Combine(logs, "hang-2026-09-22T12-00-09Z.txt");
        Assert.Equal(("Hang report written to " + second, "iordv.marrow/hang"), lines.ToArray()[3]);
        Assert.Equal("Main thread unresponsive for 4.0s, sampling", lines.ToArray()[2].Message);
        Assert.StartsWith("nth main thread unresponsive for 4.0s\nVersion 1.6.2 (20)\nSampled at 2026-09-22 12:00:09 +0000\n\n",
            Encoding.UTF8.GetString(File.ReadAllBytes(second)), StringComparison.Ordinal);
        Assert.Equal(StackSampler.CurrentOsThreadId(), calls.ToArray()[1].Main);
        Assert.Equal(2, Directory.GetFiles(logs).Length);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-018")] // Core/Support/HangWatchdog.swift:87,113-114, try? then the log line
    public void A_failed_report_write_is_silent_and_the_written_to_line_is_logged_anyway()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        Directory.CreateDirectory(Path.Combine(scratch.FullPath, "nth"));
        File.WriteAllBytes(Path.Combine(scratch.FullPath, "nth", "Logs"), [1]);
        var lines = new ConcurrentQueue<(string Message, string Channel)>();
        using var sink = Logger.Sink.Override((message, channel) => lines.Enqueue((message, channel)));
        var runner = NewRunner(scratch, (window, main) => "STACKS\n");
        runner.Start();

        for (var tick = 1; tick <= 8; tick++) Step(runtime);
        Poll.Until(() => lines.Count == 2);
        Poll.Until(() => runtime.Time.ActiveTimers == 1);
        var file = Path.Combine(scratch.FullPath, "nth", "Logs", "hang-2026-09-22T12-00-04Z.txt");
        Assert.Equal(("Hang report written to " + file, "iordv.marrow/hang"), lines.ToArray()[1]);
        Assert.False(File.Exists(file));
        Assert.True(runner.Thread!.IsAlive);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-019")] // Core/Support/HangWatchdog.swift:115,123-130
    public void Each_written_report_is_followed_by_a_trim_to_the_ten_newest()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var logs = Path.Combine(scratch.FullPath, "nth", "Logs");
        Directory.CreateDirectory(logs);
        for (var i = 0; i < 10; i++) File.WriteAllText(Path.Combine(logs, $"hang-2026-09-22T11-00-{i:00}Z.txt"), "old");
        File.WriteAllText(Path.Combine(logs, "notes.txt"), "keep");
        var lines = new ConcurrentQueue<(string Message, string Channel)>();
        using var sink = Logger.Sink.Override((message, channel) => lines.Enqueue((message, channel)));
        var runner = NewRunner(scratch, (window, main) => "STACKS\n");
        runner.Start();

        for (var tick = 1; tick <= 8; tick++) Step(runtime);
        Poll.Until(() => lines.Count == 2);
        Poll.Until(() => runtime.Time.ActiveTimers == 1);
        Assert.False(File.Exists(Path.Combine(logs, "hang-2026-09-22T11-00-00Z.txt")));
        Assert.True(File.Exists(Path.Combine(logs, "hang-2026-09-22T11-00-01Z.txt")));
        Assert.True(File.Exists(Path.Combine(logs, "hang-2026-09-22T12-00-04Z.txt")));
        Assert.True(File.Exists(Path.Combine(logs, "notes.txt")));
        Assert.Equal(10, Directory.GetFiles(logs, "hang-*.txt").Length);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-017")] // Core/Support/HangWatchdog.swift:91-93,108,111,113
    public void Capture_failure_bodies_follow_the_header_and_missing_bundle_values_print_a_question_mark()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var lines = new ConcurrentQueue<(string Message, string Channel)>();
        using var sink = Logger.Sink.Override((message, channel) => lines.Enqueue((message, channel)));
        var failure = "stack capture could not run: The operation was canceled.\n";
        var runner = new HangWatchdog.Runner((window, main) => failure, () => scratch.FullPath, key => null);
        runner.Start();

        for (var tick = 1; tick <= 8; tick++) Step(runtime);
        Poll.Until(() => lines.Count == 2);
        var file = Path.Combine(scratch.FullPath, "nth", "Logs", "hang-2026-09-22T12-00-04Z.txt");
        Assert.Equal(Encoding.UTF8.GetBytes(
            "nth main thread unresponsive for 4.0s\nVersion ? (?)\nSampled at 2026-09-22 12:00:04 +0000\n\n" + failure),
            File.ReadAllBytes(file));

        var date = new DateTimeOffset(2026, 9, 23, 10, 20, 30, TimeSpan.Zero);
        var ended = "stack capture ended early: no end of trace within 10 s\n\nThread 7:\n  1 samples\n    [Native Frames]\n";
        Assert.Equal(
            "nth main thread unresponsive for 5.0s\nVersion 1.6.2 (20)\nSampled at 2026-09-23 10:20:30 +0000\n\n" + ended,
            HangWatchdog.ReportText(4.96, "1.6.2", "20", date, ended));
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-021")] // Core/Support/HangWatchdog.swift:17-131, no stop; :50-59 the thread
    [Trait("Req", "PR-LAUNCH-080")] // Core/Support/HangWatchdog.swift:42-49, one loop however often start() runs
    public void Start_is_idempotent_and_the_watchdog_is_one_background_thread_with_no_way_to_stop_it()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var runner = NewRunner(scratch, (window, main) => "");
        runner.Start();
        var thread = runner.Thread;
        runner.Start();
        Assert.Same(thread, runner.Thread);
        Poll.Until(() => runtime.Time.ActiveTimers >= 1);
        Thread.Sleep(200);
        Assert.Equal(1, runtime.Time.ActiveTimers);
        runtime.Time.Advance(TimeSpan.FromMilliseconds(500));
        Poll.Until(() => runtime.Loop.Pending >= 1);
        Thread.Sleep(200);
        Assert.Equal(1, runtime.Loop.Pending);

        Assert.NotNull(thread);
        Assert.True(thread.IsBackground);
        Assert.Equal("hang-watchdog", thread.Name);
        Assert.True(thread.IsAlive);
        Assert.Equal(new[] { "Start" }, typeof(HangWatchdog)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Select(m => m.Name).ToArray());
    }

    private static HangWatchdog.Runner NewRunner(ScratchFolder scratch, Func<TimeSpan, uint?, string> capture) =>
        new(capture, () => scratch.FullPath, key => key switch
        {
            "CFBundleShortVersionString" => "1.6.2",
            "CFBundleVersion" => "20",
            _ => null,
        });

    private static void Step(TestRuntime runtime)
    {
        Poll.Until(() => runtime.Time.ActiveTimers == 1);
        runtime.Time.Advance(TimeSpan.FromMilliseconds(500));
    }
}
