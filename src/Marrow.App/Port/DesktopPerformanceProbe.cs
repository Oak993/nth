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
    internal static void BeginStartup()
    {
        if (DevDataRoot.PerformanceRoot.Current.Length != 0) AuroraBootTrace.Current = new AuroraBootTrace();
        InteractionRecorder.BeginStartup(DevDataRoot.PerformanceRoot.Current); // ED-NEW-stutterfix-1
    }

    internal static void ObserveWindow(Avalonia.Controls.Window window, string role)
    {
        if (DevDataRoot.PerformanceRoot.Current.Length != 0) WindowPaintProbe.Attach(window, role);
    }

    internal static void Start()
    {
        if (DevDataRoot.PerformanceRoot.Current.Length == 0) return;
        var root = DevDataRoot.PerformanceRoot.Current;
        var trace = AuroraBootTrace.Current ?? new AuroraBootTrace();
        AuroraBootTrace.Current = trace;
        Action<string, long, long, int, int> providerWork = (name, started, finished, thread, endThread) =>
        {
            if (ReferenceEquals(AuroraBootTrace.Current, trace)) trace.RecordExternalWork(name, started, finished, thread, endThread);
        };
        Marrow.Services.Providers.ProviderProbeTrace.Record = providerWork;
        InteractionRecorder.Start(root); // ED-NEW-stutterfix-1
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
            if (ReferenceEquals(Marrow.Services.Providers.ProviderProbeTrace.Record, providerWork))
                Marrow.Services.Providers.ProviderProbeTrace.Record = null;
            var process = Process.GetCurrentProcess();
            await WriteJson(root, "performance.json", new
            {
                frequency = AuroraBootTrace.Frequency, serverGc = GCSettings.IsServerGC,
                processStartedUtc = process.StartTime.ToUniversalTime(),
                recordedTimestamp = Stopwatch.GetTimestamp(),
                recordedUtc = DateTime.UtcNow,
                workingSet = process.WorkingSet64, cpuSeconds = process.TotalProcessorTime.TotalSeconds,
                events = trace.Snapshot(),
            }).ConfigureAwait(false);
            if (!InteractionRecorder.Enabled) _ = MeasureQuiet(root); // ED-NEW-stutterfix-1: the tour would overlap the quiet window
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
        await WriteJson(root, "quiet-performance.json", new
        {
            elapsedSeconds, cpuSeconds, allocatedBytes, workingSet = process.WorkingSet64,
            privateBytes = process.PrivateMemorySize64, utc = DateTime.UtcNow,
        }).ConfigureAwait(false);
    }

    // The probe must not retain a large pooled UTF-16 buffer just to report the real app's memory.
    private static async Task WriteJson<T>(string root, string name, T value)
    {
        await using var file = new FileStream(Path.Combine(root, name), FileMode.Create, FileAccess.Write,
            FileShare.Read, 16 * 1024, useAsync: true);
        await JsonSerializer.SerializeAsync(file, value).ConfigureAwait(false);
    }
}
