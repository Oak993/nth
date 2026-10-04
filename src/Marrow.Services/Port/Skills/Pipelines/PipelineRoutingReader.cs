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
        ShimDefinition? found = null;
        foreach (var item in state.RootElement.GetProperty("items").EnumerateArray())
        {
            if (item.GetProperty("installedPin").ValueKind == JsonValueKind.Null || item.GetProperty("state").GetString() is "removing" or "damaged") continue;
            var id = item.GetProperty("id").GetString() ?? throw new InvalidDataException();
            var pin = item.GetProperty("installedPin").GetString() ?? throw new InvalidDataException();
            var file = FileFor(stateFile, id, pin);
            NoLinks(file);
            var route = JsonSerializer.Deserialize(File.ReadAllBytes(file), PipelineJsonContext.Default.PipelineRoute) ?? throw new InvalidDataException();
            if (route.Id != id || route.Pin != pin || route.Shims is null) throw new InvalidDataException("Routing pin mismatch.");
            foreach (var (key, definition) in route.Shims)
            {
                if (!key.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                var root = Path.GetDirectoryName(Path.GetDirectoryName(stateFile))!;
                var folder = Path.GetFullPath(route.Folder);
                if (!Path.IsPathFullyQualified(route.Folder) ||
                    !(Under(folder, Path.Combine(root, "addons", "tools")) || Under(folder, Path.Combine(root, "skills", "store"))) ||
                    definition is null || !Path.IsPathFullyQualified(definition.Target) || !Under(Path.GetFullPath(definition.Target), folder) ||
                    definition.RoutingStateFile is not null) throw new InvalidDataException("Invalid shim routing.");
                NoLinks(definition.Target);
                if (found is not null) throw new InvalidDataException("Duplicate shim routing.");
                found = definition;
            }
        }
        return found ?? throw new InvalidOperationException("The shim's item is not installed.");
    }
    private static bool Under(string path, string root) => path.StartsWith(root + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static void NoLinks(string path)
    {
        for (string? part = path; part != null; part = Path.GetDirectoryName(part))
            if (Path.Exists(part) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0) throw new IOException("Routing cannot traverse a reparse point.");
    }
}
