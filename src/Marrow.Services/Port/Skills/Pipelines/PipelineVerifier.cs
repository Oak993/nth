using System.Security.Cryptography;
using Marrow.Services.Port.Skills.Store;
namespace Marrow.Services.Port.Skills.Pipelines;
// SK-8 extends SK-5's file-derived directory inventory with SK-4's explicitly trusted empty directories.
internal sealed class PipelineVerifier(IStoreProtection protection)
{
    public bool Verify(StoreItem item, IReadOnlyList<string> directories, bool sealOnly = false)
    {
        try
        {
            StorePaths.EnsureAncestors(item.Folder);
            if (!Directory.Exists(item.Folder) || !protection.IsLocked(item.Folder) || protection.HasNamedStreams(item.Folder)) return false;
            var expected = item.Manifest.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
            var expectedDirectories = item.Manifest.Directories.Concat(directories).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var path in expectedDirectories) StoreManifest.RequireRelative(path);
            var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var actualDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in StoreTrees.Entries(item.Folder))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0 || !protection.IsLocked(path) || protection.HasNamedStreams(path)) return false;
                var relative = Path.GetRelativePath(item.Folder, path).Replace(Path.DirectorySeparatorChar, '/');
                if ((attributes & FileAttributes.Directory) != 0) { if (!actualDirectories.Add(relative)) return false; continue; }
                if (!actual.Add(relative) || !expected.TryGetValue(relative, out var file) || (attributes & FileAttributes.ReadOnly) == 0) return false;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length != file.Size) return false;
                if (!sealOnly || Path.GetExtension(path).ToLowerInvariant() is ".exe" or ".dll" or ".pyd" or ".node")
                    if (!Convert.ToHexStringLower(SHA256.HashData(stream)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return actual.SetEquals(expected.Keys) && actualDirectories.SetEquals(expectedDirectories);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or PlatformNotSupportedException or System.Security.SecurityException) { return false; }
    }
}
