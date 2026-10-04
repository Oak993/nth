// PORT-ONLY, owner-ruled addition (OD-0099 amendments; first-release skills downloads, SK-3): the failure words of section 3.9 (word set S1)
// and the two figures they need.
namespace Marrow.Services.Port.Downloads;

public static class DownloadMessages
{
    /// <summary>"Couldn't reach libreoffice.org." (3.9, no connection / DNS / TLS).</summary>
    public static string Unreachable(string source) => throw new NotImplementedException();

    /// <summary>"The file is no longer at its source. A Marrow update will fix this." (3.9, 404 or 410).</summary>
    public const string NotFound = "";

    /// <summary>"The file didn't match its checksum." (3.9).</summary>
    public const string Mismatch = "";

    /// <summary>"Needs 1.2 GB free on C:." (3.9, disk full).</summary>
    public static string DiskFull(long bytesNeeded, string drive) => throw new NotImplementedException();

    /// <summary>The volume a path is on, as the message names it: "C:" for a drive path, the root otherwise.</summary>
    public static string DriveLabel(string path) => throw new NotImplementedException();

    /// <summary>Bytes as the message writes them, rounded UP so the figure is enough: tenths of a GB from 1 GB (decimal units), whole MB below.</summary>
    public static string Size(long bytes) => throw new NotImplementedException();
}
