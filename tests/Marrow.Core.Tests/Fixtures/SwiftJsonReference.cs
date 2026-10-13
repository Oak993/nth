using System.Text;
using System.Text.Json;
using Marrow.Core.Port;

namespace Marrow.Core.Tests.Fixtures;

/// <summary>
/// The Swift JSON writer exactly as it was before ED-NEW-perf-memory-5 (a StringBuilder, then a string, then UTF-8), frozen as the oracle that the
/// pooled-chunk writer must equal byte for byte. It is slow and allocates several times the output on purpose; only tests call it.
/// </summary>
public static class SwiftJsonReference
{
    public static byte[] Write(JsonElement value, SwiftJsonStyle style)
    {
        var output = new StringBuilder();
        WriteValue(output, value, style, 0);
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false).GetBytes(output.ToString());
    }

    private static void WriteValue(StringBuilder output, JsonElement value, SwiftJsonStyle style, int depth)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var members = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                var order = new List<string>();
                foreach (var property in value.EnumerateObject())
                {
                    if (!members.ContainsKey(property.Name)) order.Add(property.Name);
                    members[property.Name] = property.Value;
                }
                if (style.SortedKeys) order.Sort(SwiftJson.SortedKeyOrder);
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
                output.Append(SwiftJson.NumberText(value.GetRawText()));
                break;
            case JsonValueKind.True: output.Append("true"); break;
            case JsonValueKind.False: output.Append("false"); break;
            case JsonValueKind.Null: output.Append("null"); break;
            default: throw new SwiftTrapException($"Unexpected JSON value kind {value.ValueKind}.");
        }
    }

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
                case < ' ': output.Append("\\u00").Append(((int)c).ToString("x2", System.Globalization.CultureInfo.InvariantCulture)); break;
                default: output.Append(c); break;
            }
        }
        output.Append('"');
    }
}
