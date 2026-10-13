// PORT-ONLY: web surfaces W24, live-answer tools (DESIGN.md 7.3 "Fallback for any provider: fenced nth-ui and nth-html blocks in the
// answer text, parsed incrementally"; ED-NEW-web-w24-2). No Swift counterpart. The fenced fallback for a model that cannot call
// tools: a block in the answer text,
//
//     ```nth-ui id=hosting
//     card "Hosting estimate"
//       ...
//     ```
//
// or one HTML page in a block that starts with ```nth-html. The parser reads the answer as it streams and hands back, in order,
// the text between blocks and each block as it grows, so the chat can draw a view line by line while the tokens arrive.
//
// Rules, the same as ArtifactFenceParser's (CommonMark fences): a fence opens at the start of a line (up to three spaces in) with
// three or more backticks or tildes; a block closes on a line of the same character, at least as long, with nothing after it. A
// fence of the answer's own text (```python) is code, so an nth-ui line inside it is shown as text, never parsed. Text is never
// held back except a line that can still become an opener; a block's text is never held back except a line that can still become
// its closing fence. The line break that ends the opener and the one that ends the closer belong to the block and are not shown.
using System.Text;
using Marrow.Core.Models;

namespace Marrow.Core.NthUi;

public enum LiveFenceKind
{
    /// <summary>An nth-ui view.</summary>
    Ui,

    /// <summary>An HTML page for an inline widget.</summary>
    Html,
}

/// <summary>The opener's attributes: id, title (or name), height (an HTML widget's, in DIP) and the bare word lite.</summary>
public sealed record LiveFenceHeader(LiveFenceKind Kind, string? Id, string? Title, int? Height, bool Lite)
{
    public const string UiTag = "nth-ui";
    public const string HtmlTag = "nth-html";

    /// <summary>The header of an info string ("nth-ui lite id=x"), or null when it is not a live opener.</summary>
    public static LiveFenceHeader? Parse(string info)
    {
        var text = info.TrimStart();
        var kind = Tag(text, UiTag) ? LiveFenceKind.Ui : Tag(text, HtmlTag) ? LiveFenceKind.Html : (LiveFenceKind?)null;
        if (kind is null) return null;
        var rest = text[(kind == LiveFenceKind.Ui ? UiTag : HtmlTag).Length..];
        var attributes = ArtifactFenceHeader.Attributes(rest);
        string? Get(params string[] keys) => keys.Select(key => attributes.TryGetValue(key, out var value) ? value : null).FirstOrDefault(value => value is not null);
        var lite = rest.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Any(word => string.Equals(word, "lite", StringComparison.OrdinalIgnoreCase));
        int? height = int.TryParse(Get("height"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var h) ? h : null;
        return new LiveFenceHeader(kind.Value, NormalizeId(Get("id")), Get("title", "name"), height, lite);
    }

    private static bool Tag(string text, string tag) =>
        text.StartsWith(tag, StringComparison.OrdinalIgnoreCase) && (text.Length == tag.Length || char.IsWhiteSpace(text[tag.Length]));

    private static string? NormalizeId(string? id) => id is null ? null : LiveViewIds.Normalize(id);
}

/// <summary>What the parser saw, in order: answer text, a block opening, a block's text, a block closing.</summary>
public abstract record LiveFencePart
{
    private LiveFencePart() { }

    /// <summary>Answer text outside any block (append it).</summary>
    public sealed record Text(string Value) : LiveFencePart;

    public sealed record Opened(int Index, LiveFenceHeader Header) : LiveFencePart;

    /// <summary>More of block <c>Index</c>'s text (append it); it may end in the middle of a line.</summary>
    public sealed record Content(int Index, string Value) : LiveFencePart;

    /// <param name="Complete">False when the answer ended before the closing fence.</param>
    public sealed record Closed(int Index, bool Complete) : LiveFencePart;
}

public sealed class LiveFenceParser
{
    private readonly StringBuilder line = new();
    private readonly List<LiveFencePart> parts = [];
    private int emitted;
    private (char Mark, int Length)? outerFence;
    private (int Index, char Mark, int Length)? block;
    private int count;

    /// <summary>The index of the block being streamed now, or null outside one.</summary>
    public int? OpenIndex => block?.Index;

    /// <summary>Parses a whole answer at once.</summary>
    public static IReadOnlyList<LiveFencePart> Parse(string text)
    {
        var parser = new LiveFenceParser();
        var first = parser.Feed(text);
        var last = parser.Finish();
        return Merge([.. first, .. last]);
    }

    /// <summary>Takes the next piece of the streamed answer; returns what it made of it.</summary>
    public IReadOnlyList<LiveFencePart> Feed(string delta)
    {
        foreach (var c in delta)
        {
            line.Append(c);
            if (c == '\n') EndLine();
        }
        ReleasePartial();
        return Take();
    }

    /// <summary>The end of the answer: the last line counts as complete, and a block still open closes as cut off.</summary>
    public IReadOnlyList<LiveFencePart> Finish()
    {
        if (line.Length > 0) EndLine();
        if (block is { } open)
        {
            parts.Add(new LiveFencePart.Closed(open.Index, Complete: false));
            block = null;
        }
        return Take();
    }

