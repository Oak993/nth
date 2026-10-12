using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Marrow.Core.Models;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces W13 (DESIGN.md 6.1 "Download as .zip ... next to the standalone single-file HTML (everything
// inlined)", 6.3 Export; owner pick 5: export is the way to share). The standalone page needs nothing from nth or the
// network; a project exports as its folder; a Markdown document as a Word file; PDF and PNG come from the rendered view.
[Trait("Decision", "ED-NEW-web-w13-5")]
public sealed partial class ArtifactExporterTests
{
    [GeneratedRegex(@"\b(?:src|href)\s*=\s*[""']https://lib-nth\.invalid", RegexOptions.IgnoreCase)]
    private static partial Regex LibraryReference();

    private static string Html(ArtifactRuntimeRig rig, ArtifactSnapshot snapshot) =>
        Encoding.UTF8.GetString(rig.Runtime.Exporter.Export(snapshot, ArtifactExportFormat.Html));

    [Fact]
    public void A_standalone_html_page_inlines_its_libraries_and_keeps_the_sandboxes_rules()
    {
        using var rig = new ArtifactRuntimeRig();
        var page = "<!doctype html><html><head><script src=\"https://unpkg.com/react@18/umd/react.production.min.js\"></script></head>"
            + "<body><div id=\"chart\"></div><script type=\"module\">import { scaleLinear } from \"d3\"; document.body.dataset.x = scaleLinear()(1);</script></body></html>";
        var html = Html(rig, rig.Create(ArtifactKind.Html, page));

        Assert.DoesNotMatch(LibraryReference(), html);
        Assert.DoesNotContain("unpkg.com", html, StringComparison.Ordinal);
        Assert.Contains("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src &#39;none&#39;; script-src &#39;unsafe-inline&#39; data:;", html, StringComparison.Ordinal);
        Assert.Contains("connect-src &#39;none&#39;", html, StringComparison.Ordinal);
        Assert.Contains("works only inside nth", html, StringComparison.Ordinal); // the offline window.nth
        Assert.Contains("\"d3\":\"data:text/javascript;base64,", html, StringComparison.Ordinal);
        Assert.Contains("import { scaleLinear } from \"d3\"", html, StringComparison.Ordinal);
        Assert.True(html.IndexOf("<script type=\"importmap\">", StringComparison.Ordinal) < html.IndexOf("<script type=\"module\">", StringComparison.Ordinal));
        Assert.Single(Regex.Matches(html, "<script type=\"importmap\">"));
        Assert.True(html.Length > 200_000); // React and D3 travel inside the file
    }

    [Fact]
    public void A_react_component_exports_with_its_runner_and_the_libraries_it_imports()
    {
        using var rig = new ArtifactRuntimeRig();
        var code = "import { LineChart, Line } from 'recharts';\nimport { Sun } from 'lucide-react';\nexport default function App() { return <div><Sun /><LineChart width={200} height={100} data={[{v:1}]}><Line dataKey=\"v\" /></LineChart></div>; }";
        var html = Html(rig, rig.Create(ArtifactKind.React, code, "Chart"));

        Assert.DoesNotMatch(LibraryReference(), html);
        foreach (var library in new[] { "react", "react-dom", "prop-types", "recharts", "lucide-react", "sucrase", "tailwindcss" })
        {
            var script = ArtifactRuntimeRig.Libraries.Catalog.ById(library)!.Script;
            var head = Encoding.UTF8.GetString(ArtifactRuntimeRig.Libraries.Read(script)!).AsSpan(0, 200).ToString();
            Assert.Contains(head.Replace("</script", "<\\/script", StringComparison.OrdinalIgnoreCase), html, StringComparison.Ordinal);
        }

        Assert.Contains("globalThis.react = globalThis.React;", html, StringComparison.Ordinal); // Lucide React's prelude
        Assert.Contains("id=\"nth-source\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_project_exports_as_its_folder_and_as_one_page()
    {
        using var rig = new ArtifactRuntimeRig();
        var snapshot = rig.CreateProject("index.html",
            ("index.html", "<html><head><link rel=\"stylesheet\" href=\"css/site.css\"></head><body><img src=\"logo.svg\"><script type=\"module\" src=\"app.js\"></script></body></html>"),
            ("app.js", "import { greet } from './lib/greet.js';\ndocument.body.append(greet('Orbit'));"),
            ("lib/greet.js", "export const greet = (name) => 'Hello ' + name;"),
            ("css/site.css", "body { margin: 0 }"),
            ("logo.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"));

        using (var zip = new ZipArchive(new MemoryStream(rig.Runtime.Exporter.Export(snapshot, ArtifactExportFormat.Zip))))
        {
            Assert.Equal(["app.js", "css/site.css", "index.html", "lib/greet.js", "logo.svg"], zip.Entries.Select(e => e.FullName));
            using var reader = new StreamReader(zip.GetEntry("lib/greet.js")!.Open());
            Assert.Equal("export const greet = (name) => 'Hello ' + name;", reader.ReadToEnd());
        }

        Assert.Equal(rig.Runtime.Exporter.Export(snapshot, ArtifactExportFormat.Zip), rig.Runtime.Exporter.Export(snapshot, ArtifactExportFormat.Source));
        var html = Html(rig, snapshot);
        Assert.DoesNotMatch(LibraryReference(), html);
        Assert.Contains("<style>body { margin: 0 }</style>", html, StringComparison.Ordinal);
        Assert.Contains("src=\"data:image/svg+xml;base64,", html, StringComparison.Ordinal);
        Assert.DoesNotContain("src=\"app.js\"", html, StringComparison.Ordinal);
        Assert.Contains("\"entries\":[\"app.js\"]", html, StringComparison.Ordinal); // the runner resolves ./lib/greet.js offline
    }

    [Fact]
    public void A_markdown_document_exports_as_a_word_file()
    {
        using var rig = new ArtifactRuntimeRig();
        var snapshot = rig.Create(ArtifactKind.Markdown, "# Release notes\n\nOrbit **2.0** ships.\n\n1. Install\n2. Sign in\n\n[guide](https://example.com/guide)", "Notes");
        var bytes = rig.Runtime.Exporter.Export(snapshot, ArtifactExportFormat.Docx);

        using var zip = new ZipArchive(new MemoryStream(bytes));
        Assert.Equal(["[Content_Types].xml", "_rels/.rels", "docProps/core.xml", "word/document.xml", "word/styles.xml", "word/numbering.xml", "word/_rels/document.xml.rels"], zip.Entries.Select(e => e.FullName));
        string Part(string name)
        {
            using var reader = new StreamReader(zip.GetEntry(name)!.Open());
            return reader.ReadToEnd();
        }

        var document = Part("word/document.xml");
        Assert.Contains("<w:pStyle w:val=\"Heading1\" />", document.Replace("\"/>", "\" />", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("Release notes", document, StringComparison.Ordinal);
        Assert.Contains("<w:b />", document.Replace("<w:b/>", "<w:b />", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("w:numId", document, StringComparison.Ordinal);
        Assert.Contains("https://example.com/guide", Part("word/_rels/document.xml.rels"), StringComparison.Ordinal);
        Assert.Contains("<dc:title>Notes</dc:title>", Part("docProps/core.xml"), StringComparison.Ordinal);
        Assert.Contains("<h1>Release notes</h1>", Html(rig, snapshot), StringComparison.Ordinal);
        Assert.Equal(snapshot.Text, Encoding.UTF8.GetString(rig.Runtime.Exporter.Export(snapshot, ArtifactExportFormat.Markdown)));
    }

    [Fact]
    public async Task Pdf_png_and_a_diagrams_svg_come_from_the_rendered_view()
    {
        using var rig = new ArtifactRuntimeRig();
        var snapshot = rig.Create(ArtifactKind.Html, "<p>x</p>");
        var (_, view) = rig.Mount(snapshot);
        var folder = Path.Combine(rig.Threads, "..", "exports");
        Directory.CreateDirectory(folder);

        Assert.True(WebRig.Done(rig.Runtime.Exporter.ExportAsync(snapshot, ArtifactExportFormat.Pdf, Path.Combine(folder, "a.pdf"), view, CancellationToken.None)));
        Assert.Contains("PrintToPdf " + Path.Combine(folder, "a.pdf"), view.Log);
        Assert.True(WebRig.Done(rig.Runtime.Exporter.ExportAsync(snapshot, ArtifactExportFormat.Png, Path.Combine(folder, "a.png"), view, CancellationToken.None)));
        Assert.Equal(FakeWebEngine.OnePixelPng, File.ReadAllBytes(Path.Combine(folder, "a.png")));
        Assert.True(ArtifactExporter.NeedsView(ArtifactKind.Mermaid, ArtifactExportFormat.Svg));
        Assert.False(ArtifactExporter.NeedsView(ArtifactKind.Svg, ArtifactExportFormat.Svg));
        Assert.Throws<InvalidOperationException>(() => rig.Runtime.Exporter.Export(snapshot, ArtifactExportFormat.Pdf));
        await Assert.ThrowsAsync<ArgumentException>(() => rig.Runtime.Exporter.ExportAsync(snapshot, ArtifactExportFormat.Docx, Path.Combine(folder, "a.docx"), view, CancellationToken.None));
    }

    [Fact]
    public void An_export_keeps_the_versions_network_grant_and_nothing_more()
    {
        Assert.Contains("connect-src https://api.open-meteo.com;", ArtifactExporter.ExportCsp(["api.open-meteo.com"]), StringComparison.Ordinal);
        Assert.Contains("img-src data: blob: https://api.open-meteo.com;", ArtifactExporter.ExportCsp(["api.open-meteo.com"]), StringComparison.Ordinal);
        Assert.Contains("connect-src 'none';", ArtifactExporter.ExportCsp([]), StringComparison.Ordinal);
        Assert.Contains("form-action 'none'", ArtifactExporter.ExportCsp([]), StringComparison.Ordinal);
    }
}
