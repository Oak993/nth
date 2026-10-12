// PORT-ONLY: web surfaces W13 (DESIGN.md 6.1 "Export adds Download as .zip (the folder) next to the standalone
// single-file HTML (everything inlined)", 6.3 Export, 6.5 Libraries; owner pick 5: export is the way to share;
// ED-NEW-web-w13-5). Every export format of every kind:
//   html    one standalone file: the page as Tabula serves it with every library, style sheet, font and runtime file
//           inlined, the import map's modules as data addresses, an offline window.nth, and a CSP that keeps the
//           artifact's own network rules wherever the file is opened;
//   zip     a project's folder; source the version's own file; md and svg the text;
//   docx    a Markdown document through the Open XML writer;
//   pdf/png the rendered view (print to PDF, a captured frame); a Mermaid diagram's svg from the rendered view.
// No Swift counterpart.
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Web;

namespace Marrow.Services.Web;

/// <param name="network">The sites a version may reach in nth (its grant); an export keeps exactly those.</param>
public sealed partial class ArtifactExporter(ArtifactLibraryFiles libraries, Func<ArtifactSnapshot, IReadOnlyList<string>>? network = null)
{
    private ArtifactLibraryCatalog Catalog => libraries.Catalog;

    /// <summary>True when the format needs the rendered view (print, capture, or Mermaid's rendered SVG).</summary>
    public static bool NeedsView(ArtifactKind kind, ArtifactExportFormat format) =>
        format is ArtifactExportFormat.Pdf or ArtifactExportFormat.Png || (kind == ArtifactKind.Mermaid && format == ArtifactExportFormat.Svg);

