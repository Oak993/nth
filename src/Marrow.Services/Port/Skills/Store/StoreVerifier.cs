// PORT-ONLY: SK-5, PLAN 2.1 whole-tree set comparison and ONDEMAND 3.5 per-launch binary seal.
using System.Security.Cryptography;

namespace Marrow.Services.Port.Skills.Store;

public sealed class StoreVerifier(IStoreProtection protection)
{
    public StoreVerification Verify(string folder, StoreManifest manifest, bool sealOnly = false)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        try
        {
            StorePaths.EnsureAncestors(folder);
            if (!Directory.Exists(folder) || !protection.IsLocked(folder) || protection.HasNamedStreams(folder)) return new(false);
            var expected = manifest.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in StoreTrees.Entries(folder))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0 || !protection.IsLocked(path) || protection.HasNamedStreams(path)) return new(false);
                var relative = Path.GetRelativePath(folder, path).Replace(Path.DirectorySeparatorChar, '/');
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (!directories.Add(relative) || !manifest.Directories.Contains(relative)) return new(false);
                    continue;
                }
                if (!files.Add(relative) || !expected.TryGetValue(relative, out var pinned) || (attributes & FileAttributes.ReadOnly) == 0) return new(false);
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length != pinned.Size) return new(false);
                if (!sealOnly || IsBinary(path))
                {
                    var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
                    if (!hash.Equals(pinned.Sha256, StringComparison.OrdinalIgnoreCase)) return new(false);
                }
            }
            return new(files.SetEquals(expected.Keys) && directories.SetEquals(manifest.Directories));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or PlatformNotSupportedException or System.Security.SecurityException)
        {
            return new(false);
        }
    }

    private static bool IsBinary(string path) => Path.GetExtension(path).ToLowerInvariant() is ".exe" or ".dll" or ".pyd" or ".node";
}
