// PORT-ONLY: Dextra, nth's assistant (DESIGN.md revision 5, sections 12.5 and 15.3; unit AS-3). No Swift counterpart.
// ED-NEW-dextra-as3-6: every file the store and the audit log read back is parsed strictly.
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Marrow.Services.Assistant;

/// <summary>
/// Parses JSON the assistant's own files hold (audit lines, the anchor, mission journals and snapshots, profiles,
/// duties, the feed), all of which anything with write access to the folder may have planted. A text that is not JSON,
/// or that repeats a property name anywhere in it, gives null: a <see cref="JsonNode"/> builds its objects lazily, and an
/// object with a repeated name would otherwise throw <see cref="ArgumentException"/> at its first field read, outside
/// any parse guard (ED-NEW-dextra-as3-6). Duplicates are refused at parse time
/// (<see cref="JsonDocumentOptions.AllowDuplicateProperties"/> false), for nested objects too.
/// </summary>
internal static class AssistantJson
{
    private static readonly JsonDocumentOptions Strict = new() { AllowDuplicateProperties = false };

    /// <summary>The value, or null when the text is empty, is not JSON, or repeats a property name.</summary>
    public static JsonNode? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            return JsonNode.Parse(text, documentOptions: Strict);
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The object, or null when the text is not one JSON object with distinct property names.</summary>
    public static JsonObject? ParseObject(string? text) => Parse(text) as JsonObject;

    /// <summary>
    /// A JSON number as a double, or null when the node is not a number. A value built in memory from an int or a long reads
    /// as well as one parsed from a file (which holds a JsonElement and converts on its own).
    /// </summary>
    public static double? Number(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<double>(out var real)) return real;
        if (value.TryGetValue<long>(out var whole)) return whole;
        if (value.TryGetValue<int>(out var small)) return small;
        if (value.TryGetValue<decimal>(out var money)) return (double)money;
        return null;
    }
}
