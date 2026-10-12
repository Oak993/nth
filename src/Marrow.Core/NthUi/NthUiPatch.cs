// PORT-ONLY: web surfaces W24, live-answer tools (DESIGN.md 7.3 "ui_update {id, patch}: set state or replace nodes by id";
// ED-NEW-web-w24-4). No Swift counterpart. How a model changes a view it already showed, in its own words:
//
//     {"state": {"instances": 5},                      // the reader's controls jump to these values
//      "nodes":  {"summary": "text \"Now five.\""},    // the node with id=summary (and what it holds) is replaced; "" removes it
//      "append": {"rows": "row \"Backup\" = 4"}}       // lines added at the end of the node with id=rows
//
// The view's text stays the single source of truth: a patch is turned into the new text, which is read again like any view. Node
// ids are the `id=` attribute (docs/nth-ui.md section 1). Nothing here writes to disk or shows anything.
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Marrow.Core.NthUi;

/// <summary>What applying a patch made: the new text and the states to set, or why it could not be applied.</summary>
public sealed record NthUiPatchResult(string? Text, string? StateSets, IReadOnlyList<string> Done, string? Error)
{
    public bool Failed => Error is not null;
}

public static class NthUiPatch
{
    /// <summary>The longest a view may be, in characters (the parser's own body cap).</summary>
    public const int MaxTextLength = NthUiParser.MaxBodyLength;

    /// <summary>The patch as an object: the object itself, or a string holding one (some models send nested objects as text). Null when it is neither.</summary>
    public static JsonObject? Read(JsonNode? patch)
    {
        if (patch is JsonObject direct) return direct;
        if (patch is JsonValue value && value.TryGetValue<string>(out var text) && text.Trim().Length > 0)
        {
            try { return JsonNode.Parse(text) as JsonObject; }
            catch (JsonException) { return null; }
        }
        return null;
    }

    /// <summary>Applies <paramref name="patch"/> to the view <paramref name="text"/>.</summary>
    public static NthUiPatchResult Apply(string text, JsonObject patch, NthUiProfile profile = NthUiProfile.Full)
    {
        var lines = SplitLines(text);
        var document = NthUiParser.Parse(text, profile);
        var done = new List<string>();
        string? stateSets = null;

        foreach (var (key, value) in patch)
        {
            switch (key)
            {
                case "state":
                    {
                        if (value is not JsonObject sets) return Fail("state must be an object that maps each state name to its new value.");
                        var fields = new List<KeyValuePair<string, NthUiValue>>();
                        foreach (var (name, setting) in sets)
                        {
                            if (!document.Declarations.TryGetValue(name, out var declaration) || declaration.Kind != "state")
                            {
                                var known = document.Declarations.Where(pair => pair.Value.Kind == "state").Select(pair => pair.Key).Order(StringComparer.Ordinal).ToArray();
                                return Fail($"\"{name}\" is not a state of this view" + (known.Length == 0 ? "; the view declares no state." : $"; its states are {string.Join(", ", known)}."));
                            }
                            fields.Add(new(name, NthUiValue.FromJson(setting?.ToJsonString() ?? "null")));
                            done.Add($"set {name}");
                        }
                        stateSets = fields.Count == 0 ? null : NthUiValue.Object(fields).ToJson();
                        break;
                    }
                case "nodes":
                case "append":
                    {
                        if (value is not JsonObject edits) return Fail($"{key} must be an object that maps a node id to lines of nth-ui.");
                        foreach (var (id, change) in edits)
                        {
                            var replacement = change is JsonValue scalar && scalar.TryGetValue<string>(out var given) ? given
                                : change is null ? "" : null;
                            if (replacement is null) return Fail($"The lines for \"{id}\" must be text.");
                            // Each edit reads the view as the edits before it left it, so ids made by an earlier edit can be used.
                            document = NthUiParser.Parse(string.Join('\n', lines), profile);
                            var node = document.Nodes.FirstOrDefault(n => !n.Degraded && string.Equals(n.Id, id, StringComparison.OrdinalIgnoreCase));
                            if (node is null)
                            {
                                var ids = document.Nodes.Where(n => !n.Degraded && n.Id is not null).Select(n => n.Id!).ToArray();
                                return Fail($"No node has id=\"{id}\"" + (ids.Length == 0 ? "; this view gives no node an id, so only state can be changed (or show the view again)." : $"; the ids are {string.Join(", ", ids)}."));
                            }
                            var indent = Indent(lines[node.Line]);
                            var end = SubtreeEnd(lines, node.Line, indent);
                            var added = Lines(replacement);
                            if (key == "nodes")
                            {
                                var block = Reindent(added, indent);
                                lines.RemoveRange(node.Line, end - node.Line);
                                lines.InsertRange(node.Line, block);
                                done.Add(block.Count == 0 ? $"removed {id}" : $"replaced {id}");
                            }
                            else
                            {
                                if (added.Count == 0) return Fail($"There are no lines to add to \"{id}\".");
                                lines.InsertRange(end, Reindent(added, indent + 2));
                                done.Add($"added to {id}");
                            }
                        }
                        break;
                    }
                default:
                    return Fail($"\"{key}\" is not part of a patch; use state, nodes or append.");
            }
        }
        if (done.Count == 0) return Fail("The patch changes nothing; give state, nodes or append.");
        var result = string.Join('\n', lines);
        if (result.Length > MaxTextLength) return Fail($"The view would be longer than {MaxTextLength:N0} characters.");
        return new NthUiPatchResult(result, stateSets, done, null);

        static NthUiPatchResult Fail(string message) => new(null, null, [], message);
    }

