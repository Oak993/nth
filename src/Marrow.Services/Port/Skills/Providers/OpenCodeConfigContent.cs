// PORT-ONLY: SK-11d / WU-9. Adds only the skills.paths key to the base config JSON; the base text stays byte-for-byte.
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Marrow.Services.Port.Skills.Providers;

public static class OpenCodeConfigContent
{
    private static readonly JsonSerializerOptions Plain = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string WithSkillPaths(string baseJson, IReadOnlyList<string> userPaths, string marrowPath)
    {
        var all = userPaths.Append(marrowPath).ToList();
        var skills = "\"skills\":{\"paths\":" + JsonSerializer.Serialize(all, Plain) + "}";
        using var document = JsonDocument.Parse(baseJson, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow });
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new OpenCodeConfigUnreadableException("The base config is not an object.");
        var hasSkills = document.RootElement.TryGetProperty("skills", out _);
        var end = baseJson.TrimEnd();
        if (!hasSkills && end.EndsWith('}'))
        {
            var empty = !document.RootElement.EnumerateObject().Any();
            return end[..^1].TrimEnd() + (empty ? "" : ",") + skills + "}";
        }
        // The base already has a skills key: keep its other keys and replace only paths.
        var node = JsonNode.Parse(baseJson)!.AsObject();
        if (node["skills"] is not JsonObject existing) throw new OpenCodeConfigUnreadableException("The base config has a malformed skills key.");
        existing["paths"] = new JsonArray(all.Select(p => (JsonNode)JsonValue.Create(p)!).ToArray());
        return node.ToJsonString(Plain);
    }
}
