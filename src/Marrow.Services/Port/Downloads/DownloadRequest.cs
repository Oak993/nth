// PORT-ONLY, owner-ruled addition (OD-0099 amendments; first-release skills downloads, SK-3): what one download is told.
namespace Marrow.Services.Port.Downloads;

/// <summary>
/// One file to fetch: where from, where it ends up, and what the catalogue pins about it (section 3.3: length and SHA-256).
/// </summary>
/// <param name="Url">The first url (must be https and on the policy's host list).</param>
/// <param name="DestinationPath">The verified file's final path; it appears only after the length and hash matched (atomic move).</param>
/// <param name="Size">The pinned length in bytes.</param>
/// <param name="Sha256">The pinned SHA-256, 64 hex digits (either case). Also names the partial file: <c>&lt;first 8 digits&gt;.part</c>.</param>
/// <param name="SourceName">What a "Couldn't reach ..." message calls the source ("libreoffice.org"); the url's host when null.</param>
/// <param name="UnpackedBytes">What the item takes on disk once installed, for the disk-space check (download + unpacked + 10%).</param>
public sealed record DownloadRequest(Uri Url, string DestinationPath, long Size, string Sha256, string? SourceName = null, long UnpackedBytes = 0)
{
    /// <summary>The request's partial file in the part folder (section 3.1): <c>&lt;first 8 hex digits of the pinned hash, lower case&gt;.part</c>.</summary>
    public string PartFileName => Sha256[..8].ToLowerInvariant() + ".part";
}
