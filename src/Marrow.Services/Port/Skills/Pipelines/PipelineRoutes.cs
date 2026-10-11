using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Marrow.Core.Port;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Shims;
using Marrow.Services.Port.Skills.Store;
namespace Marrow.Services.Port.Skills.Pipelines;

// PORT-ONLY: SK-8. One atomic state.json read selects BOTH shim targets and future generation sources.
// Sidecars are routing data, never a trusted installed-tree hash manifest (PLAN 2.1).
public static class PipelineRoutes
{
    private static string FileFor(string stateFile, string id, string pin) => Path.Combine(Path.GetDirectoryName(stateFile)!, "routes",
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id + "\n" + pin))) + ".json");

    public static void Prepare(string stateFile, PipelineRoute route)
    {
        Validate(stateFile, route);
        var file = FileFor(stateFile, route.Id, route.Pin);
        StorePaths.EnsureAncestors(file);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(route, PipelineJsonContext.Default.PipelineRoute);
        // A pin's bindings are immutable. Do not overwrite routes a running reader may have selected.
        if (File.Exists(file))
        {
            if (!File.ReadAllBytes(file).SequenceEqual(bytes))
            {
                if (AddonsStateStore.Load(stateFile).Get(route.Id).InstalledPin == route.Pin) throw new InvalidDataException("An active pin already has different routing.");
                FileOps.WriteAtomic(file, bytes);
            }
        }
        else FileOps.WriteAtomic(file, bytes);
    }

    public static IReadOnlyList<PipelineRoute> Read(string stateFile)
    {
        StorePaths.EnsureAncestors(stateFile);
        var state = AddonsStateStore.Load(stateFile);
        return Read(stateFile, state);
    }

    internal static IReadOnlyList<PipelineRoute> Read(string stateFile, AddonsState state)
    {
        var routes = new List<PipelineRoute>();
        foreach (var record in state.Items.Where(r => r.InstalledPin != null && r.State is not (ItemState.Removing or ItemState.Damaged)))
        {
            var file = FileFor(stateFile, record.Id, record.InstalledPin!);
            StorePaths.EnsureAncestors(file);
            var route = JsonSerializer.Deserialize(File.ReadAllBytes(file), PipelineJsonContext.Default.PipelineRoute)
                ?? throw new InvalidDataException("Missing version routing.");
            if (route.Id != record.Id || route.Pin != record.InstalledPin) throw new InvalidDataException("Routing disagrees with the selected pin.");
            Validate(stateFile, route);
            routes.Add(route);
        }
        var names = routes.SelectMany(r => r.Shims.Keys.Concat(r.Links?.Keys ?? Enumerable.Empty<string>())).ToArray();
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
            throw new InvalidDataException("Two installed items provide the same shim.");
        var exports = routes.SelectMany(r => r.Exports?.Keys ?? Enumerable.Empty<string>()).ToArray();
        if (exports.Distinct(StringComparer.OrdinalIgnoreCase).Count() != exports.Length)
            throw new InvalidDataException("Two installed items export the same environment name.");
        return routes;
    }

    private static void Validate(string stateFile, PipelineRoute route)
    {
        var paths = new StorePaths(Path.GetDirectoryName(Path.GetDirectoryName(stateFile))!);
        var folder = paths.RequireManaged(route.Folder);
        if (!StorePaths.Under(folder, paths.Tools) && !StorePaths.Under(folder, paths.Store)) throw new InvalidDataException("Invalid installed route.");
        ArgumentNullException.ThrowIfNull(route.Shims);
        ArgumentNullException.ThrowIfNull(route.SparePaths);
        foreach (var spare in route.SparePaths)
            if (!StorePaths.Under(paths.RequireManaged(spare), paths.Views)) throw new InvalidDataException("Invalid generation route.");
        foreach (var (name, shim) in route.Shims)
        {
            if (name != Path.GetFileName(name) || name.IndexOfAny(['/', '\\']) >= 0 || shim is null ||
                !Path.IsPathFullyQualified(shim.Target) || !StorePaths.Under(Path.GetFullPath(shim.Target), folder) ||
                shim.PythonPathFile is { } pth && (!Path.IsPathFullyQualified(pth) || !StorePaths.Under(Path.GetFullPath(pth), folder)))
                throw new InvalidDataException("Invalid shim route.");
        }
        // ED-NEW-dl-7: a link names a shim of another item, resolved at each run; an export is a plain environment name (never PATH, which the provider launch owns).
        foreach (var (name, link) in route.Links ?? [])
            if (name != Path.GetFileName(name) || name.IndexOfAny(['/', '\\']) >= 0 || link is null || string.IsNullOrEmpty(link.Item) || link.Item == route.Id ||
                string.IsNullOrEmpty(link.Shim) || link.Arguments is null || route.Shims.Keys.Any(k => k.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Invalid shim route.");
        foreach (var (name, value) in route.Exports ?? [])
            if (name.Length == 0 || name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_')) || char.IsAsciiDigit(name[0]) || name.Equals("PATH", StringComparison.OrdinalIgnoreCase) || value is null || value.Any(char.IsControl))
                throw new InvalidDataException("Invalid exported environment.");
    }

    public static ShimDefinition ResolveShim(string stateFile, string name)
    {
        var routes = Read(stateFile); // Exactly one state read: never mix pins across a commit.
        return PipelineRoutingReader.Resolve(routes, name, Path.GetDirectoryName(Path.GetDirectoryName(stateFile))!);
    }
}
