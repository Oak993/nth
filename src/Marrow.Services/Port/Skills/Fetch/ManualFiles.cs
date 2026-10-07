// PORT-ONLY: first-release feature unit SK-16 (DL-19, ONDEMAND-PLAN.md 3.9 and the OD-0099 review item 3: "a failed item can be checked after the file was placed by hand"). There is no Swift twin.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Downloads;

namespace Marrow.Services.Port.Skills.Fetch;

/// <summary>
/// "Download it yourself": the file the user fetched from the item's own source and put where Marrow looks. ENGINEERING DECISION (ED-368): the hand-placed file is checked against the
/// item's pinned inputs (length and SHA-256, never trusted for its name) and put in the downloader's part folder under the name it would have had (<c>&lt;first 8 digits of the pinned hash&gt;.part</c>, ONDEMAND 3.1).
/// A part file that is already whole needs no request: SK-3 verifies it where it lies and finishes (3.3), so pressing Download again ("Check again") installs from it with no connection made.
/// A file that is none of the item's pinned inputs is refused with the checksum words and nothing is written. The page's words for this fallback are not built (the design page's text is not in the repository).
/// </summary>
public static class ManualFiles
{
    /// <summary>Where partial downloads live: <c>addons\downloads</c> (ONDEMAND 3.1).</summary>
    public static string PartFolder(string dataRoot) => Path.Combine(Path.GetFullPath(dataRoot), "addons", "downloads");

    /// <summary>The folder the design names for the hand-downloaded file: <c>addons\downloads\manual</c>.</summary>
    public static string ManualFolder(string dataRoot) => Path.Combine(PartFolder(dataRoot), "manual");

    /// <summary>
    /// "Check again" (the design's step 3): every file in <see cref="ManualFolder"/> is checked against the item's pinned inputs by length and SHA-256, never by name, and each one that matches is kept in the part folder
    /// for the install, exactly as <see cref="Place"/> keeps one; a file that is none of them is left out and never touched. A skill set or plugin may also come as the repository's .zip (the design's step 1: "the
    /// repository at commit ..., as a .zip"): each entry of a zip in the folder is checked the same way, whatever folder it sits in, so the zip's own folder name never matters. The user's files stay where they are.
    /// Returns how many of the item's pinned inputs were found; what is not found is fetched the normal way.
    /// </summary>
    public static int PlaceFolder(string dataRoot, CatalogueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var folder = ManualFolder(dataRoot);
        if (!Directory.Exists(folder)) return 0;
        var inputs = item.Files.Select(f => (f.Size, f.Sha256)).Concat(item.Artifacts.Where(a => a.Size is not null && a.Sha256 is not null).Select(a => (Size: a.Size!.Value, Sha256: a.Sha256!))).ToList();
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                try
                {
                    var info = new FileInfo(file);
                    if (inputs.Any(i => i.Size == info.Length)) _ = Keep(dataRoot, inputs, found, () => File.OpenRead(file));
                    if (item.Kind is ItemKind.Skillset or ItemKind.Plugin && file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        using var zip = System.IO.Compression.ZipFile.OpenRead(file);
                        foreach (var entry in zip.Entries.Where(e => e.Name.Length > 0 && inputs.Any(i => i.Size == e.Length)))
                            _ = Keep(dataRoot, inputs, found, entry.Open);
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException) { /* a file that cannot be read is not one of the item's */ }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* the manual folder disappeared or became unreadable */ }
        return found.Count;
    }

    private static bool Keep(string dataRoot, List<(long Size, string Sha256)> inputs, HashSet<string> found, Func<Stream> open)
    {
        if (inputs.Count == 0) return false;
        var folder = PartFolder(dataRoot);
        Directory.CreateDirectory(folder);
        var temporary = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            // Stage and hash the same bytes from one opening, including non-seekable zip entries.
            // No unverified bytes ever replace a downloader's partial file.
            using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            long length = 0;
            var limit = inputs.Max(i => i.Size);
            using (var source = open())
            using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = source.Read(buffer)) > 0)
                {
                    length += read;
                    if (length > limit) return false;
                    sha.AppendData(buffer, 0, read);
                    destination.Write(buffer, 0, read);
                }
                destination.Flush(flushToDisk: true);
            }
            var hash = Convert.ToHexStringLower(sha.GetHashAndReset());
            if (!inputs.Any(i => i.Size == length && string.Equals(i.Sha256, hash, StringComparison.OrdinalIgnoreCase)) || found.Contains(hash)) return false;
            var target = Path.Combine(folder, hash[..8] + ".part");
            // Callers run placement on a pool thread (LibraryController.CheckAgain).
            var turn = PartFileTurns.Take(target, CancellationToken.None).GetAwaiter().GetResult();
            try { File.Move(temporary, target, overwrite: true); }
            finally { PartFileTurns.Give(target, turn); }
            found.Add(hash); // only durable, successfully published inputs count
            return true;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>Puts the file in the part folder when it is one of the item's pinned inputs; returns that input's name (a file's path or an artifact's name).</summary>
    public static string Place(string dataRoot, CatalogueItem item, string file)
    {
        ArgumentNullException.ThrowIfNull(item);
        var info = new FileInfo(file);
        if (!info.Exists) throw new FileNotFoundException("The file is not there.", file);
        var inputs = item.Files.Select(f => (Name: f.Path, f.Size, f.Sha256)).Concat(item.Artifacts.Where(a => a.Size is not null && a.Sha256 is not null).Select(a => (Name: a.Name, Size: a.Size!.Value, Sha256: a.Sha256!))).ToList();
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Keep(dataRoot, inputs.Select(i => (i.Size, i.Sha256)).ToList(), found, () => File.OpenRead(file))) throw new InvalidDataException(DownloadMessages.Mismatch);
        return inputs.First(i => found.Contains(i.Sha256)).Name;
    }
}
