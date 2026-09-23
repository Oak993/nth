using System.Diagnostics;
using System.Linq;
using System.Text;
using Marrow.Core.Port;

namespace Marrow.UI.Markdown;

// Source: DroppyCode/UI/Markdown/SelectableMessageText.swift (B2 1fbf81e), :1-64.

// :3-8, 9-64. The message as plain text for the clipboard: one walk over the app's own blocks, no
// styling. Headings as their text, code blocks verbatim, links as "title (url)", the delegation
// block left out. Used by the row menu's Copy message (AssistantMessageRow.messageActions). The
// file keeps the name of the selectable text view it used to hold, which left with the
// whole-reply drag mode; this enum is all that stayed.
public static class MessageText
{
    // :10-19
    public static string Plain(string markdown)
    {
        var lines = new List<string>();
        var first = true;
        foreach (var block in MarkdownParser.MarkdownParserParse(markdown))
        {
            if (!first) lines.Add("");
            first = false;
            Append(block, lines);
        }
        return string.Join("\n", lines);
    }

    // :21-51
    private static void Append(MarkdownBlock block, List<string> lines)
    {
        switch (block)
        {
            // :23-24
            case MarkdownBlock.Heading heading:
                lines.Add(Inline(heading.Text));
                break;
            // :25-26
            case MarkdownBlock.Paragraph paragraph:
                lines.Add(Inline(paragraph.Text));
                break;
            // :27-29. `guard language?.lowercased() != "hydra", !code.isEmpty else { return }`:
            // a nil language is not "hydra" (guard passes on that half); the delegation-card
            // language name and the empty case are both skipped, still consuming their
            // blank-line separator from Plain (PR-THEME-122 failure_cases).
            case MarkdownBlock.Code code:
                if ((code.Language is { } language && SwiftString.Lowercased(language) == "hydra") ||
                    code.CodeText.Length == 0)
                {
                    return;
                }
                lines.Add(code.CodeText);
                break;
            // :30-42
            case MarkdownBlock.List list:
                for (var index = 0; index < list.Items.Count; index++)
                {
                    var item = list.Items[index];
                    string marker;
                    if (item.Checked is { } isChecked)
                        marker = isChecked ? "☑ " : "☐ "; // U+2611 BALLOT BOX WITH CHECK, U+2610 BALLOT BOX
                    else if (list.Ordered)
                        marker = $"{list.Start + index}.  ";
                    else
                        marker = "•  "; // U+2022 BULLET
                    if (item.Text.Length != 0) lines.Add(marker + Inline(item.Text));
                    foreach (var child in item.Children) Append(child, lines);
                }
                break;
            // :43-44
            case MarkdownBlock.Quote quote:
                foreach (var child in quote.Blocks) Append(child, lines);
                break;
            // :45-47
            case MarkdownBlock.Table table:
                lines.Add(string.Join("  ", table.Header));
                foreach (var row in table.Rows) lines.Add(string.Join("  ", row));
                break;
            // :48-49
            case MarkdownBlock.Rule:
                break;
            default:
                throw new UnreachableException();
        }
    }

    // :53-63. Links as `title (url)` when the title differs, the bare URL otherwise. Three
    // Swift Regex passes in a fixed order (PR-THEME-181), matched here by hand over grapheme
    // clusters (SwiftString.Characters) rather than System.Text.RegularExpressions, which
    // matches UTF-16 code units: a combining mark attached to a bracket, parenthesis or angle
    // bracket would otherwise still let that literal match, which Swift's Character-level Regex
    // would not do (R-RE-2).
    private static string Inline(string source)
    {
        var text = ReplaceImages(source);
        text = ReplaceLinks(text);
        text = ReplaceAutolinks(text);
        return text;
    }

    // :55. `!\[([^\[\]]*)\]\(\S+?\)` -> `$0.output.1` (the alt text; may be empty).
    private static string ReplaceImages(string source)
    {
        var gs = SwiftString.Characters(source).ToList();
        var n = gs.Count;
        var result = new StringBuilder();
        var i = 0;
        while (i < n)
        {
            if (MatchImage(gs, i) is { } match)
            {
                result.Append(match.Alt);
                i = match.End;
            }
            else
            {
                result.Append(gs[i]);
                i++;
            }
        }

        return result.ToString();
    }

    private readonly record struct ImageMatch(int End, string Alt);

    private static ImageMatch? MatchImage(List<string> gs, int i)
    {
        var n = gs.Count;
        if (i >= n || gs[i] != "!") return null;
        if (i + 1 >= n || gs[i + 1] != "[") return null;
        var j = i + 2;
        var altStart = j;
        while (j < n && gs[j] != "[" && gs[j] != "]") j++;
        if (j >= n || gs[j] != "]") return null;
        var altEnd = j;
        j++; // ']'
        if (j >= n || gs[j] != "(") return null;
        var targetStart = j + 1;
        if (targetStart >= n || SwiftCharacter.IsWhitespace(gs[targetStart])) return null;
        var p = targetStart + 1;
        while (true)
        {
            if (p < n && gs[p] == ")") return new ImageMatch(p + 1, Join(gs, altStart, altEnd));
            if (p >= n || SwiftCharacter.IsWhitespace(gs[p])) return null;
            p++;
        }
    }

