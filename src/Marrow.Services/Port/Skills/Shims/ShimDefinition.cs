using System.Text.Json;

namespace Marrow.Services.Port.Skills.Shims;

// PORT-ONLY: ONDEMAND 3.6's <name>.shim, internal wire format ED-NEW-f-sk-6.
public sealed record ShimDefinition
{
    public required string Target { get; init; }
    public Dictionary<string, string> Environment { get; init; } = new(StringComparer.Ordinal);
    public string[] PrefixArguments { get; init; } = [];
    public string? PythonPathFile { get; init; }
    public string[] PackagePaths { get; init; } = [];
    public string? NodePath { get; init; }
    public bool LibreOffice { get; init; }
    public string? WorkDirectory { get; init; }

    public static ShimDefinition Read(string path) => JsonSerializer.Deserialize(File.ReadAllText(path), ShimJsonContext.Default.ShimDefinition)
        ?? throw new InvalidDataException(path);
    public static void Write(string path, ShimDefinition definition) => File.WriteAllText(path,
        JsonSerializer.Serialize(definition, ShimJsonContext.Default.ShimDefinition));
}
