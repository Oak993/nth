// PORT-ONLY: web surfaces W13 (DESIGN.md 6.1, 6.4, 6.5; ED-NEW-web-w13-3). The pages an artifact's site serves, per
// kind: an HTML app as written (CDN addresses rewritten, the import map first in its head); a React component in a shell
// that compiles it with Sucrase and renders it; a Mermaid diagram rendered offline; an SVG image; code, Markdown and
// nth-ui source highlighted; a project's pages with its JSX and TSX modules handed to the project runner. Every page is
// self-contained except for the library host. No Swift counterpart.
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Marrow.Core.Web;

namespace Marrow.Services.Web;

public static partial class ArtifactPages
{
    /// <summary>The element that carries an artifact's source into its page, as JSON.</summary>
    public const string SourceElement = "nth-source";

    /// <summary>The element that carries a project's files into its page, as JSON.</summary>
    public const string FilesElement = "nth-files";

    /// <summary>Where an SVG artifact's image is served on its site.</summary>
    public const string SvgPath = "artifact.svg";

    private static string Lib(string path) => ArtifactLibraryCatalog.Url(path);

    /// <summary>The import map tag every sandboxed page starts with.</summary>
    public static string ImportMap(ArtifactLibraryCatalog catalog) => "<script type=\"importmap\">" + catalog.ImportMapJson() + "</script>";

    /// <summary>An HTML artifact (or a project's HTML file): CDN addresses rewritten, the import map first in its head.</summary>
    public static string Html(string html, ArtifactLibraryCatalog catalog, List<ArtifactCdnRewrite>? rewrites = null) =>
        InjectHead(catalog.RewriteCdn(html, rewrites), ImportMap(catalog));

    /// <summary>A React artifact: the component compiled and rendered in the sandbox (Tailwind's browser build included).</summary>
    public static string React(string code, string file, string title, ArtifactLibraryCatalog catalog, List<ArtifactCdnRewrite>? rewrites = null)
    {
        var source = catalog.RewriteCdn(code, rewrites);
        var builder = Shell(title, catalog);
        if (catalog.ById("tailwindcss") is { } tailwind) builder.Append("<script src=\"").Append(Lib(tailwind.Script)).Append("\"></script>\n");
        builder.Append("<style>html,body{margin:0}#root{min-height:100vh}</style>\n");
        AppendCompiler(builder, catalog);
        builder.Append("</head>\n<body>\n<div id=\"root\"></div>\n");
        builder.Append("<script type=\"application/json\" id=\"").Append(SourceElement).Append("\">")
            .Append(ScriptJson(new Dictionary<string, string> { ["code"] = source, ["file"] = file })).Append("</script>\n");
        builder.Append("<script src=\"").Append(Lib("nth/react-runner.js")).Append("\"></script>\n</body>\n</html>\n");
        return builder.ToString();
    }

    /// <summary>A Mermaid artifact, rendered offline by the vendored Mermaid in its strict security level.</summary>
    public static string Mermaid(string code, string title, ArtifactLibraryCatalog catalog)
    {
        var builder = Shell(title, catalog);
        builder.Append("<style>html,body{margin:0}body{display:grid;place-items:center;min-height:100vh;padding:16px;box-sizing:border-box}#diagram svg{max-width:100%;height:auto}</style>\n");
        if (catalog.ById("mermaid") is { } mermaid) builder.Append("<script src=\"").Append(Lib(mermaid.Script)).Append("\"></script>\n");
        builder.Append("</head>\n<body>\n<div id=\"diagram\" role=\"img\" aria-label=\"").Append(WebUtility.HtmlEncode(title)).Append("\"></div>\n");
        builder.Append("<script type=\"application/json\" id=\"").Append(SourceElement).Append("\">")
            .Append(ScriptJson(new Dictionary<string, string> { ["code"] = code })).Append("</script>\n");
        builder.Append("<script src=\"").Append(Lib("nth/mermaid-runner.js")).Append("\"></script>\n</body>\n</html>\n");
        return builder.ToString();
    }

