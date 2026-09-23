// Source: DroppyCode/Core/Support/HangWatchdog.swift (B2 1fbf81e), :17-131.
using System.Globalization;
using System.Runtime.CompilerServices;
using Marrow.Core.Port;

[assembly: InternalsVisibleTo("Marrow.Core.Tests")]

namespace Marrow.Core.Support;

public static class HangWatchdog
{
    // :18. OD-0012 F1 renames the product part of the logging identity.
    internal const string LogSubsystem = "iordv.marrow";
    internal const string LogCategory = "hang";

    // :24, :28, :32, :52. The watchdog thread and stack capture are still an open platform boundary.
    internal static readonly TimeSpan PingInterval = TimeSpan.FromMilliseconds(500);
    internal static readonly TimeSpan StallThreshold = TimeSpan.FromSeconds(4);
    internal static readonly TimeSpan SampleWindow = TimeSpan.FromSeconds(2);
    internal const int KeptReports = 10;

    // :40-46, :62-77. A pong is timestamped when the main queue runs it, not when the ping is posted.
    internal sealed class State(DateTimeOffset now)
    {
        private readonly Lock gate = new();
        private DateTimeOffset lastPong = now;
        private bool reportedThisStall;

        internal void Pong(DateTimeOffset now)
        {
            lock (gate)
            {
                lastPong = now;
                reportedThisStall = false;
            }
        }

        internal double? Check(DateTimeOffset now)
        {
            lock (gate)
            {
                var stalled = SwiftDate.TimeIntervalSince(now, lastPong);
                if (stalled < StallThreshold.TotalSeconds || reportedThisStall) return null;
                reportedThisStall = true;
                return stalled;
            }
        }
    }

    // :86-89. ISO8601.String writes whole seconds in UTC; the Windows filename uses the same colon replacement.
    internal static string ReportFileName(DateTimeOffset now) =>
        "hang-" + ISO8601.String(now).Replace(':', '-') + ".txt";

    // :118-121, OD-0008 C7. The caller supplies LOCALAPPDATA so tests never read or write the owner's profile.
    internal static string ReportsFolder(string localAppData) => Path.Combine(localAppData, "Marrow Code", "Logs");

    // :91-93, :113. The caller supplies the captured stack body and bundle metadata.
    internal static string ReportText(double stalledFor, string? shortVersion, string? build,
        DateTimeOffset sampledAt, string body)
    {
        var utc = sampledAt.ToUniversalTime();
        var description = utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " +0000";
        return "Marrow main thread unresponsive for " + stalledFor.ToString("F1", CultureInfo.InvariantCulture) + "s\n"
            + "Version " + (shortVersion ?? "?") + " (" + (build ?? "?") + ")\n"
            + "Sampled at " + description + "\n\n" + body;
    }

    // :123-130. Ignore a failed listing; ignore each failed removal and keep trying the older matches.
    internal static void TrimReports(string folder)
    {
        string[] names;
        try { names = Directory.GetFileSystemEntries(folder).Select(Path.GetFileName).OfType<string>().ToArray(); }
        catch (Exception error) when (SwiftTry.IsThrownError(error)) { return; }

        var reports = names.Where(name => name.StartsWith("hang-", StringComparison.Ordinal)
                                         && name.EndsWith(".txt", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToArray();
        for (var index = 0; index < reports.Length - KeptReports; index++)
        {
            try { FileOps.RemoveItem(Path.Combine(folder, reports[index])); }
            catch (Exception error) when (SwiftTry.IsThrownError(error)) { }
        }
    }
}
