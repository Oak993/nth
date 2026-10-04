// PORT-ONLY: shared readers and writers for the voice settings records (voice-SPEC V-16, "all keys read leniently"). A missing or
// unreadable value keeps its default; unreadable data as a whole is the default. The writer keeps text as typed (no \u escapes for
// ASCII punctuation or non-ASCII letters), compact, in the key order the caller writes.
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Marrow.Core.Port.Voice;

internal static class VoiceJson
{
    internal static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Parses <paramref name="json"/> and runs <paramref name="read"/> on its root when that is an object; otherwise (or on any parse failure) returns <paramref name="fallback"/>.</summary>
    internal static T ReadObject<T>(ReadOnlySpan<byte> json, T fallback, Func<JsonElement, T> read)
    {
        try
        {
            using var document = JsonDocument.Parse(json.ToArray());
            return document.RootElement.ValueKind == JsonValueKind.Object ? read(document.RootElement) : fallback;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException)
        {
            // malformed JSON, invalid UTF-8, a lone surrogate escape (GetString throws InvalidOperationException)
            return fallback;
        }
    }

    /// <summary>The string at <paramref name="key"/>, or null when missing, null or not a string.</summary>
    internal static string? Text(JsonElement o, string key) =>
        o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    internal static bool Bool(JsonElement o, string key, bool fallback) =>
        o.TryGetProperty(key, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : fallback;

    internal static int Int(JsonElement o, string key, int fallback) =>
        o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : fallback;

    internal static double Number(JsonElement o, string key, double fallback) =>
        o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && double.IsFinite(d) ? d : fallback;

    /// <summary>
    /// The wire name of <paramref name="value"/>, or of <paramref name="fallback"/> (the record's default) for a value outside the names:
    /// a writer never throws, and an unknown value reads back as the default anyway (V-16.6). ENGINEERING DECISION, review round 1.
    /// </summary>
    internal static string WireName<T>(string[] names, T value, T fallback) where T : struct, Enum
    {
        var i = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        return i >= 0 && i < names.Length ? names[i] : names[Convert.ToInt32(fallback, System.Globalization.CultureInfo.InvariantCulture)];
    }

    /// <summary>A finite number, or null for NaN and the infinities (JSON has no form for them; null reads back as the default).</summary>
    internal static void WriteNumberOrNull(Utf8JsonWriter w, string key, double value)
    {
        if (double.IsFinite(value)) w.WriteNumber(key, value); else w.WriteNull(key);
    }

    /// <summary>The enum value whose wire name (<paramref name="names"/>, indexed by the enum's order) is at <paramref name="key"/>, else <paramref name="fallback"/>.</summary>
    internal static T Name<T>(JsonElement o, string key, string[] names, T fallback) where T : struct, Enum
    {
        var text = Text(o, key);
        if (text is null) return fallback;
        var i = Array.IndexOf(names, text);
        return i < 0 ? fallback : (T)(object)i;
    }
}
