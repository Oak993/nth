using System.Globalization;
using System.Text;
using Marrow.Core.Port;

namespace Marrow.UI.Markdown;

// Source: DroppyCode/UI/Markdown/MarkdownParser.swift (B2 1fbf81e), :1-339.

// :3-12. Block-level Markdown for agent output. Inline styling is handled by AttributedString (a
// port concern of the inline layer, not this file).
public abstract record MarkdownBlock
{
    private MarkdownBlock() { }

    // :4
    public sealed record Heading(int Level, string Text) : MarkdownBlock;

    // :5
    public sealed record Paragraph(string Text) : MarkdownBlock;

    // :6. Swift's associated label "code" clashes with the case name "Code" (R-NAME-3): the
    // property is CodeText, cited here rather than renamed to a prefixed form because the case
    // itself, not a sibling nested type, is what the property name collides with.
    public sealed record Code(string? Language, string CodeText) : MarkdownBlock;

    // :7
    public sealed record List(bool Ordered, int Start, ValueList<MarkdownListItem> Items) : MarkdownBlock;

    // :8
    public sealed record Quote(ValueList<MarkdownBlock> Blocks) : MarkdownBlock;

    // :9
    public sealed record Table(ValueList<string> Header, ValueList<ValueList<string>> Rows) : MarkdownBlock;

    // :10
    public sealed record Rule : MarkdownBlock;
}

// :14-18
public sealed record MarkdownListItem(string Text, bool? Checked, ValueList<MarkdownBlock> Children);

