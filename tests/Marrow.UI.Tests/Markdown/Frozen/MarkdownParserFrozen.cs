using System.Text;
using Marrow.Core.Port;
using Marrow.UI.Markdown;

namespace Marrow.UI.Tests.Markdown.Frozen;

// FROZEN (ED-NEW-lean-13): src/Marrow.UI/Markdown/MarkdownParser.cs exactly as it stood on claude/release-integration
// 0a946c558, before the lean pass, kept as the oracle for MarkdownParserOracleTests. Do not edit or optimise.
// Source: DroppyCode/UI/Markdown/MarkdownParser.swift (B2 1fbf81e), :1-339.

// :20-338
internal static class MarkdownParserFrozen
{
    // :27-31. Nested type named "Parse"; the Swift functions all named "parse" clash with it
    // (R-NAME-3) and are renamed below with the owner's name prefixed, cited to their Swift
    // signature at each site.
    public sealed record Parse(ValueList<MarkdownBlock> Blocks, int Stable, int Restart);

    // :33-35. Swift: static func parse(_ text: String) -> [MarkdownBlock]
    public static ValueList<MarkdownBlock> MarkdownParserParse(string text) => ParseSubstring(text).Blocks;

    // :37-51. Swift: static func parse(_ text: String, extending previous: (text: String, parse: Parse)?) -> Parse
    public static Parse MarkdownParserParse(string text, (string Text, Parse Parse)? extending)
    {
        if (extending is not { } previous
            || previous.Parse.Stable <= 0
            || SwiftString.Utf8Count(text) <= SwiftString.Utf8Count(previous.Text)
            || !Utf8StartsWith(text, previous.Text))
        {
            return ParseSubstring(text);
        }

        var restart = previous.Parse.Restart;
        var tail = ParseSubstring(SubstringFromUtf8Offset(text, restart));
        var blocks = new ValueList<MarkdownBlock>(
            [.. Take(previous.Parse.Blocks, previous.Parse.Stable), .. tail.Blocks]);
        return new Parse(blocks, previous.Parse.Stable + tail.Stable, restart + tail.Restart);
    }

    private static IEnumerable<MarkdownBlock> Take(ValueList<MarkdownBlock> blocks, int count)
    {
        for (var i = 0; i < count; i++) yield return blocks[i];
    }

    // :53-64. Splits the UTF-8 on 0x0A without omitting empty subsequences, records each piece's
    // byte distance from the start, and drops a trailing 0x0D from each piece.
    // :66-72. The last block that starts at least two lines above the last line: the decisions
    // that end everything before it never read the line still being written.
    // Swift: private static func parse(_ text: Substring) -> Parse
    private static Parse ParseSubstring(string text)
    {
        var utf8 = Encoding.UTF8.GetBytes(text);
        var lines = new List<string>();
        var starts = new List<int>();
        var pieceStart = 0;
        for (var i = 0; i < utf8.Length; i++)
        {
            if (utf8[i] == 0x0A)
            {
                AddLine(utf8, pieceStart, i - pieceStart);
                pieceStart = i + 1;
            }
        }
        AddLine(utf8, pieceStart, utf8.Length - pieceStart);

        void AddLine(byte[] bytes, int start, int length)
        {
            starts.Add(start);
            var span = new ReadOnlySpan<byte>(bytes, start, length);
            if (span.Length > 0 && span[^1] == 0x0D) span = span[..^1];
            lines.Add(Encoding.UTF8.GetString(span));
        }

        var parsed = ParseLines(lines, 0);
        var stable = -1;
        for (var i = parsed.BlockLines.Count - 1; i >= 0; i--)
        {
            if (parsed.BlockLines[i] <= lines.Count - 3) { stable = i; break; }
        }
        if (stable < 0)
        {
            return new Parse(new ValueList<MarkdownBlock>(parsed.Blocks), 0, 0);
        }
        return new Parse(new ValueList<MarkdownBlock>(parsed.Blocks), stable, starts[parsed.BlockLines[stable]]);
    }

    // :74-76. Quotes and list items nest by recursion, one level per '>' or indent step, which
    // model output decides. Past this many levels the inner lines are paragraph text.
    private const int MaxDepth = 8;

