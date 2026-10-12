namespace Marrow.Setup.Install;

/// <summary>One look at what Velopack's <c>Setup.exe</c> has done so far.</summary>
/// <param name="CopiedBytes">Bytes of this run's files in the root's folders (<c>packages\</c>, <c>current\</c> and any folder it extracts through).</param>
/// <param name="ResultWritten">The install hook has written <c>install-result.json</c>.</param>
/// <param name="Log">What the log has said so far (the same object every time).</param>
public sealed record InstallObservation(long CopiedBytes, bool ResultWritten, VelopackLogSignals Log);

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 4, "Copying"; VELOPACK_CHECK.md check 5; unit U3, ED-NEW-inst-u3-1). Watches the install
/// root while <c>Setup.exe</c> runs. Copying counts the bytes in every folder under the root (Velopack writes
/// <c>packages\NthApp-&lt;v&gt;-full.nupkg</c>, then extracts <c>current\</c> in place; a version that extracts through a
/// folder and renames it is still counted, because the folder is inside the root) and leaves out the root's own files
/// (Update.exe and the stub, which T_pkg + T_app do not hold). Only files made during this run count: before an upgrade
/// Velopack renames the old root away, and until then the old install's files are still there, so a file whose creation
/// time is before the run (less a 2 s allowance for the file system's clock) is not this run's. A file the directory
/// listing shows as recently written is asked for its size again by path (<c>GetFileAttributesEx</c>, no handle kept),
/// because NTFS updates a directory entry's size only when the writer closes it, and the 118 MB package is written as one
/// file. The walk is about 250 files.
/// </summary>
public sealed class InstallObserver
{
    private static readonly TimeSpan Allowance = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Recent = TimeSpan.FromSeconds(5);
    private readonly string root;
    private readonly string? resultPath;
    private readonly VelopackLogTail? log;
    private readonly DateTime runStartUtc;
    private readonly Func<DateTime> fileClock;

    /// <param name="root">The install root Setup.exe was given.</param>
    /// <param name="logPath">The <c>--log</c> file, or null.</param>
    /// <param name="logStart">The log's length before the run.</param>
    /// <param name="resultPath">The hook's <c>install-result.json</c> (deleted before the run).</param>
    /// <param name="runStartUtc">When Setup.exe was started, on the file system's clock.</param>
    /// <param name="fileClock">The file system's clock (UTC now).</param>
    public InstallObserver(string root, string? logPath, long logStart, string? resultPath, DateTime runStartUtc, Func<DateTime>? fileClock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        this.root = root;
        this.resultPath = resultPath;
        this.runStartUtc = runStartUtc;
        this.fileClock = fileClock ?? (() => DateTime.UtcNow);
        log = logPath is null ? null : new VelopackLogTail(logPath, logStart);
    }

    public VelopackLogSignals Signals { get; } = new();

    /// <summary>The log's last 40 lines (for Copy details).</summary>
    public IReadOnlyList<string> RecentLog => log?.Recent ?? [];

    public InstallObservation Observe()
    {
        if (log is not null) Signals.Read(log.ReadNew());
        return new InstallObservation(CopiedBytes(), ResultWritten(), Signals);
    }

    private long CopiedBytes()
    {
        var total = 0L;
        try
        {
            var top = new DirectoryInfo(root);
            if (!top.Exists) return 0;
            var since = runStartUtc - Allowance;
            var recent = fileClock() - Recent;
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, ReturnSpecialDirectories = false };
            foreach (var folder in top.EnumerateDirectories("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                foreach (var file in folder.EnumerateFiles("*", options))
                {
                    if (file.CreationTimeUtc < since) continue;
                    var length = file.Length;
                    if (file.LastWriteTimeUtc >= recent) length = Math.Max(length, FreshLength(file.FullName));
                    total += length;
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Velopack is renaming or emptying the root at this moment; the next look counts it.
        }
        return total;
    }

    private static long FreshLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return 0; }
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
