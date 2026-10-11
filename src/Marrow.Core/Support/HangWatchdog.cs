// Source: DroppyCode/Core/Support/HangWatchdog.swift (B2 1fbf81e), :17-131.
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Marrow.Core.Port;

[assembly: InternalsVisibleTo("Marrow.Core.Tests")]

namespace Marrow.Core.Support;

/// <summary>
/// :4-17. Catches the main thread standing still, and keeps what it was doing. A thread of its own pings main twice a
/// second; when a ping goes unanswered for <see cref="StallThreshold"/>, it captures every managed thread's stack and
/// writes it to %LOCALAPPDATA%\Marrow Code\Logs\hang-&lt;date&gt;.txt. One report per stall; the next stall gets one of
/// its own. Nothing in the app reads, lists or surfaces a report (ED-003, UI-LAUNCH-008).
/// </summary>
public static class HangWatchdog
{
    // :18. DEVIATION OD-0002: upstream subsystem "iordv.droppycode"; the port's one subsystem follows Marrow (ED-163).
    internal const string LogSubsystem = Logger.Subsystem;
    internal const string LogCategory = "hang";
    private static readonly Logger log = new(LogSubsystem, LogCategory);

    // :24. How long main may go without answering before it counts as stuck.
    internal static readonly TimeSpan StallThreshold = TimeSpan.FromSeconds(4);

    // :28 sampleSeconds. How long the capture watches the process.
    internal static readonly TimeSpan SampleWindow = TimeSpan.FromSeconds(2);

    // :32. Reports kept before the oldest is dropped.
    internal const int KeptReports = 10;

    // :52. The watchdog thread's sleep between pings.
    internal static readonly TimeSpan PingInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// PORT-ONLY (ED-NEW-lean-6). Where the watchdog asks whether the app is quiet: no window of it can be seen and none has
    /// stopped answering (Marrow.UI's WindowPresence sets it). A type of its own, so <see cref="HangWatchdog"/>'s public
    /// methods stay <see cref="Start"/> alone; the signal is published and read with full fences.
    /// </summary>
    public static class Quiet
    {
        private static Func<bool>? signal;

        /// <summary>The app's quiet signal (null: never quiet). Any thread; the watchdog's thread reads it every beat.</summary>
        public static void Use(Func<bool>? quiet) => Volatile.Write(ref signal, quiet);

        /// <summary>Whether the app is quiet now (the watchdog's thread).</summary>
        internal static bool Now => Volatile.Read(ref signal)?.Invoke() == true;
    }