    // :78-185. The blocks of `lines`, and the line each top-level block starts on.
    // Swift: private static func parse(_ lines: ArraySlice<String>, depth: Int = 0) -> (blocks:, blockLines:)
    private static (List<MarkdownBlock> Blocks, List<int> BlockLines) ParseLines(IReadOnlyList<string> lines, int depth = 0)
    {
        var blocks = new List<MarkdownBlock>();
        var blockLines = new List<int>();
        var index = 0;
        var paragraph = new List<string>();
        var paragraphStart = index;

        // :86-93
        void FlushParagraph()
        {
            var text = FoundationText.Trim(string.Join("\n", paragraph), includeNewlines: true);
            if (text.Length != 0)
            {
                blocks.Add(new MarkdownBlock.Paragraph(text));
                blockLines.Add(paragraphStart);
            }
            paragraph.Clear();
        }

        while (index < lines.Count)
        {
            var line = lines[index];
            var trimmed = TrimmedLine(line);

            // :99-103
            if (trimmed.Length == 0)
            {
                FlushParagraph();
                index += 1;
                continue;
            }

            // :105-119
            if (FenceMarker(trimmed) is { } fence)
            {
                FlushParagraph();
                blockLines.Add(index);
                var language = FoundationText.Trim(SwiftString.DropFirst(trimmed, SwiftString.Count(fence)));
                var code = new List<string>();
                index += 1;
                while (index < lines.Count)
                {
                    var candidate = TrimmedLine(lines[index]);
                    if (SwiftString.HasPrefix(candidate, fence) && AllSameCharacter(candidate, FirstCharacter(fence)))
                    {
                        index += 1;
                        break;
                    }
                    code.Add(lines[index]);
                    index += 1;
                }
                blocks.Add(new MarkdownBlock.Code(language.Length == 0 ? null : language, string.Join("\n", code)));
                continue;
            }

            // :121-128
            if (HeadingLevel(trimmed) is { } heading)
            {
                FlushParagraph();
                blockLines.Add(index);
                var withoutHashes = SwiftString.DropFirst(trimmed, CountLeadingHashes(trimmed));
                var content = TrimCharacterSet(FoundationText.Trim(withoutHashes), "# ");
                blocks.Add(new MarkdownBlock.Heading(heading, content));
                index += 1;
                continue;
            }

            // :130-136
            if (IsRule(trimmed))
            {
                FlushParagraph();
                blockLines.Add(index);
                blocks.Add(new MarkdownBlock.Rule());
                index += 1;
                continue;
            }

            // :138-152
            if (SwiftString.HasPrefix(trimmed, ">") && depth < MaxDepth)
            {
                FlushParagraph();
                blockLines.Add(index);
                var quoted = new List<string>();
                while (index < lines.Count)
                {
                    var candidate = TrimmedLine(lines[index]);
                    if (!SwiftString.HasPrefix(candidate, ">")) break;
                    var content = SwiftString.DropFirst(candidate, 1);
                    if (SwiftString.HasPrefix(content, " ")) content = SwiftString.DropFirst(content, 1);
                    quoted.Add(content);
                    index += 1;
                }
                blocks.Add(new MarkdownBlock.Quote(new ValueList<MarkdownBlock>(ParseLines(quoted, depth + 1).Blocks)));
                continue;
            }

            // :154-168
            if (index + 1 < lines.Count && trimmed.Contains('|', StringComparison.Ordinal) && IsTableDelimiter(lines[index + 1]))
            {
                FlushParagraph();
                blockLines.Add(index);
                var header = TableCells(trimmed);
                var rows = new List<List<string>>();
                index += 2;
                while (index < lines.Count)
                {
                    var candidate = TrimmedLine(lines[index]);
                    if (candidate.Length == 0 || !candidate.Contains('|', StringComparison.Ordinal)) break;
                    rows.Add(TableCells(candidate));
                    index += 1;
                }
                blocks.Add(new MarkdownBlock.Table(
                    new ValueList<string>(header),
                    new ValueList<ValueList<string>>(rows.Select(r => new ValueList<string>(r)))));
                continue;
            }

            // :170-177
            if (depth < MaxDepth && ListMarkerAt(line) is { } marker
                && (paragraph.Count == 0 || !marker.Ordered || marker.Start == 1))
            {
                FlushParagraph();
                blockLines.Add(index);
                var (list, next) = ParseList(lines, index, depth);
                blocks.Add(list);
                index = next;
                continue;
            }

            // :179-181
            if (paragraph.Count == 0) paragraphStart = index;
            paragraph.Add(line);
            index += 1;
        }
        FlushParagraph();
        return (blocks, blockLines);
    }

    // :187-193
    private sealed record ListMarker(int Indent, bool Ordered, int Start, int ContentOffset, string Content);

