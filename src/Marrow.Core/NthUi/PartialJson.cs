// PORT-ONLY: web surfaces W24, live-answer tools (DESIGN.md 7.2 "streamed line by line"; ED-NEW-web-w24-3). No Swift counterpart.
// A model that calls ui_show streams its arguments as JSON, a piece at a time: {"ui":"card \"Hosting\"\n  state n = 3\n  ste
// The view inside is worth drawing before the call ends, so this reads the top-level members of a JSON object that may stop
// anywhere. A string value comes back as far as it has arrived (a cut escape is left out), flagged unfinished until its closing
// quote; a number, true, false or null comes back as written once a delimiter follows it. Nothing here throws: text that is not an
// object returns nothing.
using System.Globalization;
using System.Text;

namespace Marrow.Core.NthUi;

/// <summary>One top-level member of a JSON object read before the object ended.</summary>
/// <param name="Value">A string's text so far, or a number or literal as written.</param>
/// <param name="IsString">True for a string value.</param>
/// <param name="Complete">True when the value has ended (a string's closing quote, or a delimiter after a literal).</param>
public sealed record PartialJsonMember(string Value, bool IsString, bool Complete);

public static class PartialJson
{
    /// <summary>The top-level members of the object in <paramref name="json"/> that have a string, number, boolean or null value, in order.
    /// Members with an object or array value are skipped (their end is found, so later members are still read).</summary>
    public static IReadOnlyDictionary<string, PartialJsonMember> Members(string? json)
    {
        var result = new Dictionary<string, PartialJsonMember>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(json)) return result;
        var at = 0;
        Skip(json, ref at);
        if (at >= json.Length || json[at] != '{') return result;
        at++;
        while (true)
        {
            Skip(json, ref at);
            if (at >= json.Length) return result;
            if (json[at] == '}') return result;
            if (json[at] == ',') { at++; continue; }
            if (json[at] != '"') return result;
            var key = ReadString(json, ref at, out var keyEnded);
            if (!keyEnded) return result;
            Skip(json, ref at);
            if (at >= json.Length || json[at] != ':') return result;
            at++;
            Skip(json, ref at);
            if (at >= json.Length) return result;
            var c = json[at];
            if (c == '"')
            {
                var text = ReadString(json, ref at, out var ended);
                result[key] = new PartialJsonMember(text, true, ended);
                if (!ended) return result;
            }
            else if (c is '{' or '[')
            {
                if (!SkipNested(json, ref at)) return result;
            }
            else
            {
                var start = at;
                while (at < json.Length && json[at] is not (',' or '}' or ' ' or '\t' or '\r' or '\n')) at++;
                var literal = json[start..at];
                var ended = at < json.Length;
                if (literal.Length > 0) result[key] = new PartialJsonMember(literal, false, ended);
                if (!ended) return result;
            }
        }
    }

    /// <summary>The string member <paramref name="key"/> as far as it has arrived, and whether it ended; null when it has not started.</summary>
    public static (string Text, bool Complete)? String(string? json, string key) =>
        Members(json).TryGetValue(key, out var member) && member.IsString ? (member.Value, member.Complete) : null;

    /// <summary>The whole-number member <paramref name="key"/>, or null when it is absent, unfinished or not a whole number.</summary>
    public static int? Integer(string? json, string key) =>
        Members(json).TryGetValue(key, out var member) && !member.IsString && member.Complete
        && int.TryParse(member.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static void Skip(string json, ref int at)
    {
        while (at < json.Length && json[at] is ' ' or '\t' or '\r' or '\n') at++;
    }

    /// <summary>Reads the string at <paramref name="at"/> (which holds its opening quote) and leaves <paramref name="at"/> after its closing
    /// quote, or at the end when it has not ended.</summary>
    private static string ReadString(string json, ref int at, out bool ended)
    {
        var text = new StringBuilder();
        at++;
        ended = false;
        while (at < json.Length)
        {
            var c = json[at];
            if (c == '"')
            {
                at++;
                ended = true;
                return text.ToString();
            }
            if (c != '\\')
            {
                text.Append(c);
                at++;
                continue;
            }
            if (at + 1 >= json.Length) break; // a cut escape
            var next = json[at + 1];
            switch (next)
            {
                case 'n': text.Append('\n'); at += 2; break;
                case 't': text.Append('\t'); at += 2; break;
                case 'r': text.Append('\r'); at += 2; break;
                case 'b': text.Append('\b'); at += 2; break;
                case 'f': text.Append('\f'); at += 2; break;
                case 'u':
                    if (at + 6 > json.Length) { at = json.Length; return text.ToString(); } // a cut \uXXXX
                    if (int.TryParse(json.AsSpan(at + 2, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code)) text.Append((char)code);
                    at += 6;
                    break;
                default: text.Append(next); at += 2; break; // \" \\ \/ and anything else
            }
        }
        at = json.Length;
        return text.ToString();
    }

    /// <summary>Moves past the object or array at <paramref name="at"/>; false when it has not ended.</summary>
    private static bool SkipNested(string json, ref int at)
    {
        var depth = 0;
        while (at < json.Length)
        {
            var c = json[at];
            if (c == '"')
            {
                ReadString(json, ref at, out var ended);
                if (!ended) return false;
                continue;
            }
            if (c is '{' or '[') depth++;
            else if (c is '}' or ']')
            {
                depth--;
                if (depth == 0)
                {
                    at++;
                    return true;
                }
            }
            at++;
        }
        return false;
    }
}
