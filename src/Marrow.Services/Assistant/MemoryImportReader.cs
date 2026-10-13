// PORT-ONLY: Dextra (DESIGN.md rev 5, 8.4; unit AS-12; ED-NEW-dextra-as12-13). No Swift twin.
// Reads one markdown file an agent keeps (Claude Code's CLAUDE.md and memory folders, Codex's AGENTS.md and memory files) into
// the lines worth a note: a bullet or a paragraph at a time, outside code blocks, comments and tables, with the markdown taken
// off. Pure text in, candidates out: the file is data, and nothing in it is followed.
using System.Text;
using System.Text.RegularExpressions;
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

/// <summary>What kind of file the lines came from.</summary>
internal enum ImportFileKind
{
    /// <summary>CLAUDE.md or AGENTS.md: instructions the owner gave an agent.</summary>
    Instructions,

    /// <summary>A file in a memory folder: notes an agent kept, often with a front matter naming its type.</summary>
    MemoryFolder,
}

/// <summary>One line worth a note: its words, the kind it looks like, the topic (a heading) and the heading's words.</summary>
internal sealed record ImportCandidate(string Text, NoteKind Kind, string Topic, string Heading);

internal static partial class MemoryImportReader
{
    public const int MaxCandidatesPerFile = 400;
    public const int MaxChunksPerItem = 4;
    public const int MinItemChars = 12;

    [GeneratedRegex(@"\A(?:[-*+]|\d{1,3}[.)])\s+(?<text>.*)\z", RegexOptions.CultureInvariant, 250)] private static partial Regex Bullet();

