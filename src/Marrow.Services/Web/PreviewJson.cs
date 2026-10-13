using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Marrow.Services.Web;

// PORT-ONLY: defensive parsing of CDP diagnostics; ED-NEW-web-w9-2. Malformed page events cannot kill a tab.
internal static class PreviewJson
{
    public static JsonElement Field(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var field) ? field : default;
    public static string Text(JsonElement element, string name) => Field(element, name) is { ValueKind: JsonValueKind.String } field ? field.GetString() ?? "" : "";
    public static double Number(JsonElement element, string name) => Field(element, name) is { ValueKind: JsonValueKind.Number } field && field.TryGetDouble(out var number) && double.IsFinite(number) ? number : 0;
    public static string Bounded(string text, int limit = 16_384) => text.Length <= limit ? text : text[..limit];
    public static string Redacted(string text)
    {
        var redacted = CdpRedactor.Redact(text);
        try
        {
            var node = JsonNode.Parse(redacted);
            if (node is not null) { Scrub(node); redacted = node.ToJsonString(CdpRedactor.Wire); }
        }
        catch (JsonException) { }
        redacted = Regex.Replace(redacted, @"(?<name>[?&](?:access_token|refresh_token|token|api_key|apikey|password|secret|key)=)[^&\s""'<>\\]*", "${name}[redacted]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return Bounded(CdpRedactor.RedactHeaderText(redacted));
    }
    private static void Scrub(JsonNode node)
    {
        if (node is JsonObject obj)
            foreach (var (name, value) in obj.ToArray())
            {
                if (name is "postData" or "postDataEntries" || name.Equals("password", StringComparison.OrdinalIgnoreCase) || name.Equals("access_token", StringComparison.OrdinalIgnoreCase)) obj[name] = CdpRedactor.Mask;
                else if (value is not null) Scrub(value);
            }
        else if (node is JsonArray array) foreach (var child in array) if (child is not null) Scrub(child);
    }
}
