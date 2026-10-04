// PORT-ONLY, owner-ruled addition (OD-0099 amendments; first-release skills downloads, SK-3): the downloader's knobs. ED-304 (3)
// to (6) say which defaults come from the plan or the voice spec and which are engineering choices.
namespace Marrow.Services.Port.Downloads;

public sealed record DownloadOptions
{
    /// <summary>Longest wait for the response headers of one request (ED): 30 s. A hit is "couldn't reach".</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Longest wait for the next body bytes (ED): 60 s. A hit keeps the partial file and is "couldn't reach".</summary>
    public TimeSpan StallTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Progress is reported at most once per this long and a received byte is reported within it (sections 3.2, DL-3): 100 ms.</summary>
    public TimeSpan ProgressInterval { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Waits between automatic retries of a transient failure (unreachable, cut stream, timeout, 5xx). Empty (the default) means none: the
    /// row shows Retry (section 3.9). The voice models ask for 2 s, 4 s, 8 s (voice-SPEC V-9.5 item 6).
    /// </summary>
    public IReadOnlyList<TimeSpan> RetryDelays { get; init; } = [];

    /// <summary>Redirect hops followed per request (ED): 10.</summary>
    public int MaxRedirects { get; init; } = 10;

    /// <summary>Files in flight at once in a set (section 3.2: "six at a time").</summary>
    public int MaxInFlight { get; init; } = 6;

    /// <summary>The extra room asked for on top of download + unpacked size (section 3.2): 10%.</summary>
    public double SpaceMargin { get; init; } = 0.10;

    /// <summary>Free bytes on the volume holding a path. The default reads the drive; tests give a number.</summary>
    public Func<string, long> FreeSpace { get; init; } = DefaultFreeSpace;

    private static long DefaultFreeSpace(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path));
        return string.IsNullOrEmpty(root) ? long.MaxValue : new DriveInfo(root).AvailableFreeSpace;
    }
}
