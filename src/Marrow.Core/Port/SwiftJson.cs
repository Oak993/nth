using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Marrow.Core.Port;

/// <summary>
/// The output options of one Swift JSON writer the source uses. One named style per call-site family (R-CODE-4);
/// pick it from the Swift line, never by taste.
/// </summary>
public sealed record SwiftJsonStyle(bool SortedKeys, bool PrettyPrinted, bool EscapeSlashes)
{
    /// <summary><c>JSONEncoder.storage</c> (Coding.swift:19-26): sortedKeys, compact, "/" escaped.</summary>
    public static SwiftJsonStyle Storage { get; } = new(true, false, true);

    /// <summary>Plain <c>JSONEncoder()</c> (11 sites): unsorted (Dictionary order, unspecified), compact, "/" escaped.</summary>
    public static SwiftJsonStyle PlainEncoder { get; } = new(false, false, true);

    /// <summary><c>[.prettyPrinted, .sortedKeys]</c> (MCPStore.swift:262-275).</summary>
    public static SwiftJsonStyle PrettySorted { get; } = new(true, true, true);

    /// <summary><c>JSONValue.data()</c>: JSONSerialization, <c>.withoutEscapingSlashes</c>, unsorted.</summary>
    public static SwiftJsonStyle SerializationCompact { get; } = new(false, false, false);

    /// <summary><c>JSONValue.prettyString</c>: JSONSerialization pretty and sorted, slashes unescaped.</summary>
    public static SwiftJsonStyle SerializationPretty { get; } = new(true, true, false);
}

/// <summary>
/// PORT-ONLY. Writes a JSON tree with Swift's bytes (ED-072): raw UTF-8 for every non-control scalar (no \u
/// escapes for emoji, U+2028, U+FEFF or private use), short escapes for \b \t \n \f \r, lowercase \u00xx for
/// the other C0 controls, "\/" unless the style keeps slashes, Swift Double text, sorted keys under a sorted
/// style, and Apple's pretty layout (two spaces, <c>"key" : value</c>). A model's converter writes a
/// JsonElement (Doubles through <see cref="WriteDouble(Utf8JsonWriter, double)"/>); this renders the bytes.
/// Source: draft A (proto-A Support/SwiftJson.cs); key order kept ordinal (O-01).
/// </summary>
public static class SwiftJson
{
    /// <summary>
    /// The sorted-key order. Ordinal: the existing Coding.cs rule, identical to Foundation's for every fixed
    /// CodingKey. Foundation's own order (<see cref="FoundationKeyComparer"/>) is believed but UNVERIFIED (O-01);
    /// the oracle's answer is this one line.
    /// </summary>
    public static IComparer<string> SortedKeyOrder { get; } = StringComparer.Ordinal;

    /// <summary>
    /// The options every JSON read uses (R-CODE-8): nesting up to 512 levels, swift-foundation's JSONDecoder limit
    /// (UNVERIFIED O-24, also for JSONSerialization), where System.Text.Json stops at 64 by default. Strict syntax
    /// otherwise: no comments, no trailing commas. A BOM is not handled here (O-15).
    /// </summary>
    public static JsonDocumentOptions DocumentOptions { get; } = new() { MaxDepth = 512 };