    // :34. The process's one watchdog, made on first use as the Swift static is.
    private static readonly Lazy<Runner> shared =
        new(() => new Runner(StackSampler.Capture, LocalApplicationData, BundleValue), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>:42-60 <c>start()</c>. Idempotent; there is no stop (PR-LAUNCH-021).</summary>
    public static void Start() => shared.Value.Start();

    private static string LocalApplicationData() => DevDataRoot.LocalApplicationData(); // ED-212

    // :92 Bundle.main.infoDictionary?[key] as? String. Assembly metadata replaces the bundle's Info dictionary, as
    // AppInfo.FromAssembly reads it (Text.cs:37-43); a missing key is nil, which the header prints as "?".
    private static string? BundleValue(string key) =>
        (Assembly.GetEntryAssembly() ?? typeof(HangWatchdog).Assembly).GetCustomAttributes<AssemblyMetadataAttribute>()
            .LastOrDefault(attribute => attribute.Key == key)?.Value;

    /// <summary>:34-40 the locked state, and :43-48, :64-67, :72-77 the blocks that run under its lock.</summary>
    internal sealed class State(DateTimeOffset now)
    {
        private struct Fields
        {
            public DateTimeOffset LastPong;
            public bool ReportedThisStall;
            public bool Started;
            // ED-162: the main thread's OS id, which the capture marks " (main)"; set by the block main runs.
            public uint? MainThreadOsId;
        }

        private readonly Mutex<Fields> fields = new(new Fields { LastPong = now });

        internal DateTimeOffset LastPong => fields.WithLock((ref Fields value) => value.LastPong);

        internal uint? MainThreadOsId => fields.WithLock((ref Fields value) => value.MainThreadOsId);

        // :43-48
        internal bool Start(DateTimeOffset now) => fields.WithLock((ref Fields value) =>
        {
            if (value.Started) return false;
            value.Started = true;
            value.LastPong = now;
            return true;
        });

        // :64-67. A pong is timestamped when the main queue runs it, not when the ping is posted.
        internal void Pong(DateTimeOffset now) => fields.WithLock((ref Fields value) =>
        {
            value.LastPong = now;
            value.ReportedThisStall = false;
            value.MainThreadOsId = StackSampler.CurrentOsThreadId();
        });

        // ED-NEW-lean-6: watching resumes after a quiet spell; the wait for main's answer starts again now.
        internal void Resume(DateTimeOffset now) => fields.WithLock((ref Fields value) => { value.LastPong = now; });

        // :72-77
        internal double? Check(DateTimeOffset now) => fields.WithLock((ref Fields value) =>
        {
            var stalled = SwiftDate.TimeIntervalSince(now, value.LastPong);
            if (!(stalled >= StallThreshold.TotalSeconds) || value.ReportedThisStall) return (double?)null;
            value.ReportedThisStall = true;
            return stalled;
        });
    }

    /// <summary>
    /// R-GCD-3: the watchdog's thread and decisions, driven by <see cref="SwiftClock"/> so tests can drive them.
    /// <paramref name="capture"/> is the stack capture (<see cref="StackSampler.Capture(TimeSpan, uint?)"/>),
    /// <paramref name="localApplicationData"/> the per-user application data folder, <paramref name="bundleValue"/>
    /// the Info dictionary lookup.
    /// </summary>
    internal sealed class Runner(Func<TimeSpan, uint?, string> capture, Func<string> localApplicationData,
        Func<string, string?> bundleValue, Func<bool>? quiet = null)
    {
        /// <summary>ED-NEW-lean-6: whether the app is quiet (tests pass their own; else <see cref="HangWatchdog.Quiet"/>).</summary>
        internal Func<bool> IsQuiet { get; } = quiet ?? (static () => HangWatchdog.Quiet.Now);

        // :37 `var lastPong = Date()`.
        internal State State { get; } = new(SwiftDate.Now);

        internal Thread? Thread { get; private set; }

        // :42-60
        internal void Start()
        {
            if (!State.Start(SwiftDate.Now)) return;
            // R-GCD-3: a background thread, so it never keeps the process alive (PR-LAUNCH-021); :58's .utility QoS
            // has no counterpart in the prescribed thread shape.
            var thread = new Thread(Loop) { IsBackground = true, Name = "hang-watchdog" };
            Thread = thread;
            thread.Start();
        }

        // :51-55. `while true`: no exit and no cancellation; the thread ends with the process (PR-LAUNCH-021).
        private void Loop()
        {
            var wasQuiet = false;
            while (true)
            {
                Sleep(PingInterval);
                // ED-NEW-lean-6: while no window of the app can be seen, main is neither pinged nor checked (nothing wakes
                // it); seen again, main's time to answer counts from now, so the quiet spell is never taken for a stall. A main
                // thread that stops answering while unseen ends the quiet (Windows' own hung-window check), and is reported.
                if (IsQuiet()) { wasQuiet = true; continue; }
                if (wasQuiet) { wasQuiet = false; State.Resume(SwiftDate.Now); }
                Ping();
                Check();
            }
        }

        // :52 Thread.sleep(forTimeInterval: 0.5) blocks on a SwiftClock timer (R-GCD-3, ED-068).
        private static void Sleep(TimeSpan interval)
        {
            using var elapsed = new ManualResetEventSlim();
            using (SwiftClock.Provider.Current.CreateTimer(Wake, elapsed, interval, Timeout.InfiniteTimeSpan))
                elapsed.Wait();
        }

        private static void Wake(object? state) => ((ManualResetEventSlim)state!).Set();

        // :62-69 DispatchQueue.main.async.
        private void Ping() => MainActor.Post(() => State.Pong(SwiftDate.Now));

        // :71-81
        private void Check()
        {
            if (State.Check(SwiftDate.Now) is not { } stalledFor) return;
            // :79 .fixed(precision: 1) (ED-163, R-NUM-2).
            log.Error("Main thread unresponsive for " + SwiftNumber.Fixed(stalledFor, 1) + "s, sampling");
            WriteReport(stalledFor);
        }

        // :83-116. Runs on the watchdog's own thread, never the main one, so a stuck main thread cannot stop the report.
        private void WriteReport(double stalledFor)
        {
            var folder = ReportsFolder(localApplicationData());
            SwiftTry.Ignore(() => Directory.CreateDirectory(folder)); // :87
            var file = SwiftPath.AppendingPathComponent(folder, ReportFileName(SwiftDate.Now)); // :88-89
            var shortVersion = bundleValue("CFBundleShortVersionString"); // :92
            var build = bundleValue("CFBundleVersion");
            var sampledAt = SwiftDate.Now; // :93
            // DEVIATION ED-162: in-process EventPipe capture replaces /usr/bin/sample (:95-112); its failure bodies
            // keep ED-003 (1)'s two cases and positions.
            var body = capture(SampleWindow, State.MainThreadOsId);
            var report = ReportText(stalledFor, shortVersion, build, sampledAt, body);
            SwiftTry.Ignore(() => FileOps.WriteAtomic(file, Encoding.UTF8.GetBytes(report))); // :113 try?, UTF-8
            log.Error("Hang report written to " + file); // :114, even when the write failed
            TrimReports(folder); // :115
        }
    }

    // :88-89. ISO8601.String writes whole seconds in UTC; a Windows file name cannot hold ':' either, so the same
    // replacement applies (ordinal, on an ASCII stamp).
    internal static string ReportFileName(DateTimeOffset now) =>
        "hang-" + ISO8601.String(now).Replace(':', '-') + ".txt";

    // :118-121. DEVIATION OD-0008 C7: upstream ~/Library/Logs/Droppy Code; the port's data identity "Marrow Code"
    // names the folder under the per-user application data folder (ED-163).
    internal static string ReportsFolder(string localAppData) =>
        SwiftPath.AppendingPathComponent(SwiftPath.AppendingPathComponent(localAppData, AppInfo.DataFolderName), "Logs"); // OD-0135

    // :91-93, :113. DEVIATION OD-0012 F1: line 1 names "Marrow" where upstream reads "Droppy Code". "\(Date())" is
    // Date.description: UTC, "yyyy-MM-dd HH:mm:ss +0000".
    internal static string ReportText(double stalledFor, string? shortVersion, string? build,
        DateTimeOffset sampledAt, string body)
    {
        var utc = sampledAt.ToUniversalTime();
        var description = utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " +0000";
        return "nth main thread unresponsive for " + SwiftNumber.Fixed(stalledFor, 1) + "s\n"
            + "Version " + (shortVersion ?? "?") + " (" + (build ?? "?") + ")\n"
            + "Sampled at " + description + "\n\n" + body;
    }

    // :123-130. A failed listing returns; each failed removal is ignored.
    internal static void TrimReports(string folder)
    {
        var names = SwiftTry.Optional(() => new DirectoryInfo(folder).GetFileSystemInfos().Select(info => info.Name).ToArray());
        if (names is null) return;
        var reports = names.Where(name => SwiftString.HasPrefix(name, "hang-") && SwiftString.HasSuffix(name, ".txt"))
            .Order(SwiftStringComparer.Instance).ToArray();
        if (reports.Length <= KeptReports) return;
        foreach (var name in reports.Take(reports.Length - KeptReports))
            SwiftTry.Ignore(() => FileOps.RemoveItem(SwiftPath.AppendingPathComponent(folder, name)));
    }
}