    private IReadOnlyList<LiveFencePart> Take()
    {
        var result = Merge(parts);
        parts.Clear();
        return result;
    }

    /// <summary>Joins neighbouring text parts and neighbouring content parts of one block.</summary>
    private static IReadOnlyList<LiveFencePart> Merge(IReadOnlyList<LiveFencePart> source)
    {
        var merged = new List<LiveFencePart>();
        foreach (var part in source)
        {
            switch (part)
            {
                case LiveFencePart.Text { Value.Length: 0 } or LiveFencePart.Content { Value.Length: 0 }:
                    continue;
                case LiveFencePart.Text text when merged.Count > 0 && merged[^1] is LiveFencePart.Text before:
                    merged[^1] = new LiveFencePart.Text(before.Value + text.Value);
                    break;
                case LiveFencePart.Content content when merged.Count > 0 && merged[^1] is LiveFencePart.Content before && before.Index == content.Index:
                    merged[^1] = new LiveFencePart.Content(content.Index, before.Value + content.Value);
                    break;
                default:
                    merged.Add(part);
                    break;
            }
        }
        return merged;
    }

    /// <summary>The unfinished line: shown now unless it can still turn into an opener (outside a block) or the closing fence (inside one).</summary>
    private void ReleasePartial()
    {
        if (line.Length == emitted) return;
        var partial = line.ToString();
        if (block is { } open)
        {
            if (emitted == 0 && CouldClose(partial, open.Mark)) return;
        }
        else if (outerFence is null && emitted == 0 && CouldOpen(partial)) return;
        var piece = partial[emitted..];
        parts.Add(block is { } inside ? new LiveFencePart.Content(inside.Index, piece) : new LiveFencePart.Text(piece));
        emitted = partial.Length;
    }

    private void EndLine()
    {
        var raw = line.ToString();
        var text = raw.TrimEnd('\n').TrimEnd('\r');
        if (block is { } open)
        {
            if (emitted == 0 && IsCloser(text, open.Mark, open.Length))
            {
                parts.Add(new LiveFencePart.Closed(open.Index, Complete: true));
                block = null;
            }
            else parts.Add(new LiveFencePart.Content(open.Index, raw[emitted..]));
        }
        else if (outerFence is { } code)
        {
            parts.Add(new LiveFencePart.Text(raw[emitted..]));
            if (IsCloser(text, code.Mark, code.Length)) outerFence = null; // however much of the line was shown as it arrived
        }
        else if (emitted == 0 && Fence(text) is { } fence && LiveFenceHeader.Parse(fence.Info) is { } header)
        {
            var index = count++;
            block = (index, fence.Mark, fence.Length);
            parts.Add(new LiveFencePart.Opened(index, header));
        }
        else
        {
            parts.Add(new LiveFencePart.Text(raw[emitted..]));
            if (Fence(text) is { } opener) outerFence = (opener.Mark, opener.Length); // a fence of the answer's own code, however much of the line was shown as it arrived
        }
        line.Clear();
        emitted = 0;
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

    /// <summary>True while a partial line can still turn into a closing fence: up to three spaces, then only the mark.</summary>
    private static bool CouldClose(string partial, char mark)
    {
        var i = 0;
        while (i < partial.Length && partial[i] == ' ') i++;
        if (i > 3) return false;
        while (i < partial.Length && partial[i] == mark) i++;
        while (i < partial.Length && partial[i] is ' ' or '\r') i++;
        return i == partial.Length;
    }

    /// <summary>True while a partial line can still turn into an opener of a live block, so it is held back.</summary>
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
        return Prefix(rest, LiveFenceHeader.UiTag) || Prefix(rest, LiveFenceHeader.HtmlTag);
    }

    /// <summary>The text so far is a start of the tag, or the whole tag followed by a space (then the opener is certain, and it is held until the line ends).</summary>
    private static bool Prefix(string rest, string tag)
    {
        if (rest.Length <= tag.Length) return tag.StartsWith(rest, StringComparison.OrdinalIgnoreCase);
        return rest.StartsWith(tag, StringComparison.OrdinalIgnoreCase) && char.IsWhiteSpace(rest[tag.Length]);
    }
}

/// <summary>The ids views go by: letters, digits and hyphens, at most 48, lower case.</summary>
public static class LiveViewIds
{
    public const int MaxLength = 48;

    /// <summary>The id in normal form, or null when nothing usable is left.</summary>
    public static string? Normalize(string? id)
    {
        if (id is null) return null;
        var builder = new StringBuilder();
        foreach (var c in id.Trim().ToLowerInvariant())
        {
            if (builder.Length >= MaxLength) break;
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') builder.Append(c);
            else if (c is '-' or '_' or ' ' or '.') builder.Append('-');
        }
        var text = builder.ToString().Trim('-');
        while (text.Contains("--", StringComparison.Ordinal)) text = text.Replace("--", "-", StringComparison.Ordinal);
        return text.Length == 0 ? null : text;
    }
}