    /// <summary>The text of a view with its fence lines (a model's habit) removed and line ends made plain.</summary>
    public static string Clean(string text)
    {
        var kept = SplitLines(text).Where(line => !line.TrimStart().StartsWith("```", StringComparison.Ordinal));
        return string.Join('\n', kept);
    }

    /// <summary>The first title of a view worth naming it by: the title of the first card or section, else the first heading; null when none.</summary>
    public static string? TitleOf(NthUiDocument document)
    {
        foreach (var kind in new[] { "card", "section", "heading" })
        {
            var node = document.Nodes.FirstOrDefault(n => !n.Degraded && n.Kind == kind && n.Text is { Length: > 0 });
            if (node?.Text is { } title) return Shorten(title);
        }
        return null;
    }

    private static string Shorten(string text)
    {
        var one = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return one.Length <= 60 ? one : one[..59].TrimEnd() + "…";
    }

    private static List<string> SplitLines(string text) => [.. text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n')];

    /// <summary>The lines of a replacement: fence lines dropped, trailing blank lines dropped.</summary>
    private static List<string> Lines(string text)
    {
        var lines = SplitLines(text).Where(line => !line.TrimStart().StartsWith("```", StringComparison.Ordinal)).ToList();
        while (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
        while (lines.Count > 0 && lines[0].Trim().Length == 0) lines.RemoveAt(0);
        return lines;
    }

    /// <summary>The lines moved so the shallowest sits at <paramref name="indent"/> columns.</summary>
    private static List<string> Reindent(List<string> lines, int indent)
    {
        var least = lines.Where(line => line.Trim().Length > 0).Select(Indent).DefaultIfEmpty(0).Min();
        return [.. lines.Select(line => line.Trim().Length == 0 ? "" : new string(' ', indent) + Dedent(line, least))];
    }

    /// <summary>The line without its first <paramref name="columns"/> columns of indent (a tab counts as two).</summary>
    private static string Dedent(string line, int columns)
    {
        var i = 0;
        var seen = 0;
        while (i < line.Length && seen < columns && line[i] is ' ' or '\t')
        {
            seen += line[i] == '\t' ? 2 : 1;
            i++;
        }
        return line[i..];
    }

    public static int Indent(string line)
    {
        var columns = 0;
        foreach (var c in line)
        {
            if (c == ' ') columns++;
            else if (c == '\t') columns += 2;
            else break;
        }
        return columns;
    }

    /// <summary>The index after the last line that belongs to the node at <paramref name="start"/>: the lines under it, bodies included.</summary>
    private static int SubtreeEnd(List<string> lines, int start, int indent)
    {
        var end = start + 1;
        var last = start + 1;
        while (end < lines.Count)
        {
            if (lines[end].Trim().Length == 0) { end++; continue; }
            if (Indent(lines[end]) <= indent) break;
            end++;
            last = end;
        }
        return last;
    }
}
