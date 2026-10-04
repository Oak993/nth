// PORT-ONLY: SK-4 / DL-4, OD-0099 amendments; skills-ONDEMAND-PLAN 3.3-3.4 and skills-PLAN 2.1.
using System.IO.Compression;

namespace Marrow.Services.Port.Skills.Install;

internal static class ZipInstaller
{
    public static void Extract(string archive, string target)
    {
        using var zip = ZipFile.OpenRead(archive);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            var directory = entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith('\\');
            var name = InstallPaths.Relative(directory ? entry.FullName[..^1] : entry.FullName);
            var mode = (entry.ExternalAttributes >> 16) & 0xf000;
            if (mode is 0xa000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Archive reparse point refused.");
            if (!names.Add(name)) throw new InvalidDataException("Duplicate archive entry.");
            var destination = InstallPaths.Under(target, name);
            if (directory) { Directory.CreateDirectory(destination); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var input = entry.Open();
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
        }
    }
}
