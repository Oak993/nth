// PORT-ONLY: SK-5. Trusted whole-tree inventory from the embedded catalogue or hash-verified SK-4 installation.
using System.Collections.Frozen;
using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Services.Port.Skills.Store;

public sealed class StoreManifest
{
    public StoreManifest(IEnumerable<CatalogueFile> files)
    {
        var entries = files.ToArray();
        if (entries.Length == 0) throw new ArgumentException("A trusted file inventory is required.", nameof(files));
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in entries)
        {
            RequireRelative(file.Path);
            if (file.Size < 0 || file.Sha256.Length != 64 || file.Sha256.Any(c => !char.IsAsciiHexDigit(c)))
                throw new ArgumentException("Invalid pinned size or SHA-256.", nameof(files));
            if (!paths.Add(file.Path)) throw new ArgumentException("Duplicate Windows file path.", nameof(files));
            var slash = file.Path.LastIndexOf('/');
            while (slash >= 0)
            {
                var parent = file.Path[..slash];
                directories.Add(parent);
                slash = parent.LastIndexOf('/');
            }
        }
        if (paths.Overlaps(directories)) throw new ArgumentException("A file is also a directory.", nameof(files));
        Files = Array.AsReadOnly(entries);
        Directories = directories.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<CatalogueFile> Files { get; }
    internal IReadOnlySet<string> Directories { get; }

    internal static void RequireRelative(string path)
    {
        // Windows-safe on Linux too: no ADS, drive roots, trailing dots/spaces, reserved device names or ambiguous components.
        if (string.IsNullOrEmpty(path) || path.Contains('\\', StringComparison.Ordinal) || path.Any(c => c < 32 || "<>:\"|?*".Contains(c, StringComparison.Ordinal)))
            throw new ArgumentException("A safe relative slash-separated path is required.", nameof(path));
        foreach (var component in path.Split('/'))
        {
            if (component.Length == 0 || component is "." or ".." || component.EndsWith(' ') || component.EndsWith('.'))
                throw new ArgumentException("Unsafe path component.", nameof(path));
            var name = component.Split('.')[0];
            if (name.Equals("CON", StringComparison.OrdinalIgnoreCase) || name.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("AUX", StringComparison.OrdinalIgnoreCase) || name.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                (name.Length == 4 && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && name[3] is >= '1' and <= '9'))
                throw new ArgumentException("Reserved Windows device path.", nameof(path));
        }
    }
}
