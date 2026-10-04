// PORT-ONLY (OD-0062; first-release UE-1; closes UNK-0009): the Windows release asset is Velopack's full package, a .nupkg (a zip) whose
// root holds the nuspec that names the app (id) and its version. This reads those two facts so the install pipeline can check what it
// was handed without Velopack.
using System.IO.Compression;
using System.Xml.Linq;

namespace Marrow.Core.Port.SelfUpdate;

/// <summary>A Velopack full package on disk.</summary>
public static class VelopackPackage
{
    /// <summary>The package's id and version from its nuspec, or null when the file is not a package (not a zip, no nuspec, no version).</summary>
    public static (string Id, string Version)? ReadIdentity(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) && !e.FullName.Contains('/', StringComparison.Ordinal));
            if (entry is null) return null;
            using var stream = entry.Open();
            var metadata = XDocument.Load(stream).Descendants().FirstOrDefault(e => e.Name.LocalName == "metadata");
            string? Field(string name) => metadata?.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim();
            return Field("id") is { Length: > 0 } id && Field("version") is { Length: > 0 } version ? (id, version) : null;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return null;
        }
    }
}
