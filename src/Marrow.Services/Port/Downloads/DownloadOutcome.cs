// PORT-ONLY, owner-ruled addition (OD-0099 amendments; first-release skills downloads, SK-3): how a download ends (section 3.9).
namespace Marrow.Services.Port.Downloads;

/// <summary>How a job ended. Cancelled is not a failure (section 3.9: the row returns to Download, with Resume if a .part exists).</summary>
public enum DownloadStatus { Succeeded, Failed, Cancelled }

/// <summary>Why a download failed. The message of each is section 3.9's (see <see cref="DownloadFailure.Message"/>).</summary>
public enum DownloadFailureKind
{
    /// <summary>No connection, DNS, TLS, a timeout or a cut stream: "Couldn't reach {source}." Retry resumes from the .part.</summary>
    Unreachable,

    /// <summary>HTTP 404 or 410: "The file is no longer at its source. A Marrow update will fix this."</summary>
    NotFound,

    /// <summary>Any other non-success status (500, 503, 403 ...): the same words as Unreachable (ED-304 (7)).</summary>
    ServerError,

    /// <summary>Length or SHA-256 differs from the pin: "The file didn't match its checksum." The .part is deleted.</summary>
    Mismatch,

    /// <summary>Not enough room: "Needs 1.2 GB free on C:."</summary>
    DiskFull,

    /// <summary>A file could not be written (ED-304 (7)).</summary>
    DiskError,

    /// <summary>The url was refused before any byte was fetched: off the list, not https, or an insecure or endless redirect (ED-304 (8)).</summary>
    Refused,
}

/// <summary>The finer cause behind an Unreachable or Refused failure, for logs and tests.</summary>
public enum DownloadCause { None, Offline, Tls, Timeout, Interrupted, HostNotAllowed, NotHttps, InsecureRedirect, TooManyRedirects, BadRedirect }

public sealed record DownloadFailure(DownloadFailureKind Kind, string Message, DownloadCause Cause = DownloadCause.None, int? HttpStatus = null,
    string? DestinationPath = null);

/// <summary>One finished file. <see cref="ResumedFrom"/> is how many bytes the finishing request did not fetch again.</summary>
public sealed record DownloadFileResult(string DestinationPath, long Size, string Sha256, string FinalHost, long ResumedFrom, bool RestartedFromZero);

public sealed record DownloadOutcome(DownloadStatus Status, IReadOnlyList<DownloadFileResult> Files, DownloadFailure? Failure)
{
    public bool Succeeded => Status == DownloadStatus.Succeeded;
}

/// <summary>Bytes received over bytes pinned, counting what a resumed .part already held; files finished over files asked for.</summary>
public readonly record struct DownloadProgress(long BytesDone, long BytesTotal, int FilesDone, int FilesTotal);

public enum DownloadJobState { Running, Paused, Succeeded, Failed, Cancelled }