    // :56-59. `\[([^\[\]]+)\]\((\S+?)(?:\s+"[^"]*")?\)`: the title, unquoted target and equal
    // title/target transform, in the order match.output.1 (title), match.output.2 (target).
    private static string ReplaceLinks(string source)
    {
        var gs = SwiftString.Characters(source).ToList();
        var n = gs.Count;
        var result = new StringBuilder();
        var i = 0;
        while (i < n)
        {
            if (MatchLink(gs, i) is { } match)
            {
                result.Append(match.Replacement);
                i = match.End;
            }
            else
            {
                result.Append(gs[i]);
                i++;
            }
        }

        return result.ToString();
    }

    private readonly record struct LinkMatch(int End, string Replacement);

    private static LinkMatch? MatchLink(List<string> gs, int i)
    {
        var n = gs.Count;
        if (i >= n || gs[i] != "[") return null;
        var j = i + 1;
        var titleStart = j;
        while (j < n && gs[j] != "[" && gs[j] != "]") j++;
        if (j == titleStart) return null; // `+` requires at least one character
        if (j >= n || gs[j] != "]") return null;
        var titleEnd = j;
        j++; // ']'
        if (j >= n || gs[j] != "(") return null;
        var targetStart = j + 1;
        if (targetStart >= n || SwiftCharacter.IsWhitespace(gs[targetStart])) return null;

        var p = targetStart + 1;
        while (true)
        {
            var titleTailEnd = TryMatchTitleAttribute(gs, p);
            if (titleTailEnd is { } te && te < n && gs[te] == ")")
                return new LinkMatch(te + 1, LinkReplacement(gs, titleStart, titleEnd, targetStart, p));
            if (p < n && gs[p] == ")")
                return new LinkMatch(p + 1, LinkReplacement(gs, titleStart, titleEnd, targetStart, p));
            if (p >= n || SwiftCharacter.IsWhitespace(gs[p])) return null;
            p++;
        }
    }

    // :58-59. `match.output.1 == match.output.2 ? String(match.output.2) : "\(match.output.1) (\(match.output.2))"`
    private static string LinkReplacement(List<string> gs, int titleStart, int titleEnd, int targetStart, int targetEnd)
    {
        var title = Join(gs, titleStart, titleEnd);
        var target = Join(gs, targetStart, targetEnd);
        return SwiftString.Equal(title, target) ? target : $"{title} ({target})";
    }

    // `(?:\s+"[^"]*")?`: one or more whitespace characters, a double quote, any run of
    // non-quote characters, a closing double quote. Returns the index after the closing quote,
    // or null when the optional group does not match starting at p.
    private static int? TryMatchTitleAttribute(List<string> gs, int p)
    {
        var n = gs.Count;
        var q = p;
        if (q >= n || !SwiftCharacter.IsWhitespace(gs[q])) return null;
        while (q < n && SwiftCharacter.IsWhitespace(gs[q])) q++;
        if (q >= n || gs[q] != "\"") return null;
        q++;
        while (q < n && gs[q] != "\"") q++;
        if (q >= n) return null;
        return q + 1;
    }

    // :61. `<((?:https?|mailto):[^<>\s]+)>` -> `$0.output.1` (the bare address).
    private static string ReplaceAutolinks(string source)
    {
        var gs = SwiftString.Characters(source).ToList();
        var n = gs.Count;
        var result = new StringBuilder();
        var i = 0;
        while (i < n)
        {
            if (MatchAutolink(gs, i) is { } match)
            {
                result.Append(match.Address);
                i = match.End;
            }
            else
            {
                result.Append(gs[i]);
                i++;
            }
        }

        return result.ToString();
    }

    private readonly record struct AutolinkMatch(int End, string Address);

    private static AutolinkMatch? MatchAutolink(List<string> gs, int i)
    {
        var n = gs.Count;
        if (i >= n || gs[i] != "<") return null;
        var schemeStart = i + 1;
        int j;
        if (StartsWithLiteral(gs, schemeStart, "http"))
        {
            j = schemeStart + 4;
            if (j < n && gs[j] == "s") j++;
        }
        else if (StartsWithLiteral(gs, schemeStart, "mailto"))
        {
            j = schemeStart + 6;
        }
        else
        {
            return null;
        }

        if (j >= n || gs[j] != ":") return null;
        j++;
        var bodyStart = j;
        while (j < n && gs[j] != "<" && gs[j] != ">" && !SwiftCharacter.IsWhitespace(gs[j])) j++;
        if (j == bodyStart) return null; // `+` requires at least one character
        if (j >= n || gs[j] != ">") return null;
        return new AutolinkMatch(j + 1, Join(gs, schemeStart, j));
    }

    private static bool StartsWithLiteral(List<string> gs, int start, string literal)
    {
        var n = gs.Count;
        for (var k = 0; k < literal.Length; k++)
        {
            var index = start + k;
            if (index >= n || gs[index] != literal[k].ToString()) return false;
        }

        return true;
    }

    private static string Join(List<string> gs, int start, int end)
    {
        var sb = new StringBuilder();
        for (var index = start; index < end; index++) sb.Append(gs[index]);
        return sb.ToString();
    }
}