    public static byte[] Write(JsonElement value, SwiftJsonStyle style)
    {
        var output = new StringBuilder();
        WriteValue(output, value, style, 0);
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false).GetBytes(output.ToString());
    }

    public static string WriteString(JsonElement value, SwiftJsonStyle style) => Encoding.UTF8.GetString(Write(value, style));

    /// <summary>A model written by its System.Text.Json converter, rendered with Swift's bytes.</summary>
    public static byte[] Encode<T>(T value, JsonSerializerOptions options, SwiftJsonStyle style) =>
        Write(JsonSerializer.SerializeToElement(value, options), style);

    /// <summary>
    /// A Swift Double inside a converter. Utf8JsonWriter.WriteNumberValue(double) writes 1e-05 as "1E-05" and
    /// loses the Swift text; this writes the Swift text raw.
    /// </summary>
    public static void WriteDouble(Utf8JsonWriter writer, double value) => writer.WriteRawValue(SwiftNumber.JsonText(value), skipInputValidation: true);

    public static void WriteDouble(Utf8JsonWriter writer, string propertyName, double value)
    {
        writer.WritePropertyName(propertyName);
        WriteDouble(writer, value);
    }

    private static void WriteValue(StringBuilder output, JsonElement value, SwiftJsonStyle style, int depth)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                // Last duplicate wins, as a Swift Dictionary built from the input would.
                var members = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                var order = new List<string>();
                foreach (var property in value.EnumerateObject())
                {
                    if (!members.ContainsKey(property.Name)) order.Add(property.Name);
                    members[property.Name] = property.Value;
                }
                if (style.SortedKeys) order.Sort(SortedKeyOrder);
                output.Append('{');
                WriteMembers(output, order, name =>
                {
                    WriteStringLiteral(output, name, style);
                    output.Append(style.PrettyPrinted ? " : " : ":");
                    WriteValue(output, members[name], style, depth + 1);
                }, style, depth);
                output.Append('}');
                break;
            case JsonValueKind.Array:
                var items = value.EnumerateArray().ToList();
                output.Append('[');
                WriteMembers(output, items, item => WriteValue(output, item, style, depth + 1), style, depth);
                output.Append(']');
                break;
            case JsonValueKind.String:
                WriteStringLiteral(output, value.GetString()!, style);
                break;
            case JsonValueKind.Number:
                output.Append(NumberText(value.GetRawText()));
                break;
            case JsonValueKind.True: output.Append("true"); break;
            case JsonValueKind.False: output.Append("false"); break;
            case JsonValueKind.Null: output.Append("null"); break;
            default: throw new SwiftTrapException($"Unexpected JSON value kind {value.ValueKind}.");
        }
    }

    // Pretty: "\n", indent, ",\n" between members, then "\n" and the outer indent, also for an empty container
    // (corelibs JSONWriter; Apple's empty-container layout is UNVERIFIED O-04).
    private static void WriteMembers<T>(StringBuilder output, List<T> members, Action<T> write, SwiftJsonStyle style, int depth)
    {
        if (style.PrettyPrinted) output.Append('\n');
        for (var i = 0; i < members.Count; i++)
        {
            if (i > 0) output.Append(style.PrettyPrinted ? ",\n" : ",");
            if (style.PrettyPrinted) output.Append(' ', (depth + 1) * 2);
            write(members[i]);
        }
        if (style.PrettyPrinted)
        {
            output.Append('\n');
            output.Append(' ', depth * 2);
        }
    }

    /// <summary>An integer literal stays as written; any other number is re-rendered as Swift writes a Double.</summary>
    internal static string NumberText(string raw)
    {
        if (raw.AsSpan().IndexOfAny('.', 'e', 'E') < 0) return raw;
        return SwiftNumber.JsonText(double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture));
    }

    private static void WriteStringLiteral(StringBuilder output, string text, SwiftJsonStyle style)
    {
        output.Append('"');
        foreach (var c in text)
        {
            switch (c)
            {
                case '"': output.Append("\\\""); break;
                case '\\': output.Append("\\\\"); break;
                case '/' when style.EscapeSlashes: output.Append("\\/"); break;
                case '\b': output.Append("\\b"); break;
                case '\t': output.Append("\\t"); break;
                case '\n': output.Append("\\n"); break;
                case '\f': output.Append("\\f"); break;
                case '\r': output.Append("\\r"); break;
                case < ' ': output.Append("\\u00").Append(((int)c).ToString("x2", CultureInfo.InvariantCulture)); break;
                default: output.Append(c); break;
            }
        }
        output.Append('"');
    }
}

/// <summary>
/// PARKED (O-01). Key order under <c>.sortedKeys</c> as swift-corelibs-foundation documents it "to match Darwin":
/// <c>compare(_:options: [.numeric, .caseInsensitive, .forcedOrdering])</c>. Digit runs compare by value ("KEY2"
/// before "KEY10"), letters case-insensitively ("accept" before "Authorization"), and a tie falls back to
/// ordinal. It equals ordinal for every fixed CodingKey; it differs for user dictionaries (env, headers,
/// values). Not used until the oracle confirms it. Source: draft A (proto-A FoundationKeyComparer).
/// </summary>
public sealed class FoundationKeyComparer : IComparer<string>
{
    public static FoundationKeyComparer Instance { get; } = new();

    private FoundationKeyComparer() { }

    public int Compare(string? x, string? y)
    {
        if (x is null || y is null) return x is null ? (y is null ? 0 : -1) : 1;
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                var startX = i;
                var startY = j;
                while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
                while (j < y.Length && char.IsAsciiDigit(y[j])) j++;
                var runX = x[startX..i].TrimStart('0');
                var runY = y[startY..j].TrimStart('0');
                if (runX.Length != runY.Length) return runX.Length < runY.Length ? -1 : 1;
                var numeric = string.CompareOrdinal(runX, runY);
                if (numeric != 0) return numeric < 0 ? -1 : 1;
                continue;
            }
            var a = Fold(x, ref i);
            var b = Fold(y, ref j);
            var order = string.CompareOrdinal(a, b);
            if (order != 0) return order < 0 ? -1 : 1;
        }
        if (i < x.Length || j < y.Length) return i < x.Length ? 1 : -1;
        var forced = string.CompareOrdinal(x, y); // .forcedOrdering
        return forced < 0 ? -1 : forced > 0 ? 1 : 0;
    }

    private static string Fold(string text, ref int index)
    {
        var rune = Rune.GetRuneAt(text, index);
        index += rune.Utf16SequenceLength;
        return SwiftString.Lowercased(rune.ToString());
    }
}
