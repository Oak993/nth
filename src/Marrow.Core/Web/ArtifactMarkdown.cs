// PORT-ONLY: web surfaces W13 (DESIGN.md 6.1 kind "markdown", 6.3 Export: md, pdf, docx "Markdown to docx with Open
// XML", html; ED-NEW-web-w13-5). A small Markdown reader for Markdown artifacts: headings, paragraphs, lists (nested),
// block quotes, fenced code, rules and pipe tables, with emphasis, strong, strike-through, code spans and links inline.
// It feeds the rendered preview, the standalone HTML and PDF exports and the .docx writer. Raw HTML in the source is
// shown as text, and only web, mail and in-page links are kept. No Swift counterpart.
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Marrow.Core.Web;

public abstract record MarkdownBlock;

public sealed record MarkdownHeading(int Level, string Text) : MarkdownBlock;

public sealed record MarkdownParagraph(string Text) : MarkdownBlock;

public sealed record MarkdownCode(string? Language, string Text) : MarkdownBlock;

public sealed record MarkdownQuote(IReadOnlyList<MarkdownBlock> Blocks) : MarkdownBlock;

public sealed record MarkdownList(bool Ordered, int Start, IReadOnlyList<MarkdownListItem> Items) : MarkdownBlock;

public sealed record MarkdownListItem(string Text, IReadOnlyList<MarkdownBlock> Children);

public sealed record MarkdownRule : MarkdownBlock;

public sealed record MarkdownTable(IReadOnlyList<string> Header, IReadOnlyList<IReadOnlyList<string>> Rows) : MarkdownBlock;

/// <summary>A run of inline text with its styles; <see cref="Link"/> is a kept link's address.</summary>
public sealed record MarkdownSpan(string Text, bool Bold = false, bool Italic = false, bool Code = false, bool Strike = false, string? Link = null, bool Break = false);

public static partial class ArtifactMarkdown
{
    [GeneratedRegex(@"^(#{1,6})[ \t]+(.*?)[ \t]*#*[ \t]*$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^[ ]{0,3}(`{3,}|~{3,})[ \t]*([^`\s]*)")]
    private static partial Regex Fence();

    [GeneratedRegex(@"^[ ]{0,3}([-*_])(?:[ \t]*\1){2,}[ \t]*$")]
    private static partial Regex Rule();

    [GeneratedRegex(@"^([ ]*)([-*+]|\d{1,9}[.)])[ \t]+(.*)$")]
    private static partial Regex ListItem();

    [GeneratedRegex(@"^[ ]{0,3}>[ ]?(.*)$")]
    private static partial Regex Quote();

    [GeneratedRegex(@"^\s*\|?\s*:?-{1,}:?\s*(\|\s*:?-{1,}:?\s*)*\|?\s*$")]
    private static partial Regex TableSeparator();

