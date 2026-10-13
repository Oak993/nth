using System.Buffers;
using System.Globalization;
using System.Runtime.InteropServices;
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

    /// <summary>
    /// The document's bytes. ED-NEW-perf-memory-5: the text goes straight to UTF-8 chunks of a pooled sink and each string value is copied from the
    /// document's own UTF-8 (no string is made for it), so a save allocates the finished array and little else; the bytes are the ones the
    /// StringBuilder, string and UTF8Encoding route wrote (a lone surrogate is still U+FFFD).
    /// </summary>
    public static byte[] Write(JsonElement value, SwiftJsonStyle style)
    {
        using var sink = new SwiftJsonSink();
        WriteValue(sink, value, style, 0);
        return sink.ToArray();
    }

    public static string WriteString(JsonElement value, SwiftJsonStyle style) => Encoding.UTF8.GetString(Write(value, style));

    /// <summary>A model written by its System.Text.Json converter, rendered with Swift's bytes.</summary>
    public static byte[] Encode<T>(T value, JsonSerializerOptions options, SwiftJsonStyle style)
    {
        using var document = JsonSerializer.SerializeToDocument(value, options); // pooled buffers, given back at once (ED-NEW-perf-memory-5)
        return Write(document.RootElement, style);
    }

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

    private static readonly SearchValues<byte> SpecialBytes = SearchValues.Create(
        [.. Enumerable.Range(0, 0x20).Select(b => (byte)b), (byte)'"', (byte)'\\']);
    private static readonly SearchValues<byte> SpecialBytesWithSlash = SearchValues.Create(
        [.. Enumerable.Range(0, 0x20).Select(b => (byte)b), (byte)'"', (byte)'\\', (byte)'/']);

    private readonly struct Member(string name, JsonElement value)
    {
        public readonly string Name = name;
        public readonly JsonElement Value = value;
    }

    private sealed class MemberOrder : IComparer<Member>
    {
        public static readonly MemberOrder Instance = new();
        public int Compare(Member x, Member y) => SortedKeyOrder.Compare(x.Name, y.Name);
    }

    private static void WriteValue(SwiftJsonSink output, JsonElement value, SwiftJsonStyle style, int depth)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                WriteObject(output, value, style, depth);
                break;
            case JsonValueKind.Array:
                output.Write((byte)'[');
                if (style.PrettyPrinted) output.Write((byte)'\n');
                var index = 0;
                foreach (var item in value.EnumerateArray())
                {
                    WriteSeparator(output, style, depth, index++);
                    WriteValue(output, item, style, depth + 1);
                }
                WriteClose(output, style, depth);
                output.Write((byte)']');
                break;
            case JsonValueKind.String:
                WriteRawString(output, JsonMarshal.GetRawUtf8Value(value), style);
                break;
            case JsonValueKind.Number:
                var raw = JsonMarshal.GetRawUtf8Value(value);
                if (raw.IndexOfAny((byte)'.', (byte)'e', (byte)'E') < 0) output.Write(raw); // an integer literal stays as written
                else output.WriteAscii(NumberText(value.GetRawText()));
                break;
            case JsonValueKind.True: output.WriteAscii("true"); break;
            case JsonValueKind.False: output.WriteAscii("false"); break;
            case JsonValueKind.Null: output.WriteAscii("null"); break;
            default: throw new SwiftTrapException($"Unexpected JSON value kind {value.ValueKind}.");
        }
    }

    private static void WriteObject(SwiftJsonSink output, JsonElement value, SwiftJsonStyle style, int depth)
    {
        var members = ArrayPool<Member>.Shared.Rent(value.GetPropertyCount());
        try
        {
            var count = 0;
            foreach (var property in value.EnumerateObject()) members[count++] = new Member(property.Name, property.Value);
            count = Distinct(members, count, style);
            output.Write((byte)'{');
            if (style.PrettyPrinted) output.Write((byte)'\n');
            for (var i = 0; i < count; i++)
            {
                WriteSeparator(output, style, depth, i);
                WriteName(output, members[i].Name, style);
                output.WriteAscii(style.PrettyPrinted ? " : " : ":");
                WriteValue(output, members[i].Value, style, depth + 1);
            }
            WriteClose(output, style, depth);
            output.Write((byte)'}');
        }
        finally
        {
            ArrayPool<Member>.Shared.Return(members, clearArray: true); // the names and elements reference the document
        }
    }

    // Last duplicate wins, as a Swift Dictionary built from the input would; then the sorted-key order under a sorted style. The members are
    // in document order on entry, and in writing order on return (the count of the distinct ones). No allocation unless a name repeats.
    private static int Distinct(Member[] members, int count, SwiftJsonStyle style)
    {
        if (count > 1 && HasRepeatedName(members, count))
        {
            var latest = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            var order = new List<string>();
            for (var i = 0; i < count; i++)
            {
                if (!latest.ContainsKey(members[i].Name)) order.Add(members[i].Name);
                latest[members[i].Name] = members[i].Value;
            }
            if (style.SortedKeys) order.Sort(SortedKeyOrder);
            for (var i = 0; i < order.Count; i++) members[i] = new Member(order[i], latest[order[i]]);
            return order.Count;
        }
        if (style.SortedKeys && count > 1) Array.Sort(members, 0, count, MemberOrder.Instance);
        return count;
    }

    private static bool HasRepeatedName(Member[] members, int count)
    {
        if (count <= 12)
        {
            for (var i = 1; i < count; i++)
                for (var j = 0; j < i; j++)
                    if (string.Equals(members[i].Name, members[j].Name, StringComparison.Ordinal)) return true;
            return false;
        }
        var seen = new HashSet<string>(count, StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
            if (!seen.Add(members[i].Name)) return true;
        return false;
    }

    // Pretty: "\n", indent, ",\n" between members, then "\n" and the outer indent, also for an empty container
    // (corelibs JSONWriter; Apple's empty-container layout is UNVERIFIED O-04).
    private static void WriteSeparator(SwiftJsonSink output, SwiftJsonStyle style, int depth, int index)
    {
        if (index > 0)
        {
            output.Write((byte)',');
            if (style.PrettyPrinted) output.Write((byte)'\n');
        }
        if (style.PrettyPrinted) output.WriteSpaces((depth + 1) * 2);
    }

    private static void WriteClose(SwiftJsonSink output, SwiftJsonStyle style, int depth)
    {
        if (!style.PrettyPrinted) return;
        output.Write((byte)'\n');
        output.WriteSpaces(depth * 2);
    }

    /// <summary>An integer literal stays as written; any other number is re-rendered as Swift writes a Double.</summary>
    internal static string NumberText(string raw)
    {
        if (raw.AsSpan().IndexOfAny('.', 'e', 'E') < 0) return raw;
        return SwiftNumber.JsonText(double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture));
    }

    // One ASCII character of a string literal, as the old per-character switch wrote it.
    private static void AppendAscii(SwiftJsonSink output, int c, SwiftJsonStyle style)
    {
        switch (c)
        {
            case '"': output.Write((byte)'\\'); output.Write((byte)'"'); break;
            case '\\': output.Write((byte)'\\'); output.Write((byte)'\\'); break;
            case '/' when style.EscapeSlashes: output.Write((byte)'\\'); output.Write((byte)'/'); break;
            case '\b': output.Write((byte)'\\'); output.Write((byte)'b'); break;
            case '\t': output.Write((byte)'\\'); output.Write((byte)'t'); break;
            case '\n': output.Write((byte)'\\'); output.Write((byte)'n'); break;
            case '\f': output.Write((byte)'\\'); output.Write((byte)'f'); break;
            case '\r': output.Write((byte)'\\'); output.Write((byte)'r'); break;
            case < ' ':
                output.WriteAscii("\\u00");
                output.Write((byte)"0123456789abcdef"[c >> 4]);
                output.Write((byte)"0123456789abcdef"[c & 15]);
                break;
            default: output.Write((byte)c); break;
        }
    }

    private static void AppendScalar(SwiftJsonSink output, int scalar, SwiftJsonStyle style)
    {
        if (scalar < 0x80) AppendAscii(output, scalar, style);
        else if (scalar < 0x800)
        {
            output.Write((byte)(0xC0 | (scalar >> 6)));
            output.Write((byte)(0x80 | (scalar & 0x3F)));
        }
        else if (scalar < 0x10000)
        {
            output.Write((byte)(0xE0 | (scalar >> 12)));
            output.Write((byte)(0x80 | ((scalar >> 6) & 0x3F)));
            output.Write((byte)(0x80 | (scalar & 0x3F)));
        }
        else
        {
            output.Write((byte)(0xF0 | (scalar >> 18)));
            output.Write((byte)(0x80 | ((scalar >> 12) & 0x3F)));
            output.Write((byte)(0x80 | ((scalar >> 6) & 0x3F)));
            output.Write((byte)(0x80 | (scalar & 0x3F)));
        }
    }

    // A member name (a .NET string): a surrogate pair is one scalar, a lone surrogate is U+FFFD, as UTF8Encoding wrote it.
    private static void WriteName(SwiftJsonSink output, string text, SwiftJsonStyle style)
    {
        output.Write((byte)'"');
        for (var i = 0; i < text.Length;)
        {
            var c = text[i];
            if (c < 0x80)
            {
                AppendAscii(output, c, style);
                i++;
                continue;
            }
            Rune.DecodeFromUtf16(text.AsSpan(i), out var rune, out var consumed);
            AppendScalar(output, rune.Value, style);
            i += Math.Max(1, consumed);
        }
        output.Write((byte)'"');
    }

    // A string value, from the document's own bytes (the token with its quotes, JSON escapes as written). The text between escapes is
    // already valid UTF-8 and is copied whole; an escape is decoded and written the way the old per-character switch wrote that character.
    private static void WriteRawString(SwiftJsonSink output, ReadOnlySpan<byte> token, SwiftJsonStyle style)
    {
        var rest = token[1..^1];
        var special = style.EscapeSlashes ? SpecialBytesWithSlash : SpecialBytes;
        output.Write((byte)'"');
        while (!rest.IsEmpty)
        {
            var at = rest.IndexOfAny(special);
            if (at < 0)
            {
                output.Write(rest);
                break;
            }
            output.Write(rest[..at]);
            var found = rest[at];
            rest = rest[(at + 1)..];
            if (found != (byte)'\\')
            {
                AppendAscii(output, found, style);
                continue;
            }
            var escape = rest[0];
            rest = rest[1..];
            switch (escape)
            {
                case (byte)'"': AppendAscii(output, '"', style); break;
                case (byte)'\\': AppendAscii(output, '\\', style); break;
                case (byte)'/': AppendAscii(output, '/', style); break;
                case (byte)'b': AppendAscii(output, '\b', style); break;
                case (byte)'f': AppendAscii(output, '\f', style); break;
                case (byte)'n': AppendAscii(output, '\n', style); break;
                case (byte)'r': AppendAscii(output, '\r', style); break;
                case (byte)'t': AppendAscii(output, '\t', style); break;
                case (byte)'u':
                    var unit = Hex4(rest);
                    rest = rest[4..];
                    if (char.IsHighSurrogate((char)unit))
                    {
                        if (rest.Length >= 6 && rest[0] == (byte)'\\' && rest[1] == (byte)'u' && char.IsLowSurrogate((char)Hex4(rest[2..])))
                        {
                            AppendScalar(output, char.ConvertToUtf32((char)unit, (char)Hex4(rest[2..])), style);
                            rest = rest[6..];
                        }
                        else AppendScalar(output, 0xFFFD, style);
                    }
                    else if (char.IsLowSurrogate((char)unit)) AppendScalar(output, 0xFFFD, style);
                    else AppendScalar(output, unit, style);
                    break;
                default: throw new SwiftTrapException($"Unexpected JSON escape \\{(char)escape}.");
            }
        }
        output.Write((byte)'"');
    }

    private static int Hex4(ReadOnlySpan<byte> digits)
    {
        var value = 0;
        for (var i = 0; i < 4; i++) value = (value << 4) | HexValue(digits[i]);
        return value;
    }

    private static int HexValue(byte digit) => digit switch
    {
        >= (byte)'0' and <= (byte)'9' => digit - '0',
        >= (byte)'a' and <= (byte)'f' => digit - 'a' + 10,
        >= (byte)'A' and <= (byte)'F' => digit - 'A' + 10,
        _ => throw new SwiftTrapException("Unexpected JSON hex digit."),
    };
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
