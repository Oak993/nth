namespace Marrow.Services.Port.Skills.Shims;

// PORT-ONLY: ONDEMAND 3.6 PATH rule, DL-6; provider wiring belongs to SK-11.
public static class ShimPath
{
    public static string Insert(string? path, string bin, char separator)
    {
        if (string.IsNullOrEmpty(path)) return bin;
        var entries = path.Split(separator).ToList();
        var index = entries.FindIndex(IsWindowsApps);
        entries.Insert(index < 0 ? entries.Count : index, bin);
        return string.Join(separator, entries);
    }

    public static Dictionary<string, string> ForChild(IReadOnlyDictionary<string, string> environment, string dataRoot)
    {
        var bin = Path.Combine(dataRoot, "addons", "bin");
        Directory.CreateDirectory(bin); // Present on PATH even before the first install.
        var child = new Dictionary<string, string>(environment, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        child.TryGetValue("PATH", out var path);
        child["PATH"] = Insert(path, bin, Path.PathSeparator);
        return child;
    }

    private static bool IsWindowsApps(string entry) => entry.Trim().Trim('"').Replace('\\', '/').TrimEnd('/')
        .EndsWith("/Microsoft/WindowsApps", StringComparison.OrdinalIgnoreCase);
}
