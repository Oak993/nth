// PORT-ONLY: SK-4 / DL-4, OD-0099 amendments; skills-ONDEMAND-PLAN 3.3-3.4 and skills-PLAN 2.1.
using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Services.Port.Skills.Install;

internal static class InstallTree
{
    public static IReadOnlyList<CatalogueFile> Snapshot(string root) => InstallPaths.TreeFiles(root).Select(path =>
        new CatalogueFile { Path = InstallPaths.Relative(Path.GetRelativePath(root, path)), Size = new FileInfo(path).Length, Sha256 = InstallHash.Sha256File(path) }).ToArray();

    public static string[] Directories(IReadOnlyList<CatalogueFile> files)
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var relative = InstallPaths.Relative(file.Path);
            for (var pos = relative.LastIndexOf('/'); pos > 0; pos = relative.LastIndexOf('/'))
            { relative = relative[..pos]; directories.Add(relative); }
        }
        return directories.ToArray();
    }
    public static void Verify(string root, IReadOnlyList<CatalogueFile> manifest, IInstallLock seal, IReadOnlyList<string> directories)
    {
        if (!seal.IsLocked(root)) throw new InvalidDataException("Install lock is absent.");
        var expected = manifest.Select(f => InstallPaths.Relative(f.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actual = InstallPaths.TreeFiles(root).Select(p => InstallPaths.Relative(Path.GetRelativePath(root, p))).ToArray();
        if (actual.Length != expected.Count || !expected.SetEquals(actual)) throw new InvalidDataException("Installed files differ from the manifest.");
        var dirs = directories.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in expected)
        {
            var pos = file.LastIndexOf('/');
            while (pos > 0) { var dir = file[..pos]; dirs.Add(dir); pos = dir.LastIndexOf('/'); }
        }
        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            if (!dirs.Contains(InstallPaths.Relative(Path.GetRelativePath(root, dir)))) throw new InvalidDataException("Extra install directory.");
        foreach (var file in manifest) InstallHash.Verify(InstallPaths.Under(root, file.Path), file.Size, file.Sha256);
    }
    public static void Copy(string source, string destination)
    {
        InstallPaths.NoReparseAncestors(source);
        InstallPaths.NoReparseAncestors(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, false);
    }
}
