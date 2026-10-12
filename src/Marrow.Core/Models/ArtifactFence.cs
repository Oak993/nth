// PORT-ONLY: web surfaces W12, Tabula (DESIGN.md 6.2 fallback; ED-NEW-web-w12-2). The fenced fallback for models with no
// tool calls or weak tool streaming: a block in the answer text,
//
//     ```nth-artifact id=pricing kind=html title="Pricing calculator"
//     ...content...
//     ```
//
// parsed while it streams. The answer keeps a compact chip in place of the block, so the user never reads the raw
// content in the chat. A project's files are fenced blocks inside the block, each naming its path:
//
//     ````nth-artifact id=site kind=project title="Site"
//     ```html path=index.html
//     ...
//     ```
//     ```css path=style.css
//     ...
//     ```
//     ````
//
// Rules (CommonMark fences, plus one for nesting): a fence opens at the start of a line (up to three spaces in) with three
// or more backticks or tildes; the block closes on a line of the same character, at least as long, with nothing after it.
// Inside the block a fence line with an info string opens an inner block that its own bare fence closes, so a Markdown
// document's code blocks never end the artifact early. A fence of the outer text (```python) is code, so an nth-artifact
// line inside it is shown, not parsed. Text is never held back except a line that can still become an opener. No Swift
// counterpart.
using System.Text;

namespace Marrow.Core.Models;

/// <summary>The opener's attributes: id, kind (or type), title (or name), language (or lang) and entry; a bare kind word works too.</summary>
public sealed record ArtifactFenceHeader(string? Id, ArtifactKind? Kind, string? Title, string? Language, string? Entry)
{
    public const string Tag = "nth-artifact";

    /// <summary>The header of an info string ("nth-artifact id=x kind=html ..."), or null when it is not an nth-artifact opener.</summary>
    public static ArtifactFenceHeader? Parse(string info)
    {
        var text = info.TrimStart();
        if (!text.StartsWith(Tag, StringComparison.OrdinalIgnoreCase)) return null;
        var rest = text[Tag.Length..];
        if (rest.Length > 0 && !char.IsWhiteSpace(rest[0])) return null;
        var attributes = Attributes(rest);
        string? Get(params string[] keys) => keys.Select(key => attributes.TryGetValue(key, out var value) ? value : null).FirstOrDefault(value => value is not null);
        var kind = ArtifactKinds.Parse(Get("kind", "type"));
        if (kind is null && attributes.TryGetValue("", out var bare)) kind = ArtifactKinds.Parse(bare);
        return new ArtifactFenceHeader(Get("id"), kind, Get("title", "name"), Get("language", "lang"), Get("entry"));
    }

    /// <summary>key=value pairs (a value quoted with " or ', with \" inside double quotes) and the first bare word under "".</summary>
    public static Dictionary<string, string> Attributes(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            if (i >= text.Length) break;
            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '=') i++;
            var key = text[start..i];
            if (i < text.Length && text[i] == '=')
            {
                i++;
                string value;
                if (i < text.Length && text[i] is '"' or '\'')
                {
                    var quote = text[i++];
                    var builder = new StringBuilder();
                    while (i < text.Length && text[i] != quote)
                    {
                        if (quote == '"' && text[i] == '\\' && i + 1 < text.Length && text[i + 1] is '"' or '\\') i++;
                        builder.Append(text[i++]);
                    }
                    i++;
                    value = builder.ToString();
                }
                else
                {
                    var from = i;
                    while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
                    value = text[from..i];
                }
                if (key.Length > 0) result.TryAdd(key, value);
            }
            else if (key.Length > 0) result.TryAdd("", key);
        }
        return result;
    }
}

/// <summary>What the parser saw: a block opened, or a block closed (complete, or cut off by the end of the answer).</summary>
public abstract record ArtifactFenceEvent(int Index, ArtifactFenceHeader Header)
{
    public sealed record Opened(int Index, ArtifactFenceHeader Header) : ArtifactFenceEvent(Index, Header);

