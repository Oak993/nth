using Marrow.UI.NthUi.Diagram;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.NthUi.Diagram;

// DESIGN.md 7.2 (web surfaces W16c; ED-NEW-web-w16c-1): the SVG the real Mermaid hands over, recorded. The native pass (Marrow.Services
// .Tests MermaidRunnerNativeTests, run by the lead) draws one diagram of every family in light and dark through the renderer's runner
// and saves each result under Fixtures/mermaid-recorded. Every recorded drawing must pass the sanitiser with nothing but the
// notes the sanitiser is allowed to make, keep its text, and draw with the painter. This managed check requires
// all 26 recordings; an empty fixture folder is not acceptance (ED-NEW-web-review-25). The recordings are committed, the check only
// reads the files and starts no Edge, so it runs in the ordinary !~Native sweeps (ED-NEW-web-review-30).
[Collection("Headless")]
public sealed class NthSvgRecordedMermaidTests(HeadlessSession headless)
{
    private static IReadOnlyList<string> Recorded()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (!File.Exists(Path.Combine(dir.FullName, "Marrow.sln"))) continue;
            var folder = Path.Combine(dir.FullName, "tests", "Marrow.UI.Tests", "Fixtures", "mermaid-recorded");
            return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.svg") : [];
        }
        return [];
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Every_recorded_mermaid_drawing_passes_the_sanitiser_whole_and_draws() => headless.OnUiThread(() =>
    {
        var recorded = Recorded();
        Assert.Equal(26, recorded.Count);
        foreach (var file in recorded)
        {
            var markup = File.ReadAllText(file);
            var result = NthSvgSanitizer.Parse(markup, NthSvgLimits.Renderer);
            Assert.True(result.Ok, Path.GetFileName(file) + ": " + result.Error);
            var document = result.Document!;
            // The runner already removed styles and foreign objects; the sanitiser must not have had to.
            Assert.True(!document.Removed.Any(note => note.Contains("<style>", StringComparison.Ordinal) || note.Contains("foreignObject", StringComparison.Ordinal) || note.Contains("script", StringComparison.Ordinal)),
                Path.GetFileName(file) + ": " + string.Join("; ", document.Removed));
            Assert.True(document.NodeCount >= 3, Path.GetFileName(file));
            using var rig = new SvgRig(markup, limits: NthSvgLimits.Renderer, window: 1200, height: 900);
            rig.Capture();
            Assert.True(rig.Canvas.Drawn >= 2, Path.GetFileName(file) + ": drew " + rig.Canvas.Drawn);
            // The written-back markup is stable.
            var again = NthSvgWriter.ToMarkup(document);
            Assert.Equal(again, NthSvgWriter.ToMarkup(NthSvgSanitizer.Parse(again, NthSvgLimits.Renderer).Document!));
        }
    });

    // ED-NEW-secfinal-44: the paint's glyph budget (200,000 characters) must not cut any real drawing. The recorded drawings of every
    // Mermaid family, light and dark, draw all of their text and use under 1% of it.
    [Fact]
    [Trait("Decision", "ED-NEW-secfinal-44")]
    public void No_recorded_mermaid_drawing_comes_near_the_glyph_budget() => headless.OnUiThread(() =>
    {
        foreach (var file in Recorded())
        {
            using var rig = new SvgRig(File.ReadAllText(file), limits: NthSvgLimits.Renderer, window: 1200, height: 900);
            rig.Capture();
            Assert.InRange(rig.Canvas.Glyphs, 0, NthSvgLimits.MaxPaintGlyphs / 100);
        }
    });
}