    /// <summary>An SVG artifact: the image (served beside the page) as an img, so any script inside it never runs.</summary>
    public static string Svg(string title, ArtifactLibraryCatalog catalog)
    {
        var builder = Shell(title, catalog);
        builder.Append("<style>html,body{margin:0}body{display:grid;place-items:center;min-height:100vh}img{max-width:100%;max-height:100vh}</style>\n");
        builder.Append("</head>\n<body>\n<img src=\"").Append(SvgPath).Append("\" alt=\"").Append(WebUtility.HtmlEncode(title)).Append("\">\n</body>\n</html>\n");
        return builder.ToString();
    }

    /// <summary>Code, Markdown or nth-ui source, highlighted by the vendored highlight.js (light or dark with the system).</summary>
    public static string Code(string code, string? language, string title, ArtifactLibraryCatalog catalog)
    {
        var builder = Shell(title, catalog);
        if (catalog.ById("highlight.js") is { } highlight)
        {
            foreach (var css in highlight.Css)
            {
                var media = css.Contains("dark", StringComparison.Ordinal) ? "(prefers-color-scheme: dark)" : "(prefers-color-scheme: light)";
                builder.Append("<link rel=\"stylesheet\" href=\"").Append(Lib(css)).Append("\" media=\"").Append(media).Append("\">\n");
            }

            builder.Append("<script src=\"").Append(Lib(highlight.Script)).Append("\"></script>\n");
        }

        builder.Append("<style>html,body{margin:0}pre{margin:0;padding:16px;white-space:pre-wrap;word-break:break-word;font:13px/1.55 ui-monospace,'Cascadia Code',Consolas,monospace}pre code.hljs{padding:0;background:transparent}</style>\n");
        var lang = HighlightLanguage(language);
        builder.Append("</head>\n<body>\n<pre><code").Append(lang is null ? "" : " class=\"language-" + lang + "\"").Append('>')
            .Append(WebUtility.HtmlEncode(code)).Append("</code></pre>\n");
        builder.Append("<script src=\"").Append(Lib("nth/code-view.js")).Append("\"></script>\n</body>\n</html>\n");
        return builder.ToString();
    }

    /// <summary>The page of a rendered Markdown document (its code blocks highlighted, light or dark with the system).</summary>
    public static string Document(string bodyHtml, string title, ArtifactLibraryCatalog catalog)
    {
        var builder = Shell(title, catalog);
        if (catalog.ById("highlight.js") is { } highlight)
        {
            foreach (var css in highlight.Css)
            {
                var media = css.Contains("dark", StringComparison.Ordinal) ? "(prefers-color-scheme: dark)" : "(prefers-color-scheme: light)";
                builder.Append("<link rel=\"stylesheet\" href=\"").Append(Lib(css)).Append("\" media=\"").Append(media).Append("\">\n");
            }

            builder.Append("<script src=\"").Append(Lib(highlight.Script)).Append("\"></script>\n");
        }

        builder.Append("<style>").Append(DocumentStyle).Append("</style>\n</head>\n<body>\n<article>\n").Append(bodyHtml).Append("</article>\n");
        builder.Append("<script src=\"").Append(Lib("nth/code-view.js")).Append("\"></script>\n</body>\n</html>\n");
        return builder.ToString();
    }

