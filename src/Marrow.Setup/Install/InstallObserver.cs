using Marrow.Setup.Port;

namespace Marrow.Setup.Install;

/// <summary>One look at what Velopack's Setup.exe has done so far.</summary>
public sealed record InstallObservation(long CopiedBytes, bool ResultWritten, VelopackLogSignals Log);

/// <summary>
/// PORT-ONLY: PLAN 4, ED-NEW-inst-c7-1. Snapshot the old file identities before the runner starts, then count only new
/// identities in the root's folders. Recent timestamps, a rename, or a hard link never turn leftover files into new
/// attempt bytes. A replaced root can reuse the same paths, sizes and timestamps while holding different files.
/// Identity uncertainty withholds copied-byte evidence; log/hook/exit evidence remains available.
/// </summary>
public sealed class InstallObserver
{
    private static readonly TimeSpan Allowance = TimeSpan.FromSeconds(2);
    private readonly string root;
    private readonly string? resultPath;
    private readonly VelopackLogTail? log;
    private readonly DateTime runStartUtc;
    private readonly Func<string, InstallFileSnapshot?> snapshot;
    private readonly HashSet<InstallFileIdentity> baseline = [];
    private readonly bool baselineKnown;

    public InstallObserver(string root, string? logPath, long logStart, string? resultPath, DateTime runStartUtc,
        Func<DateTime>? fileClock = null, Func<string, InstallFileSnapshot?>? snapshot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        this.root = root;
        this.resultPath = resultPath;
        this.runStartUtc = runStartUtc;
        this.snapshot = snapshot ?? WindowsInstallFileIdentity.Read;
        log = logPath is null ? null : new VelopackLogTail(logPath, logStart);
        baselineKnown = ReadBaseline();
    }

    public VelopackLogSignals Signals { get; } = new();
    public IReadOnlyList<string> RecentLog => log?.Recent ?? [];

    public InstallObservation Observe()
    {
        if (log is not null) Signals.Read(log.ReadNew());
        return new InstallObservation(CopiedBytes(), ResultWritten(), Signals);
    }

    private bool ReadBaseline()
    {
        try
        {
            foreach (var file in Files(ignoreInaccessible: false, includeRoot: true))
            {
                if (snapshot(file.FullName) is not { } seen) return false;
                baseline.Add(seen.Identity);
            }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    private long CopiedBytes()
    {
        if (!baselineKnown) return 0;
        var total = 0L;
        var counted = new HashSet<InstallFileIdentity>();
        try
        {
            foreach (var file in Files(ignoreInaccessible: true, includeRoot: false))
            {
                if (snapshot(file.FullName) is not { } seen || baseline.Contains(seen.Identity) || !counted.Add(seen.Identity)) continue;
                var length = Math.Max(0, seen.Length);
                total = length > long.MaxValue - total ? long.MaxValue : total + length;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Velopack is replacing the root; the next look counts it. No old identity ever becomes eligible.
        }
        return total;
    }

    private IEnumerable<FileInfo> Files(bool ignoreInaccessible, bool includeRoot)
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(root); }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { yield break; }
        if ((attributes & FileAttributes.Directory) == 0) throw new IOException("The install root is not a directory.");
        var top = new DirectoryInfo(root);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true, IgnoreInaccessible = ignoreInaccessible,
            AttributesToSkip = FileAttributes.ReparsePoint, ReturnSpecialDirectories = false,
        };
        if (includeRoot)
        {
            foreach (var file in top.EnumerateFiles("*", options)) yield return file;
            yield break;
        }
        foreach (var folder in top.EnumerateDirectories("*", new EnumerationOptions
                 { IgnoreInaccessible = ignoreInaccessible, AttributesToSkip = FileAttributes.ReparsePoint }))
            foreach (var file in folder.EnumerateFiles("*", options)) yield return file;
    }

    private bool ResultWritten()
    {
        if (resultPath is null) return false;
        try
        {
            var file = new FileInfo(resultPath);
            return file.Exists && file.Length > 0 && file.LastWriteTimeUtc >= runStartUtc - Allowance;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }
}
