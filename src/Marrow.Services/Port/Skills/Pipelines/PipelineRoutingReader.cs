using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Marrow.Services.Port.Skills.Shims;
namespace Marrow.Services.Port.Skills.Pipelines;
// BCL-only reader also compiled into the NativeAOT shim. The state file is read once per invocation.
public static class PipelineRoutingReader
{
    public static string FileFor(string stateFile, string id, string pin) => Path.Combine(Path.GetDirectoryName(stateFile)!, "routes",
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id + "\n" + pin))) + ".json");

    public static ShimDefinition ResolveShim(string stateFile, string name)
    {
        if (!Path.IsPathFullyQualified(stateFile)) throw new InvalidDataException("Absolute routing state required.");
        NoLinks(stateFile);
        using var state = JsonDocument.Parse(File.ReadAllBytes(stateFile), new JsonDocumentOptions { AllowDuplicateProperties = false });
        if (state.RootElement.GetProperty("schema").GetInt32() != 1) throw new InvalidDataException("Unsupported routing state.");
        var routes = new List<PipelineRoute>();
        foreach (var item in state.RootElement.GetProperty("items").EnumerateArray())
        {
            if (item.GetProperty("installedPin").ValueKind == JsonValueKind.Null || item.GetProperty("state").GetString() is "removing" or "damaged") continue;
            var id = item.GetProperty("id").GetString() ?? throw new InvalidDataException();
            var pin = item.GetProperty("installedPin").GetString() ?? throw new InvalidDataException();
            var file = FileFor(stateFile, id, pin);
            NoLinks(file);
            var route = JsonSerializer.Deserialize(File.ReadAllBytes(file), PipelineJsonContext.Default.PipelineRoute) ?? throw new InvalidDataException();
            if (route.Id != id || route.Pin != pin || route.Shims is null) throw new InvalidDataException("Routing pin mismatch.");
            routes.Add(route);
        }
        return Resolve(routes, name, Path.GetDirectoryName(Path.GetDirectoryName(stateFile))!);
    }

    /// <summary>
    /// The definition <paramref name="name"/> runs with, from the routes of one state read: the item's own shim, or (ED-NEW-dl-7) the linked shim of another selected item with the
    /// link's arguments first; a Python shim lists every selected Python package set; every selected item's exports are added to the child's environment (the shim's own values win).
    /// </summary>
    public static ShimDefinition Resolve(IReadOnlyList<PipelineRoute> routes, string name, string root)
    {
        var tools = Path.Combine(root, "addons", "tools");
        string Folder(PipelineRoute route)
        {
            var folder = Path.GetFullPath(route.Folder);
            if (!Path.IsPathFullyQualified(route.Folder) || !(Under(folder, tools) || Under(folder, Path.Combine(root, "skills", "store")))) throw new InvalidDataException("Invalid shim routing.");
            return folder;
        }
        ShimDefinition Own(PipelineRoute route, ShimDefinition? definition)
        {
            var folder = Folder(route);
            if (definition is null || !Path.IsPathFullyQualified(definition.Target) || !Under(Path.GetFullPath(definition.Target), folder) || definition.RoutingStateFile is not null ||
                definition.PythonPathFile is { } pth && (!Path.IsPathFullyQualified(pth) || !Under(Path.GetFullPath(pth), folder)))
                throw new InvalidDataException("Invalid shim routing.");
            NoLinks(definition.Target);
            return definition;
        }
        ShimDefinition? found = null;
        foreach (var route in routes)
        {
            foreach (var (key, definition) in route.Shims)
            {
                if (!key.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                var own = Own(route, definition);
                if (found is not null) throw new InvalidDataException("Duplicate shim routing.");
                found = own;
            }
            foreach (var (key, link) in route.Links ?? [])
            {
                if (!key.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                _ = Folder(route);
                if (link is null || link.Arguments is null) throw new InvalidDataException("Invalid shim routing.");
                var target = routes.SingleOrDefault(r => r.Id == link.Item) ?? throw new InvalidOperationException("The shim's item is not installed.");
                var linked = target.Shims.SingleOrDefault(p => p.Key.Equals(link.Shim, StringComparison.OrdinalIgnoreCase)).Value ?? throw new InvalidDataException("Invalid shim routing.");
                var own = Own(target, linked);
                if (found is not null) throw new InvalidDataException("Duplicate shim routing.");
                found = own with { PrefixArguments = [.. own.PrefixArguments ?? [], .. link.Arguments] };
            }
        }
        if (found is null) throw new InvalidOperationException("The shim's item is not installed.");
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var route in routes.OrderBy(r => r.Id, StringComparer.Ordinal))
            foreach (var (key, value) in route.Exports ?? [])
            {
                if (key.Length == 0 || value is null || key.Equals("PATH", StringComparison.OrdinalIgnoreCase) || !environment.TryAdd(key, value)) throw new InvalidDataException("Invalid exported environment.");
                _ = Folder(route);
            }
        foreach (var (key, value) in found.Environment ?? []) environment[key] = value;
        var packages = found.PythonPathFile is null ? found.PackagePaths ?? [] :
            [.. found.PackagePaths ?? [], .. routes.Where(r => r.PythonPackages).OrderBy(r => r.Id, StringComparer.Ordinal).Select(Folder)];
        return found with { Environment = new Dictionary<string, string>(environment, StringComparer.Ordinal), PackagePaths = packages };
    }

    private static bool Under(string path, string root) => path.StartsWith(root + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static void NoLinks(string path)
    {
        for (string? part = path; part != null; part = Path.GetDirectoryName(part))
            if (Path.Exists(part) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0) throw new IOException("Routing cannot traverse a reparse point.");
    }
}