    /// <param name="Complete">False when the answer ended before the closing fence.</param>
    public sealed record Closed(int Index, ArtifactFenceHeader Header, string Content, bool Complete) : ArtifactFenceEvent(Index, Header);
}

/// <summary>One step of the parser: the text to show (append it) and the events, in order.</summary>
public sealed record ArtifactFenceStep(string Display, IReadOnlyList<ArtifactFenceEvent> Events);

public sealed class ArtifactFenceParser
{
    private readonly Func<int, ArtifactFenceHeader, string> chip;
    private readonly StringBuilder line = new();
    private int lineShown;
    private (char Mark, int Length)? outerFence;
    private OpenBlock? block;
    private int count;
    private char lastShown = '\n', beforeLastShown = '\n';
    private StringBuilder display = new();
    private List<ArtifactFenceEvent> events = [];

    /// <param name="chip">The text shown in place of block <c>index</c> (one line, ending in a line break); <see cref="Chip"/> when null.</param>
    public ArtifactFenceParser(Func<int, ArtifactFenceHeader, string>? chip = null) =>
        this.chip = chip ?? ((_, header) => Chip(ArtifactStore.NormalizeId(header.Id) ?? "", header.Title, header.Kind, header.Language));

    /// <summary>The block being streamed now (its index, header and content so far), or null outside one.</summary>
    public (int Index, ArtifactFenceHeader Header, string Content)? Open => block is { } open ? (open.Index, open.Header, open.Content.ToString()) : null;

    /// <summary>The index of the block being streamed now, or null outside one; unlike <see cref="Open"/> it copies no text.</summary>
    public int? OpenIndex => block?.Index;

    /// <summary>
    /// The open block's text as it arrives (web W14, Tabula's streaming render): its lines so far and the partial last
    /// line, unless that line could still become the block's closing fence; null outside a block.
    /// </summary>
    public string? Streaming
    {
        get
        {
            if (block is not { } open) return null;
            if (line.Length == 0) return open.Content.ToString();
            var partial = line.ToString();
            var at = 0;
            while (at < partial.Length && at < 3 && partial[at] == ' ') at++;
            var couldClose = at == partial.Length || partial[at] == open.Mark;
            return couldClose ? open.Content.ToString() : open.Content + partial;
        }
    }

    /// <summary>
    /// The answer's chip for an artifact: a Markdown link on its own line with the nth-artifact scheme, which the
    /// timeline opens in Tabula, reading "Title · Kind" so it says what it is even where no chip is drawn.
    /// </summary>
    public static string Chip(string id, string? title, ArtifactKind? kind, string? language) =>
        "[" + Escape(ArtifactStore.CleanTitle(title ?? id)) + (kind is { } known ? " · " + ArtifactKinds.DisplayName(known, language) : "") + "](" + LinkScheme + "://" + id + ")\n";

    /// <summary>The scheme of a chip's link.</summary>
    public const string LinkScheme = "nth-artifact";

    /// <summary>The artifact id a chip link names ("nth-artifact://pricing"), or null for any other link.</summary>
    public static string? ChipId(string? url)
    {
        if (url is null || !url.StartsWith(LinkScheme + "://", StringComparison.OrdinalIgnoreCase)) return null;
        return ArtifactStore.NormalizeId(url[(LinkScheme.Length + 3)..].TrimEnd('/'));
    }

    /// <summary>Parses a whole answer at once: the text to show and every block, closed or cut off.</summary>
    public static (string Display, IReadOnlyList<ArtifactFenceEvent.Closed> Blocks) Parse(string text, Func<int, ArtifactFenceHeader, string>? chip = null)
    {
        var parser = new ArtifactFenceParser(chip);
        var first = parser.Feed(text);
        var last = parser.Finish();
        return (first.Display + last.Display, [.. first.Events.Concat(last.Events).OfType<ArtifactFenceEvent.Closed>()]);
    }

