using System.Diagnostics;
using System.Runtime;
using System.Text.Json;
using Avalonia.Threading;
using Marrow.Core.Support;
using Marrow.UI.Port;

namespace Marrow.App.Port;

// PORT-ONLY: T44 bounded diagnostic recording. Active only for an explicitly marked copied profile.
internal static class DesktopPerformanceProbe
{
    internal static void Start()
    {
        if (DevDataRoot.PerformanceRoot.Current.Length == 0) return;
        var root = DevDataRoot.PerformanceRoot.Current;
        var trace = new AuroraBootTrace();
        AuroraBootTrace.Current = trace;
        _ = Task.Run(async () =>
        {
            var begin = Stopwatch.GetTimestamp();
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
            var waiting = 0;
            var answer = new Action(() => { trace.RecordQueueDelay(); Volatile.Write(ref waiting, 0); });
            while (Stopwatch.GetElapsedTime(begin).TotalSeconds < 60 && await timer.WaitForNextTickAsync().ConfigureAwait(false))
            {
                if (Interlocked.CompareExchange(ref waiting, 1, 0) != 0) continue;
                trace.QueuePosted();
                Dispatcher.UIThread.Post(answer, DispatcherPriority.Input);
            }
            if (ReferenceEquals(AuroraBootTrace.Current, trace)) AuroraBootTrace.Current = null;
            var process = Process.GetCurrentProcess();
            await File.WriteAllTextAsync(Path.Combine(root, "performance.json"), JsonSerializer.Serialize(new
            {
                frequency = AuroraBootTrace.Frequency, serverGc = GCSettings.IsServerGC,
                workingSet = process.WorkingSet64, cpuSeconds = process.TotalProcessorTime.TotalSeconds,
                events = trace.Snapshot(),
            })).ConfigureAwait(false);
            _ = MeasureQuiet(root);
        });
    }

    // Runtime counters allocate inside the observed process. Sample total allocation directly
    // after recording and warm-up have ended, with just one delayed continuation in the interval.
    private static async Task MeasureQuiet(string root)
    {
        await Task.Delay(TimeSpan.FromSeconds(100)).ConfigureAwait(false);
        using var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime.TotalSeconds;
        var allocated = GC.GetTotalAllocatedBytes(precise: true);
        var started = Stopwatch.GetTimestamp();
        await Task.Delay(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        process.Refresh();
        var cpuSeconds = process.TotalProcessorTime.TotalSeconds - cpu;
        var elapsedSeconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
        var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocated;
        await File.WriteAllTextAsync(Path.Combine(root, "quiet-performance.json"), JsonSerializer.Serialize(new
        {
            elapsedSeconds, cpuSeconds, allocatedBytes, workingSet = process.WorkingSet64,
            privateBytes = process.PrivateMemorySize64, utc = DateTime.UtcNow,
        })).ConfigureAwait(false);
    }
}
