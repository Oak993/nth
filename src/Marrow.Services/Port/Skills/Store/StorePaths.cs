// PORT-ONLY: SK-5, ONDEMAND 3.1 data layout; explicit root and confinement (ED-321).
namespace Marrow.Services.Port.Skills.Store;

public sealed class StorePaths
{
    public StorePaths(string dataRoot)
    {
        if (string.IsNullOrWhiteSpace(dataRoot) || !Path.IsPathFullyQualified(dataRoot))
            throw new ArgumentException("An absolute nth data root is required.", nameof(dataRoot));
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataRoot));
        if (Root == Path.GetPathRoot(Root)) throw new ArgumentException("A filesystem root is not a data root.", nameof(dataRoot));
        Addons = Path.Combine(Root, "addons");
        Tools = Path.Combine(Addons, "tools");
        Skills = Path.Combine(Root, "skills");
        Store = Path.Combine(Skills, "store");
        Views = Path.Combine(Skills, "views");
        StateFile = Path.Combine(Addons, "state.json");
        CondemnedFile = Path.Combine(Skills, "condemned.json");
        EnabledFile = Path.Combine(Skills, "enabled.json");
        EnsureAncestors(Root);
    }

    public string Root { get; }
    public string Skills { get; }
    public string Tools { get; }
    public string Store { get; }
    public string Views { get; }
    public string Addons { get; }
    public string StateFile { get; }
    public string CondemnedFile { get; }
    public string EnabledFile { get; }
    internal static StringComparer Comparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static bool Under(string path, string parent) => path.StartsWith(parent + Path.DirectorySeparatorChar,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    internal string RequireManaged(string folder)
    {
        if (!Path.IsPathFullyQualified(folder)) throw new ArgumentException("An absolute managed folder is required.", nameof(folder));
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (!Under(path, Tools) && !Under(path, Store) && !Under(path, Views))
            throw new ArgumentException("The folder is outside managed store trees.", nameof(folder));
        // Check parents, but not the leaf: GC must be able to unlink a planted leaf link, without following it.
        EnsureAncestors(Path.GetDirectoryName(path)!);
        return path;
    }

    internal static void EnsureAncestors(string path)
    {
        for (string? parent = path; parent is not null; parent = Path.GetDirectoryName(parent))
        {
            try
            {
                if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("A managed path has a reparse-point ancestor: " + parent);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
