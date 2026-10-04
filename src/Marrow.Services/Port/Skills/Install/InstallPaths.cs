// PORT-ONLY: SK-4 / DL-4, OD-0099 amendments; skills-ONDEMAND-PLAN 3.3-3.4 and skills-PLAN 2.1.

namespace Marrow.Services.Port.Skills.Install;

internal static class InstallPaths
{
    public static string Relative(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("Empty install path.");
        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal)) throw new InvalidDataException("Rooted install path.");
        var parts = normalized.Split('/');
        foreach (var part in parts)
        {
            if (part.Length == 0 || part is "." or ".." || part.EndsWith(' ') || part.EndsWith('.') || part.Any(c => char.IsControl(c) || "<>:\"|?*".Contains(c, StringComparison.Ordinal)))
                throw new InvalidDataException("Unsafe install path.");
            var basename = part.Split('.')[0];
            if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(basename, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Reserved Windows install path.");
        }
        return string.Join('/', parts);
    }
    public static string Under(string root, string relative)
    {
        var path = Path.Combine(root, Relative(relative).Replace('/', Path.DirectorySeparatorChar));
        NoReparseAncestors(path);
        return path;
    }
    public static void NoReparseAncestors(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            // GetAttributes also sees dangling links; File.Exists would hide them.
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Reparse point refused."); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
    public static IReadOnlyList<string> TreeFiles(string root)
    {
        NoReparseAncestors(root);
        WindowsDataStreams.RefuseNamedStreams(root);
        var files = new List<string>();
        void Walk(string dir)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Reparse point refused.");
                Relative(Path.GetRelativePath(root, entry));
                WindowsDataStreams.RefuseNamedStreams(entry);
                if ((attributes & FileAttributes.Directory) != 0) Walk(entry); else files.Add(entry);
            }
        }
        Walk(root);
        return files;
    }
}
