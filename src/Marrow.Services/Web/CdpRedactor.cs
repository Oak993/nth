using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Marrow.Services.Web;

/// <summary>
/// DESIGN 5.8, 8.1 (ED-NEW-web-W7-4): cookies, <c>Set-Cookie</c>, <c>Authorization</c> and <c>Proxy-Authorization</c>
/// values never reach the model or chat, so every CDP result and event that passes through <see cref="CdpBroker"/> is
/// redacted here first. It covers header objects (<c>headers</c>, <c>requestHeaders</c>), header arrays of
/// <c>{name, value}</c> (the Fetch domain), raw header text (<c>headersText</c>, <c>requestHeadersText</c>) and cookie
/// lists (<c>cookies</c>, <c>associatedCookies</c>, <c>blockedCookies</c>, <c>exemptedCookies</c>, and a nested
/// <c>cookie</c> object or <c>cookieLine</c>). Names keep their case; only values change.
/// </summary>
public static partial class CdpRedactor
{
    /// <summary>What a redacted value reads.</summary>
    public const string Mask = "[redacted]";

    /// <summary>How CDP JSON is written: CDP is not HTML, so "1+1" stays "1+1" and non-ASCII text stays as it was.</summary>
    internal static readonly JsonSerializerOptions Wire = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "cookie", "set-cookie", "authorization", "proxy-authorization",
    };

    private static readonly HashSet<string> HeaderObjects = new(StringComparer.Ordinal) { "headers", "requestHeaders", "responseHeaders" };
    private static readonly HashSet<string> HeaderTexts = new(StringComparer.Ordinal) { "headersText", "requestHeadersText" };
    private static readonly HashSet<string> CookieLists = new(StringComparer.Ordinal) { "cookies", "associatedCookies", "blockedCookies", "exemptedCookies" };

    [GeneratedRegex(@"^(?<name>cookie|set-cookie|authorization|proxy-authorization)(?<sep>[ \t]*:[ \t]*)[^\r\n]*", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveHeaderLine();

    /// <summary>True when a header name is one whose value is never shown.</summary>
    public static bool IsSensitiveHeader(string name) => SensitiveHeaders.Contains(name);

    /// <summary>Redacts a CDP JSON text (a result or an event's params). Text that is not JSON comes back unchanged.</summary>
    public static string Redact(string json)
    {
        if (string.IsNullOrEmpty(json)) return json;
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return json;
        }

        if (root is null || !Walk(root)) return json;
        return root.ToJsonString(Wire);
    }

    /// <summary>Redacts raw HTTP header text line by line.</summary>
    public static string RedactHeaderText(string text) =>
        SensitiveHeaderLine().Replace(text, m => m.Groups["name"].Value + m.Groups["sep"].Value + Mask);

    // Returns true when anything changed.
    private static bool Walk(JsonNode node)
    {
        var changed = false;
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, value) in obj.ToList())
                {
                    if (value is null) continue;
                    if (HeaderObjects.Contains(name)) changed |= RedactHeaders(value);
                    else if (HeaderTexts.Contains(name) && value is JsonValue text && text.TryGetValue<string>(out var raw))
                    {
                        var redacted = RedactHeaderText(raw);
                        if (!string.Equals(redacted, raw, StringComparison.Ordinal))
                        {
                            obj[name] = redacted;
                            changed = true;
                        }

                        continue;
                    }
                    else if (CookieLists.Contains(name) && value is JsonArray cookies)
                    {
                        foreach (var cookie in cookies)
                            if (cookie is JsonObject entry) changed |= RedactCookie(entry);
                    }
                    else if (string.Equals(name, "cookie", StringComparison.Ordinal) && value is JsonObject single)
                    {
                        changed |= RedactCookie(single);
                    }

                    changed |= Walk(value);
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                    if (item is not null) changed |= Walk(item);
                break;
        }

        return changed;
    }

    private static bool RedactHeaders(JsonNode headers)
    {
        var changed = false;
        switch (headers)
        {
            case JsonObject obj:
                foreach (var (name, _) in obj.ToList())
                {
                    if (!SensitiveHeaders.Contains(name)) continue;
                    obj[name] = Mask;
                    changed = true;
                }

                break;
            case JsonArray list:
                foreach (var item in list)
                {
                    if (item is not JsonObject pair) continue;
                    if (pair["name"] is JsonValue n && n.TryGetValue<string>(out var headerName) && SensitiveHeaders.Contains(headerName) && pair.ContainsKey("value"))
                    {
                        pair["value"] = Mask;
                        changed = true;
                    }
                }

                break;
        }

        return changed;
    }

    private static bool RedactCookie(JsonObject entry)
    {
        var changed = false;
        if (entry.ContainsKey("value"))
        {
            entry["value"] = Mask;
            changed = true;
        }

        if (entry.ContainsKey("cookieLine"))
        {
            entry["cookieLine"] = Mask;
            changed = true;
        }

        if (entry["cookie"] is JsonObject inner) changed |= RedactCookie(inner);
        return changed;
    }
}