// :20-338
public static class MarkdownParser
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
        var lines = new List<string>();
        var starts = new List<int>();
        if (IsWellFormed(text)) SplitLines(text, lines, starts);
        else SplitLinesUtf8(text, lines, starts);

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

    // ENGINEERING DECISION (ED-NEW-lean-13): the same split over the UTF-16 text, without encoding it. 0x0A and
    // 0x0D are never part of a longer UTF-8 sequence, so splitting the characters on '\n' gives the same pieces,
    // and on well-formed UTF-16 decoding the encoded piece gives the piece back; each piece's UTF-8 length is
    // counted to keep `starts` in bytes. A text with a lone surrogate (which UTF-8 turns into U+FFFD) keeps the
    // byte path below.
    private static void SplitLines(string text, List<string> lines, List<int> starts)
    {
        var lineStart = 0;
        var byteStart = 0;
        while (true)
        {
            var newline = text.IndexOf('\n', lineStart);
            var end = newline < 0 ? text.Length : newline;
            starts.Add(byteStart);
            var contentEnd = end > lineStart && text[end - 1] == '\r' ? end - 1 : end;
            lines.Add(text.Substring(lineStart, contentEnd - lineStart));
            if (newline < 0) return;
            byteStart += Encoding.UTF8.GetByteCount(text.AsSpan(lineStart, end - lineStart)) + 1;
            lineStart = end + 1;
        }
    }

    // :53-64 as first ported: the split over the UTF-8 bytes, kept for text that is not well-formed UTF-16.
    private static void SplitLinesUtf8(string text, List<string> lines, List<int> starts)
    {
        var utf8 = Encoding.UTF8.GetBytes(text);
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
                    if (!CannotStartWith(candidate, fence) && SwiftString.HasPrefix(candidate, fence)
                        && AllSameCharacter(candidate, FirstCharacter(fence)))
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
            if (StartsWithCharacter(trimmed, 0, '>') && depth < MaxDepth)
            {
                FlushParagraph();
                blockLines.Add(index);
                var quoted = new List<string>();
                while (index < lines.Count)
                {
                    var candidate = TrimmedLine(lines[index]);
                    if (!StartsWithCharacter(candidate, 0, '>')) break;
                    var content = SwiftString.DropFirst(candidate, 1);
                    if (StartsWithCharacter(content, 0, ' ')) content = SwiftString.DropFirst(content, 1);
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
            // A text that starts with an ASCII character other than '[' (lowercased: the same character) has neither
            // prefix, so the lowercased copy is made only when it can matter (ED-NEW-lean-13).
            if (!CannotStartWith(text, "[ ] "))
            {
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
                if (childLines.Count == 0 && ListMarkerAt(line) is null && FenceMarker(trimmed) is null)
                {
                    currentText.Add(trimmed);
                }
                else
                {
                    childLines.Add(SwiftString.DropFirst(line, Math.Min(Indentation(line), first.ContentOffset)));
                }
                index += 1;
                continue;
            }

            // :253-257
            if (childLines.Count == 0 && HeadingLevel(trimmed) is null && FenceMarker(trimmed) is null && !StartsWithCharacter(trimmed, 0, '>'))
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
    // ENGINEERING DECISION (ED-NEW-lean-13): the first and last UTF-16 characters are checked instead of the first and
    // last UTF-8 bytes: a byte in 0x21-0x7E there is exactly an ASCII character in that range there (every byte of a
    // longer sequence, and of the U+FFFD a lone surrogate becomes, is 0x80 or above).
    private static string TrimmedLine(string line)
    {
        if (line.Length > 0)
        {
            var first = line[0];
            var last = line[^1];
            if (first > 0x20 && first < 0x7F && last > 0x20 && last < 0x7F) return line;
        }
        return FoundationText.Trim(line);
    }

    // :272-289. Swift: private static func listMarker(_ line: String) -> ListMarker?
    // ENGINEERING DECISION (ED-NEW-lean-13): when the body's first Character is one ASCII character, the marker is read
    // from offsets into `line` (no list of Characters, no substrings but the content); every other line takes the
    // Character-list reading below, unchanged. An ASCII Character other than a digit is not isNumber, so a line
    // that starts with a letter is no marker at once.
    private static ListMarker? ListMarkerAt(string line)
    {
        var indent = Indentation(line);
        var bodyStart = OffsetAfterCharacters(line, 0, indent);
        if (bodyStart == line.Length) return null; // :275 no first Character, :283 no digits
        var head = line[bodyStart];
        if (head >= 0x80 || CharacterLength(line, bodyStart) != 1) return ListMarkerAtCharacters(line);
        if (head is '-' or '*' or '+')
        {
            var rest = bodyStart + 1;
            if (!(StartsWithCharacter(line, rest, ' ') || StartsWithCharacter(line, rest, '\t'))) return null;
            return new ListMarker(indent, false, 1, indent + 2, line[SkipCharacters(line, rest, ' ', '\t')..]);
        }
        if (head is < '0' or > '9') return null;

        var digitCount = 0;
        var digitEnd = bodyStart;
        while (digitEnd < line.Length)
        {
            var length = CharacterLength(line, digitEnd);
            var character = line[digitEnd];
            var isNumber = length == 1 && character < 0x80
                ? character is >= '0' and <= '9'
                : SwiftCharacter.IsNumber(line.Substring(digitEnd, length));
            if (!isNumber) break;
            digitCount += 1;
            digitEnd += length;
        }
        if (digitCount > 9) return null;
        if (digitEnd == line.Length) return null;
        if (CharacterLength(line, digitEnd) != 1 || line[digitEnd] is not ('.' or ')')) return null;
        var afterDelimiter = digitEnd + 1;
        if (!(StartsWithCharacter(line, afterDelimiter, ' ') || afterDelimiter == line.Length)) return null;
        var content = line[SkipCharacters(line, afterDelimiter, ' ', ' ')..];
        var startValue = SwiftNumber.ParseInt(line[bodyStart..digitEnd]) is { } n ? (int)n : 1;
        return new ListMarker(indent, true, startValue, indent + digitCount + 2, content);
    }

    // :272-289 read over a list of the body's Characters: lines whose body starts with a Character that is not one
    // ASCII character (a combining mark on the marker, a non-ASCII digit or letter, an emoji, CRLF).
    private static ListMarker? ListMarkerAtCharacters(string line)
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
    // Counted over Character lengths: a space or tab followed by a combining mark is one other Character.
    private static int Indentation(string line)
    {
        var count = 0;
        var index = 0;
        while (index < line.Length && CharacterLength(line, index) == 1)
        {
            if (line[index] == ' ') count += 1;
            else if (line[index] == '\t') count += 4;
            else break;
            index += 1;
        }
        return count;
    }

    private static readonly string[] FenceMarkers = ["```", "~~~"];

    // :299-305. Swift: private static func fenceMarker(_ trimmed: String) -> String?
    private static string? FenceMarker(string trimmed)
    {
        if (CannotStartWith(trimmed, FenceMarkers[0]) && CannotStartWith(trimmed, FenceMarkers[1])) return null;
        foreach (var marker in FenceMarkers)
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
        // Each leading "#" Character is one UTF-16 character, so the rest starts at offset `hashes`.
        return hashes == trimmed.Length || StartsWithCharacter(trimmed, hashes, ' ') ? hashes : null;
    }

    private static int CountLeadingHashes(string trimmed)
    {
        var count = 0;
        while (count < trimmed.Length && trimmed[count] == '#' && CharacterLength(trimmed, count) == 1) count += 1;
        return count;
    }

    // :314-323. Three or more of one of '-', '*', '_', spaces between them allowed. Every bullet
    // line starts with one of those, so this checks characters rather than building a string.
    // Swift: private static func isRule(_ trimmed: String) -> Bool
    // The UTF-16 characters stand in for the UTF-8 bytes (ED-NEW-lean-13): every byte the test accepts is ASCII, and
    // a non-ASCII character fails it in both forms.
    private static bool IsRule(string trimmed)
    {
        if (trimmed.Length == 0) return false;
        var first = trimmed[0];
        if (first != '-' && first != '*' && first != '_') return false;
        var count = 0;
        foreach (var c in trimmed)
        {
            if (c == first) count += 1;
            else if (c != ' ') return false;
        }
        return count >= 3;
    }

    // :325-329. Swift: private static func isTableDelimiter(_ line: String) -> Bool
    // Every Character is one of "|:- " exactly when every UTF-16 character is (those are single ASCII Characters,
    // and a Character holding any other character is no substring of "|:- ").
    private static bool IsTableDelimiter(string line)
    {
        var trimmed = TrimmedLine(line);
        if (!trimmed.Contains('-', StringComparison.Ordinal)) return false;
        return trimmed.AsSpan().IndexOfAnyExcept("|:- ".AsSpan()) < 0;
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

    // ---- ED-NEW-lean-13: Character checks on offsets instead of substrings ----

    // The length in UTF-16 characters of the Character starting at `offset` (SwiftString.Characters' step).
    private static int CharacterLength(string text, int offset) => StringInfo.GetNextTextElementLength(text.AsSpan(offset));

    // SwiftString.DropFirst's offset: `count` Characters on from `start`, clamped at the end.
    private static int OffsetAfterCharacters(string text, int start, int count)
    {
        var index = start;
        for (var i = 0; i < count && index < text.Length; i++) index += CharacterLength(text, index);
        return index;
    }

    // The offset of the first Character from `start` that is neither `a` nor `b` alone (SkipWhile over Characters).
    private static int SkipCharacters(string text, int start, char a, char b)
    {
        var index = start;
        while (index < text.Length && (text[index] == a || text[index] == b) && CharacterLength(text, index) == 1) index += 1;
        return index;
    }

    // SwiftString.HasPrefix(text[offset..], character) for one ASCII character. When the text's Character there is one
    // ASCII character, canonical equivalence is identity and the answer is a compare; any other Character (a mark
    // after it, CRLF, non-ASCII) is handed to SwiftString.HasPrefix itself, so its result and its exceptions stay.
    private static bool StartsWithCharacter(string text, int offset, char character)
    {
        if (offset >= text.Length) return false;
        var first = text[offset];
        if (first < 0x80 && CharacterLength(text, offset) == 1) return first == character;
        return SwiftString.HasPrefix(text[offset..], character.ToString());
    }

    // True only where SwiftString.HasPrefix(text, prefix) is false without comparing anything that could throw: the
    // text is empty, or its first character and the prefix's are different ASCII characters. A Character that starts
    // with an ASCII character decomposes to one that starts with it, so it is never canonically equal to a Character
    // starting with another one, and the compare stops at that first Character.
    private static bool CannotStartWith(string text, string prefix) =>
        prefix.Length > 0 && (text.Length == 0 || text[0] < 0x80 && prefix[0] < 0x80 && text[0] != prefix[0]);

    // True when `text` has no lone surrogate, so its UTF-8 form decodes back to it exactly.
    private static bool IsWellFormed(ReadOnlySpan<char> text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1])) return false;
                i += 1;
            }
            else if (char.IsLowSurrogate(c))
            {
                return false;
            }
        }
        return true;
    }

    // :41. Swift: text.utf8.starts(with: previous.text.utf8)
    // On well-formed UTF-16 the UTF-8 forms start with each other exactly when the texts do (UTF-8 is prefix-free and
    // the prefix ends on a scalar), so no copy is made; a text with a lone surrogate compares its encoded bytes.
    private static bool Utf8StartsWith(string text, string prefix)
    {
        if (IsWellFormed(text) && IsWellFormed(prefix)) return text.StartsWith(prefix, StringComparison.Ordinal);
        var textBytes = Encoding.UTF8.GetBytes(text);
        var prefixBytes = Encoding.UTF8.GetBytes(prefix);
        if (prefixBytes.Length > textBytes.Length) return false;
        return textBytes.AsSpan(0, prefixBytes.Length).SequenceEqual(prefixBytes);
    }

    // :45. Swift: text.utf8.index(text.utf8.startIndex, offsetBy: restart)...
    // On well-formed UTF-16 the byte offset is walked to its character offset (the restart is a line start, so it
    // falls on a scalar) and the text is sliced there; otherwise, or off a scalar, the bytes are decoded as before.
    private static string SubstringFromUtf8Offset(string text, int byteOffset)
    {
        if (IsWellFormed(text))
        {
            var bytes = 0;
            var index = 0;
            while (bytes < byteOffset && index < text.Length)
            {
                var c = text[index];
                if (c < 0x80) { bytes += 1; index += 1; }
                else if (c < 0x800) { bytes += 2; index += 1; }
                else if (char.IsHighSurrogate(c)) { bytes += 4; index += 2; }
                else { bytes += 3; index += 1; }
            }
            if (bytes == byteOffset) return text[index..];
        }
        var encoded = Encoding.UTF8.GetBytes(text);
        return Encoding.UTF8.GetString(encoded, byteOffset, encoded.Length - byteOffset);
    }
}
