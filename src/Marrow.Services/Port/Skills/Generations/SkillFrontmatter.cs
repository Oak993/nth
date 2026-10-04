// PORT-ONLY: SK-9 read-only clash metadata and explicit-only gate. Unsupported YAML fails closed, never guesses.
using System.Text.Json;

namespace Marrow.Services.Port.Skills.Generations;

public static class SkillFrontmatter
{
    public static IReadOnlyDictionary<string, string> Read(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimStart('\uFEFF').Split('\n');
        if (lines.Length < 3 || lines[0] != "---") throw new InvalidDataException("A skill requires frontmatter.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var closed = false;
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line == "---") { closed = true; break; }
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) continue;
            // Unneeded YAML structures (metadata, allowed-tools, etc.) are skipped; only the disclosure fields are interpreted.
            if (char.IsWhiteSpace(line[0])) continue;
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0) throw new InvalidDataException("Invalid skill frontmatter.");
            var key = line[..colon];
            if (key is not ("name" or "description" or "disable-model-invocation")) continue;
            var value = StripComment(line[(colon + 1)..], whitespaceRequired: true).Trim();
            if (value is ">" or ">-" or "|" or "|-")
            {
                var fragments = new List<string>();
                while (i + 1 < lines.Length && (lines[i + 1].Length == 0 || char.IsWhiteSpace(lines[i + 1][0]))) fragments.Add(lines[++i].Trim());
                value = string.Join(' ', fragments).Trim(); // description is disclosed as one line; source is never rewritten.
            }
            else if (value.StartsWith('"')) value = JsonSerializer.Deserialize<string>(value) ?? throw new InvalidDataException("Invalid quoted skill value.");
            else if (value.StartsWith('\''))
            {
                if (!value.EndsWith('\'') || value.Length < 2) throw new InvalidDataException("Invalid literal skill value.");
                value = value[1..^1].Replace("''", "'", StringComparison.Ordinal);
            }
            else if (value.StartsWith('[') || value.StartsWith('{') || value.StartsWith('&') || value.StartsWith('*'))
                throw new InvalidDataException("Unsupported skill disclosure YAML.");
            if (!values.TryAdd(key, value)) throw new InvalidDataException("Duplicate skill disclosure field.");
        }
        if (!closed) throw new InvalidDataException("Unclosed skill frontmatter.");
        return values;
    }

    internal static string StripComment(string value, bool whitespaceRequired)
    {
        var quote = '\0';
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (quote != '\0')
            {
                if (c == '\\' && quote == '"') { i++; continue; }
                if (c == quote) quote = '\0';
            }
            else if (c is '\'' or '"') quote = c;
            else if (c == '#' && (!whitespaceRequired || i == 0 || char.IsWhiteSpace(value[i - 1]))) return value[..i];
        }
        return value;
    }

    public static bool ExplicitOnly(string text)
    {
        var values = Read(text);
        return values.TryGetValue("disable-model-invocation", out var value) && value == "true";
    }

    public static ExistingSkill Disclosure(string text)
    {
        var values = Read(text);
        if (!values.TryGetValue("name", out var name) || !values.TryGetValue("description", out var description) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(description))
            throw new InvalidDataException("A user skill has incomplete disclosure metadata.");
        return new(name, description);
    }
}
