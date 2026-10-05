// PORT-ONLY: first-release feature unit SK-16 (DL-19, ONDEMAND-PLAN.md 3.9 and the OD-0099 review item 3: "a failed item can be checked after the file was placed by hand"). There is no Swift twin.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Downloads;
using Marrow.Services.Port.Skills.Install;

namespace Marrow.Services.Port.Skills.Fetch;

/// <summary>
/// "Download it yourself": the file the user fetched from the item's own source and put where Marrow looks. ENGINEERING DECISION (ED-NEW-cloud-sk-16): the hand-placed file is checked against the
/// item's pinned inputs (length and SHA-256, never trusted for its name) and put in the downloader's part folder under the name it would have had (<c>&lt;first 8 digits of the pinned hash&gt;.part</c>, ONDEMAND 3.1).
/// A part file that is already whole needs no request: SK-3 verifies it where it lies and finishes (3.3), so pressing Download again ("Check again") installs from it with no connection made.
/// A file that is none of the item's pinned inputs is refused with the checksum words and nothing is written. The page's words for this fallback are not built (the design page's text is not in the repository).
/// </summary>
public static class ManualFiles
{
    /// <summary>Where partial downloads live: <c>addons\downloads</c> (ONDEMAND 3.1).</summary>
    public static string PartFolder(string dataRoot) => Path.Combine(Path.GetFullPath(dataRoot), "addons", "downloads");

    /// <summary>Puts the file in the part folder when it is one of the item's pinned inputs; returns that input's name (a file's path or an artifact's name).</summary>
    public static string Place(string dataRoot, CatalogueItem item, string file)
    {
        ArgumentNullException.ThrowIfNull(item);
        var info = new FileInfo(file);
        if (!info.Exists) throw new FileNotFoundException("The file is not there.", file);
        var hash = InstallHash.Sha256File(file);
        var inputs = item.Files.Select(f => (Name: f.Path, f.Size, f.Sha256)).Concat(item.Artifacts.Where(a => a.Size is not null && a.Sha256 is not null).Select(a => (Name: a.Name, Size: a.Size!.Value, Sha256: a.Sha256!)));
        var match = inputs.FirstOrDefault(i => i.Size == info.Length && string.Equals(i.Sha256, hash, StringComparison.OrdinalIgnoreCase));
        if (match.Name is null) throw new InvalidDataException(DownloadMessages.Mismatch);
        var folder = PartFolder(dataRoot);
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, hash[..8].ToLowerInvariant() + ".part");
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(file, temporary, overwrite: false);
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return match.Name;
    }
}