    public static IReadOnlyList<MarkdownBlock> Parse(string? markdown)
    {
        var lines = (markdown ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n')
            .Select(l => l.Replace("\t", "    ", StringComparison.Ordinal)).ToList();
        return Blocks(lines);
    }

    private static List<MarkdownBlock> Blocks(List<string> lines)
    {
        var blocks = new List<MarkdownBlock>();
        var i = 0;
        while (i < lines.Count)
        {
            var line = lines[i];
            if (line.Trim().Length == 0) { i++; continue; }
            if (Fence().Match(line) is { Success: true } fence)
            {
                var marker = fence.Groups[1].Value;
                var code = new List<string>();
                i++;
                while (i < lines.Count && !lines[i].TrimStart().StartsWith(marker, StringComparison.Ordinal)) code.Add(lines[i++]);
                i++;
                blocks.Add(new MarkdownCode(fence.Groups[2].Value is { Length: > 0 } lang ? lang : null, string.Join('\n', code)));
                continue;
            }

            if (Heading().Match(line.TrimStart()) is { Success: true } heading && line.Length - line.TrimStart().Length <= 3)
            {
                blocks.Add(new MarkdownHeading(heading.Groups[1].Value.Length, heading.Groups[2].Value));
                i++;
                continue;
            }

            if (Rule().IsMatch(line)) { blocks.Add(new MarkdownRule()); i++; continue; }
            if (Quote().IsMatch(line))
            {
                var quoted = new List<string>();
                for (; i < lines.Count && lines[i].Trim().Length > 0; i++)
                    quoted.Add(Quote().Match(lines[i]) is { Success: true } q ? q.Groups[1].Value : lines[i].Trim());
                blocks.Add(new MarkdownQuote(Blocks(quoted)));
                continue;
            }

            if (ListItem().Match(line) is { Success: true } first && first.Groups[1].Value.Length <= 3)
            {
                blocks.Add(List(lines, ref i, first));
                continue;
            }

            if (line.Contains('|', StringComparison.Ordinal) && i + 1 < lines.Count && TableSeparator().IsMatch(lines[i + 1]) && lines[i + 1].Contains('-', StringComparison.Ordinal))
            {
                var header = Cells(line);
                var rows = new List<IReadOnlyList<string>>();
                i += 2;
                while (i < lines.Count && lines[i].Trim().Length > 0 && lines[i].Contains('|', StringComparison.Ordinal))
                {
                    var cells = Cells(lines[i++]);
                    rows.Add(Enumerable.Range(0, header.Count).Select(c => c < cells.Count ? cells[c] : "").ToList());
                }

                blocks.Add(new MarkdownTable(header, rows));
                continue;
            }

            // A paragraph runs to a blank line or the start of another block; a line of = or - under it makes a heading.
            var text = new List<string> { line.TrimStart() };
            i++;
            while (i < lines.Count && lines[i].Trim().Length > 0 && !StartsBlock(lines[i]))
            {
                var next = lines[i].Trim();
                if (next.All(c => c == '=') || next.All(c => c == '-'))
                {
                    blocks.Add(new MarkdownHeading(next[0] == '=' ? 1 : 2, string.Join(' ', text.Select(t => t.Trim()))));
                    text.Clear();
                    i++;
                    break;
                }

                text.Add(lines[i].TrimStart());
                i++;
            }

            if (text.Count > 0) blocks.Add(new MarkdownParagraph(JoinParagraph(text)));
        }

        return blocks;
    }

    private static string JoinParagraph(List<string> text)
    {
        var builder = new StringBuilder();
        for (var n = 0; n < text.Count; n++)
        {
            var piece = text[n];
            builder.Append(piece.TrimEnd());
            if (n < text.Count - 1) builder.Append(piece.EndsWith("  ", StringComparison.Ordinal) ? "\n" : " ");
        }

        return builder.ToString();
    }

    private static bool StartsBlock(string line) =>
        Fence().IsMatch(line) || (Heading().IsMatch(line.TrimStart()) && line.Length - line.TrimStart().Length <= 3) || Rule().IsMatch(line)
        || Quote().IsMatch(line) || (ListItem().Match(line) is { Success: true } item && item.Groups[1].Value.Length <= 3);

    private static MarkdownList List(List<string> lines, ref int i, Match first)
    {
        var ordered = char.IsDigit(first.Groups[2].Value[0]);
        var start = ordered && int.TryParse(first.Groups[2].Value.TrimEnd('.', ')'), out var number) ? number : 1;
        var indent = first.Groups[1].Value.Length;
        var items = new List<MarkdownListItem>();
        while (i < lines.Count)
        {
            var match = ListItem().Match(lines[i]);
            if (!match.Success || match.Groups[1].Value.Length != indent || char.IsDigit(match.Groups[2].Value[0]) != ordered) break;
            var content = indent + match.Groups[2].Value.Length + 1;
            var body = new List<string> { match.Groups[3].Value };
            i++;
            while (i < lines.Count)
            {
                var line = lines[i];
                var lead = line.Length - line.TrimStart().Length;
                if (line.Trim().Length == 0)
                {
                    // A blank line continues the item only when an indented line follows.
                    if (i + 1 < lines.Count && lines[i + 1].Trim().Length > 0 && lines[i + 1].Length - lines[i + 1].TrimStart().Length > indent) { body.Add(""); i++; continue; }
                    break;
                }

                if (lead <= indent && (ListItem().IsMatch(line) || StartsBlock(line))) break;
                body.Add(lead >= content ? line[content..] : line.TrimStart());
                i++;
            }

            var blocks = Blocks(body);
            var text = blocks.FirstOrDefault() is MarkdownParagraph paragraph ? paragraph.Text : "";
            items.Add(new MarkdownListItem(text, text.Length > 0 || blocks.FirstOrDefault() is MarkdownParagraph ? blocks.Skip(1).ToList() : blocks));
        }

        return new MarkdownList(ordered, start, items);
    }

    private static List<string> Cells(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('|')) trimmed = trimmed[1..];
        if (trimmed.EndsWith('|') && !trimmed.EndsWith("\\|", StringComparison.Ordinal)) trimmed = trimmed[..^1];
        var cells = new List<string>();
        var cell = new StringBuilder();
        for (var n = 0; n < trimmed.Length; n++)
        {
            if (trimmed[n] == '\\' && n + 1 < trimmed.Length && trimmed[n + 1] == '|') { cell.Append('|'); n++; }
            else if (trimmed[n] == '|') { cells.Add(cell.ToString().Trim()); cell.Clear(); }
            else cell.Append(trimmed[n]);
        }

        cells.Add(cell.ToString().Trim());
        return cells;
    }

