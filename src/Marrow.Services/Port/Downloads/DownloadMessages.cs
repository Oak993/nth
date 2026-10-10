// PORT-ONLY, owner-ruled addition (OD-0099 amendments; first-release skills downloads, SK-3): the failure words of section 3.9 (word set S1)
// and the two figures they need.
using System.Globalization;
using System.Text.RegularExpressions;

namespace Marrow.Services.Port.Downloads;

public static partial class DownloadMessages
{
    /// <summary>"The file is no longer at its source. A Marrow update will fix this." (3.9, 404 or 410).</summary>
    public const string NotFound = "The file is no longer at its source. An nth update will fix this.";

    /// <summary>"The file didn't match its checksum." (3.9).</summary>
    public const string Mismatch = "The file didn't match its checksum.";

    /// <summary>"The file could not be written to disk." (ED-304 (7): section 3.9 has no row for a local write failure.)</summary>
    public const string DiskError = "The file couldn't be written to disk.";

    [GeneratedRegex(@"^([A-Za-z]):")]
    private static partial Regex DriveLetter();

    /// <summary>"Couldn't reach libreoffice.org." (3.9, no connection / DNS / TLS).</summary>
    public static string Unreachable(string source) => $"Couldn't reach {source}.";

    /// <summary>"Needs 1.2 GB free on C:." (3.9, disk full).</summary>
    public static string DiskFull(long bytesNeeded, string drive) => $"Needs {Size(bytesNeeded)} free on {drive}.";

    /// <summary>
    /// The volume a path is on, as the message names it: "C:" for a drive path (either slash, either case), the server and share of a UNC path,
    /// the root otherwise.
    /// </summary>
    public static string DriveLabel(string path)
    {
        if (DriveLetter().Match(path) is { Success: true } drive) return drive.Groups[1].Value.ToUpperInvariant() + ":";
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
        {
            var parts = path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) return @"\\" + parts[0] + @"\" + parts[1];
        }
        if (path.StartsWith('/') || path.StartsWith('\\')) return path.StartsWith('/') ? "/" : "\\";
        var root = Path.GetPathRoot(path);
        return string.IsNullOrEmpty(root) ? path : root;
    }

    /// <summary>
    /// Bytes as the message writes them (decimal units, as the catalogue's sizes are), rounded UP so the figure is enough: tenths of a GB from
    /// 1000 MB, whole MB below.
    /// </summary>
    public static string Size(long bytes)
    {
        var megabytes = (bytes + 999_999) / 1_000_000;
        if (megabytes < 1000) return string.Create(CultureInfo.InvariantCulture, $"{megabytes} MB");
        var tenths = (bytes + 99_999_999) / 100_000_000;
        return string.Create(CultureInfo.InvariantCulture, $"{tenths / 10}.{tenths % 10} GB");
    }
}
