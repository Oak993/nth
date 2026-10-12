// PORT-ONLY: Dextra (DESIGN.md rev 5, 0 point 12, 15.5 "Transport"; unit AS-4; ED-NEW-dextra-as4-6). No Swift twin.
// The fenced-text fallback: a model with no tool calls asks for an nth-dextra tool with a fenced block in its answer,
//
//     ```nth-dextra recall
//     {"query": "invoice address"}
//     ```
//
// whose info line is the server's name and the tool's name and whose body is the arguments as one JSON object. Only a
// closed block at the top level counts: a block inside another fence (an example the model quotes), inside a quote
// ("> ```"), or one left open at the end is text. Pure: it finds the blocks; the caller runs them through the same
// policy-checked path as every other route.
namespace Marrow.Core.Port.Assistant;

/// <summary>One fenced call: the tool it names (empty when the info line names none), its body, and its line (1-based).</summary>
public sealed record AssistantFencedCall(string Tool, string Body, int Line);

public static class AssistantFencedCalls
{
    /// <summary>The most blocks one answer may run; later ones are reported as not run.</summary>
    public const int MaxPerAnswer = 4;

    /// <summary>The longest body read, in UTF-16 units; a longer one is reported as too long.</summary>
    public const int MaxBody = 64 * 1024;

    /// <summary>The closed top-level nth-dextra blocks of an answer, in order (at most <paramref name="limit"/>).</summary>
    public static IReadOnlyList<AssistantFencedCall> Parse(string? answer, int limit = int.MaxValue)
    {
        var calls = new List<AssistantFencedCall>();
        if (string.IsNullOrEmpty(answer) || !answer.Contains(AssistantNames.Server, StringComparison.OrdinalIgnoreCase)) return calls;
        var lines = answer.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var index = 0;
        while (index < lines.Length && calls.Count < limit)
        {
            if (Opening(lines[index]) is not { } fence) { index++; continue; }
            var close = index + 1;
            while (close < lines.Length && !Closes(lines[close], fence)) close++;
            if (close >= lines.Length) break; // left open: text, and nothing after it is a fence either
            if (Tool(fence.Info) is { } tool)
            {
                var body = string.Join("\n", lines[(index + 1)..close]);
                calls.Add(new AssistantFencedCall(tool, body, index + 1));
            }
            index = close + 1;
        }
        return calls;
    }

    private readonly record struct Fence(char Mark, int Length, string Info);

    // An opening fence: at most three spaces, then three or more backticks or tildes, then the info line (which, for a
    // backtick fence, holds no backtick), as CommonMark reads it.
    private static Fence? Opening(string line)
    {
        var at = Indent(line);
        if (at is null) return null;
        var start = at.Value;
        if (start >= line.Length || line[start] is not ('`' or '~')) return null;
        var mark = line[start];
        var end = start;
        while (end < line.Length && line[end] == mark) end++;
        if (end - start < 3) return null;
        var info = line[end..].Trim();
        if (mark == '`' && info.Contains('`', StringComparison.Ordinal)) return null;
        return new Fence(mark, end - start, info);
    }

    private static bool Closes(string line, Fence fence)
    {
        var at = Indent(line);
        if (at is null) return false;
        var end = at.Value;
        while (end < line.Length && line[end] == fence.Mark) end++;
        return end - at.Value >= fence.Length && line[end..].Trim().Length == 0;
    }

    private static int? Indent(string line)
    {
        var spaces = 0;
        while (spaces < line.Length && line[spaces] == ' ') spaces++;
        return spaces <= 3 ? spaces : null;
    }

    // "nth-dextra recall" names recall; "nth-dextra" alone names no tool (an empty name, so the caller can say so);
    // any other info word is not one of ours.
    private static string? Tool(string info)
    {
        var words = info.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0 || !string.Equals(words[0], AssistantNames.Server, StringComparison.OrdinalIgnoreCase)) return null;
        return words.Length > 1 ? words[1] : "";
    }
}