    // ---- inline ----

    [GeneratedRegex(@"(?<code>`+)(?<codetext>.+?)\k<code>|!\[(?<alt>[^\]]*)\]\((?<src>[^)\s]*)(?:\s+""[^""]*"")?\)|\[(?<label>[^\]]+)\]\((?<href>[^)\s]*)(?:\s+""[^""]*"")?\)|(?<strong>\*\*|__)(?<strongtext>.+?)\k<strong>|~~(?<strike>.+?)~~|(?<em>[*_])(?<emtext>[^*_\s](?:.*?[^*_\s])?)\k<em>|<(?<auto>https?://[^>\s]+)>|\n", RegexOptions.Singleline)]
    private static partial Regex InlineToken();

    /// <summary>The inline runs of a paragraph, heading, cell or item.</summary>
    public static IReadOnlyList<MarkdownSpan> Inline(string text) => InlineSpans(text, new MarkdownSpan(""));

    private static List<MarkdownSpan> InlineSpans(string text, MarkdownSpan style)
    {
        var spans = new List<MarkdownSpan>();
        var at = 0;
        foreach (Match match in InlineToken().Matches(text))
        {
            if (match.Index > at) spans.Add(style with { Text = Unescape(text[at..match.Index]) });
            at = match.Index + match.Length;
            if (match.Value == "\n") spans.Add(style with { Text = "", Break = true });
            else if (match.Groups["codetext"].Success) spans.Add(style with { Text = match.Groups["codetext"].Value.Trim(), Code = true });
            else if (match.Groups["alt"].Success) spans.Add(style with { Text = match.Groups["alt"].Value, Italic = true });
            else if (match.Groups["label"].Success)
            {
                var href = SafeLink(match.Groups["href"].Value);
                spans.AddRange(InlineSpans(match.Groups["label"].Value, href is null ? style : style with { Link = href }));
            }
            else if (match.Groups["strongtext"].Success) spans.AddRange(InlineSpans(match.Groups["strongtext"].Value, style with { Bold = true }));
            else if (match.Groups["strike"].Success) spans.AddRange(InlineSpans(match.Groups["strike"].Value, style with { Strike = true }));
            else if (match.Groups["emtext"].Success) spans.AddRange(InlineSpans(match.Groups["emtext"].Value, style with { Italic = true }));
            else if (match.Groups["auto"].Success) spans.Add(style with { Text = match.Groups["auto"].Value, Link = match.Groups["auto"].Value });
        }

        if (at < text.Length) spans.Add(style with { Text = Unescape(text[at..]) });
        return spans.Where(s => s.Text.Length > 0 || s.Break).ToList();
    }

