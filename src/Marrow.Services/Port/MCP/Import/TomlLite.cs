// PORT-ONLY: owner request 2026-10-11 (ED-NEW-mcpimport-1). A small read-only TOML reader, enough for ~/.codex/config.toml's [mcp_servers.*] tables: tables and dotted keys, basic and literal
// strings (one line or multiline), arrays, inline tables, and bare scalars (numbers, booleans, dates) kept as their text. Arrays of tables are read and dropped. Anything it cannot make sense of throws a
// FormatException, which the scanner reports as an unreadable file.
using System.Globalization;
using System.Text;

namespace Marrow.Services.Port.MCP.Import;

internal sealed class TomlLite
{
    /// <summary>A bare value (a number, a boolean, a date), kept as its text and told apart from a string.</summary>
    public sealed class Scalar(string text)
    {
        public string Text { get; } = text;

        public override string ToString() => Text;
    }

    /// <summary>How deep arrays and inline tables may nest (System.Text.Json's own limit): a deeper file is refused as unreadable instead of overflowing the stack.</summary>
    internal const int MaxDepth = 64;

    private readonly string text;
    private int at;
    private int depth;

    private TomlLite(string text) => this.text = text;

    /// <summary>The document as nested dictionaries: a value is a string, a Scalar, a List&lt;object&gt; or a Dictionary&lt;string, object&gt;.</summary>
    public static Dictionary<string, object> Parse(string text) => new TomlLite(text).Document();

    private char Peek(int ahead = 0) => at + ahead < text.Length ? text[at + ahead] : '\0';

    private bool End => at >= text.Length;

    private bool Starts(string token) => string.CompareOrdinal(text, at, token, 0, token.Length) == 0;

    private FormatException Fail(string what)
    {
        var line = 1;
        for (var i = 0; i < at && i < text.Length; i++) if (text[i] == '\n') line++;
        return new FormatException(what + " on line " + line.ToString(CultureInfo.InvariantCulture) + ".");
    }

    private Dictionary<string, object> Document()
    {
        var root = new Dictionary<string, object>(StringComparer.Ordinal);
        var current = root;
        if (Peek() == '﻿') at++;
        while (true)
        {
            SkipBlank(newlines: true);
            if (End) break;
            if (Peek() == '[')
            {
                if (Peek(1) == '[')
                {
                    at += 2;
                    var path = KeyPath();
                    SkipBlank(newlines: false);
                    if (!Starts("]]")) throw Fail("An array table is not closed");
                    at += 2;
                    current = new Dictionary<string, object>(StringComparer.Ordinal); // read, kept apart, dropped
                    _ = path;
                }
                else
                {
                    at++;
                    var path = KeyPath();
                    SkipBlank(newlines: false);
                    if (Peek() != ']') throw Fail("A table header is not closed");
                    at++;
                    current = Table(root, path);
                }
                LineEnd();
                continue;
            }
            var keys = KeyPath();
            SkipBlank(newlines: false);
            if (Peek() != '=') throw Fail("A key has no value");
            at++;
            SkipBlank(newlines: false);
            Assign(current, keys, Value());
            LineEnd();
        }
        return root;
    }

    private static Dictionary<string, object> Table(Dictionary<string, object> root, IReadOnlyList<string> path)
    {
        var table = root;
        foreach (var key in path)
        {
            if (!table.TryGetValue(key, out var next)) table[key] = next = new Dictionary<string, object>(StringComparer.Ordinal);
            table = next as Dictionary<string, object> ?? throw new FormatException("“" + key + "” is both a value and a table.");
        }
        return table;
    }

    private static void Assign(Dictionary<string, object> table, IReadOnlyList<string> keys, object value)
    {
        var parent = Table(table, keys.Take(keys.Count - 1).ToArray());
        parent[keys[^1]] = value;
    }

    private void SkipBlank(bool newlines)
    {
        while (!End)
        {
            var c = Peek();
            if (c is ' ' or '\t') at++;
            else if (newlines && c is '\r' or '\n') at++;
            else if (newlines && c == '#') { while (!End && Peek() != '\n') at++; }
            else break;
        }
    }

    private void LineEnd()
    {
        SkipBlank(newlines: false);
        if (Peek() == '#') while (!End && Peek() != '\n') at++;
        if (End) return;
        if (Peek() == '\r') at++;
        if (Peek() != '\n') throw Fail("Unexpected text after a value");
        at++;
    }

    private List<string> KeyPath()
    {
        var keys = new List<string>();
        while (true)
        {
            SkipBlank(newlines: false);
            var c = Peek();
            if (c == '"') keys.Add(Basic());
            else if (c == '\'') keys.Add(Literal());
            else
            {
                var start = at;
                while (!End && (char.IsAsciiLetterOrDigit(Peek()) || Peek() is '_' or '-')) at++;
                if (at == start) throw Fail("A key is missing");
                keys.Add(text[start..at]);
            }
            SkipBlank(newlines: false);
            if (Peek() != '.') return keys;
            at++;
        }
    }

