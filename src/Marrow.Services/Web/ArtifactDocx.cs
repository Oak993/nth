// PORT-ONLY: web surfaces W13 (DESIGN.md 6.3 Export: "docx (Markdown documents, through the existing document path if
// present, else Markdown to docx with Open XML)"; ED-NEW-web-w13-5). nth has no document path, so a Markdown artifact
// becomes a WordprocessingML package written here with the framework's zip and XML writers: headings, paragraphs,
// bulleted and numbered lists (nested), quotes, code blocks, rules, tables, and bold, italic, strike-through, code and
// links inline. No Swift counterpart.
using System.IO.Compression;
using System.Xml.Linq;
using Marrow.Core.Web;

namespace Marrow.Services.Web;

public static class ArtifactDocx
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace Types = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace Cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private const string RelationshipBase = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    public const string MediaType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    /// <summary>The .docx package of a parsed Markdown document.</summary>
    public static byte[] Write(IReadOnlyList<MarkdownBlock> blocks, string title)
    {
        var writer = new Writer();
        foreach (var block in blocks) writer.Block(block, 0);
        var document = new XDocument(new XElement(W + "document",
            new XAttribute(XNamespace.Xmlns + "w", W), new XAttribute(XNamespace.Xmlns + "r", R),
            new XElement(W + "body", writer.Body, new XElement(W + "sectPr",
                new XElement(W + "pgSz", new XAttribute(W + "w", 12240), new XAttribute(W + "h", 15840)),
                new XElement(W + "pgMar", new XAttribute(W + "top", 1440), new XAttribute(W + "right", 1440), new XAttribute(W + "bottom", 1440),
                    new XAttribute(W + "left", 1440), new XAttribute(W + "header", 720), new XAttribute(W + "footer", 720), new XAttribute(W + "gutter", 0))))));

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", new XDocument(new XElement(Types + "Types",
                new XElement(Types + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(Types + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                Override("/word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"),
                Override("/word/styles.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"),
                Override("/word/numbering.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"),
                Override("/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml"))));
            Add(zip, "_rels/.rels", Relationships(
                ("rId1", RelationshipBase + "officeDocument", "word/document.xml", false),
                ("rId2", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml", false)));
            Add(zip, "docProps/core.xml", new XDocument(new XElement(Cp + "coreProperties",
                new XAttribute(XNamespace.Xmlns + "cp", Cp), new XAttribute(XNamespace.Xmlns + "dc", Dc),
                new XElement(Dc + "title", title))));
            Add(zip, "word/document.xml", document);
            Add(zip, "word/styles.xml", Styles());
            Add(zip, "word/numbering.xml", Numbering(writer.OrderedStarts));
            var relationships = new List<(string, string, string, bool)>
            {
                ("rIdStyles", RelationshipBase + "styles", "styles.xml", false),
                ("rIdNumbering", RelationshipBase + "numbering", "numbering.xml", false),
            };
            relationships.AddRange(writer.Links.Select(link => (link.Value, RelationshipBase + "hyperlink", link.Key, true)));
            Add(zip, "word/_rels/document.xml.rels", Relationships([.. relationships]));
        }

        return buffer.ToArray();
    }

    private static XElement Override(string part, string type) =>
        new(Types + "Override", new XAttribute("PartName", part), new XAttribute("ContentType", type));

    private static XDocument Relationships(params (string Id, string Type, string Target, bool External)[] items) =>
        new(new XElement(Rel + "Relationships", items.Select(item => new XElement(Rel + "Relationship",
            new XAttribute("Id", item.Id), new XAttribute("Type", item.Type), new XAttribute("Target", item.Target),
            item.External ? new XAttribute("TargetMode", "External") : null))));

    private static void Add(ZipArchive zip, string name, XDocument xml)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var stream = entry.Open();
        xml.Declaration = new XDeclaration("1.0", "UTF-8", "yes");
        xml.Save(stream, SaveOptions.DisableFormatting);
    }

    private sealed class Writer
    {
        public List<XElement> Body { get; } = [];
        public Dictionary<string, string> Links { get; } = new(StringComparer.Ordinal);
        public List<int> OrderedStarts { get; } = [];

        public void Block(MarkdownBlock block, int depth, string? quoteStyle = null)
        {
            switch (block)
            {
                case MarkdownHeading h:
                    Body.Add(Paragraph("Heading" + h.Level, Runs(h.Text)));
                    break;
                case MarkdownParagraph p:
                    Body.Add(Paragraph(quoteStyle, Runs(p.Text)));
                    break;
                case MarkdownCode c:
                    var lines = c.Text.Split('\n');
                    var runs = new List<XElement>();
                    for (var i = 0; i < lines.Length; i++)
                    {
                        if (i > 0) runs.Add(new XElement(W + "r", new XElement(W + "br")));
                        runs.Add(Run(lines[i], new MarkdownSpan(lines[i])));
                    }

                    Body.Add(Paragraph("Code", runs));
                    break;
                case MarkdownQuote q:
                    foreach (var inner in q.Blocks) Block(inner, depth, "Quote");
                    break;
                case MarkdownList l:
                    var numId = 1;
                    if (l.Ordered)
                    {
                        OrderedStarts.Add(l.Start);
                        numId = 1 + OrderedStarts.Count;
                    }

                    foreach (var item in l.Items)
                    {
                        var paragraph = Paragraph("ListParagraph", Runs(item.Text));
                        paragraph.Element(W + "pPr")!.Add(new XElement(W + "numPr",
                            new XElement(W + "ilvl", new XAttribute(W + "val", Math.Min(depth, 8))), new XElement(W + "numId", new XAttribute(W + "val", numId))));
                        Body.Add(paragraph);
                        foreach (var child in item.Children) Block(child, depth + 1);
                    }

                    break;
                case MarkdownRule:
                    Body.Add(new XElement(W + "p", new XElement(W + "pPr", new XElement(W + "pBdr",
                        new XElement(W + "bottom", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", 6), new XAttribute(W + "space", 1), new XAttribute(W + "color", "auto"))))));
                    break;
                case MarkdownTable t:
                    Body.Add(Table(t));
                    Body.Add(new XElement(W + "p"));
                    break;
            }
        }

        private XElement Table(MarkdownTable table)
        {
            XElement Border(string side) => new(W + side, new XAttribute(W + "val", "single"), new XAttribute(W + "sz", 4), new XAttribute(W + "space", 0), new XAttribute(W + "color", "BFBFBF"));
            XElement Row(IEnumerable<string> cells, bool header) => new(W + "tr", cells.Select(cell => new XElement(W + "tc",
                new XElement(W + "tcPr", new XElement(W + "tcW", new XAttribute(W + "w", 0), new XAttribute(W + "type", "auto"))),
                Paragraph(null, Runs(cell, header)))));
            return new XElement(W + "tbl",
                new XElement(W + "tblPr",
                    new XElement(W + "tblW", new XAttribute(W + "w", 0), new XAttribute(W + "type", "auto")),
                    new XElement(W + "tblBorders", Border("top"), Border("left"), Border("bottom"), Border("right"), Border("insideH"), Border("insideV"))),
                Row(table.Header, header: true),
                table.Rows.Select(row => Row(row, header: false)));
        }

        private static XElement Paragraph(string? style, IEnumerable<XElement> runs) =>
            new(W + "p", new XElement(W + "pPr", style is null ? null : new XElement(W + "pStyle", new XAttribute(W + "val", style))), runs);

        private List<XElement> Runs(string text, bool bold = false)
        {
            var result = new List<XElement>();
            foreach (var span in ArtifactMarkdown.Inline(text))
            {
                if (span.Break) { result.Add(new XElement(W + "r", new XElement(W + "br"))); continue; }
                var run = Run(span.Text, bold ? span with { Bold = true } : span);
                if (span.Link is { } link && !link.StartsWith('#'))
                {
                    if (!Links.TryGetValue(link, out var id)) Links[link] = id = "rIdLink" + (Links.Count + 1);
                    result.Add(new XElement(W + "hyperlink", new XAttribute(R + "id", id), run));
                }
                else result.Add(run);
            }

            return result;
        }

        private static XElement Run(string text, MarkdownSpan style)
        {
            var properties = new XElement(W + "rPr");
            // The schema's order: one rStyle, then fonts, bold, italic, strike-through.
            if (style.Link is not null && !style.Link.StartsWith('#'))
            {
                properties.Add(new XElement(W + "rStyle", new XAttribute(W + "val", "Hyperlink")));
                if (style.Code) properties.Add(new XElement(W + "rFonts", new XAttribute(W + "ascii", "Consolas"), new XAttribute(W + "hAnsi", "Consolas"), new XAttribute(W + "cs", "Consolas")));
            }
            else if (style.Code) properties.Add(new XElement(W + "rStyle", new XAttribute(W + "val", "CodeChar")));
            if (style.Bold) properties.Add(new XElement(W + "b"));
            if (style.Italic) properties.Add(new XElement(W + "i"));
            if (style.Strike) properties.Add(new XElement(W + "strike"));
            return new XElement(W + "r", properties.HasElements ? properties : null,
                new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), Clean(text)));
        }

        /// <summary>Characters XML 1.0 cannot hold are dropped.</summary>
        private static string Clean(string text) =>
            new(text.Where(c => c is '\t' or '\n' or '\r' || (c >= 0x20 && c != 0xFFFE && c != 0xFFFF)).ToArray());
    }

    private static XDocument Styles()
    {
        XElement Style(string type, string id, string name, string? basedOn, XElement? paragraph, XElement? run) =>
            new(W + "style", new XAttribute(W + "type", type), new XAttribute(W + "styleId", id),
                new XElement(W + "name", new XAttribute(W + "val", name)),
                basedOn is null ? null : new XElement(W + "basedOn", new XAttribute(W + "val", basedOn)),
                new XElement(W + "qFormat"), paragraph, run);
        XElement Size(int halfPoints) => new(W + "sz", new XAttribute(W + "val", halfPoints));
        XElement Spacing(int before, int after) => new(W + "spacing", new XAttribute(W + "before", before), new XAttribute(W + "after", after));
        XElement Mono() => new(W + "rFonts", new XAttribute(W + "ascii", "Consolas"), new XAttribute(W + "hAnsi", "Consolas"), new XAttribute(W + "cs", "Consolas"));
        int[] headingSizes = [32, 28, 26, 24, 22, 22];
        return new XDocument(new XElement(W + "styles", new XAttribute(XNamespace.Xmlns + "w", W),
            new XElement(W + "docDefaults",
                new XElement(W + "rPrDefault", new XElement(W + "rPr",
                    new XElement(W + "rFonts", new XAttribute(W + "ascii", "Calibri"), new XAttribute(W + "hAnsi", "Calibri"), new XAttribute(W + "cs", "Calibri"), new XAttribute(W + "eastAsia", "Calibri")),
                    Size(22))),
                new XElement(W + "pPrDefault", new XElement(W + "pPr", Spacing(0, 160), new XElement(W + "spacing", new XAttribute(W + "line", 276), new XAttribute(W + "lineRule", "auto"))))),
            Style("paragraph", "Normal", "Normal", null, null, null),
            headingSizes.Select((size, i) => Style("paragraph", "Heading" + (i + 1), "heading " + (i + 1), "Normal",
                new XElement(W + "pPr", new XElement(W + "keepNext"), Spacing(i == 0 ? 360 : 240, 120), new XElement(W + "outlineLvl", new XAttribute(W + "val", i))),
                new XElement(W + "rPr", new XElement(W + "b"), Size(size)))),
            Style("paragraph", "Code", "Code", "Normal",
                new XElement(W + "pPr", Spacing(120, 120), new XElement(W + "shd", new XAttribute(W + "val", "clear"), new XAttribute(W + "color", "auto"), new XAttribute(W + "fill", "F2F2F2"))),
                new XElement(W + "rPr", Mono(), Size(19))),
            Style("paragraph", "Quote", "Quote", "Normal",
                new XElement(W + "pPr", new XElement(W + "ind", new XAttribute(W + "left", 720))),
                new XElement(W + "rPr", new XElement(W + "i"), new XElement(W + "color", new XAttribute(W + "val", "595959")))),
            Style("paragraph", "ListParagraph", "List Paragraph", "Normal", new XElement(W + "pPr", Spacing(0, 60)), null),
            Style("character", "Hyperlink", "Hyperlink", null, null,
                new XElement(W + "rPr", new XElement(W + "color", new XAttribute(W + "val", "0563C1")), new XElement(W + "u", new XAttribute(W + "val", "single")))),
            Style("character", "CodeChar", "Code Char", null, null, new XElement(W + "rPr", Mono(), Size(20)))));
    }

    private static XDocument Numbering(IReadOnlyList<int> orderedStarts)
    {
        XElement Abstract(int id, bool ordered) => new(W + "abstractNum", new XAttribute(W + "abstractNumId", id),
            new XElement(W + "multiLevelType", new XAttribute(W + "val", "hybridMultilevel")),
            Enumerable.Range(0, 9).Select(level => new XElement(W + "lvl", new XAttribute(W + "ilvl", level),
                new XElement(W + "start", new XAttribute(W + "val", 1)),
                new XElement(W + "numFmt", new XAttribute(W + "val", ordered ? (level % 3 == 0 ? "decimal" : level % 3 == 1 ? "lowerLetter" : "lowerRoman") : "bullet")),
                new XElement(W + "lvlText", new XAttribute(W + "val", ordered ? "%" + (level + 1) + "." : (level % 3 == 0 ? "•" : level % 3 == 1 ? "◦" : "▪"))),
                new XElement(W + "lvlJc", new XAttribute(W + "val", "left")),
                new XElement(W + "pPr", new XElement(W + "ind", new XAttribute(W + "left", 720 * (level + 1)), new XAttribute(W + "hanging", 360))))));
        return new XDocument(new XElement(W + "numbering", new XAttribute(XNamespace.Xmlns + "w", W),
            Abstract(0, ordered: false),
            Abstract(1, ordered: true),
            new XElement(W + "num", new XAttribute(W + "numId", 1), new XElement(W + "abstractNumId", new XAttribute(W + "val", 0))),
            orderedStarts.Select((start, i) => new XElement(W + "num", new XAttribute(W + "numId", i + 2),
                new XElement(W + "abstractNumId", new XAttribute(W + "val", 1)),
                new XElement(W + "lvlOverride", new XAttribute(W + "ilvl", 0), new XElement(W + "startOverride", new XAttribute(W + "val", start)))))));
    }
}