    /// <summary>A link kept in exports: web, mail and in-page addresses only.</summary>
    public static string? SafeLink(string href)
    {
        var value = href.Trim();
        if (value.StartsWith('#')) return value;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto" ? value : null;
    }

    [GeneratedRegex(@"\\([\\`*_{}\[\]()#+\-.!|~>])")]
    private static partial Regex Escaped();

    private static string Unescape(string text) => Escaped().Replace(text, "$1");

    // ---- HTML ----

    /// <summary>The document as HTML body content (every text escaped; no raw HTML from the source).</summary>
    public static string ToHtml(string? markdown)
    {
        var builder = new StringBuilder();
        foreach (var block in Parse(markdown)) Html(builder, block);
        return builder.ToString();
    }

    private static void Html(StringBuilder b, MarkdownBlock block)
    {
        switch (block)
        {
            case MarkdownHeading h:
                b.Append("<h").Append(h.Level).Append('>').Append(InlineHtml(h.Text)).Append("</h").Append(h.Level).Append(">\n");
                break;
            case MarkdownParagraph p:
                b.Append("<p>").Append(InlineHtml(p.Text)).Append("</p>\n");
                break;
            case MarkdownCode c:
                b.Append("<pre><code").Append(c.Language is { } lang ? " class=\"language-" + WebUtility.HtmlEncode(lang) + "\"" : "").Append('>')
                    .Append(WebUtility.HtmlEncode(c.Text)).Append("</code></pre>\n");
                break;
            case MarkdownQuote q:
                b.Append("<blockquote>\n");
                foreach (var inner in q.Blocks) Html(b, inner);
                b.Append("</blockquote>\n");
                break;
            case MarkdownList l:
                b.Append(l.Ordered ? (l.Start != 1 ? "<ol start=\"" + l.Start + "\">\n" : "<ol>\n") : "<ul>\n");
                foreach (var item in l.Items)
                {
                    b.Append("<li>").Append(InlineHtml(item.Text));
                    if (item.Children.Count > 0)
                    {
                        b.Append('\n');
                        foreach (var inner in item.Children) Html(b, inner);
                    }

                    b.Append("</li>\n");
                }

                b.Append(l.Ordered ? "</ol>\n" : "</ul>\n");
                break;
            case MarkdownRule:
                b.Append("<hr>\n");
                break;
            case MarkdownTable t:
                b.Append("<table>\n<thead><tr>");
                foreach (var cell in t.Header) b.Append("<th>").Append(InlineHtml(cell)).Append("</th>");
                b.Append("</tr></thead>\n<tbody>\n");
                foreach (var row in t.Rows)
                {
                    b.Append("<tr>");
                    foreach (var cell in row) b.Append("<td>").Append(InlineHtml(cell)).Append("</td>");
                    b.Append("</tr>\n");
                }

                b.Append("</tbody>\n</table>\n");
                break;
        }
    }

    public static string InlineHtml(string text)
    {
        var b = new StringBuilder();
        foreach (var span in Inline(text))
        {
            if (span.Break) { b.Append("<br>"); continue; }
            var inner = WebUtility.HtmlEncode(span.Text);
            if (span.Code) inner = "<code>" + inner + "</code>";
            if (span.Bold) inner = "<strong>" + inner + "</strong>";
            if (span.Italic) inner = "<em>" + inner + "</em>";
            if (span.Strike) inner = "<del>" + inner + "</del>";
            if (span.Link is { } link) inner = "<a href=\"" + WebUtility.HtmlEncode(link) + "\">" + inner + "</a>";
            b.Append(inner);
        }

        return b.ToString();
    }
}
