// PORT-ONLY: web surfaces W13 (DESIGN.md 6.5 "Libraries are vendored and served offline"; ED-NEW-web-w13-3). The
// vendored library folder beside the app (Assets\lib, copied there by the build): its manifest, and the bytes of each
// file the manifest lists or its ES module wrappers, read once and kept. Only those paths are ever served, so a request
// can never reach another file on disk. No Swift counterpart.
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Marrow.Core.Port;
using Marrow.Core.Web;

namespace Marrow.Services.Web;

public sealed class ArtifactLibraryFiles
{
    private readonly ConcurrentDictionary<string, byte[]> cache = new(StringComparer.Ordinal);
    private readonly Lazy<(ArtifactLibraryCatalog Catalog, IReadOnlyDictionary<string, string?> Paths)> manifest;
    private readonly Logger log = new(Logger.Subsystem, "web");

    /// <param name="folder">The library folder; <see cref="DefaultFolder"/> in the app.</param>
    public ArtifactLibraryFiles(string folder)
    {
        Folder = folder;
        manifest = new Lazy<(ArtifactLibraryCatalog, IReadOnlyDictionary<string, string?>)>(Load);
    }

    /// <summary>Assets\lib beside the app's assemblies.</summary>
    public static string DefaultFolder => Path.Combine(AppContext.BaseDirectory, "Assets", "lib");

    public string Folder { get; }

    /// <summary>The libraries; empty (no import map entries, every CDN file missing) when the folder has no manifest.</summary>
    public ArtifactLibraryCatalog Catalog => manifest.Value.Catalog;

    /// <summary>True when <paramref name="path"/> is a file nth serves from the library host.</summary>
    public bool Contains(string path) => manifest.Value.Paths.ContainsKey(path);

    /// <summary>A served file's bytes, or null for any path the manifest does not list (or a file that fails its hash).</summary>
    public byte[]? Read(string path)
    {
        if (!manifest.Value.Paths.TryGetValue(path, out var hash)) return null;
        if (cache.TryGetValue(path, out var cached)) return cached;
        var full = Path.Combine(Folder, path.Replace('/', Path.DirectorySeparatorChar));
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(full);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            log.Error($"web: library file {path} could not be read: {error.Message}");
            return null;
        }

        if (hash is not null && !Convert.ToHexString(SHA256.HashData(bytes)).Equals(hash, StringComparison.OrdinalIgnoreCase))
        {
            log.Error($"web: library file {path} does not match its manifest hash; not served");
            return null;
        }

        return cache.GetOrAdd(path, bytes);
    }

    private (ArtifactLibraryCatalog, IReadOnlyDictionary<string, string?>) Load()
    {
        var path = Path.Combine(Folder, "manifest.json");
        try
        {
            var catalog = ArtifactLibraryCatalog.Parse(File.ReadAllText(path));
            var paths = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var library in catalog.Libraries)
            {
                foreach (var file in library.Files) paths[file.Path] = file.Sha256;
                if (library.Esm is { } esm) paths[esm] = null; // generated from the manifest itself; no hash recorded
            }

            return (catalog, paths);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException or InvalidOperationException)
        {
            log.Error($"web: the artifact library manifest could not be read ({error.Message}); artifacts run without libraries");
            return (ArtifactLibraryCatalog.Empty, new Dictionary<string, string?>());
        }
    }
}