    /// <summary>Takes the next piece of the streamed answer.</summary>
    public ArtifactFenceStep Feed(string delta)
    {
        foreach (var c in delta)
        {
            line.Append(c);
            if (c == '\n') EndLine();
        }
        if (line.Length > 0 && block is null && lineShown < line.Length && (outerFence is not null || lineShown > 0 || !CouldOpen(line.ToString())))
        {
            Show(line.ToString(lineShown, line.Length - lineShown));
            lineShown = line.Length;
        }
        return Take();
    }

    /// <summary>The end of the answer: the last line counts as complete, and a block still open closes as cut off.</summary>
    public ArtifactFenceStep Finish()
    {
        if (line.Length > 0) EndLine();
        if (block is { } open)
        {
            events.Add(new ArtifactFenceEvent.Closed(open.Index, open.Header, open.Content.ToString(), Complete: false));
            block = null;
            Show("\n");
        }
        return Take();
    }

    private ArtifactFenceStep Take()
    {
        var step = new ArtifactFenceStep(display.ToString(), events);
        display = new StringBuilder();
        events = [];
        return step;
    }

    private void EndLine()
    {
        var raw = line.ToString();
        var text = raw.TrimEnd('\n').TrimEnd('\r');
        if (block is { } open)
        {
            if (open.Inner.Count == 0 && IsCloser(text, open.Mark, open.Length))
            {
                events.Add(new ArtifactFenceEvent.Closed(open.Index, open.Header, open.Content.ToString(), Complete: true));
                block = null;
                Show("\n"); // the chip stays a paragraph of its own
            }
            else
            {
                if (open.Inner.Count > 0 && IsCloser(text, open.Inner.Peek().Mark, open.Inner.Peek().Length)) open.Inner.Pop();
                else if (Fence(text) is { } inner && (inner.Info.Length > 0 || inner.Mark != open.Mark || inner.Length < open.Length)) open.Inner.Push((inner.Mark, inner.Length));
                open.Content.Append(raw);
            }
        }
        else if (outerFence is { } code)
        {
            Show(raw[lineShown..]);
            if (IsCloser(text, code.Mark, code.Length)) outerFence = null;
        }
        else if (lineShown == 0 && Fence(text) is { } fence && ArtifactFenceHeader.Parse(fence.Info) is { } header)
        {
            var index = count++;
            block = new OpenBlock(index, header, fence.Mark, fence.Length);
            events.Add(new ArtifactFenceEvent.Opened(index, header));
            if (!(lastShown == '\n' && beforeLastShown == '\n')) Show(lastShown == '\n' ? "\n" : "\n\n");
            Show(chip(index, header));
        }
        else
        {
            Show(raw[lineShown..]);
            if (Fence(text) is { } opener) outerFence = (opener.Mark, opener.Length);
        }
        line.Clear();
        lineShown = 0;
    }

    private void Show(string text)
    {
        if (text.Length == 0) return;
        display.Append(text);
        if (text.Length >= 2) (beforeLastShown, lastShown) = (text[^2], text[^1]);
        else (beforeLastShown, lastShown) = (lastShown, text[0]);
    }

    /// <summary>A fence line: up to three spaces, three or more of one mark, then the info string (empty for a bare fence).</summary>
    private static (char Mark, int Length, string Info)? Fence(string text)
    {
        var i = 0;
        while (i < text.Length && i < 3 && text[i] == ' ') i++;
        if (i >= text.Length || text[i] is not ('`' or '~')) return null;
        var mark = text[i];
        var start = i;
        while (i < text.Length && text[i] == mark) i++;
        var length = i - start;
        if (length < 3) return null;
        var info = text[i..].Trim();
        if (mark == '`' && info.Contains('`', StringComparison.Ordinal)) return null; // CommonMark: not a fence
        return (mark, length, info);
    }

    private static bool IsCloser(string text, char mark, int length) =>
        Fence(text) is { } fence && fence.Mark == mark && fence.Length >= length && fence.Info.Length == 0;

