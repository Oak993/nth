using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// W16c Native (DESIGN 13.4, run alone and with focus by the lead at the native pass, never in the ordinary filters): the real
// Mermaid in the real engine. Edge's own headless mode (no window) loads the renderer's page with the vendored Mermaid and nth's
// runner and draws one diagram of every family through window.nthRenderDiagram, so what the hidden renderer will hand to the live
// view is checked as the engine really produces it: well-formed SVG with every style written into the elements, no style block,
// no foreignObject, no classes, labels as text. Each drawing is saved under tests/Marrow.UI.Tests/Fixtures/mermaid-recorded
// (light and dark) as a recorded fixture, which the ordinary sanitiser and painter tests then read (NthSvgRecordedMermaidTests).
// The hosting checks that need nth's window (a real WebView2 inside the seat, cold start time, a hidden controller, idle close)
// are W21's, by hand: ED-NEW-web-w16c-1 lists them.
[Trait("Decision", "ED-NEW-web-w16c-1")]
[Trait("Feature", "W-16c")]
public sealed class MermaidRunnerNativeTests
{
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Marrow.sln"))) return dir.FullName;
        }
        throw new InvalidOperationException("Marrow.sln was not found above the test binaries.");
    }

    private static string Edge()
    {
        foreach (var path in new[]
        {
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
        })
        {
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Microsoft Edge is not installed where the runtime installs it.");
    }

    private static Dictionary<string, string> Variables(bool dark) => new()
    {
        ["primaryColor"] = dark ? "#2a2a2d" : "#ffffff",
        ["primaryTextColor"] = dark ? "#ececee" : "#1c1c1e",
        ["primaryBorderColor"] = dark ? "#8e8e93" : "#6f6f72",
        ["lineColor"] = dark ? "#b5b5b9" : "#5f5f61",
        ["textColor"] = dark ? "#ececee" : "#1c1c1e",
        ["background"] = dark ? "#161618" : "#eaeaec",
        ["fontFamily"] = "Segoe UI Variable, Segoe UI, sans-serif",
    };

    [Fact]
    public async Task Real_mermaid_draws_every_family_through_the_runner_as_flat_well_formed_svg()
    {
        var work = Path.Combine(Path.GetTempPath(), "nth-mermaid-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var library = Path.Combine(ArtifactLibraryFiles.DefaultFolder, "mermaid", "mermaid.min.js");
            Assert.True(File.Exists(library), library);
            var site = new MermaidRenderSite(ArtifactRuntimeRig.Libraries);
            var requests = MermaidSamples.All.SelectMany(s => new[] { false, true }.Select(dark => new { s.Name, s.Source, dark })).Append(new { Name = "broken", Source = "graph LR\n  A -->", dark = false }).ToList();
            var json = JsonSerializer.Serialize(requests.Select(r => new { name = r.Name + (r.dark ? "-dark" : "-light"), source = r.Source, dark = r.dark, variables = Variables(r.dark) }));
            var harness = "<script>(async () => { const samples = JSON.parse(atob('" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json)) + "'));"
                + " const out = []; for (const s of samples) { let result; try { result = await window.nthRenderDiagram(s.source, { dark: s.dark, fontFamily: 'Segoe UI Variable, Segoe UI, sans-serif', fontSize: 14, variables: s.variables }); }"
                + " catch (e) { result = { ok: false, kind: 'threw', error: String(e) }; } out.push({ name: s.name, result }); }"
                + " const pre = document.createElement('pre'); pre.id = 'nth-out'; pre.textContent = JSON.stringify({ ready: window.nthDiagramReady, out }); document.body.appendChild(pre); })();</script>\n</body>";
            var page = site.Page().Replace("https://lib-nth.invalid/mermaid/mermaid.min.js", new Uri(library).AbsoluteUri, StringComparison.Ordinal).Replace("</body>", harness, StringComparison.Ordinal);
            var file = Path.Combine(work, "page.html");
            await File.WriteAllTextAsync(file, page);

            var start = new ProcessStartInfo(Edge())
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in new[] { "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--user-data-dir=" + Path.Combine(work, "profile"), "--virtual-time-budget=90000", "--dump-dom", new Uri(file).AbsoluteUri })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Edge did not start.");
            var output = await process.StandardOutput.ReadToEndAsync();
            Assert.True(process.WaitForExit(120_000), "Edge did not finish in two minutes");

            var match = Regex.Match(output, "<pre id=\"nth-out\">(.*?)</pre>", RegexOptions.Singleline);
            Assert.True(match.Success, "the runner wrote nothing; the page said: " + output[..Math.Min(output.Length, 600)]);
            using var document = JsonDocument.Parse(WebUtility.HtmlDecode(match.Groups[1].Value));
            Assert.True(document.RootElement.GetProperty("ready").GetBoolean(), "Mermaid did not load");
            var recorded = Path.Combine(RepoRoot(), "tests", "Marrow.UI.Tests", "Fixtures", "mermaid-recorded");
            Directory.CreateDirectory(recorded);
            var failures = new List<string>();
            foreach (var item in document.RootElement.GetProperty("out").EnumerateArray())
            {
                var name = item.GetProperty("name").GetString()!;
                var result = item.GetProperty("result");
                if (name.StartsWith("broken", StringComparison.Ordinal))
                {
                    Assert.False(result.GetProperty("ok").GetBoolean(), "a broken source must not draw");
                    Assert.Equal("syntax", result.GetProperty("kind").GetString());
                    continue;
                }
                if (!result.GetProperty("ok").GetBoolean())
                {
                    failures.Add(name + ": " + result.GetProperty("error").GetString());
                    continue;
                }
                var svg = result.GetProperty("svg").GetString()!;
                await File.WriteAllTextAsync(Path.Combine(recorded, name + ".svg"), svg);
                var xml = XDocument.Parse(svg);
                var all = xml.Descendants().ToList();
                if (all.Any(e => e.Name.LocalName is "style" or "script" or "foreignObject")) failures.Add(name + ": a style, script or foreignObject element is left");
                if (all.Any(e => e.Attribute("class") is not null || e.Attribute("style") is not null)) failures.Add(name + ": a style attribute or CSS classes are left");
                if (xml.Root?.Attribute("viewBox") is null) failures.Add(name + ": no viewBox");
                var shapes = all.Count(e => e.Name.LocalName is "rect" or "path" or "circle" or "ellipse" or "polygon" or "line" or "polyline");
                if (shapes < 2) failures.Add(name + ": only " + shapes + " shapes");
                var unpainted = all.Count(e => e.Name.LocalName is "rect" or "path" or "circle" && e.Attribute("fill") is null);
                if (unpainted > 0) failures.Add(name + ": " + unpainted + " shapes have no fill attribute");
                var text = string.Concat(all.Where(e => e.Name.LocalName is "text" or "tspan").Select(e => e.Value));
                var sample = MermaidSamples.All.First(s => s.Name == name[..name.LastIndexOf('-')]);
                foreach (var label in sample.Labels)
                {
                    if (!text.Contains(label, StringComparison.Ordinal)) failures.Add(name + ": the label \"" + label + "\" is not drawn as text");
                }
            }
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (IOException)
            {
                // Edge may hold its profile folder for a moment.
            }
        }
    }
}