    // :195-262
    private static (MarkdownBlock, int) ParseList(IReadOnlyList<string> lines, int start, int depth)
    {
        if (ListMarkerAt(lines[start]) is not { } first)
        {
            return (new MarkdownBlock.Paragraph(lines[start]), start + 1);
        }
        var items = new List<MarkdownListItem>();
        var index = start;
        var currentText = new List<string>();
        var childLines = new List<string>();
        var hasItem = false;

        // :203-217
        void FlushItem()
        {
            if (!hasItem) return;
            var text = FoundationText.Trim(string.Join("\n", currentText));
            bool? isChecked = null;
            if (SwiftString.HasPrefix(text, "[ ] "))
            {
                isChecked = false;
                text = SwiftString.DropFirst(text, 4);
            }
            else if (SwiftString.HasPrefix(SwiftString.Lowercased(text), "[x] "))
            {
                isChecked = true;
                text = SwiftString.DropFirst(text, 4);
            }
            items.Add(new MarkdownListItem(text, isChecked, new ValueList<MarkdownBlock>(ParseLines(childLines, depth + 1).Blocks)));
            currentText.Clear();
            childLines.Clear();
        }

        while (index < lines.Count)
        {
            var line = lines[index];
            var trimmed = TrimmedLine(line);

            // :222-234
            if (trimmed.Length == 0)
            {
                var next = index + 1;
                if (next < lines.Count && Indentation(lines[next]) > first.Indent && TrimmedLine(lines[next]).Length != 0)
                {
                    childLines.Add("");
                    index += 1;
                    continue;
                }
                if (next < lines.Count && ListMarkerAt(lines[next]) is { } sibling
                    && sibling.Indent == first.Indent && sibling.Ordered == first.Ordered)
                {
                    index += 1;
                    continue;
                }
                break;
            }

            // :235-242
            if (ListMarkerAt(line) is { } marker && marker.Indent <= first.Indent)
            {
                if (marker.Ordered != first.Ordered) break;
                FlushItem();
                hasItem = true;
                currentText = [marker.Content];
                index += 1;
                continue;
            }

            // :243-252
            if (Indentation(line) > first.Indent)
            {
                var dropped = SwiftString.DropFirst(line, Math.Min(Indentation(line), first.ContentOffset));
                if (childLines.Count == 0 && ListMarkerAt(line) is null && FenceMarker(trimmed) is null)
                {
                    currentText.Add(trimmed);
                }
                else
                {
                    childLines.Add(dropped);
                }
                index += 1;
                continue;
            }

            // :253-257
            if (childLines.Count == 0 && HeadingLevel(trimmed) is null && FenceMarker(trimmed) is null && !SwiftString.HasPrefix(trimmed, ">"))
            {
                currentText.Add(trimmed);
                index += 1;
                continue;
            }
            break;
        }
        FlushItem();
        return (new MarkdownBlock.List(first.Ordered, first.Start, new ValueList<MarkdownListItem>(items)), index);
    }

    // :264-270. `trimmingCharacters(in: .whitespaces)`, skipped for the usual line that starts
    // and ends with printable ASCII: no space or tab there, and no lead byte of a Unicode space
    // either. Swift: private static func trimmed(_ line: String) -> String
    private static string TrimmedLine(string line)
    {
        var utf8 = Encoding.UTF8.GetBytes(line);
        if (utf8.Length > 0)
        {
            var first = utf8[0];
            var last = utf8[^1];
            if (first > 0x20 && first < 0x7F && last > 0x20 && last < 0x7F) return line;
        }
        return FoundationText.Trim(line);
    }

    // :272-289. Swift: private static func listMarker(_ line: String) -> ListMarker?
    private static ListMarker? ListMarkerAt(string line)
    {
        var indent = Indentation(line);
        var body = SwiftString.DropFirst(line, indent);
        var bodyChars = SwiftString.Characters(body).ToList();
        if (bodyChars.Count > 0 && bodyChars[0] is "-" or "*" or "+")
        {
            var rest = SwiftString.DropFirst(body, 1);
            if (!(SwiftString.HasPrefix(rest, " ") || SwiftString.HasPrefix(rest, "\t"))) return null;
            var content = string.Concat(SwiftString.Characters(rest).SkipWhile(c => c is " " or "\t"));
            return new ListMarker(indent, false, 1, indent + 2, content);
        }

        var digitChars = bodyChars.TakeWhile(c => SwiftCharacter.IsNumber(c)).ToList();
        if (digitChars.Count == 0 || digitChars.Count > 9) return null;
        var rest2 = SwiftString.DropFirst(body, digitChars.Count);
        var rest2Chars = SwiftString.Characters(rest2).ToList();
        if (rest2Chars.Count == 0) return null;
        var delimiter = rest2Chars[0];
        if (delimiter != "." && delimiter != ")") return null;
        var afterDelimiter = SwiftString.DropFirst(rest2, 1);
        if (!(SwiftString.HasPrefix(afterDelimiter, " ") || afterDelimiter.Length == 0)) return null;
        var content2 = string.Concat(SwiftString.Characters(afterDelimiter).SkipWhile(c => c == " "));
        var digitsText = string.Concat(digitChars);
        var startValue = SwiftNumber.ParseInt(digitsText) is { } n ? (int)n : 1;
        return new ListMarker(indent, true, startValue, indent + digitChars.Count + 2, content2);
    }