    /// <summary>True while a partial line can still turn into an nth-artifact opener, so it is held back.</summary>
    private static bool CouldOpen(string partial)
    {
        var i = 0;
        while (i < partial.Length && partial[i] == ' ') i++;
        if (i > 3) return false;
        if (i == partial.Length) return true;
        if (partial[i] is not ('`' or '~')) return false;
        var mark = partial[i];
        var start = i;
        while (i < partial.Length && partial[i] == mark) i++;
        if (i == partial.Length) return true;
        if (i - start < 3) return false;
        while (i < partial.Length && partial[i] is ' ' or '\t') i++;
        var rest = partial[i..];
        if (rest.Length <= ArtifactFenceHeader.Tag.Length) return ArtifactFenceHeader.Tag.StartsWith(rest, StringComparison.OrdinalIgnoreCase);
        return rest.StartsWith(ArtifactFenceHeader.Tag, StringComparison.OrdinalIgnoreCase) && char.IsWhiteSpace(rest[ArtifactFenceHeader.Tag.Length]);
    }

    private static string Escape(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is '\\' or '[' or ']' or '*' or '_' or '`' or '<' or '>') builder.Append('\\');
            builder.Append(c);
        }
        return builder.ToString();
    }

    private sealed class OpenBlock(int index, ArtifactFenceHeader header, char mark, int length)
    {
        public int Index { get; } = index;
        public ArtifactFenceHeader Header { get; } = header;
        public char Mark { get; } = mark;
        public int Length { get; } = length;
        public StringBuilder Content { get; } = new();
        public Stack<(char Mark, int Length)> Inner { get; } = new();
    }
}

/// <summary>The files of a project block: its inner fenced blocks that name a path.</summary>
public static class ArtifactFenceProject
{
    /// <summary>
    /// Each inner block whose info string names its path (path=..., file=..., or a word with a dot or slash, as in
    /// "```css style.css") becomes that file; text between blocks is ignored. Empty when no block names a path.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Files(string content)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        string? path = null;
        (char Mark, int Length) fence = default;
        StringBuilder? body = null;
        var depth = 0;
        foreach (var raw in Lines(content))
        {
            var text = raw.TrimEnd('\n').TrimEnd('\r');
            var trimmed = text.TrimStart(' ');
            var mark = trimmed.Length > 0 ? trimmed[0] : '\0';
            var run = mark is '`' or '~' ? trimmed.TakeWhile(c => c == mark).Count() : 0;
            var info = run >= 3 && text.Length - trimmed.Length <= 3 ? trimmed[run..].Trim() : null;
            if (body is null)
            {
                if (info is { Length: > 0 } && PathOf(info) is { } named)
                {
                    (path, fence, body, depth) = (named, (mark, run), new StringBuilder(), 0);
                }
                continue;
            }
            if (info is not null && info.Length == 0 && mark == fence.Mark && run >= fence.Length && depth == 0)
            {
                files[path!] = body.ToString();
                (path, body) = (null, null);
                continue;
            }
            if (info is not null && info.Length == 0 && depth > 0) depth--;
            else if (info is { Length: > 0 }) depth++;
            body.Append(raw);
        }
        if (body is not null && path is not null) files[path] = body.ToString();
        return files;
    }

    private static string? PathOf(string info)
    {
        var attributes = ArtifactFenceHeader.Attributes(info);
        if (attributes.TryGetValue("path", out var path) || attributes.TryGetValue("file", out path) || attributes.TryGetValue("filename", out path)) return path;
        foreach (var word in info.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            if (!word.Contains('=', StringComparison.Ordinal) && (word.Contains('.', StringComparison.Ordinal) || word.Contains('/', StringComparison.Ordinal))) return word;
        return null;
    }

    private static IEnumerable<string> Lines(string text)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\n')
            {
                yield return text[start..(i + 1)];
                start = i + 1;
            }
        if (start < text.Length) yield return text[start..];
    }
}