    /// <summary>The reading style of Markdown documents (system fonts, a calm measure, both appearances).</summary>
    public const string DocumentStyle =
        ":root{color-scheme:light dark;--fg:#1d1d1f;--muted:#6e6e73;--line:#d2d2d7;--code:#f5f5f7}" +
        "@media (prefers-color-scheme: dark){:root{--fg:#f5f5f7;--muted:#a1a1a6;--line:#424245;--code:#1c1c1e}}" +
        "html,body{margin:0;color:var(--fg);background:Canvas}" +
        "article{max-width:46rem;margin:0 auto;padding:32px 24px 64px;font:16px/1.6 'Segoe UI Variable Text','Segoe UI',system-ui,sans-serif}" +
        "h1,h2,h3,h4,h5,h6{line-height:1.25;margin:1.6em 0 .6em}h1{font-size:2em}h2{font-size:1.5em}h3{font-size:1.25em}" +
        "a{color:LinkText}code{font:0.9em ui-monospace,'Cascadia Code',Consolas,monospace;background:var(--code);padding:.1em .3em;border-radius:4px}" +
        "pre{background:var(--code);padding:12px 14px;border-radius:8px;overflow:auto}pre code{padding:0;background:transparent}" +
        "blockquote{margin:1em 0;padding:0 1em;color:var(--muted);border-left:3px solid var(--line)}" +
        "table{border-collapse:collapse;margin:1em 0}th,td{border:1px solid var(--line);padding:6px 10px;text-align:left}" +
        "hr{border:0;border-top:1px solid var(--line);margin:2em 0}img{max-width:100%}";

