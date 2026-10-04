// PORT-ONLY: SK-4 / DL-4, OD-0099 amendments; skills-ONDEMAND-PLAN 3.3-3.4 and skills-PLAN 2.1.
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Marrow.Services.Port.Skills.Install;

internal static class NodesetInstaller
{
    public static void Build(InstallPlan plan, string target)
    {
        var packages = plan.NodePackages ?? throw new InvalidDataException("Node package tree is required.");
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in packages)
        {
            var tree = InstallPaths.Relative(package.TreePath);
            if (!tree.StartsWith("node_modules/", StringComparison.Ordinal) || !destinations.Add(tree)) throw new InvalidDataException("Invalid Node lock tree.");
            InstallHash.Verify(package.Path, package.Size, package.Sha256);
            using (var bytes = File.OpenRead(package.Path))
                if (!package.Integrity.Equals("sha512-" + Convert.ToBase64String(SHA512.HashData(bytes)), StringComparison.Ordinal)) throw new InvalidDataException("Node integrity mismatch.");
            using var compressed = File.OpenRead(package.Path);
            using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
            using var reader = new TarReader(gzip);
            var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (reader.GetNextEntry() is { } entry)
            {
                var name = entry.Name.TrimEnd('/');
                InstallPaths.Relative(name);
                if (name == "package" && entry.EntryType == TarEntryType.Directory) continue;
                if (!name.StartsWith("package/", StringComparison.Ordinal)) throw new InvalidDataException("npm package prefix absent.");
                var relative = InstallPaths.Relative(name[8..]);
                if (!entries.Add(relative)) throw new InvalidDataException("Duplicate tar entry.");
                var destination = InstallPaths.Under(target, tree + "/" + relative);
                if (entry.EntryType == TarEntryType.Directory) { Directory.CreateDirectory(destination); continue; }
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)) throw new InvalidDataException("Tar links and special entries refused.");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                entry.DataStream?.CopyTo(output);
            }
        }
    }
}
