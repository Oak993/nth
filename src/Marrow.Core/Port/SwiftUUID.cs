using System.Text.Json;
using System.Text.Json.Serialization;

namespace Marrow.Core.Port;

/// <summary>
/// PORT-ONLY. Foundation <c>UUID</c> text (ED-073): <c>uuidString</c> is UPPERCASE 8-4-4-4-12, in JSON, in file
/// names (<c>"\(id.uuidString).json"</c>) and everywhere else Swift writes it; the few <c>.lowercased()</c> sites
/// (ClaudeSession.swift:84) lowercase explicitly. Guid.ToString() is lowercase and never reaches a file or a
/// name. Source: draft A (strict parser) and draft B (the converter).
/// </summary>
public static class SwiftUUID
{
    /// <summary><c>id.uuidString</c>.</summary>
    public static string UuidString(Guid id) => id.ToString("D").ToUpperInvariant();

    /// <summary>
    /// <c>UUID(uuidString:)</c>: exactly 36 characters, hyphens at 8, 13, 18 and 23, hex digits of either case
    /// elsewhere. Guid.TryParse also takes braces, parentheses and no hyphens; even TryParseExact(s, "D")
    /// takes surrounding whitespace and a '+' inside a group, all of which Foundation rejects.
    /// </summary>
    public static Guid? Parse(string? text)
    {
        if (text is null || text.Length != 36) return null;
        for (var i = 0; i < 36; i++)
        {
            var hyphen = i is 8 or 13 or 18 or 23;
            if (hyphen ? text[i] != '-' : !char.IsAsciiHexDigit(text[i])) return null;
        }
        return Guid.TryParseExact(text, "D", out var id) ? id : null;
    }

    /// <summary>The Codable form: a JSON string holding <c>uuidString</c>. For any Guid the storage options meet.</summary>
    public sealed class JsonConverter : JsonConverter<Guid>
    {
        public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String && Parse(reader.GetString()) is { } id
                ? id : throw new DecodingError("dataCorrupted", "Attempted to decode UUID from invalid UUID string.");

        public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) =>
            writer.WriteStringValue(UuidString(value));
    }
}