    /// <summary>Writes an export to <paramref name="path"/>; formats that need the view use <paramref name="view"/>.</summary>
    public async Task<bool> ExportAsync(ArtifactSnapshot snapshot, ArtifactExportFormat format, string path, IWebEngineView? view, CancellationToken cancellationToken)
    {
        if (!ArtifactExport.Formats(snapshot.Artifact.Kind).Contains(format))
            throw new ArgumentException("A " + snapshot.Artifact.Kind + " artifact does not export as " + format + ".", nameof(format));
        if (!NeedsView(snapshot.Artifact.Kind, format))
        {
            FileOps.WriteAtomic(path, Export(snapshot, format));
            return true;
        }

        if (view is null) throw new InvalidOperationException("This export needs the artifact's rendered view.");
        switch (format)
        {
            case ArtifactExportFormat.Pdf:
                return await view.PrintToPdfAsync(path, cancellationToken).ConfigureAwait(true);
            case ArtifactExportFormat.Png:
                if (await view.CaptureSnapshotAsync(cancellationToken).ConfigureAwait(true) is not { } frame) return false;
                FileOps.WriteAtomic(path, frame.Png);
                return true;
            default:
                var json = await view.ExecuteScriptAsync("(() => { const svg = document.querySelector('#diagram svg'); return svg ? new XMLSerializer().serializeToString(svg) : null; })()", cancellationToken).ConfigureAwait(true);
                if (JsonSerializer.Deserialize<string?>(json) is not { Length: > 0 } svg) return false;
                FileOps.WriteAtomic(path, Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + svg));
                return true;
        }
    }

    /// <summary>The bytes of an export that needs no view.</summary>
    public byte[] Export(ArtifactSnapshot snapshot, ArtifactExportFormat format)
    {
        var kind = snapshot.Artifact.Kind;
        if (NeedsView(kind, format)) throw new InvalidOperationException("This export needs the artifact's rendered view.");
        return format switch
        {
            ArtifactExportFormat.Html => Encoding.UTF8.GetBytes(StandaloneHtml(snapshot)),
            ArtifactExportFormat.Zip => Zip(snapshot),
            ArtifactExportFormat.Source when snapshot.IsProject => Zip(snapshot),
            ArtifactExportFormat.Docx => ArtifactDocx.Write(ArtifactMarkdown.Parse(snapshot.Text), snapshot.Artifact.Title),
            ArtifactExportFormat.Source or ArtifactExportFormat.Markdown or ArtifactExportFormat.Svg => Encoding.UTF8.GetBytes(snapshot.Text ?? ""),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }

    /// <summary>A project's folder as a .zip (its files at their paths).</summary>
    public static byte[] Zip(ArtifactSnapshot snapshot)
    {
        var files = snapshot.Files ?? throw new InvalidOperationException("Only a project exports as a folder.");
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, text) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
            {
                var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                entry.LastWriteTime = snapshot.Version.Timestamp;
                using var stream = entry.Open();
                stream.Write(Encoding.UTF8.GetBytes(text));
            }
        }

        return buffer.ToArray();
    }

    // ---- the standalone page ----

    /// <summary>The standalone, single-file page of a version: everything inlined, nothing fetched.</summary>
    public string StandaloneHtml(ArtifactSnapshot snapshot)
    {
        var catalog = Catalog;
        var artifact = snapshot.Artifact;
        string page;
        var code = new List<string>();
        var react = false;
        switch (artifact.Kind)
        {
            case ArtifactKind.Project:
                var files = snapshot.Files!;
                var entry = snapshot.Version.Entry ?? ArtifactStore.DefaultEntry;
                page = ArtifactRuntime.IsHtml(entry) && files.TryGetValue(entry, out var html)
                    ? InlineProjectFiles(ArtifactPages.ProjectHtml(html, entry, files, catalog, allModules: true), entry, files)
                    : ArtifactPages.ProjectComponent(entry, files, artifact.Title, catalog);
                code.AddRange(files.Where(f => IsCode(f.Key)).Select(f => f.Value));
                react = page.Contains("nth/project-runner.js", StringComparison.Ordinal);
                break;
            case ArtifactKind.Svg:
                page = ArtifactPages.Svg(artifact.Title, catalog).Replace("src=\"" + ArtifactPages.SvgPath + "\"", "src=\"" + DataUri("image/svg+xml", Encoding.UTF8.GetBytes(snapshot.Text ?? "")) + "\"", StringComparison.Ordinal);
                break;
            default:
                page = ArtifactMount.SinglePage(snapshot, artifact.Title, catalog, null);
                if (artifact.Kind == ArtifactKind.React) { code.Add(snapshot.Text ?? ""); react = true; }
                break;
        }

        var csp = ExportCsp(network?.Invoke(snapshot) ?? []);
        return Standalone(page, code, react, csp);
    }

    /// <summary>
    /// The policy an exported page carries in a meta tag: the artifact's own rules (no network unless it declared sites,
    /// no frames, forms or workers) over inlined code and data addresses.
    /// </summary>
    public static string ExportCsp(IReadOnlyList<string> network)
    {
        var hosts = string.Concat(network.Select(h => " https://" + h.ToLowerInvariant()));
        return "default-src 'none'; script-src 'unsafe-inline' data:; style-src 'unsafe-inline'; img-src data: blob:" + hosts +
               "; font-src data:; media-src data: blob:; connect-src " + (hosts.Length > 0 ? hosts.Trim() : "'none'") +
               "; frame-src 'none'; form-action 'none'; base-uri 'none'; worker-src 'none'; manifest-src 'none'";
    }

    [GeneratedRegex(@"<script\b[^>]*\btype\s*=\s*[""']?importmap[""']?[^>]*>.*?</script\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ImportMapTag();

    [GeneratedRegex(@"<link\b(?<attrs>[^>]*)>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LinkTag();

    [GeneratedRegex(@"(?:\bfrom\s*|\bimport\s*\(?\s*|\brequire\s*\(\s*)[""'](?<spec>[^""'\s]+)[""']")]
    private static partial Regex Specifier();

    [GeneratedRegex(@"type\s*=\s*[""']?text/(?:babel|jsx|tsx)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BabelScript();

    [GeneratedRegex(@"url\(\s*(?<q>[""']?)(?<url>[^""')]+)\k<q>\s*\)")]
    private static partial Regex CssUrl();

    private string Standalone(string page, IReadOnlyList<string> code, bool react, string csp)
    {
        var catalog = Catalog;
        var tagged = new HashSet<string>(StringComparer.Ordinal);
        var needed = new HashSet<string>(StringComparer.Ordinal);
        if (react) { needed.Add("react"); needed.Add("react-dom"); }
        if (BabelScript().IsMatch(page)) needed.Add("sucrase");
        foreach (var text in code.Append(page))
            foreach (Match match in Specifier().Matches(text))
                if (LibraryOf(match.Groups["spec"].Value) is { } id) needed.Add(id);

        // Library script tags already in the page are inlined where they stand.
        page = ArtifactPages.EmptyScript().Replace(page, match =>
        {
            var attributes = ArtifactPages.Attributes(match.Groups["attrs"].Value);
            if (!attributes.TryGetValue("src", out var src) || LibraryPath(src) is not { } path) return match.Value;
            var type = attributes.TryGetValue("type", out var t) ? " type=\"" + System.Net.WebUtility.HtmlEncode(t) + "\"" : "";
            if (path.StartsWith(ArtifactLibraryCatalog.RuntimePrefix, StringComparison.Ordinal))
                return Script(Runtime(path, src), type);
            if (catalog.Libraries.FirstOrDefault(l => l.Script == path) is not { } library) return match.Value;
            tagged.Add(library.Id);
            return (library.Prelude is { Length: > 0 } prelude ? Script(prelude, "") : "") + Script(Text(path), type);
        });
        page = LinkTag().Replace(page, match =>
        {
            var attributes = ArtifactPages.Attributes(match.Groups["attrs"].Value);
            if (!attributes.TryGetValue("href", out var href) || LibraryPath(href) is not { } path || !path.EndsWith(".css", StringComparison.OrdinalIgnoreCase)) return match.Value;
            var owner = catalog.Libraries.FirstOrDefault(l => l.Css.Contains(path));
            return Style(path, owner?.Id, attributes.GetValueOrDefault("media"));
        });

        var included = catalog.LoadOrder(needed.Concat(tagged)).ToList();
        var head = new StringBuilder();
        head.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"").Append(System.Net.WebUtility.HtmlEncode(csp)).Append("\">\n");
        head.Append(Script(SandboxScripts.Read("export-shim.js"), ""));
        head.Append("<script type=\"importmap\">").Append(ImportMap(included)).Append("</script>\n");
        foreach (var library in included.Where(l => !tagged.Contains(l.Id)))
        {
            foreach (var css in library.Css)
                if (!page.Contains("data-nth-lib=\"" + library.Id + "\"", StringComparison.Ordinal)) head.Append(Style(css, library.Id));
            if (library.Prelude is { Length: > 0 } prelude) head.Append(Script(prelude, ""));
            head.Append(Script(Text(library.Script), ""));
        }

        page = ImportMapTag().Replace(page, "");
        return ArtifactPages.InjectHead(page, head.ToString());
    }

    private string? LibraryOf(string specifier)
    {
        if (specifier is "react/jsx-runtime" or "react/jsx-dev-runtime") return "react";
        if (Catalog.BySpecifier(specifier) is { } library) return library.Id;
        var bare = specifier.StartsWith('@') ? string.Join('/', specifier.Split('/').Take(2)) : specifier.Split('/')[0];
        return Catalog.BySpecifier(bare)?.Id;
    }

    private static string? LibraryPath(string url) =>
        url.StartsWith(SandboxSites.LibraryOrigin + "/", StringComparison.OrdinalIgnoreCase) ? Uri.UnescapeDataString(url[(SandboxSites.LibraryOrigin.Length + 1)..].Split('?', '#')[0]) : null;

    private string Runtime(string path, string src)
    {
        var name = path[ArtifactLibraryCatalog.RuntimePrefix.Length..];
        return name switch
        {
            "loader.js" => SandboxScripts.Loader(Catalog),
            "missing.js" => SandboxScripts.Missing(Uri.TryCreate(src, UriKind.Absolute, out var uri) ? System.Web.HttpUtility.ParseQueryString(uri.Query)["u"] ?? "" : ""),
            _ when SandboxScripts.RuntimeFiles.Contains(name) => SandboxScripts.Read(name),
            _ => "",
        };
    }

    private string Text(string path) => libraries.Read(path) is { } bytes ? Encoding.UTF8.GetString(bytes) : "";

    /// <summary>A library style sheet inline, its fonts as data addresses.</summary>
    private string Style(string path, string? library, string? media = null)
    {
        var folder = ArtifactPages.Folder(path);
        var css = CssUrl().Replace(Text(path), match =>
        {
            var url = match.Groups["url"].Value.Trim();
            if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || ArtifactPages.ProjectPath(folder, url) is not { } local || libraries.Read(local) is not { } bytes)
                return match.Value;
            return "url(" + DataUri(SandboxHeaders.ContentType(local), bytes) + ")";
        });
        if (media is { Length: > 0 } && !media.Equals("all", StringComparison.OrdinalIgnoreCase)) css = "@media " + media + " {\n" + css + "\n}";
        return "<style" + (library is null ? "" : " data-nth-lib=\"" + library + "\"") + ">" + css.Replace("</style", "<\\/style", StringComparison.OrdinalIgnoreCase) + "</style>\n";
    }

    private static string Script(string code, string typeAttribute) =>
        "<script" + typeAttribute + ">" + code.Replace("</script", "<\\/script", StringComparison.OrdinalIgnoreCase) + "</script>\n";

    /// <summary>The import map of an exported page: each included library's specifiers as a data-address module over its global.</summary>
    private static string ImportMap(IReadOnlyList<ArtifactLibrary> included)
    {
        var imports = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var library in included.Where(l => l.Global is not null))
        {
            var module = DataUri("text/javascript", Encoding.UTF8.GetBytes(Module(library)));
            foreach (var specifier in library.Specifiers) imports[specifier] = module;
            if (library.Id == "react")
            {
                var jsx = DataUri("text/javascript", Encoding.UTF8.GetBytes(JsxModule));
                imports["react/jsx-runtime"] = jsx;
                imports["react/jsx-dev-runtime"] = jsx;
            }
        }

        return ArtifactPages.ScriptJson(new { imports });
    }

    private const string JsxModule =
        "const R = globalThis.React; export const Fragment = R.Fragment;" +
        " export function jsx(t, p, k) { return R.createElement(t, k === undefined ? p : Object.assign({}, p, { key: k })); }" +
        " export const jsxs = jsx; export const jsxDEV = jsx; export default { Fragment, jsx, jsxs, jsxDEV };";

    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "break", "case", "catch", "class", "const", "continue", "debugger", "default", "delete", "do", "else", "enum", "export", "extends",
        "false", "finally", "for", "function", "if", "import", "in", "instanceof", "new", "null", "return", "super", "switch", "this",
        "throw", "true", "try", "typeof", "var", "void", "while", "with", "yield", "let", "static", "implements", "interface", "package",
        "private", "protected", "public", "await", "arguments", "eval",
    };

    [GeneratedRegex(@"^[A-Za-z_$][A-Za-z0-9_$]*$")]
    private static partial Regex Identifier();

    private static string Module(ArtifactLibrary library)
    {
        var read = "globalThis" + string.Concat(library.Global!.Split('.').Select(p => "[" + JsonSerializer.Serialize(p) + "]"));
        var builder = new StringBuilder("const value = ").Append(read).Append("; export default value;");
        if (library.ExportAs is { } name && Identifier().IsMatch(name)) builder.Append(" export const ").Append(name).Append(" = value;");
        var names = library.Exports.Where(n => Identifier().IsMatch(n) && !Reserved.Contains(n) && n != library.ExportAs).Distinct(StringComparer.Ordinal).ToList();
        if (names.Count > 0) builder.Append(" export const { ").Append(string.Join(", ", names)).Append(" } = value;");
        return builder.ToString();
    }

    /// <summary>A project page's own style sheets, classic scripts and SVG images inline (an export has no site to fetch them from).</summary>
    private static string InlineProjectFiles(string page, string entry, IReadOnlyDictionary<string, string> files)
    {
        var folder = ArtifactPages.Folder(entry);
        page = LinkTag().Replace(page, match =>
        {
            var attributes = ArtifactPages.Attributes(match.Groups["attrs"].Value);
            if (attributes.GetValueOrDefault("rel")?.ToLowerInvariant() != "stylesheet" || !attributes.TryGetValue("href", out var href)
                || ArtifactPages.ProjectPath(folder, href) is not { } local || !files.TryGetValue(local, out var css)) return match.Value;
            return "<style>" + css.Replace("</style", "<\\/style", StringComparison.OrdinalIgnoreCase) + "</style>";
        });
        page = ArtifactPages.EmptyScript().Replace(page, match =>
        {
            var attributes = ArtifactPages.Attributes(match.Groups["attrs"].Value);
            if (!attributes.TryGetValue("src", out var src) || ArtifactPages.ProjectPath(folder, src) is not { } local || !files.TryGetValue(local, out var js)) return match.Value;
            return Script(js, "");
        });
        return ImageSource().Replace(page, match =>
        {
            var local = ArtifactPages.ProjectPath(folder, System.Net.WebUtility.HtmlDecode(match.Groups["src"].Value));
            return local is not null && files.TryGetValue(local, out var svg) && local.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                ? match.Groups["before"].Value + DataUri("image/svg+xml", Encoding.UTF8.GetBytes(svg)) + match.Groups["after"].Value
                : match.Value;
        });
    }

    [GeneratedRegex(@"(?<before><img\b[^>]*?\bsrc\s*=\s*[""'])(?<src>[^""']+)(?<after>[""'])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImageSource();

    private static bool IsCode(string path) => Path.GetExtension(path).ToLowerInvariant() is ".js" or ".mjs" or ".cjs" or ".jsx" or ".ts" or ".tsx" or ".mts" or ".html" or ".htm";

    private static string DataUri(string type, byte[] bytes) => "data:" + type.Split(';')[0] + ";base64," + Convert.ToBase64String(bytes);
}