    [GeneratedRegex(@"\A#{1,6}\s+(?<text>.*?)\s*#*\z", RegexOptions.CultureInvariant, 250)] private static partial Regex Heading();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)", RegexOptions.CultureInvariant, 250)] private static partial Regex Image();

    [GeneratedRegex(@"\[(?<label>[^\]]+)\]\((?<target>[^)\s]*)[^)]*\)", RegexOptions.CultureInvariant, 250)] private static partial Regex Link();

    [GeneratedRegex(@"(?<![\w*])(?<m>\*\*|__|\*|_|~~)(?<text>[^*_~\s](?:[^*_~]*[^*_~\s])?)\k<m>(?![\w*])", RegexOptions.CultureInvariant, 250)] private static partial Regex Emphasis();

    [GeneratedRegex(@"\A(?:[-*_]\s*){3,}\z", RegexOptions.CultureInvariant, 250)] private static partial Regex Rule();

    [GeneratedRegex(@"\A\s*\|.*\|\s*\z", RegexOptions.CultureInvariant, 250)] private static partial Regex TableRow();

    [GeneratedRegex(@"[.!?]\s+", RegexOptions.CultureInvariant, 250)] private static partial Regex SentenceEnd();

    /// <summary>
    /// The candidates of a file. <paramref name="siblingExists"/> says whether a relative link in a memory index names a file that is
    /// there: such a line is an index entry, and the file it names is imported on its own, so the line is skipped.
    /// </summary>
    public static IReadOnlyList<ImportCandidate> Read(string fileName, string text, ImportFileKind kind, Func<string, bool> siblingExists)
    {
        var fallback = kind == ImportFileKind.Instructions ? NoteKind.Procedure : NoteKind.Fact;
        var lines = text.TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var start = 0;
        string? name = null;
        string? description = null;
        string? type = null;
        if (lines.Length > 2 && lines[0].TrimEnd() == "---")
        {
            var end = Array.FindIndex(lines, 1, Math.Min(lines.Length - 1, 40), l => l.TrimEnd() == "---");
            if (end > 0)
            {
                foreach (var line in lines[1..end])
                {
                    var colon = line.IndexOf(':', StringComparison.Ordinal);
                    if (colon <= 0) continue;
                    var key = line[..colon].Trim().ToLowerInvariant();
                    var value = line[(colon + 1)..].Trim().Trim('"', '\'');
                    if (key == "name") name = value;
                    else if (key == "description") description = value;
                    else if (key == "type") type = value.ToLowerInvariant();
                }
                start = end + 1;
            }
        }
        if (type is not null) fallback = type switch
        {
            "feedback" => NoteKind.Preference,
            "project" => NoteKind.Project,
            "user" or "reference" => NoteKind.Fact,
            _ => fallback,
        };

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var defaultTopic = MemoryRules.TopicSlug(name) is { Length: > 0 } fromName ? fromName : MemoryRules.TopicSlug(stem);
        var heading = "";
        var results = new List<ImportCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(string raw)
        {
            if (results.Count >= MaxCandidatesPerFile) return;
            var topic = MemoryRules.TopicSlug(heading) is { Length: > 0 } h ? h : defaultTopic;
            foreach (var chunk in Chunks(Plain(raw, siblingExists)))
                if (seen.Add(MemoryRules.NormalKey(chunk)))
                    results.Add(new ImportCandidate(chunk, MemoryRules.GuessKind(chunk, fallback), topic, heading));
        }

        if (description is { Length: > 0 }) Add(description);

        var inFence = false;
        var fence = "";
        var inComment = false;
        var item = new StringBuilder();
        void Flush()
        {
            if (item.Length > 0) Add(item.ToString());
            item.Clear();
        }
        for (var i = start; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();
            if (inComment)
            {
                if (trimmed.Contains("-->", StringComparison.Ordinal)) inComment = false;
                continue;
            }
            if (inFence)
            {
                if (trimmed.StartsWith(fence, StringComparison.Ordinal)) inFence = false;
                continue;
            }
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                Flush();
                inFence = true;
                fence = trimmed[..3];
                continue;
            }
            if (trimmed.StartsWith("<!--", StringComparison.Ordinal))
            {
                Flush();
                if (!trimmed.Contains("-->", StringComparison.Ordinal)) inComment = true;
                continue;
            }
            if (trimmed.Length == 0 || Rule().IsMatch(trimmed) || TableRow().IsMatch(trimmed)) { Flush(); continue; }
            if (Heading().Match(trimmed) is { Success: true } h)
            {
                Flush();
                heading = Plain(h.Groups["text"].Value, _ => false);
                continue;
            }
            var content = trimmed.StartsWith('>') ? trimmed.TrimStart('>', ' ') : trimmed;
            if (Bullet().Match(content) is { Success: true } b)
            {
                Flush();
                item.Append(b.Groups["text"].Value);
                continue;
            }
            // A line that continues the bullet or paragraph before it.
            if (item.Length > 0) item.Append(' ');
            item.Append(content);
        }
        Flush();
        return results;
    }

    /// <summary>The words of a markdown span: links, images, emphasis and code marks taken off, and a link to a file that is there dropped whole.</summary>
    private static string Plain(string raw, Func<string, bool> siblingExists)
    {
        try
        {
            var indexLink = false;
            var text = Image().Replace(raw, "");
            text = Link().Replace(text, m =>
            {
                var target = m.Groups["target"].Value;
                if (target.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && !target.Contains("://", StringComparison.Ordinal) && siblingExists(target)) indexLink = true;
                return m.Groups["label"].Value;
            });
            if (indexLink) return "";
            text = Emphasis().Replace(text, "${text}");
            text = text.Replace("`", "", StringComparison.Ordinal);
            return MemoryRules.Clean(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return "";
        }
    }

    /// <summary>The text as notes of at most <see cref="MemoryRules.MaxNoteChars"/>: whole sentences where it can, a cut at a space where it cannot.</summary>
    private static IEnumerable<string> Chunks(string text)
    {
        if (text.Length < MinItemChars || !text.Any(char.IsLetter)) yield break;
        if (text.Length <= MemoryRules.MaxNoteChars)
        {
            yield return text;
            yield break;
        }
        var rest = text;
        for (var n = 0; n < MaxChunksPerItem && rest.Length >= MinItemChars; n++)
        {
            if (rest.Length <= MemoryRules.MaxNoteChars)
            {
                yield return rest;
                yield break;
            }
            var cut = -1;
            try
            {
                foreach (Match m in SentenceEnd().Matches(rest))
                {
                    if (m.Index + 1 > MemoryRules.MaxNoteChars) break;
                    cut = m.Index + 1;
                }
            }
            catch (RegexMatchTimeoutException) { cut = -1; }
            if (cut < MinItemChars) cut = rest.LastIndexOf(' ', MemoryRules.MaxNoteChars - 1) is var space and >= MinItemChars ? space : MemoryRules.MaxNoteChars;
            yield return rest[..cut].Trim();
            rest = rest[cut..].Trim();
        }
    }
}
