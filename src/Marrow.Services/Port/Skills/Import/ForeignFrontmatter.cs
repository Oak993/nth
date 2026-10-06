// PORT-ONLY: first-release feature unit SK-18 (OD-0118). A tolerant reader for the front matter of skills other tools wrote. SkillFrontmatter (SK-9) fails closed on anything it does not know, which is right for
// what Marrow hands out; here a skill from another app is read to be LISTED, so a file that cannot be understood is reported and left out, never an exception.
using System.Text;
using System.Text.Json;
using Marrow.Services.Port.Skills.Generations;

namespace Marrow.Services.Port.Skills.Import;

public static class ForeignFrontmatter
{
    public sealed record Parsed(string? Name, string? Description, string Body, bool HasFrontmatter);

    private readonly record struct Segment(string Content, string Ending);

    /// <summary>Reads <c>name</c> and <c>description</c> from a leading <c>---</c> block. Never throws.</summary>
    public static Parsed Parse(string text)
    {
        try
        {
            return ParseCore(text);
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or IndexOutOfRangeException)
        {
            return new Parsed(null, null, text, false);
        }
    }

    private static Parsed ParseCore(string text)
    {
        var raw = text.TrimStart('﻿');
        var lines = Segments(raw);
        if (lines.Count < 2 || lines[0].Content.TrimEnd() != "---") return new Parsed(null, null, raw, false);
        var close = lines.FindIndex(1, l => l.Content.TrimEnd() == "---");
        if (close < 0) return new Parsed(null, null, raw, false);

        string? name = null, description = null;
        for (var i = 1; i < close; i++)
        {
            var line = lines[i].Content;
            if (line.Length == 0 || char.IsWhiteSpace(line[0]) || line.TrimStart().StartsWith('#')) continue;
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0) continue;
            var key = line[..colon].Trim();
            var wanted = key is "name" or "description";
            var value = SkillFrontmatter.StripComment(line[(colon + 1)..], whitespaceRequired: true).Trim();
            var continuation = new List<string>();
            while (i + 1 < close && (lines[i + 1].Content.Length == 0 || char.IsWhiteSpace(lines[i + 1].Content[0])))
                continuation.Add(lines[++i].Content.Trim());
            if (!wanted) continue;

            string? result;
            if (value is ">" or ">-" or ">+" or "|" or "|-" or "|+") result = string.Join(' ', continuation);
            else if (value.StartsWith('"')) result = DoubleQuoted(value, continuation);
            else if (value.StartsWith('\'')) result = value.Length >= 2 && value.EndsWith('\'') ? value[1..^1].Replace("''", "'", StringComparison.Ordinal) : null;
            else if (value.Length == 0 && continuation.Count == 0) result = null;
            else if (value.Length > 0 && value[0] is '[' or '{' or '&' or '*' or '!' or '%' or '@' or '`') result = null;
            else result = string.Join(' ', new[] { value }.Concat(continuation));
            if (result is null) continue;
            var cleaned = Clean(result);
            if (cleaned.Length == 0) continue;
            if (key == "name") name ??= cleaned;
            else description ??= cleaned;
        }
        var body = string.Concat(lines.Skip(close + 1).Select(l => l.Content + l.Ending));
        return new Parsed(name, description, body, true);
    }

    private static string? DoubleQuoted(string value, List<string> continuation)
    {
        var joined = continuation.Count == 0 ? value : value + " " + string.Join(' ', continuation);
        try { return JsonSerializer.Deserialize<string>(joined); }
        catch (JsonException) { return null; }
    }

    private static string Clean(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c is '\n' or '\r' or '\t') builder.Append(' ');
            else if (!char.IsControl(c)) builder.Append(c);
        }
        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static List<Segment> Segments(string text)
    {
        var segments = new List<Segment>();
        var start = 0;
        while (start < text.Length)
        {
            var end = text.IndexOf('\n', start);
            if (end < 0) { segments.Add(new Segment(text[start..], "")); break; }
            var content = text[start..end];
            var ending = content.EndsWith('\r') ? "\r\n" : "\n";
            segments.Add(new Segment(content.TrimEnd('\r'), ending));
            start = end + 1;
        }
        return segments;
    }

    /// <summary>The same text with the front matter's <c>name</c> set to <paramref name="name"/> (added right after the opening line when missing); everything else is untouched.</summary>
    public static string WithName(string text, string name)
    {
        var lines = Segments(text);
        var close = lines.Count > 1 && lines[0].Content.TrimEnd() == "---" ? lines.FindIndex(1, l => l.Content.TrimEnd() == "---") : -1;
        if (close < 0) return "---\nname: " + name + "\n---\n" + text;
        var eol = lines[0].Ending.Length > 0 ? lines[0].Ending : "\n";
        bool IsName(Segment l) => l.Content.StartsWith("name:", StringComparison.Ordinal);
        var builder = new StringBuilder();
        var hasName = lines.Take(close).Skip(1).Any(IsName);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (i > 0 && i < close && IsName(line))
            {
                builder.Append("name: ").Append(name).Append(line.Ending.Length == 0 ? eol : line.Ending);
                while (i + 1 < close && (lines[i + 1].Content.Length == 0 || char.IsWhiteSpace(lines[i + 1].Content[0]))) i++;
                continue;
            }
            builder.Append(line.Content).Append(line.Ending);
            if (i == 0 && !hasName) builder.Append("name: ").Append(name).Append(eol);
        }
        return builder.ToString();
    }

    /// <summary>The text of a Marrow-made SKILL.md.</summary>
    public static string Compose(string name, string description, string body)
    {
        var quoted = NeedsQuotes(description) ? JsonSerializer.Serialize(description, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) : description;
        return "---\nname: " + name + "\ndescription: " + quoted + "\n---\n" + body.TrimEnd('\n', '\r') + "\n";
    }

    private static bool NeedsQuotes(string value) =>
        value.Length == 0 || value != value.Trim() || value.IndexOfAny([':', '#', '"', '\'', '[', ']', '{', '}', '&', '*', '!', '|', '>', '%', '@', '`', ',']) >= 0 && (value.Contains(": ", StringComparison.Ordinal) || value.Contains(" #", StringComparison.Ordinal) || "[]{}&*!|>%@`\"'".Contains(value[0], StringComparison.Ordinal) || value.EndsWith(':'));
}