    private object Value()
    {
        if (Starts("\"\"\"")) return MultilineBasic();
        if (Starts("'''")) return MultilineLiteral();
        switch (Peek())
        {
            case '"': return Basic();
            case '\'': return Literal();
            case '[' or '{':
                if (++depth > MaxDepth) throw Fail("Values are nested too deeply");
                try { return Peek() == '[' ? Array() : Inline(); }
                finally { depth--; }
        }
        var start = at;
        while (!End && Peek() is not (',' or ']' or '}' or '#' or '\r' or '\n')) at++;
        var bare = text[start..at].TrimEnd(' ', '\t');
        at = start + bare.Length;
        if (bare.Length == 0) throw Fail("A value is missing");
        return new Scalar(bare);
    }

    private string Basic()
    {
        at++; // "
        var built = new StringBuilder();
        while (true)
        {
            if (End || Peek() is '\n' or '\r') throw Fail("A string is not closed");
            var c = text[at++];
            if (c == '"') return built.ToString();
            if (c == '\\') Escape(built);
            else built.Append(c);
        }
    }

    private string MultilineBasic()
    {
        at += 3;
        if (Peek() == '\r') at++;
        if (Peek() == '\n') at++;
        var built = new StringBuilder();
        while (true)
        {
            if (End) throw Fail("A string is not closed");
            if (Starts("\"\"\"")) { at += 3; while (Peek() == '"') { built.Append('"'); at++; } return built.ToString(); }
            var c = text[at++];
            if (c == '\\')
            {
                // a backslash at the end of a line trims the line break and the blanks after it
                var look = at;
                while (look < text.Length && text[look] is ' ' or '\t') look++;
                if (look < text.Length && text[look] is '\r' or '\n')
                {
                    at = look;
                    while (!End && Peek() is ' ' or '\t' or '\r' or '\n') at++;
                    continue;
                }
                Escape(built);
            }
            else built.Append(c);
        }
    }

    private void Escape(StringBuilder built)
    {
        if (End) throw Fail("A string is not closed");
        var e = text[at++];
        switch (e)
        {
            case 'b': built.Append('\b'); break;
            case 't': built.Append('\t'); break;
            case 'n': built.Append('\n'); break;
            case 'f': built.Append('\f'); break;
            case 'r': built.Append('\r'); break;
            case 'e': built.Append('\u001B'); break;
            case '"': built.Append('"'); break;
            case '\\': built.Append('\\'); break;
            case 'u': built.Append(Unicode(4)); break;
            case 'U': built.Append(Unicode(8)); break;
            default: throw Fail("A string has an unknown escape");
        }
    }

    private string Unicode(int digits)
    {
        if (at + digits > text.Length || !int.TryParse(text.AsSpan(at, digits), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code) || code > 0x10FFFF || code is >= 0xD800 and <= 0xDFFF)
            throw Fail("A string has a bad unicode escape");
        at += digits;
        return char.ConvertFromUtf32(code);
    }

    private string Literal()
    {
        at++; // '
        var start = at;
        while (!End && Peek() != '\'' && Peek() is not ('\n' or '\r')) at++;
        if (Peek() != '\'') throw Fail("A string is not closed");
        var value = text[start..at];
        at++;
        return value;
    }

    private string MultilineLiteral()
    {
        at += 3;
        if (Peek() == '\r') at++;
        if (Peek() == '\n') at++;
        var close = text.IndexOf("'''", at, StringComparison.Ordinal);
        if (close < 0) throw Fail("A string is not closed");
        var value = text[at..close];
        at = close + 3;
        while (Peek() == '\'') { value += "'"; at++; }
        return value;
    }

    private List<object> Array()
    {
        at++; // [
        var items = new List<object>();
        while (true)
        {
            SkipBlank(newlines: true);
            if (End) throw Fail("An array is not closed");
            if (Peek() == ']') { at++; return items; }
            items.Add(Value());
            SkipBlank(newlines: true);
            if (Peek() == ',') { at++; continue; }
            if (Peek() == ']') { at++; return items; }
            throw Fail("An array is not closed");
        }
    }

    private Dictionary<string, object> Inline()
    {
        at++; // {
        var table = new Dictionary<string, object>(StringComparer.Ordinal);
        SkipBlank(newlines: true);
        if (Peek() == '}') { at++; return table; }
        while (true)
        {
            SkipBlank(newlines: true);
            if (Peek() == '}') { at++; return table; } // a trailing comma
            var keys = KeyPath();
            SkipBlank(newlines: true);
            if (Peek() != '=') throw Fail("A key has no value");
            at++;
            SkipBlank(newlines: true);
            Assign(table, keys, Value());
            SkipBlank(newlines: true);
            if (Peek() == ',') { at++; continue; }
            if (Peek() == '}') { at++; return table; }
            throw Fail("An inline table is not closed");
        }
    }
}