    // :291-297. Swift: private static func indentation(_ line: String) -> Int
    private static int Indentation(string line)
    {
        var count = 0;
        foreach (var character in SwiftString.Characters(line))
        {
            if (character == " ") count += 1;
            else if (character == "\t") count += 4;
            else break;
        }
        return count;
    }

    // :299-305. Swift: private static func fenceMarker(_ trimmed: String) -> String?
    private static string? FenceMarker(string trimmed)
    {
        foreach (var marker in new[] { "```", "~~~" })
        {
            if (SwiftString.HasPrefix(trimmed, marker))
            {
                var fenceCharacter = FirstCharacter(marker);
                return string.Concat(SwiftString.Characters(trimmed).TakeWhile(c => c == fenceCharacter));
            }
        }
        return null;
    }

    // :307-312. Swift: private static func headingLevel(_ trimmed: String) -> Int?
    private static int? HeadingLevel(string trimmed)
    {
        var hashes = CountLeadingHashes(trimmed);
        if (hashes is < 1 or > 6) return null;
        var rest = SwiftString.DropFirst(trimmed, hashes);
        return rest.Length == 0 || SwiftString.HasPrefix(rest, " ") ? hashes : null;
    }

    private static int CountLeadingHashes(string trimmed) =>
        SwiftString.Characters(trimmed).TakeWhile(c => c == "#").Count();

    // :314-323. Three or more of one of '-', '*', '_', spaces between them allowed. Every bullet
    // line starts with one of those, so this checks bytes rather than building a string.
    // Swift: private static func isRule(_ trimmed: String) -> Bool
    private static bool IsRule(string trimmed)
    {
        var utf8 = Encoding.UTF8.GetBytes(trimmed);
        if (utf8.Length == 0) return false;
        var first = utf8[0];
        if (first != 0x2D && first != 0x2A && first != 0x5F) return false;
        var count = 0;
        foreach (var b in utf8)
        {
            if (b == first) count += 1;
            else if (b != 0x20) return false;
        }
        return count >= 3;
    }

    // :325-329. Swift: private static func isTableDelimiter(_ line: String) -> Bool
    private static bool IsTableDelimiter(string line)
    {
        var trimmed = TrimmedLine(line);
        if (!trimmed.Contains('-', StringComparison.Ordinal)) return false;
        return SwiftString.Characters(trimmed).All(c => "|:- ".Contains(c, StringComparison.Ordinal));
    }

    // :331-337. Swift: private static func tableCells(_ line: String) -> [String]
    private static List<string> TableCells(string line)
    {
        var content = TrimmedLine(line);
        if (SwiftString.HasPrefix(content, "|")) content = SwiftString.DropFirst(content, 1);
        if (SwiftString.HasSuffix(content, "|")) content = SwiftString.DropLast(content, 1);
        return SwiftString.Split(content, "|", maxSplits: int.MaxValue, omittingEmptySubsequences: false)
            .Select(cell => FoundationText.Trim(cell))
            .ToList();
    }

    private static string FirstCharacter(string text) => SwiftString.Characters(text).First();

    private static bool AllSameCharacter(string text, string character) =>
        SwiftString.Characters(text).All(c => c == character);

    // :265-268 in spirit: trims exactly the given character set from both ends, matching
    // `trimmingCharacters(in: CharacterSet(charactersIn:))`.
    private static string TrimCharacterSet(string text, string charset)
    {
        var start = 0;
        var end = text.Length;
        while (start < end && charset.IndexOf(text[start], StringComparison.Ordinal) >= 0) start++;
        while (end > start && charset.IndexOf(text[end - 1], StringComparison.Ordinal) >= 0) end--;
        return text[start..end];
    }

    // :41. Swift: text.utf8.starts(with: previous.text.utf8)
    private static bool Utf8StartsWith(string text, string prefix)
    {
        var textBytes = Encoding.UTF8.GetBytes(text);
        var prefixBytes = Encoding.UTF8.GetBytes(prefix);
        if (prefixBytes.Length > textBytes.Length) return false;
        return textBytes.AsSpan(0, prefixBytes.Length).SequenceEqual(prefixBytes);
    }

    // :45. Swift: text.utf8.index(text.utf8.startIndex, offsetBy: restart)...
    private static string SubstringFromUtf8Offset(string text, int byteOffset)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return Encoding.UTF8.GetString(bytes, byteOffset, bytes.Length - byteOffset);
    }
}