    [GeneratedRegex(@"<script\b(?<attrs>[^>]*)>\s*</script\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    internal static partial Regex EmptyScript();

    [GeneratedRegex(@"\b(?<name>src|type|href|rel|media)\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)'|(?<value>[^\s>]+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TagAttribute();

    /// <summary>A tag's src, type, href and rel attributes, by lower-case name (decoded; the first of each).</summary>
    internal static Dictionary<string, string> Attributes(string attributes)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in TagAttribute().Matches(attributes))
            found.TryAdd(match.Groups["name"].Value.ToLowerInvariant(), WebUtility.HtmlDecode(match.Groups["value"].Value).Trim());
        return found;
    }

    /// <summary>
    /// A project's HTML page. Scripts that load JSX, TSX or TypeScript files go to the project runner, which compiles the
    /// module graph in the sandbox; everything else runs as written, served from the artifact's own site at its path.
    /// With <paramref name="allModules"/> (a standalone export, which has no site) every local module script goes to the
    /// runner too, so relative imports still resolve.
    /// </summary>
    public static string ProjectHtml(string html, string path, IReadOnlyDictionary<string, string> files, ArtifactLibraryCatalog catalog, List<ArtifactCdnRewrite>? rewrites = null, bool allModules = false)
    {
        var entries = new List<string>();
        var folder = Folder(path);
        var rewritten = EmptyScript().Replace(html, match =>
        {
            var attributes = Attributes(match.Groups["attrs"].Value);
            if (!attributes.TryGetValue("src", out var src) || ProjectPath(folder, src) is not { } local || !files.ContainsKey(local)) return match.Value;
            var extension = Path.GetExtension(local).ToLowerInvariant();
            var compiled = extension is ".jsx" or ".tsx" or ".ts" or ".mts";
            var module = allModules && attributes.GetValueOrDefault("type")?.ToLowerInvariant() == "module" && extension is ".js" or ".mjs";
            if (!compiled && !module) return match.Value;
            entries.Add(local);
            return "";
        });
        var page = Html(rewritten, catalog, rewrites);
        if (entries.Count == 0) return page;
        var runner = new StringBuilder();
        AppendCompiler(runner, catalog);
        runner.Append("<script type=\"application/json\" id=\"").Append(FilesElement).Append("\">")
            .Append(ScriptJson(new { files = Rewritten(files, catalog, rewrites), entries, mount = false })).Append("</script>\n");
        runner.Append("<script src=\"").Append(Lib("nth/project-runner.js")).Append("\"></script>\n");
        return InjectBodyEnd(page, runner.ToString());
    }

    /// <summary>A project whose entry is a component file (no HTML page): a shell that runs and renders it.</summary>
    public static string ProjectComponent(string entry, IReadOnlyDictionary<string, string> files, string title, ArtifactLibraryCatalog catalog, List<ArtifactCdnRewrite>? rewrites = null)
    {
        var builder = Shell(title, catalog);
        if (catalog.ById("tailwindcss") is { } tailwind) builder.Append("<script src=\"").Append(Lib(tailwind.Script)).Append("\"></script>\n");
        builder.Append("<style>html,body{margin:0}#root{min-height:100vh}</style>\n");
        AppendCompiler(builder, catalog);
        builder.Append("</head>\n<body>\n<div id=\"root\"></div>\n");
        builder.Append("<script type=\"application/json\" id=\"").Append(FilesElement).Append("\">")
            .Append(ScriptJson(new { files = Rewritten(files, catalog, rewrites), entries = new[] { entry }, mount = true })).Append("</script>\n");
        builder.Append("<script src=\"").Append(Lib("nth/project-runner.js")).Append("\"></script>\n</body>\n</html>\n");
        return builder.ToString();
    }

    /// <summary>The folder of a project path ("" at the root).</summary>
    public static string Folder(string path) => path.Contains('/', StringComparison.Ordinal) ? path[..path.LastIndexOf('/')] : "";

    /// <summary>A project path from a page's folder and a script address ("./main.tsx", "/src/main.tsx", "src/main.tsx").</summary>
    public static string? ProjectPath(string folder, string address)
    {
        if (address.Contains("://", StringComparison.Ordinal) || address.StartsWith("//", StringComparison.Ordinal)) return null;
        var start = address.StartsWith('/') ? "" : folder;
        var parts = new List<string>();
        foreach (var part in (start.Length > 0 ? start + "/" : "").Split('/').Concat(Uri.UnescapeDataString(address.Split('?', '#')[0]).Split('/')))
        {
            if (part.Length == 0 || part == ".") continue;
            if (part == "..")
            {
                if (parts.Count == 0) return null;
                parts.RemoveAt(parts.Count - 1);
            }
            else parts.Add(part);
        }

        return parts.Count == 0 ? null : string.Join('/', parts);
    }

    [GeneratedRegex(@"</body\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.RightToLeft)]
    private static partial Regex BodyEnd();

    /// <summary>
    /// Puts <paramref name="injection"/> first in the head the browser's HTML parser really opens (ED-NEW-web-sec-rev-3): right after a
    /// head start tag that opens it, or else just before the first thing that makes the parser open the head itself (any other element,
    /// text, or the end of the page). Only what the parser lets come before the head is walked over: white space, comments (closed the
    /// ways HTML closes them: "--&gt;", "--!&gt;", and "&lt;!--&gt;" or "&lt;!---&gt;" whole), bogus comments ("&lt;!...&gt;", which is
    /// how HTML reads a CDATA section here, "&lt;?...&gt;" and "&lt;/ ...&gt;"), the doctype, the html start tag, and the end tags the
    /// parser ignores there. A tag is read with its attributes, so a quoted "&gt;" or "&lt;head&gt;" never counts, a longer name such as
    /// head-x is another element, and case does not matter. A comment or tag the page leaves open at its end is not walked over, so the
    /// injection never lands inside it. A leading byte order mark stays first.
    /// </summary>
    public static string InjectHead(string html, string injection) => html.Insert(HeadInsertionPoint(html), injection);

    private enum BeforeHead { Skip, Head, Stop }

    private static int HeadInsertionPoint(string html)
    {
        var position = html.Length > 0 && html[0] == '﻿' ? 1 : 0;
        var after = position; // the end of the last thing walked over that is not white space
        while (position < html.Length)
        {
            if (IsHtmlSpace(html[position]))
            {
                position++;
                continue;
            }

            if (html[position] != '<') return after; // text
            var (kind, end) = ReadBeforeHead(html, position);
            switch (kind)
            {
                case BeforeHead.Head:
                    return end;
                case BeforeHead.Stop:
                    return after;
                default:
                    position = after = end;
                    break;
            }
        }

        return after;
    }

    // One token at a '<', read as the tokenizer reads it in the modes before the head ("initial", "before html", "before head").
    private static (BeforeHead Kind, int End) ReadBeforeHead(string html, int start)
    {
        var next = start + 1;
        if (next >= html.Length) return (BeforeHead.Stop, start); // a lone '<' is text
        var c = html[next];
        if (c == '!')
        {
            if (string.CompareOrdinal(html, next + 1, "--", 0, 2) == 0) return CommentEnd(html, start, next + 3);
            return ToFirstClose(html, start, next + 1); // the doctype, or a bogus comment ("<![CDATA[" included)
        }

        if (c == '?') return ToFirstClose(html, start, next + 1); // a bogus comment
        if (c == '/')
        {
            if (next + 1 >= html.Length) return (BeforeHead.Stop, start); // "</" at the end is text
            var first = html[next + 1];
            if (first == '>') return (BeforeHead.Skip, next + 2); // "</>" is dropped
            if (!char.IsAsciiLetter(first)) return ToFirstClose(html, start, next + 1); // a bogus comment
            var (endName, endEnd) = ReadTag(html, next + 1);
            // head, body, html and br end tags make the parser open the head; it ignores the others before the head.
            return endEnd < 0 || endName is "head" or "body" or "html" or "br" ? (BeforeHead.Stop, start) : (BeforeHead.Skip, endEnd);
        }

        if (!char.IsAsciiLetter(c)) return (BeforeHead.Stop, start); // '<' before anything else is text
        var (name, tagEnd) = ReadTag(html, next);
        if (tagEnd < 0) return (BeforeHead.Stop, start);
        return name switch
        {
            "html" => (BeforeHead.Skip, tagEnd),
            "head" => (BeforeHead.Head, tagEnd),
            _ => (BeforeHead.Stop, start),
        };
    }

    // A doctype or bogus comment ends at its first '>'; one the page leaves open is dropped by the parser, so it is not walked over.
    private static (BeforeHead Kind, int End) ToFirstClose(string html, int start, int from)
    {
        var close = html.IndexOf('>', from);
        return close < 0 ? (BeforeHead.Stop, start) : (BeforeHead.Skip, close + 1);
    }

    // A comment's end: "<!-->" and "<!--->" are whole comments; otherwise the first "-->" or "--!>" after "<!--" ends it.
    private static (BeforeHead Kind, int End) CommentEnd(string html, int start, int body)
    {
        if (body < html.Length && html[body] == '>') return (BeforeHead.Skip, body + 1);
        if (body + 1 < html.Length && html[body] == '-' && html[body + 1] == '>') return (BeforeHead.Skip, body + 2);
        var dashes = html.IndexOf("-->", body, StringComparison.Ordinal);
        var bang = html.IndexOf("--!>", body, StringComparison.Ordinal);
        if (dashes < 0 && bang < 0) return (BeforeHead.Stop, start);
        return dashes >= 0 && (bang < 0 || dashes < bang) ? (BeforeHead.Skip, dashes + 3) : (BeforeHead.Skip, bang + 4);
    }

    // A start or end tag from its name's first letter: its lowercase name and the index after its '>', or -1 when the page ends inside
    // it. Attribute values are read as the tokenizer reads them (double-quoted, single-quoted or bare), so a quoted '>' stays inside.
    private static (string Name, int End) ReadTag(string html, int index)
    {
        var length = html.Length;
        var nameStart = index;
        while (index < length && !IsHtmlSpace(html[index]) && html[index] != '/' && html[index] != '>') index++;
        var name = AsciiLower(html[nameStart..index]);
        while (true)
        {
            while (index < length && (IsHtmlSpace(html[index]) || html[index] == '/')) index++; // before an attribute name
            if (index >= length) return (name, -1);
            if (html[index] == '>') return (name, index + 1);
            index++; // an attribute name's first character, '=' included
            while (index < length && !IsHtmlSpace(html[index]) && html[index] is not ('/' or '>' or '=')) index++;
            while (index < length && IsHtmlSpace(html[index])) index++;
            if (index >= length) return (name, -1);
            if (html[index] != '=') continue; // '/', '>' or the next attribute
            index++;
            while (index < length && IsHtmlSpace(html[index])) index++;
            if (index >= length) return (name, -1);
            var quote = html[index];
            if (quote is '"' or '\'')
            {
                var close = html.IndexOf(quote, index + 1);
                if (close < 0) return (name, -1);
                index = close + 1;
            }
            else if (quote == '>')
            {
                return (name, index + 1);
            }
            else
            {
                while (index < length && !IsHtmlSpace(html[index]) && html[index] != '>') index++;
            }
        }
    }

    // White space as HTML's tokenizer knows it: tab, line feed, form feed, carriage return and space.
    private static bool IsHtmlSpace(char c) => c is '\t' or '\n' or '\f' or '\r' or ' ';

    private static string AsciiLower(string text) => string.Create(text.Length, text, static (span, source) =>
    {
        for (var i = 0; i < source.Length; i++) span[i] = source[i] is >= 'A' and <= 'Z' ? (char)(source[i] + 32) : source[i];
    });

    /// <summary>Puts <paramref name="injection"/> just before the last body end tag, or at the end.</summary>
    public static string InjectBodyEnd(string html, string injection)
    {
        var match = BodyEnd().Match(html);
        return match.Success ? html.Insert(match.Index, injection) : html + injection;
    }

    /// <summary>JSON that is safe inside a script element (every &lt;, &gt; and &amp; escaped).</summary>
    public static string ScriptJson(object value) => JsonSerializer.Serialize(value, ScriptJsonOptions);

    private static readonly JsonSerializerOptions ScriptJsonOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Default };

    private static StringBuilder Shell(string title, ArtifactLibraryCatalog catalog) =>
        new StringBuilder("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n<meta name=\"color-scheme\" content=\"light dark\">\n")
            .Append("<title>").Append(WebUtility.HtmlEncode(title)).Append("</title>\n")
            .Append(ImportMap(catalog)).Append('\n');

    private static void AppendCompiler(StringBuilder builder, ArtifactLibraryCatalog catalog)
    {
        if (catalog.ById("sucrase") is { } sucrase) builder.Append("<script src=\"").Append(Lib(sucrase.Script)).Append("\"></script>\n");
        builder.Append("<script src=\"").Append(Lib("nth/loader.js")).Append("\"></script>\n");
    }

    private static Dictionary<string, string> Rewritten(IReadOnlyDictionary<string, string> files, ArtifactLibraryCatalog catalog, List<ArtifactCdnRewrite>? rewrites) =>
        files.ToDictionary(pair => pair.Key, pair => IsRewritable(pair.Key) ? catalog.RewriteCdn(pair.Value, rewrites) : pair.Value, StringComparer.Ordinal);

    /// <summary>Text files whose CDN addresses are rewritten when served (pages, scripts and style sheets).</summary>
    public static bool IsRewritable(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".html" or ".htm" or ".js" or ".mjs" or ".cjs" or ".jsx" or ".tsx" or ".ts" or ".css";

    /// <summary>The highlight.js class for a language (null leaves detection to highlight.js).</summary>
    public static string? HighlightLanguage(string? language)
    {
        var name = language?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(name)) return null;
        name = name switch { "c#" => "csharp", "c++" => "cpp", "sh" or "shell" or "zsh" => "bash", "ps1" or "pwsh" => "powershell", "yml" => "yaml", "md" => "markdown", "jsx" => "javascript", "tsx" => "typescript", _ => name };
        return name.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_') ? name : null;
    }
}
