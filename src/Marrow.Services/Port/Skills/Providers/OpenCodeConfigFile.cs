// PORT-ONLY: SK-11d / WU-9, skills-PLAN.md 2.2: JSONC with comments and trailing commas; skills.paths read verbatim.
using System.Text.Json;

namespace Marrow.Services.Port.Skills.Providers;

public static class OpenCodeConfigFile
{
    private static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>Null: the file does not set skills.paths. Throws OpenCodeConfigUnreadableException for anything unreadable or unusable.</summary>
    public static IReadOnlyList<string>? ReadSkillPaths(string path)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new OpenCodeConfigUnreadableException("A config source cannot be read."); }
        return ParseSkillPaths(bytes);
    }

    public static IReadOnlyList<string>? ParseSkillPaths(ReadOnlySpan<byte> json)
    {
        try
        {
            using var document = JsonDocument.Parse(json.ToArray(), Options);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new OpenCodeConfigUnreadableException("A config source is not an object.");
            if (!document.RootElement.TryGetProperty("skills", out var skills)) return null;
            if (skills.ValueKind != JsonValueKind.Object) throw new OpenCodeConfigUnreadableException("A config source has a malformed skills key.");
            if (!skills.TryGetProperty("paths", out var paths)) return null;
            if (paths.ValueKind != JsonValueKind.Array) throw new OpenCodeConfigUnreadableException("A config source has a malformed skills.paths.");
            var result = new List<string>();
            foreach (var entry in paths.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String) throw new OpenCodeConfigUnreadableException("A skills.paths entry is not a string.");
                var value = entry.GetString()!;
                if (value.Contains("{env:", StringComparison.Ordinal) || value.Contains("{file:", StringComparison.Ordinal))
                    throw new OpenCodeConfigUnreadableException("A skills.paths entry uses substitution.");
                result.Add(value);
            }
            return result;
        }
        catch (JsonException) { throw new OpenCodeConfigUnreadableException("A config source cannot be parsed."); }
    }
}
